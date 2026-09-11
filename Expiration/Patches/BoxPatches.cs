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
                // increments ProductCount. After startup the manager accepts ONLY
                // an exact current-session BoxData.UID bridge; transform matching
                // is load-only so a new delivery cannot steal an old carton state.
                bool hydratedKnownState =
                    ExpirationSaveManager
                        .TryHydrateRuntimeFromKnownState(
                            __instance,
                            productId,
                            currentCount);

                int expirationDay = -1;
                int deliveryDay = -1;
                bool assignedFreshDelivery = false;

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

                    assignedFreshDelivery = true;
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

                if (assignedFreshDelivery && StatisticMod.Plugin.EnableLogs)
                {
                    int uid =
                        ExpirationSaveManager
                            .GetStableBoxUid(__instance);

                    StatisticMod.Plugin.DebugLog(
                        $"[PBOX3] Fresh AddProduct metadata: uid={uid}, " +
                        $"product={productId}, index={currentCount}, " +
                        $"deliveryDay={deliveryDay}, expirationDay={expirationDay}");
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    $"[PBOX3] AddProduct_Prefix error: {ex.Message}");
            }
        }

        // Build/attach the authoritative runtime cache BEFORE the native call
        // decreases ProductCount. Doing this in a postfix is too late: the cache
        // can otherwise be rebuilt from only the remaining physical Products and
        // then one extra metadata entry gets removed.
        [HarmonyPrefix]
        [HarmonyPatch(nameof(Box.GetProductFromBox))]
        private static void GetProductFromBox_Prefix(Box __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                ExpirationSaveManager
                    .EnsureRuntimeBoxState(__instance);
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    $"[PBOX3] GetProductFromBox_Prefix error: {ex.Message}");
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
                    // Do not rebuild here after the native count already changed.
                    // The returned physical Product keeps its component fail-soft.
                    return;
                }

                int pairedCount =
                    Math.Min(
                        dates.Count,
                        deliveries.Count);

                while (dates.Count > pairedCount)
                    dates.RemoveAt(dates.Count - 1);

                while (deliveries.Count > pairedCount)
                    deliveries.RemoveAt(deliveries.Count - 1);

                if (pairedCount <= 0)
                    return;

                int productId =
                    ExpirationSaveManager
                        .GetProductIdFromProduct(__result);

                if (productId <= 0)
                {
                    productId =
                        ExpirationSaveManager
                            .GetBoxProductId(__instance);
                }

                // Verified game behavior: native Box.GetProductFromBox removes
                // the newest/end Product. PBOX3 AddProduct uses the same insertion
                // order, therefore the LAST metadata pair is authoritative.
                // Never trust a stale ProductExpirationComponent carried by a
                // pooled/materialized Product object over this exact box cache.
                int metadataIndex =
                    pairedCount - 1;

                int expirationDay =
                    dates[metadataIndex];

                int deliveryDay =
                    ExpirationSaveManager
                        .NormalizeDeliveryDay(
                            productId,
                            expirationDay,
                            deliveries[metadataIndex]);

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

                dates.RemoveAt(metadataIndex);
                deliveries.RemoveAt(metadataIndex);

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

    // RackSlot keeps most warehouse cartons virtual as BoxData. When a box is
    // materialized/taken from the rack after nightly spoilage, force the Box
    // visuals to rebuild from the already-updated BoxData.ProductCount.
    // Without this, the native Box GameObject can temporarily keep the old
    // spawned product layout even though the save-backed count was reduced.
    [HarmonyPatch(typeof(RackSlot))]
    internal static class RackSlotCartonRefreshPatch
    {
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(RackSlot), nameof(RackSlot.TakeBoxFromRack)) != null;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(RackSlot.TakeBoxFromRack))]
        private static void TakeBoxFromRack_Postfix(Box __result)
        {
            if (__result == null)
                return;

            try
            {
                // Re-attach ONLY the exact current-session UID state.
                // ExpirationSaveManager no longer performs transform matching
                // here after initial save-load.
                bool hasRuntimeState =
                    ExpirationSaveManager
                        .EnsureRuntimeBoxState(__result);

                if (__result.Data != null)
                {
                    int uid = 0;
                    try { uid = __result.Data.UID; } catch { }

                    int nativeCount =
                        __result.Data.ProductCount;

                    if (nativeCount < 0)
                        nativeCount = 0;

                    // First synchronize already spawned Product components from
                    // PBOX3. This is much cheaper than rebuilding the carton and
                    // prevents pooled/materialized Product GameObjects from keeping
                    // stale expiration metadata from an older box.
                    int physicalCount = 0;
                    if (hasRuntimeState)
                    {
                        physicalCount =
                            ExpirationSaveManager
                                .SyncRuntimeBoxComponents(__result);
                    }

                    bool needsVisualRefresh =
                        uid > 0 &&
                        ExpirationSaveManager
                            .rackUidsNeedingVisualRefresh
                            .Contains(uid);

                    // A count mismatch means the physical child hierarchy is stale
                    // even if this UID was not explicitly marked by nightly cleanup.
                    if (hasRuntimeState &&
                        physicalCount != nativeCount)
                    {
                        needsVisualRefresh = true;
                    }

                    if (needsVisualRefresh)
                    {
                        __result.RefreshSpawnedProducts();

                        // Refresh creates/reuses physical Product objects. Reapply
                        // the exact PBOX3 pairs immediately so GetProductFromBox
                        // cannot treat a stale Product component as authoritative.
                        if (ExpirationSaveManager
                                .EnsureRuntimeBoxState(__result))
                        {
                            ExpirationSaveManager
                                .SyncRuntimeBoxComponents(__result);
                        }

                        if (uid > 0)
                        {
                            ExpirationSaveManager
                                .rackUidsNeedingVisualRefresh
                                .Remove(uid);
                        }

                        if (StatisticMod.Plugin.EnableLogs)
                        {
                            StatisticMod.Plugin.DebugLog(
                                $"[PBOX3] Rack box visual refresh: uid={uid}, " +
                                $"product={__result.Data.ProductID}, count={nativeCount}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    $"[PBOX3] Rack TakeBox refresh warning: {ex.Message}");
            }
        }
    }

}
