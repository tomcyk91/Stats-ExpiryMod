using StatisticMod;
using System.Collections.Generic;
using UnityEngine;
using SmartExpiration.Patches;

namespace SmartExpiration
{
    public static class ExpirationManager
    {
        public static HashSet<int> syncedSlots = new HashSet<int>();

        // Native DisplaySlot already owns the authoritative product list. Using it avoids
        // GetComponentsInChildren + Sort allocations on every customer/restocker action.
        public static int GetProductCount(DisplaySlot slot)
        {
            if (slot == null) return 0;
            try
            {
                var products = slot.m_Products;
                return products != null ? products.Count : 0;
            }
            catch { return 0; }
        }

        public static global::Product GetProductAt(DisplaySlot slot, int index)
        {
            if (slot == null || index < 0) return null;
            try
            {
                var products = slot.m_Products;
                if (products == null || index >= products.Count) return null;
                return products[index];
            }
            catch { return null; }
        }

        public static global::Product GetLastProduct(DisplaySlot slot)
        {
            int count = GetProductCount(slot);
            return count > 0 ? GetProductAt(slot, count - 1) : null;
        }

        private static void ApplySavedOrNewExpiration(global::Product product, DisplaySlot slot, int index, bool hasSavedData, List<int> savedDates)
        {
            if (product == null) return;

            var comp = product.GetComponent<ProductExpirationComponent>();
            if (comp != null) return;

            if (hasSavedData && savedDates != null && index >= 0 && index < savedDates.Count)
            {
                comp = product.gameObject.AddComponent<ProductExpirationComponent>();
                comp.hideFlags = HideFlags.DontSave | HideFlags.HideInInspector;
                comp.ProductID = slot.ProductID;
                comp.ExpirationDay = savedDates[index];
            }
            else
            {
                EnsureExpiration(product, slot);
            }
        }

        public static void SyncShelf(DisplaySlot slot)
        {
            if (slot == null || !slot.HasProduct || slot.ProductID <= 0) return;

            string path = ExpirationSaveManager.GetSlotPath(slot);
            bool hasSavedData = ExpirationSaveManager.slotDates.TryGetValue(path, out List<int> savedDates);

            int nativeCount = GetProductCount(slot);
            if (nativeCount > 0)
            {
                for (int i = 0; i < nativeCount; i++)
                    ApplySavedOrNewExpiration(GetProductAt(slot, i), slot, i, hasSavedData, savedDates);
            }
            else
            {
                // Fail-soft fallback for a future game version where m_Products changes.
                var products = ExpirationSaveManager.GetSortedProducts(slot.transform);
                for (int i = 0; i < products.Count; i++)
                    ApplySavedOrNewExpiration(products[i], slot, i, hasSavedData, savedDates);
            }

            syncedSlots.Add(slot.GetInstanceID());
        }

        public static ProductExpirationComponent EnsureExpiration(global::Product product, DisplaySlot slot)
        {
            if (product == null) return null;

            CustomExpirationLoader.Load();

            var comp = product.GetComponent<ProductExpirationComponent>();
            if (comp != null) return comp;

            comp = product.gameObject.AddComponent<ProductExpirationComponent>();
            comp.hideFlags = HideFlags.DontSave | HideFlags.HideInInspector;
            comp.ProductID = slot != null ? slot.ProductID : 0;

            // 1) clipboard (shelf -> box transfer)
            if (SmartExpiration.Patches.BoxLabelPatch.TryDequeueClipboardDate(out int queuedDate))
            {
                comp.ExpirationDay = queuedDate;
                StatisticMod.Plugin.DebugLog($"[EnsureExpiration] Applied queued clipboard date {queuedDate} to product (id={comp.ProductID})");
                return comp;
            }

            // 2) runtime dates of the source box
            try
            {
                var parentBox = product.GetComponentInParent<Box>();
                if (parentBox != null)
                {
                    int boxKey = parentBox.GetInstanceID();
                    if (ExpirationSaveManager.runtimeBoxDates.TryGetValue(boxKey, out List<int> list) && list.Count > 0)
                    {
                        comp.ExpirationDay = list[0];
                        StatisticMod.Plugin.DebugLog($"[EnsureExpiration] Applied runtimeBoxDates date {comp.ExpirationDay} for box {boxKey}");
                        return comp;
                    }
                }
            }
            catch { }

            // 3) fallback: calculator / config
            comp.ExpirationDay = ExpirationCalculator.GetDaysForProduct(slot, comp.ProductID) +
                                 (DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1);
            StatisticMod.Plugin.DebugLog($"[EnsureExpiration] Applied fallback date {comp.ExpirationDay} to product (id={comp.ProductID})");
            return comp;
        }

        // Rebuilds only the small list of dates. No scene hierarchy scan, no sorting and,
        // importantly, no random shuffling of dates between physical products.
        public static void UpdateMemory(DisplaySlot slot)
        {
            if (slot == null) return;

            string path = ExpirationSaveManager.GetSlotPath(slot);
            int count = GetProductCount(slot);
            List<int> currentDates = new List<int>(count > 0 ? count : 0);

            if (count > 0)
            {
                for (int i = 0; i < count; i++)
                {
                    var p = GetProductAt(slot, i);
                    if (p == null) continue;
                    var comp = p.GetComponent<ProductExpirationComponent>();
                    if (comp != null) currentDates.Add(comp.ExpirationDay);
                }
            }
            else if (slot.HasProduct)
            {
                // Fail-soft fallback only if the native list is unexpectedly unavailable.
                var products = ExpirationSaveManager.GetSortedProducts(slot.transform);
                for (int i = 0; i < products.Count; i++)
                {
                    var p = products[i];
                    if (p == null) continue;
                    var comp = p.GetComponent<ProductExpirationComponent>();
                    if (comp != null) currentDates.Add(comp.ExpirationDay);
                }
            }

            ExpirationSaveManager.slotDates[path] = currentDates;
        }

        // O(1) normal path after DisplaySlot.AddProduct.
        public static void RecordProductAdded(DisplaySlot slot, global::Product addedProduct)
        {
            if (slot == null) return;

            var comp = EnsureExpiration(addedProduct, slot);
            if (comp == null)
            {
                UpdateMemory(slot);
                return;
            }

            string path = ExpirationSaveManager.GetSlotPath(slot);
            int count = GetProductCount(slot);

            if (ExpirationSaveManager.slotDates.TryGetValue(path, out List<int> dates) &&
                dates != null && dates.Count == count - 1)
            {
                dates.Add(comp.ExpirationDay);
            }
            else
            {
                UpdateMemory(slot);
            }

            syncedSlots.Add(slot.GetInstanceID());
        }

        // O(1) normal path after DisplaySlot.TakeProductFromDisplay. The game removes
        // the last entry from m_Products, matching the existing mod's previous assumption.
        public static void RecordProductRemoved(DisplaySlot slot)
        {
            if (slot == null) return;

            string path = ExpirationSaveManager.GetSlotPath(slot);
            int count = GetProductCount(slot);

            if (ExpirationSaveManager.slotDates.TryGetValue(path, out List<int> dates) &&
                dates != null && dates.Count == count + 1)
            {
                dates.RemoveAt(dates.Count - 1);
            }
            else
            {
                UpdateMemory(slot);
            }
        }

        public static bool TryGetLastExpirationDay(DisplaySlot slot, out int expirationDay)
        {
            expirationDay = -1;
            var last = GetLastProduct(slot);
            if (last == null) return false;

            var comp = last.GetComponent<ProductExpirationComponent>();
            if (comp == null)
            {
                SyncShelf(slot);
                comp = last.GetComponent<ProductExpirationComponent>();
            }

            if (comp == null) return false;
            expirationDay = comp.ExpirationDay;
            return true;
        }

        public static bool HasExpiredProduct(DisplaySlot slot, int currentDay)
        {
            if (slot == null || !slot.HasProduct) return false;

            int count = GetProductCount(slot);
            bool missingComponent = false;

            for (int i = 0; i < count; i++)
            {
                var p = GetProductAt(slot, i);
                if (p == null) continue;

                var comp = p.GetComponent<ProductExpirationComponent>();
                if (comp == null)
                {
                    missingComponent = true;
                    continue;
                }

                if (comp.ExpirationDay <= currentDay) return true;
            }

            if (missingComponent && ExpirationSaveManager.SaveLoaded)
            {
                SyncShelf(slot);
                for (int i = 0; i < count; i++)
                {
                    var p = GetProductAt(slot, i);
                    if (p == null) continue;
                    var comp = p.GetComponent<ProductExpirationComponent>();
                    if (comp != null && comp.ExpirationDay <= currentDay) return true;
                }
            }

            return false;
        }

        // DisplaySlot.TakeProductFromDisplay removes the last native product. To remove a
        // specific expired unit without corrupting DisplaySlot's internal state, move only
        // the expiration metadata of one expired unit to the last native product. Products
        // in a slot have the same ProductID, so this preserves the exact multiset of dates.
        public static bool PrepareExpiredProductForNativeTake(DisplaySlot slot, int currentDay, out int expirationDay)
        {
            expirationDay = -1;
            if (slot == null || !slot.HasProduct) return false;

            SyncShelf(slot);

            int count = GetProductCount(slot);
            if (count <= 0) return false;

            int expiredIndex = -1;
            ProductExpirationComponent expiredComp = null;

            for (int i = 0; i < count; i++)
            {
                var p = GetProductAt(slot, i);
                if (p == null) continue;
                var comp = p.GetComponent<ProductExpirationComponent>();
                if (comp != null && comp.ExpirationDay <= currentDay)
                {
                    expiredIndex = i;
                    expiredComp = comp;
                    break;
                }
            }

            if (expiredIndex < 0 || expiredComp == null) return false;

            int lastIndex = count - 1;
            expirationDay = expiredComp.ExpirationDay;

            if (expiredIndex != lastIndex)
            {
                var lastProduct = GetProductAt(slot, lastIndex);
                var lastComp = lastProduct != null ? lastProduct.GetComponent<ProductExpirationComponent>() : null;
                if (lastComp == null && lastProduct != null)
                    lastComp = EnsureExpiration(lastProduct, slot);

                if (lastComp == null) return false;

                int lastDate = lastComp.ExpirationDay;
                lastComp.ExpirationDay = expirationDay;
                expiredComp.ExpirationDay = lastDate;

                string path = ExpirationSaveManager.GetSlotPath(slot);
                if (ExpirationSaveManager.slotDates.TryGetValue(path, out List<int> dates) &&
                    dates != null && dates.Count == count)
                {
                    dates[expiredIndex] = lastDate;
                    dates[lastIndex] = expirationDay;
                }
                else
                {
                    UpdateMemory(slot);
                }
            }

            return true;
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
