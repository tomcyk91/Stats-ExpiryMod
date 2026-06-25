using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace SmartExpiration.Patches
{
    [HarmonyPatch(typeof(DisplaySlot))]
    internal static class DisplaySlotPatches
    {

        public static bool Prepare()
        {
            return AccessTools.Method(typeof(DisplaySlot), "TakeProductFromDisplay") != null &&
                   AccessTools.Method(typeof(DisplaySlot), "AddProduct") != null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(DisplaySlot.TakeProductFromDisplay))]
        private static void TakeProductFromDisplay_Prefix(DisplaySlot __instance)
        {
            try
            {
                ExpirationManager.SyncShelf(__instance);

                var products = ExpirationSaveManager.GetSortedProducts(__instance.transform);
                if (products.Count > 0)
                {
                    var lastP = products.Last();
                    if (lastP != null)
                    {
                        var comp = lastP.GetComponent<ProductExpirationComponent>();
                        if (comp != null)
                        {
                            // Enqueue date from shelf so EnsureExpiration can apply it to the taken product.
                            // To avoid "leak" back into boxes, BoxPatches.AddProduct_Prefix ignores products coming from DisplaySlot.
                            BoxLabelPatch.EnqueueClipboardDate(comp.ExpirationDay);
                            StatisticMod.Plugin.DebugLog($"[DisplaySlot] Enqueued date from shelf: {comp.ExpirationDay}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugLog($"[DisplaySlot] TakeProductFromDisplay_Prefix error: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(DisplaySlot.TakeProductFromDisplay))]
        private static void TakeProductFromDisplay_Postfix(DisplaySlot __instance)
        {
            try { ExpirationManager.UpdateMemory(__instance); } catch { }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(DisplaySlot.AddProduct))]
        private static void AddProduct_Prefix(DisplaySlot __instance)
        {
            try
            {
                ExpirationManager.SyncShelf(__instance);

                var heldLabel = BoxLabelPatch.HeldBoxLabel;
                if (heldLabel != null && heldLabel.BoxKey > 0)
                {
                    int boxKey = heldLabel.BoxKey;

                    if (ExpirationSaveManager.runtimeBoxDates.TryGetValue(boxKey, out List<int> dates) && dates.Count > 0)
                    {
                        // zamiast ustawiać ClipboardDate bezpośrednio, enqueujemy datę z kartonu
                        int dateToUse = dates[0];
                        BoxLabelPatch.EnqueueClipboardDate(dateToUse);
                        StatisticMod.Plugin.DebugLog($"[DisplaySlot] Enqueued date from held box (boxKey={boxKey}): {dateToUse}");
                        dates.RemoveAt(0);
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError($"[SmartExpiration] Błąd AddProduct_Prefix: {ex.Message}");
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(DisplaySlot.AddProduct))]
        private static void AddProduct_Postfix(DisplaySlot __instance)
        {
            try
            {
                var products = ExpirationSaveManager.GetSortedProducts(__instance.transform);

                foreach (var p in products)
                {
                    if (p != null && p.GetComponent<ProductExpirationComponent>() == null)
                    {
                        ExpirationManager.EnsureExpiration(p, __instance);
                    }
                }

                ExpirationManager.UpdateMemory(__instance);
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError($"[SmartExpiration] Błąd AddProduct_Postfix: {ex.Message}");
            }
        }
    }
}
