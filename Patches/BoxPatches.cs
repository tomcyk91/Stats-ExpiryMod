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
                if (__instance == null || item == null)
                    return;

                int boxKey = __instance.GetInstanceID();

                int currentCount = 0;
                try
                {
                    currentCount = __instance.ProductCount;
                }
                catch { }

                if (currentCount < 0)
                    currentCount = 0;

                int productId = productID;

                if (productId <= 0)
                {
                    try
                    {
                        productId =
                            ExpirationSaveManager.GetProductIdFromProduct(item);
                    }
                    catch
                    {
                        productId = -1;
                    }
                }

                if (productId <= 0 &&
                    __instance.Data != null)
                {
                    try
                    {
                        productId = __instance.Data.ProductID;
                    }
                    catch
                    {
                        productId = -1;
                    }
                }

                int stableUid =
                    ExpirationSaveManager.GetStableBoxUid(__instance);

                int dateToStore = -1;
                bool restoredFromSave = false;

                // ======================================================
                // 1. Produkt już ma własny termin.
                // Normalny transfer shelf -> box.
                // ======================================================
                var comp =
                    item.GetComponent<ProductExpirationComponent>();

                if (comp != null)
                {
                    dateToStore = comp.ExpirationDay;

                    ExpirationSaveManager
                        .runtimeBoxConfigVersion[boxKey] = -1;

                    StatisticMod.Plugin.DebugLog(
                        $"[BoxPatches] Preserved exact product date " +
                        $"{dateToStore} while adding product {productId} " +
                        $"to box {boxKey}");
                }

                // ======================================================
                // 2. Wczytywanie istniejącego kartonu z PBOX2.
                //
                // Box.AddProduct może zostać wywołany podczas rekonstrukcji
                // kartonów ZANIM BoxExpirationLabel.Start/Update zdąży
                // zastosować zapis. Dlatego PBOX2 obsługujemy już tutaj.
                // ======================================================
                if (dateToStore < 0 &&
                    stableUid > 0 &&
                    ExpirationSaveManager
                        .pendingLoadedBoxesByUid
                        .TryGetValue(
                            stableUid,
                            out SavedBoxData saved) &&
                    saved != null &&
                    saved.Dates != null &&
                    saved.Dates.Count > 0 &&
                    (saved.ProductId <= 0 ||
                     saved.ProductId == productId))
                {
                    if (!ExpirationSaveManager
                            .runtimeBoxDatesFromSave
                            .TryGetValue(
                                boxKey,
                                out bool alreadyFromSave) ||
                        !alreadyFromSave)
                    {
                        ExpirationSaveManager
                            .runtimeBoxDates[boxKey] =
                            new List<int>(saved.Dates);

                        int savedDeliveryDay =
                            saved.DeliveryDay > 0
                                ? saved.DeliveryDay
                                : 1;

                        ExpirationSaveManager
                            .runtimeBoxDeliveryDays[boxKey] =
                            savedDeliveryDay;

                        ExpirationSaveManager
                            .runtimeBoxDatesFromSave[boxKey] =
                            true;

                        ExpirationSaveManager
                            .boxDates[boxKey] =
                            new List<int>(saved.Dates);

                        ExpirationSaveManager
                            .boxDeliveryDays[boxKey] =
                            savedDeliveryDay;

                        ExpirationSaveManager
                            .boxDates[stableUid] =
                            new List<int>(saved.Dates);

                        ExpirationSaveManager
                            .boxDeliveryDays[stableUid] =
                            savedDeliveryDay;

                        StatisticMod.Plugin.DebugLog(
                            $"[BoxPatches] Preloaded PBOX2 during Box.AddProduct. " +
                            $"uid={stableUid}, product={productId}, " +
                            $"deliveryDay={savedDeliveryDay}, " +
                            $"dates={saved.Dates.Count}");
                    }

                    if (currentCount < saved.Dates.Count)
                    {
                        dateToStore =
                            saved.Dates[currentCount];

                        restoredFromSave = true;
                    }
                }

                // ======================================================
                // 3. Runtime został już odtworzony z sejwa.
                // Używamy dokładnej daty dla indeksu ładowanego produktu.
                // ======================================================
                if (dateToStore < 0 &&
                    ExpirationSaveManager
                        .runtimeBoxDatesFromSave
                        .TryGetValue(
                            boxKey,
                            out bool fromSavedRuntime) &&
                    fromSavedRuntime &&
                    ExpirationSaveManager
                        .runtimeBoxDates
                        .TryGetValue(
                            boxKey,
                            out List<int> savedRuntimeDates) &&
                    savedRuntimeDates != null &&
                    currentCount < savedRuntimeDates.Count)
                {
                    dateToStore =
                        savedRuntimeDates[currentCount];

                    restoredFromSave = true;
                }

                // ======================================================
                // 4. Naprawdę nowy produkt.
                // Zawsze deterministycznie CurrentDay + shelfLife.
                // ======================================================
                if (dateToStore < 0)
                {
                    try
                    {
                        CustomExpirationLoader.Load();
                    }
                    catch { }

                    int shelfLife = -1;

                    if (productId > 0)
                    {
                        int overrideDays =
                            BoxLabelPatch.GetConfigOverrideDirectly(productId);

                        if (overrideDays >= 0)
                        {
                            shelfLife = overrideDays;

                            ExpirationSaveManager
                                .runtimeBoxConfigVersion[boxKey] =
                                CustomExpirationLoader.ConfigVersion;
                        }
                    }

                    if (shelfLife < 0)
                    {
                        shelfLife =
                            ExpirationCalculator.GetDaysForProduct(
                                null,
                                productId);

                        ExpirationSaveManager
                            .runtimeBoxConfigVersion[boxKey] = -1;
                    }

                    var dcm =
                        DayCycleManager.HasInstance
                            ? DayCycleManager.Instance
                            : null;

                    int currentDay =
                        dcm != null && dcm.CurrentDay > 0
                            ? dcm.CurrentDay
                            : 1;

                    dateToStore =
                        currentDay + shelfLife;

                    StatisticMod.Plugin.DebugLog(
                        $"[BoxPatches] Assigned fresh deterministic date. " +
                        $"product={productId}, currentDay={currentDay}, " +
                        $"shelfLife={shelfLife}, expDay={dateToStore}, " +
                        $"box={boxKey}");
                }

                if (dateToStore < 0)
                    return;

                // Każdy fizyczny Product dostaje swój dokładny komponent.
                if (comp == null)
                {
                    comp =
                        item.gameObject
                            .AddComponent<ProductExpirationComponent>();

                    comp.hideFlags =
                        HideFlags.DontSave |
                        HideFlags.HideInInspector;
                }

                comp.ProductID = productId;
                comp.ExpirationDay = dateToStore;

                if (!ExpirationSaveManager
                        .runtimeBoxDates
                        .TryGetValue(
                            boxKey,
                            out List<int> dates) ||
                    dates == null)
                {
                    dates = new List<int>();

                    ExpirationSaveManager
                        .runtimeBoxDates[boxKey] = dates;
                }

                if (dates.Count > currentCount)
                {
                    dates[currentCount] =
                        dateToStore;
                }
                else
                {
                    if (dates.Count < currentCount)
                    {
                        // Fail-soft. Nie powinno wystąpić w normalnej ścieżce.
                        int fallbackDate =
                            dateToStore;

                        try
                        {
                            int fallbackShelfLife =
                                BoxLabelPatch.GetConfigOverrideDirectly(
                                    productId);

                            if (fallbackShelfLife < 0)
                            {
                                fallbackShelfLife =
                                    ExpirationCalculator.GetDaysForProduct(
                                        null,
                                        productId);
                            }

                            var fallbackDcm =
                                DayCycleManager.HasInstance
                                    ? DayCycleManager.Instance
                                    : null;

                            int fallbackDay =
                                fallbackDcm != null &&
                                fallbackDcm.CurrentDay > 0
                                    ? fallbackDcm.CurrentDay
                                    : 1;

                            fallbackDate =
                                fallbackDay + fallbackShelfLife;
                        }
                        catch { }

                        while (dates.Count < currentCount)
                            dates.Add(fallbackDate);
                    }

                    dates.Add(dateToStore);
                }

                var dayManager =
                    DayCycleManager.HasInstance
                        ? DayCycleManager.Instance
                        : null;

                int today =
                    dayManager != null &&
                    dayManager.CurrentDay > 0
                        ? dayManager.CurrentDay
                        : 1;

                bool runtimeIsFromSave =
                    ExpirationSaveManager
                        .runtimeBoxDatesFromSave
                        .TryGetValue(
                            boxKey,
                            out bool savedFlag) &&
                    savedFlag;

                if (restoredFromSave || runtimeIsFromSave)
                {
                    // Niczego nie nadpisujemy.
                    // runtimeBoxDeliveryDays został ustawiony z PBOX2.
                }
                else if (currentCount == 0)
                {
                    // Pierwszy produkt w naprawdę nowym / ponownie
                    // napełnianym pustym kartonie = nowa dostawa.
                    ExpirationSaveManager
                        .runtimeBoxDeliveryDays[boxKey] =
                        today;

                    ExpirationSaveManager
                        .runtimeBoxDatesFromSave[boxKey] =
                        false;

                    ExpirationSaveManager
                        .boxDeliveryDays[boxKey] =
                        today;

                    if (stableUid > 0)
                    {
                        ExpirationSaveManager
                            .boxDeliveryDays[stableUid] =
                            today;
                    }
                }
                else
                {
                    if (!ExpirationSaveManager
                            .runtimeBoxDeliveryDays
                            .ContainsKey(boxKey))
                    {
                        ExpirationSaveManager
                            .runtimeBoxDeliveryDays[boxKey] =
                            today;
                    }

                    if (!ExpirationSaveManager
                            .runtimeBoxDatesFromSave
                            .ContainsKey(boxKey))
                    {
                        ExpirationSaveManager
                            .runtimeBoxDatesFromSave[boxKey] =
                            false;
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugLog(
                    $"[BoxPatches] AddProduct_Prefix error: {ex.Message}");
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
                    // Bardzo ważne:
                    // jeśli gra zwróci null, NIE zużywamy terminu z listy.
                    return;
                }

                int boxKey =
                    __instance.GetInstanceID();

                if (!ExpirationSaveManager
                        .runtimeBoxDates
                        .TryGetValue(
                            boxKey,
                            out List<int> dates) ||
                    dates == null ||
                    dates.Count <= 0)
                {
                    return;
                }

                // Zachowujemy tę samą semantykę kolejki co wcześniej,
                // ale datę przypisujemy bezpośrednio do zwróconego Product.
                int exactDate =
                    dates[0];

                dates.RemoveAt(0);

                var comp =
                    __result.GetComponent<ProductExpirationComponent>();

                if (comp == null)
                {
                    comp =
                        __result.gameObject
                            .AddComponent<ProductExpirationComponent>();

                    comp.hideFlags =
                        HideFlags.DontSave |
                        HideFlags.HideInInspector;
                }

                int productId = 0;

                try
                {
                    productId =
                        ExpirationSaveManager
                            .GetProductIdFromProduct(__result);
                }
                catch { }

                if (productId <= 0 &&
                    __instance.Data != null)
                {
                    try
                    {
                        productId =
                            __instance.Data.ProductID;
                    }
                    catch { }
                }

                comp.ProductID =
                    productId;

                comp.ExpirationDay =
                    exactDate;

                StatisticMod.Plugin.DebugLog(
                    $"[BoxPatches] Attached exact box date {exactDate} " +
                    $"to returned product {productId} from box {boxKey}");

                // Czyścimy tylko pustą listę runtime.
                // Sam Product niesie już datę do półki.
                if (dates.Count == 0)
                {
                    ExpirationSaveManager
                        .runtimeBoxDates
                        .Remove(boxKey);

                    ExpirationSaveManager
                        .runtimeBoxDeliveryDays
                        .Remove(boxKey);

                    ExpirationSaveManager
                        .runtimeBoxDatesFromSave
                        .Remove(boxKey);

                    ExpirationSaveManager
                        .runtimeBoxConfigVersion
                        .Remove(boxKey);

                    // Karton jest pusty. Trwały cache poprzedniej dostawy
                    // nie może zostać użyty, gdy karton zostanie później
                    // napełniony nowym towarem.
                    int stableUid =
                        ExpirationSaveManager
                            .GetStableBoxUid(__instance);

                    if (stableUid > 0)
                    {
                        ExpirationSaveManager
                            .boxDates
                            .Remove(stableUid);

                        ExpirationSaveManager
                            .boxDeliveryDays
                            .Remove(stableUid);
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugLog(
                    $"[BoxPatches] GetProductFromBox_Postfix error: {ex.Message}");
            }
        }
    }
}
