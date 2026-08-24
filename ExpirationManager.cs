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

            // Saved shelf data is authoritative even if a transient component
            // was created earlier during scene reconstruction.
            if (hasSavedData &&
                savedDates != null &&
                index >= 0 &&
                index < savedDates.Count)
            {
                if (comp == null)
                {
                    comp =
                        product.gameObject
                            .AddComponent<ProductExpirationComponent>();

                    comp.hideFlags =
                        HideFlags.DontSave |
                        HideFlags.HideInInspector;
                }

                int productId =
                    slot != null
                        ? slot.ProductID
                        : ExpirationSaveManager.GetProductIdFromProduct(product);

                int expirationDay =
                    savedDates[index];

                int deliveryDay = 0;

                if (slot != null)
                {
                    string path =
                        ExpirationSaveManager.GetSlotPath(slot);

                    if (ExpirationSaveManager
                            .slotDeliveryDays
                            .TryGetValue(
                                path,
                                out List<int> savedDeliveries) &&
                        savedDeliveries != null &&
                        index < savedDeliveries.Count)
                    {
                        deliveryDay =
                            savedDeliveries[index];
                    }
                }

                comp.ProductID =
                    productId;

                comp.ExpirationDay =
                    expirationDay;

                comp.DeliveryDay =
                    ExpirationSaveManager.NormalizeDeliveryDay(
                        productId,
                        expirationDay,
                        deliveryDay);

                return;
            }

            if (comp != null)
            {
                int productId =
                    comp.ProductID > 0
                        ? comp.ProductID
                        : (slot != null
                            ? slot.ProductID
                            : ExpirationSaveManager.GetProductIdFromProduct(product));

                comp.ProductID =
                    productId;

                comp.DeliveryDay =
                    ExpirationSaveManager.NormalizeDeliveryDay(
                        productId,
                        comp.ExpirationDay,
                        comp.DeliveryDay);

                return;
            }

            EnsureExpiration(
                product,
                slot);
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

            if (comp != null)
            {
                if (comp.ProductID <= 0)
                    comp.ProductID = productId;

                comp.DeliveryDay =
                    ExpirationSaveManager.NormalizeDeliveryDay(
                        productId,
                        comp.ExpirationDay,
                        comp.DeliveryDay);

                return comp;
            }

            comp =
                product.gameObject
                    .AddComponent<ProductExpirationComponent>();

            comp.hideFlags =
                HideFlags.DontSave |
                HideFlags.HideInInspector;

            comp.ProductID =
                productId;

            // A genuinely new physical product receives both pieces of metadata.
            // DeliveryDay never comes from the cardboard box.
            int currentDay =
                ExpirationSaveManager.GetCurrentDaySafe();

            int shelfLife =
                ExpirationCalculator.GetDaysForProduct(
                    slot,
                    productId);

            comp.DeliveryDay =
                currentDay;

            comp.ExpirationDay =
                currentDay + shelfLife;


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

            List<int> currentDeliveries =
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

                    if (comp == null)
                    {
                        comp =
                            EnsureExpiration(
                                p,
                                slot);
                    }

                    if (comp == null)
                        continue;

                    comp.DeliveryDay =
                        ExpirationSaveManager.NormalizeDeliveryDay(
                            slot.ProductID,
                            comp.ExpirationDay,
                            comp.DeliveryDay);

                    currentDates.Add(
                        comp.ExpirationDay);

                    currentDeliveries.Add(
                        comp.DeliveryDay);
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

                    if (comp == null)
                    {
                        comp =
                            EnsureExpiration(
                                p,
                                slot);
                    }

                    if (comp == null)
                        continue;

                    comp.DeliveryDay =
                        ExpirationSaveManager.NormalizeDeliveryDay(
                            slot.ProductID,
                            comp.ExpirationDay,
                            comp.DeliveryDay);

                    currentDates.Add(
                        comp.ExpirationDay);

                    currentDeliveries.Add(
                        comp.DeliveryDay);
                }
            }

            ExpirationSaveManager
                .slotDates[path] =
                currentDates;

            ExpirationSaveManager
                .slotDeliveryDays[path] =
                currentDeliveries;
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

            comp.DeliveryDay =
                ExpirationSaveManager.NormalizeDeliveryDay(
                    slot.ProductID,
                    comp.ExpirationDay,
                    comp.DeliveryDay);

            string path =
                ExpirationSaveManager.GetSlotPath(slot);

            int count =
                GetProductCount(slot);

            bool datesOk =
                ExpirationSaveManager
                    .slotDates
                    .TryGetValue(
                        path,
                        out List<int> dates) &&
                dates != null &&
                dates.Count == count - 1;

            bool deliveriesOk =
                ExpirationSaveManager
                    .slotDeliveryDays
                    .TryGetValue(
                        path,
                        out List<int> deliveries) &&
                deliveries != null &&
                deliveries.Count == count - 1;

            if (datesOk && deliveriesOk)
            {
                dates.Add(
                    comp.ExpirationDay);

                deliveries.Add(
                    comp.DeliveryDay);
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

            bool datesOk =
                ExpirationSaveManager
                    .slotDates
                    .TryGetValue(
                        path,
                        out List<int> dates) &&
                dates != null &&
                dates.Count == count + 1;

            bool deliveriesOk =
                ExpirationSaveManager
                    .slotDeliveryDays
                    .TryGetValue(
                        path,
                        out List<int> deliveries) &&
                deliveries != null &&
                deliveries.Count == count + 1;

            if (datesOk && deliveriesOk)
            {
                dates.RemoveAt(
                    dates.Count - 1);

                deliveries.RemoveAt(
                    deliveries.Count - 1);
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

            int earliestDelivery =
                ExpirationSaveManager.NormalizeDeliveryDay(
                    slot.ProductID,
                    earliestComp.ExpirationDay,
                    earliestComp.DeliveryDay);

            int lastDelivery =
                ExpirationSaveManager.NormalizeDeliveryDay(
                    slot.ProductID,
                    lastComp.ExpirationDay,
                    lastComp.DeliveryDay);

            // FEFO swaps the complete metadata pair.
            // Physical Product GameObjects remain in their native positions.
            lastComp.ExpirationDay =
                earliestDate;

            lastComp.DeliveryDay =
                earliestDelivery;

            earliestComp.ExpirationDay =
                lastDate;

            earliestComp.DeliveryDay =
                lastDelivery;

            string path =
                ExpirationSaveManager.GetSlotPath(slot);

            bool datesOk =
                ExpirationSaveManager
                    .slotDates
                    .TryGetValue(
                        path,
                        out List<int> dates) &&
                dates != null &&
                dates.Count == count;

            bool deliveriesOk =
                ExpirationSaveManager
                    .slotDeliveryDays
                    .TryGetValue(
                        path,
                        out List<int> deliveryDays) &&
                deliveryDays != null &&
                deliveryDays.Count == count;

            if (datesOk && deliveriesOk)
            {
                dates[earliestIndex] =
                    lastDate;

                dates[lastIndex] =
                    earliestDate;

                deliveryDays[earliestIndex] =
                    lastDelivery;

                deliveryDays[lastIndex] =
                    earliestDelivery;
            }
            else
            {
                UpdateMemory(slot);
            }


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

                int expiredDelivery =
                    ExpirationSaveManager.NormalizeDeliveryDay(
                        slot.ProductID,
                        expiredComp.ExpirationDay,
                        expiredComp.DeliveryDay);

                int lastDelivery =
                    ExpirationSaveManager.NormalizeDeliveryDay(
                        slot.ProductID,
                        lastComp.ExpirationDay,
                        lastComp.DeliveryDay);

                lastComp.ExpirationDay =
                    expirationDay;

                lastComp.DeliveryDay =
                    expiredDelivery;

                expiredComp.ExpirationDay =
                    lastDate;

                expiredComp.DeliveryDay =
                    lastDelivery;

                string path =
                    ExpirationSaveManager.GetSlotPath(slot);

                bool datesOk =
                    ExpirationSaveManager
                        .slotDates
                        .TryGetValue(
                            path,
                            out List<int> dates) &&
                    dates != null &&
                    dates.Count == count;

                bool deliveriesOk =
                    ExpirationSaveManager
                        .slotDeliveryDays
                        .TryGetValue(
                            path,
                            out List<int> deliveryDays) &&
                    deliveryDays != null &&
                    deliveryDays.Count == count;

                if (datesOk && deliveriesOk)
                {
                    dates[expiredIndex] =
                        lastDate;

                    dates[lastIndex] =
                        expirationDay;

                    deliveryDays[expiredIndex] =
                        lastDelivery;

                    deliveryDays[lastIndex] =
                        expiredDelivery;
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
