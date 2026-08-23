using StatisticMod;
using System.Collections.Generic;
using UnityEngine;
using SmartExpiration.Patches;

namespace SmartExpiration
{
    public static class ExpirationManager
    {
        public static HashSet<int> syncedSlots =
            new HashSet<int>();

        public static int GetProductCount(DisplaySlot slot)
        {
            if (slot == null)
                return 0;

            try
            {
                var products =
                    slot.m_Products;

                return products != null
                    ? products.Count
                    : 0;
            }
            catch
            {
                return 0;
            }
        }

        public static global::Product GetProductAt(
            DisplaySlot slot,
            int index)
        {
            if (slot == null ||
                index < 0)
            {
                return null;
            }

            try
            {
                var products =
                    slot.m_Products;

                if (products == null ||
                    index >= products.Count)
                {
                    return null;
                }

                return products[index];
            }
            catch
            {
                return null;
            }
        }

        public static global::Product GetLastProduct(
            DisplaySlot slot)
        {
            int count =
                GetProductCount(slot);

            return count > 0
                ? GetProductAt(slot, count - 1)
                : null;
        }

        private static void ApplySavedOrNewExpiration(
            global::Product product,
            DisplaySlot slot,
            int index,
            bool hasSavedData,
            List<int> savedDates)
        {
            if (product == null)
                return;

            var comp =
                product.GetComponent<ProductExpirationComponent>();

            if (comp != null)
                return;

            if (hasSavedData &&
                savedDates != null &&
                index >= 0 &&
                index < savedDates.Count)
            {
                comp =
                    product.gameObject
                        .AddComponent<ProductExpirationComponent>();

                comp.hideFlags =
                    HideFlags.DontSave |
                    HideFlags.HideInInspector;

                comp.ProductID =
                    slot.ProductID;

                comp.ExpirationDay =
                    savedDates[index];
            }
            else
            {
                EnsureExpiration(
                    product,
                    slot);
            }
        }

        public static void SyncShelf(
            DisplaySlot slot)
        {
            if (slot == null ||
                !slot.HasProduct ||
                slot.ProductID <= 0)
            {
                return;
            }

            string path =
                ExpirationSaveManager.GetSlotPath(slot);

            bool hasSavedData =
                ExpirationSaveManager
                    .slotDates
                    .TryGetValue(
                        path,
                        out List<int> savedDates);

            int nativeCount =
                GetProductCount(slot);

            if (nativeCount > 0)
            {
                for (int i = 0;
                     i < nativeCount;
                     i++)
                {
                    ApplySavedOrNewExpiration(
                        GetProductAt(slot, i),
                        slot,
                        i,
                        hasSavedData,
                        savedDates);
                }
            }
            else
            {
                var products =
                    ExpirationSaveManager
                        .GetSortedProducts(slot.transform);

                for (int i = 0;
                     i < products.Count;
                     i++)
                {
                    ApplySavedOrNewExpiration(
                        products[i],
                        slot,
                        i,
                        hasSavedData,
                        savedDates);
                }
            }

            syncedSlots.Add(
                slot.GetInstanceID());
        }

        public static ProductExpirationComponent EnsureExpiration(
            global::Product product,
            DisplaySlot slot)
        {
            if (product == null)
                return null;

            CustomExpirationLoader.Load();

            var comp =
                product.GetComponent<ProductExpirationComponent>();

            if (comp != null)
                return comp;

            comp =
                product.gameObject
                    .AddComponent<ProductExpirationComponent>();

            comp.hideFlags =
                HideFlags.DontSave |
                HideFlags.HideInInspector;

            int productId =
                slot != null
                    ? slot.ProductID
                    : 0;

            if (productId <= 0)
            {
                try
                {
                    productId =
                        ExpirationSaveManager
                            .GetProductIdFromProduct(product);
                }
                catch { }
            }

            comp.ProductID =
                productId;

            // =========================================================
            // Brak ClipboardDate i brak odczytu "pierwszej daty" z
            // przypadkowego rodzica Box.
            //
            // Produkt wyjęty z kartonu dostaje swój dokładny termin już
            // w BoxPatches.GetProductFromBox_Postfix.
            //
            // Jeżeli trafiamy tutaj bez ProductExpirationComponent,
            // traktujemy obiekt jako naprawdę nowy produkt.
            // =========================================================

            // Jedyna ścieżka dla naprawdę NOWEGO produktu:
            // bieżący dzień + stała wartość z cfg/calculator.
            int currentDay =
                DayCycleManager.HasInstance &&
                DayCycleManager.Instance != null &&
                DayCycleManager.Instance.CurrentDay > 0
                    ? DayCycleManager.Instance.CurrentDay
                    : 1;

            int shelfLife =
                ExpirationCalculator.GetDaysForProduct(
                    slot,
                    productId);

            comp.ExpirationDay =
                currentDay + shelfLife;

            StatisticMod.Plugin.DebugLog(
                $"[EnsureExpiration] Fresh deterministic date: " +
                $"product={productId}, day={currentDay}, " +
                $"shelfLife={shelfLife}, exp={comp.ExpirationDay}");

            return comp;
        }

        public static void UpdateMemory(
            DisplaySlot slot)
        {
            if (slot == null)
                return;

            string path =
                ExpirationSaveManager.GetSlotPath(slot);

            int count =
                GetProductCount(slot);

            List<int> currentDates =
                new List<int>(
                    count > 0
                        ? count
                        : 0);

            if (count > 0)
            {
                for (int i = 0;
                     i < count;
                     i++)
                {
                    var p =
                        GetProductAt(
                            slot,
                            i);

                    if (p == null)
                        continue;

                    var comp =
                        p.GetComponent<ProductExpirationComponent>();

                    if (comp != null)
                    {
                        currentDates.Add(
                            comp.ExpirationDay);
                    }
                }
            }
            else if (slot.HasProduct)
            {
                var products =
                    ExpirationSaveManager
                        .GetSortedProducts(slot.transform);

                for (int i = 0;
                     i < products.Count;
                     i++)
                {
                    var p =
                        products[i];

                    if (p == null)
                        continue;

                    var comp =
                        p.GetComponent<ProductExpirationComponent>();

                    if (comp != null)
                    {
                        currentDates.Add(
                            comp.ExpirationDay);
                    }
                }
            }

            ExpirationSaveManager
                .slotDates[path] =
                currentDates;
        }

        public static void RecordProductAdded(
            DisplaySlot slot,
            global::Product addedProduct)
        {
            if (slot == null)
                return;

            var comp =
                EnsureExpiration(
                    addedProduct,
                    slot);

            if (comp == null)
            {
                UpdateMemory(slot);
                return;
            }

            string path =
                ExpirationSaveManager.GetSlotPath(slot);

            int count =
                GetProductCount(slot);

            if (ExpirationSaveManager
                    .slotDates
                    .TryGetValue(
                        path,
                        out List<int> dates) &&
                dates != null &&
                dates.Count == count - 1)
            {
                dates.Add(
                    comp.ExpirationDay);
            }
            else
            {
                UpdateMemory(slot);
            }

            syncedSlots.Add(
                slot.GetInstanceID());
        }

        public static void RecordProductRemoved(
            DisplaySlot slot)
        {
            if (slot == null)
                return;

            string path =
                ExpirationSaveManager.GetSlotPath(slot);

            int count =
                GetProductCount(slot);

            if (ExpirationSaveManager
                    .slotDates
                    .TryGetValue(
                        path,
                        out List<int> dates) &&
                dates != null &&
                dates.Count == count + 1)
            {
                dates.RemoveAt(
                    dates.Count - 1);
            }
            else
            {
                UpdateMemory(slot);
            }
        }

        public static bool TryGetLastExpirationDay(
            DisplaySlot slot,
            out int expirationDay)
        {
            expirationDay = -1;

            var last =
                GetLastProduct(slot);

            if (last == null)
                return false;

            var comp =
                last.GetComponent<ProductExpirationComponent>();

            if (comp == null)
            {
                SyncShelf(slot);

                comp =
                    last.GetComponent<ProductExpirationComponent>();
            }

            if (comp == null)
                return false;

            expirationDay =
                comp.ExpirationDay;

            return true;
        }

        public static bool HasExpiredProduct(
            DisplaySlot slot,
            int currentDay)
        {
            if (slot == null ||
                !slot.HasProduct)
            {
                return false;
            }

            int count =
                GetProductCount(slot);

            bool missingComponent =
                false;

            for (int i = 0;
                 i < count;
                 i++)
            {
                var p =
                    GetProductAt(
                        slot,
                        i);

                if (p == null)
                    continue;

                var comp =
                    p.GetComponent<ProductExpirationComponent>();

                if (comp == null)
                {
                    missingComponent = true;
                    continue;
                }

                if (comp.ExpirationDay <= currentDay)
                    return true;
            }

            if (missingComponent &&
                ExpirationSaveManager.SaveLoaded)
            {
                SyncShelf(slot);

                for (int i = 0;
                     i < count;
                     i++)
                {
                    var p =
                        GetProductAt(
                            slot,
                            i);

                    if (p == null)
                        continue;

                    var comp =
                        p.GetComponent<ProductExpirationComponent>();

                    if (comp != null &&
                        comp.ExpirationDay <= currentDay)
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// FEFO (First Expired, First Out / najkrótszy termin pierwszy).
        ///
        /// DisplaySlot.TakeProductFromDisplay() natywnie usuwa ostatni Product
        /// z m_Products. Nie zmieniamy kolejności fizycznych obiektów ani
        /// natywnej listy gry. Zamiast tego przenosimy WYŁĄCZNIE metadane
        /// najkrótszego terminu na ostatni Product.
        ///
        /// Dzięki temu:
        /// - klient zawsze zabiera sztukę z najbliższym terminem,
        /// - gracz zdejmujący towar z półki również dostaje najstarszą sztukę,
        /// - restocker korzystający z tej samej metody zachowuje FEFO,
        /// - układ produktów/prefabów na półce pozostaje nietknięty.
        /// </summary>
        public static bool PrepareFefoProductForNativeTake(
            DisplaySlot slot,
            out int expirationDay)
        {
            expirationDay = -1;

            if (slot == null ||
                !slot.HasProduct)
            {
                return false;
            }

            // Gwarantujemy obecność ProductExpirationComponent.
            SyncShelf(slot);

            int count =
                GetProductCount(slot);

            if (count <= 0)
                return false;

            int earliestIndex = -1;
            int earliestDate = int.MaxValue;
            ProductExpirationComponent earliestComp = null;

            for (int i = 0;
                 i < count;
                 i++)
            {
                var product =
                    GetProductAt(
                        slot,
                        i);

                if (product == null)
                    continue;

                var comp =
                    product.GetComponent<ProductExpirationComponent>();

                if (comp == null)
                {
                    comp =
                        EnsureExpiration(
                            product,
                            slot);
                }

                if (comp == null)
                    continue;

                if (comp.ExpirationDay < earliestDate)
                {
                    earliestDate =
                        comp.ExpirationDay;

                    earliestIndex =
                        i;

                    earliestComp =
                        comp;
                }
            }

            if (earliestIndex < 0 ||
                earliestComp == null ||
                earliestDate == int.MaxValue)
            {
                return false;
            }

            expirationDay =
                earliestDate;

            int lastIndex =
                count - 1;

            // Jeśli najkrótszy termin już siedzi na natywnym końcu,
            // gra zabierze właściwą sztukę bez żadnej zmiany.
            if (earliestIndex == lastIndex)
            {
                return true;
            }

            var lastProduct =
                GetProductAt(
                    slot,
                    lastIndex);

            if (lastProduct == null)
                return false;

            var lastComp =
                lastProduct.GetComponent<ProductExpirationComponent>();

            if (lastComp == null)
            {
                lastComp =
                    EnsureExpiration(
                        lastProduct,
                        slot);
            }

            if (lastComp == null)
                return false;

            int lastDate =
                lastComp.ExpirationDay;

            // Zamiana WYŁĄCZNIE terminów.
            // Fizyczne Product GameObjecty pozostają na swoich miejscach.
            lastComp.ExpirationDay =
                earliestDate;

            earliestComp.ExpirationDay =
                lastDate;

            // Pamięć slotu musi odzwierciedlać ten sam swap,
            // aby zapis i RecordProductRemoved() były zgodne.
            string path =
                ExpirationSaveManager.GetSlotPath(slot);

            if (ExpirationSaveManager
                    .slotDates
                    .TryGetValue(
                        path,
                        out List<int> dates) &&
                dates != null &&
                dates.Count == count)
            {
                dates[earliestIndex] =
                    lastDate;

                dates[lastIndex] =
                    earliestDate;
            }
            else
            {
                // Fail-soft przy niespójnym cache.
                UpdateMemory(slot);
            }

            StatisticMod.Plugin.DebugLog(
                $"[FEFO] Prepared shelf take: " +
                $"product={slot.ProductID}, " +
                $"earliestDate={earliestDate}, " +
                $"sourceIndex={earliestIndex}, " +
                $"nativeTakeIndex={lastIndex}");

            return true;
        }

        public static bool PrepareExpiredProductForNativeTake(
            DisplaySlot slot,
            int currentDay,
            out int expirationDay)
        {
            expirationDay = -1;

            if (slot == null ||
                !slot.HasProduct)
            {
                return false;
            }

            SyncShelf(slot);

            int count =
                GetProductCount(slot);

            if (count <= 0)
                return false;

            int expiredIndex = -1;

            ProductExpirationComponent expiredComp =
                null;

            for (int i = 0;
                 i < count;
                 i++)
            {
                var p =
                    GetProductAt(
                        slot,
                        i);

                if (p == null)
                    continue;

                var comp =
                    p.GetComponent<ProductExpirationComponent>();

                if (comp != null &&
                    comp.ExpirationDay <= currentDay)
                {
                    expiredIndex = i;
                    expiredComp = comp;
                    break;
                }
            }

            if (expiredIndex < 0 ||
                expiredComp == null)
            {
                return false;
            }

            int lastIndex =
                count - 1;

            expirationDay =
                expiredComp.ExpirationDay;

            if (expiredIndex != lastIndex)
            {
                var lastProduct =
                    GetProductAt(
                        slot,
                        lastIndex);

                var lastComp =
                    lastProduct != null
                        ? lastProduct
                            .GetComponent<ProductExpirationComponent>()
                        : null;

                if (lastComp == null &&
                    lastProduct != null)
                {
                    lastComp =
                        EnsureExpiration(
                            lastProduct,
                            slot);
                }

                if (lastComp == null)
                    return false;

                int lastDate =
                    lastComp.ExpirationDay;

                lastComp.ExpirationDay =
                    expirationDay;

                expiredComp.ExpirationDay =
                    lastDate;

                string path =
                    ExpirationSaveManager.GetSlotPath(slot);

                if (ExpirationSaveManager
                        .slotDates
                        .TryGetValue(
                            path,
                            out List<int> dates) &&
                    dates != null &&
                    dates.Count == count)
                {
                    dates[expiredIndex] =
                        lastDate;

                    dates[lastIndex] =
                        expirationDay;
                }
                else
                {
                    UpdateMemory(slot);
                }
            }

            return true;
        }

        public static int GetFreshDate(
            DisplaySlot slot)
        {
            int currentDay =
                DayCycleManager.HasInstance &&
                DayCycleManager.Instance != null &&
                DayCycleManager.Instance.CurrentDay > 0
                    ? DayCycleManager.Instance.CurrentDay
                    : 1;

            int prodId =
                slot != null
                    ? slot.ProductID
                    : 0;

            int daysToSpoil =
                ExpirationCalculator.GetDaysForProduct(
                    slot,
                    prodId);

            return currentDay +
                   daysToSpoil;
        }
    }
}
