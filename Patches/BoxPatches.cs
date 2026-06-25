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

                // Jeśli produkt pochodzi z półki (czyli był wykładany), nie zapisujemy daty do boxa tutaj.
                try
                {
                    var parent = item.transform.parent;
                    if (parent != null && parent.GetComponentInParent<DisplaySlot>() != null)
                    {
                        StatisticMod.Plugin.DebugLog($"[BoxPatches] AddProduct skipped storing date because product came from DisplaySlot for box {instanceId}");
                        return;
                    }
                }
                catch { /* ignoruj błędy transformów */ }

                // 1) jeśli produkt ma komponent z datą, użyj jej
                var comp = item.GetComponent<ProductExpirationComponent>();
                int dateToStore = -1;
                if (comp != null)
                {
                    dateToStore = comp.ExpirationDay;
                    ExpirationSaveManager.runtimeBoxConfigVersion[instanceId] = -1;
                }

                // 2) jeśli brak komponentu, spróbuj pobrać z clipboardu (schowka)
                if (dateToStore == -1)
                {
                    if (BoxLabelPatch.TryDequeueClipboardDate(out int queuedDate))
                    {
                        dateToStore = queuedDate;
                        ExpirationSaveManager.runtimeBoxConfigVersion[instanceId] = -1;
                    }
                }

                // 3) jeśli dalej brak daty, spróbuj użyć override z configu (preferujemy config nad kalkulatorem)
                if (dateToStore == -1)
                {
                    // wymuś załadowanie najnowszego configu (na wypadek, gdyby zmiana była tuż przed zakupem)
                    try { CustomExpirationLoader.Load(); } catch { }

                    int prodId = -1;
                    try
                    {
                        prodId = ExpirationSaveManager.GetProductIdFromProduct(item);
                    }
                    catch { prodId = -1; }

                    // jeśli nadal 0, spróbuj pobrać productId z Box (czasem prefab nie ma Data, ale Box ma info)
                    if (prodId <= 0)
                    {
                        try
                        {
                            var boxDataProp = __instance.GetType().GetProperty("Data", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                            if (boxDataProp != null)
                            {
                                var dataObj = boxDataProp.GetValue(__instance);
                                if (dataObj != null)
                                {
                                    var idProp = dataObj.GetType().GetProperty("ProductID") ?? dataObj.GetType().GetProperty("ID") ?? dataObj.GetType().GetProperty("Uid") ?? dataObj.GetType().GetProperty("UID");
                                    if (idProp != null)
                                    {
                                        var val = idProp.GetValue(dataObj);
                                        if (val is int) prodId = (int)val;
                                    }
                                }
                            }
                        }
                        catch { prodId = -1; }
                    }

                    if (prodId > 0)
                    {
                        int overrideDays = BoxLabelPatch.GetConfigOverrideDirectly(prodId);
                        if (overrideDays != -1)
                        {
                            int deliveryDay = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;
                            dateToStore = deliveryDay + overrideDays;
                            ExpirationSaveManager.runtimeBoxConfigVersion[instanceId] = CustomExpirationLoader.ConfigVersion;
                            StatisticMod.Plugin.DebugLog($"[BoxPatches] Using config override for product {prodId}: {overrideDays} -> expDay {dateToStore} for box {instanceId} (configVer={CustomExpirationLoader.ConfigVersion})");
                        }
                        else
                        {
                            StatisticMod.Plugin.DebugLog($"[BoxPatches] No override found for prodId={prodId} (checked after reload). Will fallback to calculator.");
                        }
                    }
                    else
                    {
                        StatisticMod.Plugin.DebugLog($"[BoxPatches] Could not determine productId for item (instanceId={instanceId}). Will fallback to calculator.");
                    }
                }

                // 4) fallback: jeśli dalej brak daty, użyj kalkulatora (stare zachowanie)
                if (dateToStore == -1)
                {
                    int prodId = -1;
                    try { prodId = ExpirationSaveManager.GetProductIdFromProduct(item); } catch { prodId = -1; }
                    int shelfLife = ExpirationCalculator.GetDaysForProduct(null, prodId);
                    int day = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;
                    dateToStore = day + shelfLife;
                    ExpirationSaveManager.runtimeBoxConfigVersion[instanceId] = -1;
                    StatisticMod.Plugin.DebugLog($"[BoxPatches] Fallback calculator used for box {instanceId}: prod={prodId}, shelfLife={shelfLife}, expDay={dateToStore}");
                }

                // Zapisz datę do runtimeBoxDates
                if (dateToStore != -1)
                {
                    if (!ExpirationSaveManager.runtimeBoxDates.ContainsKey(instanceId))
                        ExpirationSaveManager.runtimeBoxDates[instanceId] = new List<int>();

                    ExpirationSaveManager.runtimeBoxDates[instanceId].Add(dateToStore);

                    if (!ExpirationSaveManager.runtimeBoxDeliveryDays.ContainsKey(instanceId))
                        ExpirationSaveManager.runtimeBoxDeliveryDays[instanceId] = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;

                    if (!ExpirationSaveManager.runtimeBoxDatesFromSave.ContainsKey(instanceId))
                        ExpirationSaveManager.runtimeBoxDatesFromSave[instanceId] = false;

                    StatisticMod.Plugin.DebugLog($"[BoxPatches] Stored date {dateToStore} into runtimeBoxDates for box {instanceId} (configVer={ExpirationSaveManager.runtimeBoxConfigVersion.GetValueOrDefault(instanceId, -1)})");
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

                if (ExpirationSaveManager.runtimeBoxDates.TryGetValue(instanceId, out List<int> dates) && dates.Count > 0)
                {
                    int dateForProduct = dates[0];
                    dates.RemoveAt(0);

                    BoxLabelPatch.EnqueueClipboardDate(dateForProduct);
                    StatisticMod.Plugin.DebugLog($"[BoxPatches] Enqueued clipboard date from box {instanceId}: {dateForProduct}");

                    if (dates.Count == 0)
                    {
                        ExpirationSaveManager.runtimeBoxDates.Remove(instanceId);
                        if (ExpirationSaveManager.runtimeBoxDeliveryDays.ContainsKey(instanceId))
                            ExpirationSaveManager.runtimeBoxDeliveryDays.Remove(instanceId);
                        if (ExpirationSaveManager.runtimeBoxDatesFromSave.ContainsKey(instanceId))
                            ExpirationSaveManager.runtimeBoxDatesFromSave.Remove(instanceId);
                        if (ExpirationSaveManager.runtimeBoxConfigVersion.ContainsKey(instanceId))
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
