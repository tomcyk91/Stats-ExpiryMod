using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;

namespace SmartExpiration.Patches
{
    // =====================================================================
    // ZOPTYMALIZOWANY SKANER DZIENNY (Usunięto ciężki Harmony Patch z Update!)
    // =====================================================================
    public static class RestockerScanner
    {

        public static bool Prepare()
        {
            return AccessTools.Method(typeof(DayCycleManager), "FinishTheDay") != null;
        }

        private static float _lastProcessTime = 0f;
        private static float _lastCacheTime = 0f;
        private static DisplaySlot[] _cachedSlots = new DisplaySlot[0];
        private static Dictionary<int, int> _slotChildCounts = new Dictionary<int, int>();

        public static void Process()
        {
            if (Time.time - _lastProcessTime < 1.5f) return;
            _lastProcessTime = Time.time;

            long __pf = SmartExpiration.SEProfiler.Begin();
            try
            {
                // PERF: wspoldzielony cache zamiast wlasnego skanu sceny.
                _cachedSlots = SmartExpiration.SceneSlotCache.GetSlots();

                foreach (var slot in _cachedSlots)
                {
                    if (slot == null || !slot.HasProduct) continue;

                    int instanceId = slot.GetInstanceID();
                    int currentChildren = slot.transform.childCount;

                    if (_slotChildCounts.ContainsKey(instanceId) && _slotChildCounts[instanceId] == currentChildren)
                    {
                        continue;
                    }

                    _slotChildCounts[instanceId] = currentChildren;

                    ExpirationManager.SyncShelf(slot);

                    var products = slot.GetComponentsInChildren<global::Product>(true);
                    bool shelfChanged = false;

                    foreach (var p in products)
                    {
                        if (p.GetComponent<ProductExpirationComponent>() == null)
                        {
                            ExpirationManager.EnsureExpiration(p, slot);
                            shelfChanged = true;
                        }
                    }

                    if (shelfChanged)
                    {
                        ExpirationManager.UpdateMemory(slot);
                    }
                }
            }
            catch (Exception ex)
            {
                UnityEngine.Debug.Log("[SmartExpiration] Błąd RestockerScanner: " + ex.Message);
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
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        public static void Prefix_BeforeOvernight()
        {
            var allSlots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
            foreach (var slot in allSlots)
            {
                if (slot == null || !slot.HasProduct) continue;
                ExpirationManager.UpdateMemory(slot);
            }
        }

        [HarmonyPostfix]
        [HarmonyPriority(Priority.Last)]
        public static void Postfix_AfterOvernight()
        {
            var allSlots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
            int currentDay = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;

            foreach (var slot in allSlots)
            {
                if (slot == null || !slot.HasProduct) continue;

                string path = ExpirationSaveManager.GetSlotPath(slot);
                var products = ExpirationSaveManager.GetSortedProducts(slot.transform);

                List<int> oldDates = new List<int>();
                if (ExpirationSaveManager.slotDates.ContainsKey(path))
                {
                    oldDates = new List<int>(ExpirationSaveManager.slotDates[path]);
                }

                List<int> newDatesToSave = new List<int>();

                for (int i = 0; i < products.Count; i++)
                {
                    var p = products[i];
                    var comp = p.GetComponent<ProductExpirationComponent>();

                    if (comp == null) comp = p.gameObject.AddComponent<ProductExpirationComponent>();

                    comp.ProductID = slot.ProductID;

                    if (i < oldDates.Count)
                    {
                        comp.ExpirationDay = oldDates[i];
                    }
                    else
                    {
                        int daysToSpoil = ExpirationCalculator.GetDaysForProduct(slot, slot.ProductID);
                        comp.ExpirationDay = currentDay + daysToSpoil;
                    }
                    
                    newDatesToSave.Add(comp.ExpirationDay);
                }

                ExpirationSaveManager.slotDates[path] = newDatesToSave;
                ExpirationManager.syncedSlots.Add(slot.GetInstanceID());
            }
        }
    }
}