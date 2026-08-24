using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace SmartExpiration.Patches
{
    [HarmonyPatch(typeof(Box))]
    internal static class BoxPatches
    {
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(Box), "AddProduct") != null &&
                   AccessTools.Method(typeof(Box), "GetProductFromBox") != null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(Box.AddProduct))]
        private static void AddProduct_Prefix(
            Box __instance,
            int productID,
            global::Product item)
        {
            try
            {
                if (__instance == null ||
                    item == null)
                {
                    return;
                }

                int runtimeKey =
                    __instance.GetInstanceID();

                int currentCount = 0;

                try
                {
                    currentCount =
                        __instance.ProductCount;
                }
                catch { }

                if (currentCount < 0)
                    currentCount = 0;

                int productId =
                    productID;

                if (productId <= 0)
                {
                    productId =
                        ExpirationSaveManager
                            .GetProductIdFromProduct(item);
                }

                if (productId <= 0)
                {
                    productId =
                        ExpirationSaveManager
                            .GetBoxProductId(__instance);
                }

                if (productId <= 0)
                    return;

                // PBOX3 can hydrate an existing box before the native AddProduct
                // increments ProductCount. This is used both during startup load
                // and when another mod rebuilds a Box with a new InstanceID.
                bool hydratedKnownState =
                    ExpirationSaveManager
                        .TryHydrateRuntimeFromKnownState(
                            __instance,
                            productId,
                            currentCount);

                int expirationDay = -1;
                int deliveryDay = -1;

                var comp =
                    item.GetComponent<ProductExpirationComponent>();

                // 1. During startup/reconstruction the exact PBOX3 pair is
                //    authoritative. A transient ProductExpirationComponent may
                //    already have been created earlier in the load sequence
                //    with current-day metadata, so it must NOT override PBOX3.
                if (hydratedKnownState &&
                    ExpirationSaveManager
                        .runtimeBoxDates
                        .TryGetValue(
                            runtimeKey,
                            out List<int> savedDates) &&
                    savedDates != null &&
                    ExpirationSaveManager
                        .runtimeBoxDeliveryDaysPerProduct
                        .TryGetValue(
                            runtimeKey,
                            out List<int> savedDeliveries) &&
                    savedDeliveries != null &&
                    savedDates.Count == savedDeliveries.Count &&
                    currentCount < savedDates.Count)
                {
                    expirationDay =
                        savedDates[currentCount];

                    deliveryDay =
                        ExpirationSaveManager
                            .NormalizeDeliveryDay(
                                productId,
                                expirationDay,
                                savedDeliveries[currentCount]);

                }

                // 2. Otherwise a transferred physical product is authoritative.
                //    Shelf -> Box, Box -> Box, restocker etc.
                if (expirationDay <= 0 &&
                    comp != null &&
                    comp.ExpirationDay > 0)
                {
                    expirationDay =
                        comp.ExpirationDay;

                    deliveryDay =
                        ExpirationSaveManager
                            .NormalizeDeliveryDay(
                                productId,
                                expirationDay,
                                comp.DeliveryDay);

                }

                // 3. Truly new physical product.
                if (expirationDay <= 0)
                {
                    CustomExpirationLoader.Load();

                    int shelfLife =
                        BoxLabelPatch
                            .GetConfigOverrideDirectly(
                                productId);

                    if (shelfLife < 0)
                    {
                        shelfLife =
                            ExpirationCalculator
                                .GetDaysForProduct(
                                    null,
                                    productId);
                    }

                    deliveryDay =
                        ExpirationSaveManager
                            .GetCurrentDaySafe();

                    expirationDay =
                        deliveryDay +
                        shelfLife;

                }

                if (comp == null)
                {
                    comp =
                        item.gameObject
                            .AddComponent<ProductExpirationComponent>();

                    comp.hideFlags =
                        HideFlags.DontSave |
                        HideFlags.HideInInspector;
                }

                comp.ProductID =
                    productId;

                comp.ExpirationDay =
                    expirationDay;

                comp.DeliveryDay =
                    deliveryDay;

                if (!ExpirationSaveManager
                        .runtimeBoxDates
                        .TryGetValue(
                            runtimeKey,
                            out List<int> dates) ||
                    dates == null)
                {
                    dates =
                        new List<int>();

                    ExpirationSaveManager
                        .runtimeBoxDates[runtimeKey] =
                        dates;
                }

                if (!ExpirationSaveManager
                        .runtimeBoxDeliveryDaysPerProduct
                        .TryGetValue(
                            runtimeKey,
                            out List<int> deliveries) ||
                    deliveries == null)
                {
                    deliveries =
                        new List<int>();

                    ExpirationSaveManager
                        .runtimeBoxDeliveryDaysPerProduct[runtimeKey] =
                        deliveries;
                }

                // If PBOX3 hydrated the complete list, write exactly into the
                // native insertion index. Otherwise append a new paired entry.
                if (dates.Count > currentCount &&
                    deliveries.Count > currentCount)
                {
                    dates[currentCount] =
                        expirationDay;

                    deliveries[currentCount] =
                        deliveryDay;
                }
                else
                {
                    // Fail-soft gap repair. Gaps should not happen in normal
                    // Box.AddProduct order; use the exact current product pair
                    // rather than inventing unrelated metadata.
                    while (dates.Count < currentCount)
                        dates.Add(expirationDay);

                    while (deliveries.Count < currentCount)
                        deliveries.Add(deliveryDay);

                    if (dates.Count == currentCount)
                        dates.Add(expirationDay);

                    if (deliveries.Count == currentCount)
                        deliveries.Add(deliveryDay);
                }

                // Lists must remain paired.
                int pairedCount =
                    Math.Min(
                        dates.Count,
                        deliveries.Count);

                while (dates.Count > pairedCount)
                    dates.RemoveAt(dates.Count - 1);

                while (deliveries.Count > pairedCount)
                    deliveries.RemoveAt(deliveries.Count - 1);

                if (!ExpirationSaveManager
                        .runtimeBoxDatesFromSave
                        .ContainsKey(runtimeKey))
                {
                    ExpirationSaveManager
                        .runtimeBoxDatesFromSave[runtimeKey] =
                        hydratedKnownState;
                }

                ExpirationSaveManager
                    .runtimeBoxConfigVersion[runtimeKey] =
                    -1;

                ExpirationSaveManager
                    .TouchRuntimeBoxState(__instance);
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    $"[PBOX3] AddProduct_Prefix error: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Box.GetProductFromBox))]
        private static void GetProductFromBox_Postfix(
            Box __instance,
            global::Product __result)
        {
            try
            {
                if (__instance == null ||
                    __result == null)
                {
                    // Native GetProductFromBox can return null in special cases.
                    // Never consume metadata when no physical product returned.
                    return;
                }

                int runtimeKey =
                    __instance.GetInstanceID();

                ExpirationSaveManager
                    .EnsureRuntimeBoxState(__instance);

                if (!ExpirationSaveManager
                        .runtimeBoxDates
                        .TryGetValue(
                            runtimeKey,
                            out List<int> dates) ||
                    dates == null ||
                    dates.Count == 0 ||
                    !ExpirationSaveManager
                        .runtimeBoxDeliveryDaysPerProduct
                        .TryGetValue(
                            runtimeKey,
                            out List<int> deliveries) ||
                    deliveries == null ||
                    deliveries.Count == 0)
                {
                    return;
                }

                // Keep the existing queue semantics: the first metadata pair
                // belongs to the physical product returned by the box.
                int expirationDay =
                    dates[0];

                int productId =
                    ExpirationSaveManager
                        .GetProductIdFromProduct(__result);

                if (productId <= 0)
                {
                    productId =
                        ExpirationSaveManager
                            .GetBoxProductId(__instance);
                }

                int deliveryDay =
                    ExpirationSaveManager
                        .NormalizeDeliveryDay(
                            productId,
                            expirationDay,
                            deliveries[0]);

                dates.RemoveAt(0);
                deliveries.RemoveAt(0);

                var comp =
                    __result
                        .GetComponent<ProductExpirationComponent>();

                if (comp == null)
                {
                    comp =
                        __result.gameObject
                            .AddComponent<ProductExpirationComponent>();

                    comp.hideFlags =
                        HideFlags.DontSave |
                        HideFlags.HideInInspector;
                }

                comp.ProductID =
                    productId;

                comp.ExpirationDay =
                    expirationDay;

                comp.DeliveryDay =
                    deliveryDay;


                if (dates.Count == 0 ||
                    deliveries.Count == 0)
                {
                    // An empty box owns no delivery history. When it is filled
                    // later, metadata comes from the inserted Product.
                    ExpirationSaveManager
                        .RemoveRuntimeBoxInstance(
                            __instance,
                            true);
                }
                else
                {
                    ExpirationSaveManager
                        .TouchRuntimeBoxState(__instance);
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    $"[PBOX3] GetProductFromBox_Postfix error: {ex.Message}");
            }
        }
    }
}
