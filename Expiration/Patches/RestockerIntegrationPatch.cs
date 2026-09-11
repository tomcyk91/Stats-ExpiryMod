using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;

namespace SmartExpiration.Patches
{
    // =====================================================================
    // ZOPTYMALIZOWANY SKANER DZIENNY
    // =====================================================================
    public static class RestockerScanner
    {
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(DayCycleManager), "FinishTheDay") != null;
        }

        private static float _lastProcessTime = 0f;
        private static DisplaySlot[] _cachedSlots = new DisplaySlot[0];
        private static Dictionary<int, int> _slotChildCounts = new Dictionary<int, int>();
        private static int _batchCursor = 0;
        private const int BatchSize = 16;

        public static void Process()
        {
            if (Time.time - _lastProcessTime < 1.5f) return;
            _lastProcessTime = Time.time;

            long __pf = SmartExpiration.SEProfiler.Begin();
            try
            {
                // SceneSlotCache no longer performs TTL rescans. Reading it here is O(1)
                // and the safety scanner itself examines only a fixed number of slots.
                _cachedSlots = SmartExpiration.SceneSlotCache.GetSlots();

                int total = _cachedSlots != null ? _cachedSlots.Length : 0;
                if (total == 0) return;
                if (_batchCursor >= total) _batchCursor = 0;

                int examined = 0;
                int idx = _batchCursor;

                // BUGFIX PERF: the old condition counted only CHANGED slots. When no shelf
                // changed it walked the entire store every 1.5 s. We now inspect at most
                // BatchSize slots per tick, regardless of whether they changed.
                while (examined < total && examined < BatchSize)
                {
                    var slot = _cachedSlots[idx];
                    idx++; if (idx >= total) idx = 0;
                    examined++;

                    if (slot == null) continue;

                    int instanceId = slot.GetInstanceID();
                    int currentProducts = ExpirationManager.GetProductCount(slot);

                    if (_slotChildCounts.TryGetValue(instanceId, out int prevCount) && prevCount == currentProducts)
                        continue;

                    // Remember empty state too, so a later refill with the same count is still
                    // detected correctly after the slot passed through zero products.
                    _slotChildCounts[instanceId] = currentProducts;
                    if (currentProducts <= 0 || !slot.HasProduct)
                    {
                        LabelExclamationOverlay.QueueSlot(slot);
                        continue;
                    }

                    // SyncShelf now iterates DisplaySlot.m_Products directly, without a
                    // hierarchy scan/sort. It also already creates every missing component.
                    ExpirationManager.SyncShelf(slot);
                    LabelExclamationOverlay.QueueSlot(slot);
                }
                _batchCursor = idx;
            }
            catch (Exception ex)
            {
                // B2 FIX: Oficjalny logger BepInEx zamiast surowego UnityEngine.Debug.Log
                StatisticMod.Plugin.Log.LogError("[SmartExpiration] Błąd RestockerScanner: " + ex.Message);
            }
            finally { SmartExpiration.SEProfiler.End("RestockerScan", __pf); }
        }
    }

    // =====================================================================
    // SKANER NOCNY (Uruchamia się tylko raz na dzień, nie wpływa na FPS)
    // =====================================================================
    [HarmonyPatch(typeof(DayCycleManager), "FinishTheDay")]
    public static class OvernightWorkersIntegration
    {
        // B3 FIX: Pancerna tarcza Fail-Soft chroniąca przed aktualizacjami gry
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(DayCycleManager), "FinishTheDay") != null;
        }

        [HarmonyPrefix]
        // Run after DailySpoilagePatches (Priority.First), but still before the
        // default-priority OvernightWorkers prefix. This way UpdateMemory sees
        // the shelf state after expired units were removed.
        [HarmonyPriority(Priority.VeryHigh)]
        [HarmonyBefore("com.sms.apollo.overnightworkers")]
        public static void Prefix_BeforeOvernight()
        {
            // PERF FIX: Korzystamy z błyskawicznego bufora SceneSlotCache
            var allSlots = SmartExpiration.SceneSlotCache.GetSlots();
            if (allSlots == null) return;

            for (int i = 0; i < allSlots.Length; i++)
            {
                var slot = allSlots[i];
                if (slot != null && slot.HasProduct) ExpirationManager.UpdateMemory(slot);
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix_AfterOvernight()
        {
            var allSlots = SmartExpiration.SceneSlotCache.GetSlots();
            if (allSlots == null) return;

            for (int s = 0; s < allSlots.Length; s++)
            {
                var slot = allSlots[s];
                if (slot == null || !slot.HasProduct) continue;

                string path = ExpirationSaveManager.GetSlotPath(slot);

                // ExpirationManager already exposes the native m_Products list.
                // Avoid a hierarchy traversal + transform sort for every shelf at
                // the end of the day. SyncShelf uses this same native order.
                int productCount = ExpirationManager.GetProductCount(slot);
                List<global::Product> fallbackProducts = null;
                if (productCount <= 0 && slot.HasProduct)
                {
                    // Fail-soft fallback for unusual game/mod states where the
                    // native list is temporarily unavailable. Normal gameplay
                    // stays on the allocation-light native path above.
                    fallbackProducts =
                        ExpirationSaveManager.GetSortedProducts(slot.transform);
                    productCount = fallbackProducts.Count;
                }

                List<int> newDatesToSave =
                    new List<int>(productCount);

                List<int> newDeliveriesToSave =
                    new List<int>(productCount);

                for (int i = 0; i < productCount; i++)
                {
                    var p = fallbackProducts != null
                        ? fallbackProducts[i]
                        : ExpirationManager.GetProductAt(slot, i);

                    if (p == null)
                        continue;

                    // The Product itself is authoritative after OvernightWorkers
                    // moved it from a carton to a shelf. NEVER reassign oldDates[i]
                    // from the pre-restock shelf snapshot: that attached historical
                    // dates to newly restocked products purely by shelf index and
                    // could resurrect products already expired several days ago.
                    var comp =
                        p.GetComponent<ProductExpirationComponent>();

                    if (comp == null ||
                        comp.ExpirationDay <= 0)
                    {
                        comp =
                            ExpirationManager.EnsureExpiration(
                                p,
                                slot);
                    }

                    if (comp == null ||
                        comp.ExpirationDay <= 0)
                    {
                        continue;
                    }

                    comp.ProductID =
                        slot.ProductID;

                    comp.DeliveryDay =
                        ExpirationSaveManager.NormalizeDeliveryDay(
                            slot.ProductID,
                            comp.ExpirationDay,
                            comp.DeliveryDay);

                    newDatesToSave.Add(
                        comp.ExpirationDay);

                    newDeliveriesToSave.Add(
                        comp.DeliveryDay);
                }

                // Rebuild shelf memory FROM the actual post-restock Products.
                // Do not carry the pre-restock index map into the next day.
                ExpirationSaveManager.slotDates[path] =
                    newDatesToSave;

                ExpirationSaveManager.slotDeliveryDays[path] =
                    newDeliveriesToSave;

                ExpirationManager.syncedSlots.Add(
                    slot.GetInstanceID());
            }
        }
    }
}