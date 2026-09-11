using HarmonyLib;
using System;
using UnityEngine;

namespace SmartExpiration.Patches
{
    [HarmonyPatch(typeof(DisplaySlot))]
    internal static class DisplaySlotPatches
    {
        public static bool Prepare()
        {
            return AccessTools.Method(
                       typeof(DisplaySlot),
                       "TakeProductFromDisplay") != null &&
                   AccessTools.Method(
                       typeof(DisplaySlot),
                       "AddProduct") != null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(DisplaySlot.TakeProductFromDisplay))]
        private static void TakeProductFromDisplay_Prefix(
            DisplaySlot __instance)
        {
            try
            {
                if (__instance == null)
                    return;

                // FEFO:
                // gra natywnie zabiera ostatni Product z m_Products.
                // Przed pobraniem przenosimy na niego WYŁĄCZNIE najkrótszy
                // ExpirationDay. Nie zmieniamy kolejności obiektów półki.
                ExpirationManager.PrepareFefoProductForNativeTake(
                    __instance,
                    out _);

                // Nadal NIE używamy globalnego ClipboardDate.
                // Dokładny Product zwrócony przez grę niesie własny
                // ProductExpirationComponent.
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugLog(
                    $"[DisplaySlot] FEFO TakeProductFromDisplay_Prefix error: " +
                    $"{ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(DisplaySlot.TakeProductFromDisplay))]
        private static void TakeProductFromDisplay_Postfix(
            DisplaySlot __instance,
            global::Product __result)
        {
            try
            {
                // __result zachowuje ProductExpirationComponent.
                // Jeżeli gracz przenosi go do kartonu, Box.AddProduct
                // odczyta datę bezpośrednio z tego Product.
                ExpirationManager.RecordProductRemoved(__instance);

                LabelExclamationOverlay.QueueSlot(__instance);
            }
            catch { }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(DisplaySlot.AddProduct))]
        private static void AddProduct_Prefix(
            DisplaySlot __instance,
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

                // Nie używamy HeldBoxLabel ani ClipboardDate.
                //
                // Box.GetProductFromBox_Postfix przypina dokładny termin
                // bezpośrednio do item. Jeśli produkt pochodzi z innego
                // źródła i nie ma komponentu, RecordProductAdded/EnsureExpiration
                // nada mu świeży deterministiczny termin z cfg.
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError(
                    $"[SmartExpiration] AddProduct_Prefix: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(DisplaySlot.AddProduct))]
        private static void AddProduct_Postfix(
            DisplaySlot __instance,
            int productID,
            global::Product item)
        {
            try
            {
                // Używamy dokładnego argumentu Product przekazanego do gry,
                // zamiast zgadywać przez "ostatni produkt na półce".
                ExpirationManager.RecordProductAdded(
                    __instance,
                    item);

                LabelExclamationOverlay.QueueSlot(__instance);
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError(
                    $"[SmartExpiration] Błąd AddProduct_Postfix: " +
                    $"{ex.Message}");
            }
        }
    }
}
