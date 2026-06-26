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
        private static void AddProduct_Prefix(Box __instance, global::Product item)
        {
            try
            {
                if (__instance == null || item == null) return;
                int instanceId = __instance.GetInstanceID();

                try
                {
                    var parent = item.transform.parent;
                    if (parent != null && parent.GetComponentInParent<DisplaySlot>() != null)
                    {
                        StatisticMod.Plugin.DebugLog($"[BoxPatches] AddProduct skipped storing date because product came from DisplaySlot for box {instanceId}");
                        return;
                    }
                }
                catch { }

                var comp = item.GetComponent<ProductExpirationComponent>();
                int dateToStore = -1;
                if (comp != null)
                {
                    dateToStore = comp.ExpirationDay;
                    ExpirationSaveManager.runtimeBoxConfigVersion[instanceId] = -1;
                }

                if (dateToStore == -1)
                {
                    if (BoxLabelPatch.TryDequeueClipboardDate(out int queuedDate))
                    {
                        dateToStore = queuedDate;
                        ExpirationSaveManager.runtimeBoxConfigVersion[instanceId] = -1;
                    }
                }

                if (dateToStore == -1)
                {
                    try { CustomExpirationLoader.Load(); } catch { }

                    int prodId = -1;
                    try { prodId = ExpirationSaveManager.GetProductIdFromProduct(item); } catch { prodId = -1; }

                    // C1 FIX: Bezpośredni odczyt bezpiecznej natywnej właściwości .Data pomijający refleksję GetValue
                    if (prodId <= 0 && __instance.Data != null)
                    {
                        try { prodId = __instance.Data.ProductID; } catch { prodId = -1; }
                    }

                    if (prodId > 0)
                    {
                        int overrideDays = BoxLabelPatch.GetConfigOverrideDirectly(prodId);
                        if (overrideDays != -1)
                        {
                            var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                            int deliveryDay = dcm != null ? dcm.CurrentDay : 1;

                            dateToStore = deliveryDay + overrideDays;
                            ExpirationSaveManager.runtimeBoxConfigVersion[instanceId] = CustomExpirationLoader.ConfigVersion;
                            StatisticMod.Plugin.DebugLog($"[BoxPatches] Using config override for product {prodId}: {overrideDays} -> expDay {dateToStore} for box {instanceId}");
                        }
                    }
                }

                if (dateToStore == -1)
                {
                    int prodId = -1;
                    try { prodId = ExpirationSaveManager.GetProductIdFromProduct(item); } catch { prodId = -1; }
                    int shelfLife = ExpirationCalculator.GetDaysForProduct(null, prodId);

                    var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                    int day = dcm != null ? dcm.CurrentDay : 1;

                    dateToStore = day + shelfLife;
                    ExpirationSaveManager.runtimeBoxConfigVersion[instanceId] = -1;
                }

                if (dateToStore != -1)
                {
                    if (!ExpirationSaveManager.runtimeBoxDates.ContainsKey(instanceId))
                        ExpirationSaveManager.runtimeBoxDates[instanceId] = new List<int>();

                    ExpirationSaveManager.runtimeBoxDates[instanceId].Add(dateToStore);

                    var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                    if (!ExpirationSaveManager.runtimeBoxDeliveryDays.ContainsKey(instanceId))
                        ExpirationSaveManager.runtimeBoxDeliveryDays[instanceId] = dcm != null ? dcm.CurrentDay : 1;

                    if (!ExpirationSaveManager.runtimeBoxDatesFromSave.ContainsKey(instanceId))
                        ExpirationSaveManager.runtimeBoxDatesFromSave[instanceId] = false;
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugLog($"[BoxPatches] AddProduct_Prefix error: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(Box.GetProductFromBox))]
        private static void GetProductFromBox_Postfix(Box __instance)
        {
            try
            {
                if (__instance == null) return;
                int instanceId = __instance.GetInstanceID();

                if (ExpirationSaveManager.runtimeBoxDates.TryGetValue(instanceId, out List<int> dates) && dates != null && dates.Count > 0)
                {
                    int dateForProduct = dates[0];
                    dates.RemoveAt(0);

                    BoxLabelPatch.EnqueueClipboardDate(dateForProduct);

                    if (dates.Count == 0)
                    {
                        ExpirationSaveManager.runtimeBoxDates.Remove(instanceId);
                        ExpirationSaveManager.runtimeBoxDeliveryDays.Remove(instanceId);
                        ExpirationSaveManager.runtimeBoxDatesFromSave.Remove(instanceId);
                        ExpirationSaveManager.runtimeBoxConfigVersion.Remove(instanceId);
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugLog($"[BoxPatches] GetProductFromBox_Postfix error: {ex.Message}");
            }
        }
    }
}