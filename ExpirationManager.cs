using StatisticMod;
using System.Collections.Generic;
using UnityEngine;
using SmartExpiration.Patches; // <-- DODANE: aby widzieć BoxLabelPatch

namespace SmartExpiration
{
    public static class ExpirationManager
    {
        public static HashSet<int> syncedSlots = new HashSet<int>();

        public static void SyncShelf(DisplaySlot slot)
        {
            if (slot == null || !slot.HasProduct || slot.ProductID <= 0) return;

            string path = ExpirationSaveManager.GetSlotPath(slot);
            var products = ExpirationSaveManager.GetSortedProducts(slot.transform);
            bool hasSavedData = ExpirationSaveManager.slotDates.TryGetValue(path, out List<int> savedDates);

            for (int i = 0; i < products.Count; i++)
            {
                var p = products[i];
                if (p == null) continue;

                var comp = p.GetComponent<ProductExpirationComponent>();
                if (comp == null)
                {
                    if (hasSavedData && i < savedDates.Count)
                    {
                        comp = p.gameObject.AddComponent<ProductExpirationComponent>();
                        comp.hideFlags = HideFlags.DontSave | HideFlags.HideInInspector; // Natychmiastowe zabezpieczenie
                        comp.ProductID = slot.ProductID;
                        comp.ExpirationDay = savedDates[i];
                    }
                    else
                    {
                        EnsureExpiration(p, slot);
                    }
                }
            }
            syncedSlots.Add(slot.GetInstanceID());
        }

        public static ProductExpirationComponent EnsureExpiration(global::Product product, DisplaySlot slot)
        {
            if (product == null) return null;

            CustomExpirationLoader.Load();

            var comp = product.GetComponent<ProductExpirationComponent>();
            if (comp != null) return comp; // nie nadpisujemy istniejącej daty

            comp = product.gameObject.AddComponent<ProductExpirationComponent>();
            comp.hideFlags = HideFlags.DontSave | HideFlags.HideInInspector;
            comp.ProductID = slot != null ? slot.ProductID : 0;

            // 1) clipboard (schowek)
            if (SmartExpiration.Patches.BoxLabelPatch.TryDequeueClipboardDate(out int queuedDate))
            {
                comp.ExpirationDay = queuedDate;
                StatisticMod.Plugin.DebugLog($"[EnsureExpiration] Applied queued clipboard date {queuedDate} to product (id={comp.ProductID})");
                return comp;
            }

            // 2) jeśli produkt pochodzi z boxa i runtimeBoxDates ma wpis (opcjonalne)
            try
            {
                var parentBox = product.GetComponentInParent<Box>();
                if (parentBox != null)
                {
                    int boxKey = parentBox.GetInstanceID();
                    if (ExpirationSaveManager.runtimeBoxDates.TryGetValue(boxKey, out List<int> list) && list.Count > 0)
                    {
                        // użyj pierwszej daty (ale nie usuwamy tutaj — BoxPatches to robi przy wyjmowaniu)
                        comp.ExpirationDay = list[0];
                        StatisticMod.Plugin.DebugLog($"[EnsureExpiration] Applied runtimeBoxDates date {comp.ExpirationDay} for box {boxKey}");
                        return comp;
                    }
                }
            }
            catch { }

            // 3) fallback: kalkulator / config
            comp.ExpirationDay = ExpirationCalculator.GetDaysForProduct(slot, comp.ProductID) + (DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1);
            StatisticMod.Plugin.DebugLog($"[EnsureExpiration] Applied fallback date {comp.ExpirationDay} to product (id={comp.ProductID})");
            return comp;
        }


        public static void UpdateMemory(DisplaySlot slot)
        {
            if (slot == null) return;
            string path = ExpirationSaveManager.GetSlotPath(slot);

            var products = ExpirationSaveManager.GetSortedProducts(slot.transform);

            List<int> currentDates = new List<int>();
            foreach (var p in products)
            {
                var comp = p.GetComponent<ProductExpirationComponent>();
                if (comp != null) currentDates.Add(comp.ExpirationDay);
            }

            int n = currentDates.Count;
            for (int i = 0; i < n; i++)
            {
                int randomIndex = UnityEngine.Random.Range(i, n);
                int temp = currentDates[i];
                currentDates[i] = currentDates[randomIndex];
                currentDates[randomIndex] = temp;
            }

            int index = 0;
            foreach (var p in products)
            {
                var comp = p.GetComponent<ProductExpirationComponent>();
                if (comp != null && index < currentDates.Count)
                {
                    comp.ExpirationDay = currentDates[index];
                    index++;
                }
            }

            ExpirationSaveManager.slotDates[path] = currentDates;
        }

        public static int GetFreshDate(DisplaySlot slot)
        {
            int currentDay = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;
            int prodId = slot != null ? slot.ProductID : 0;
            int daysToSpoil = ExpirationCalculator.GetDaysForProduct(slot, prodId);
            return currentDay + daysToSpoil;
        }
    }
}
