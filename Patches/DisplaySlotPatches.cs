using HarmonyLib;
using System;
using System.Collections.Generic;
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
                // PERF: O(1) lookup from DisplaySlot.m_Products instead of
                // GetComponentsInChildren + Sort for every customer purchase.
                if (ExpirationManager.TryGetLastExpirationDay(__instance, out int expirationDay))
                {
                    BoxLabelPatch.EnqueueClipboardDate(expirationDay);
                    StatisticMod.Plugin.DebugLog($"[DisplaySlot] Enqueued date from shelf: {expirationDay}");
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
            try
            {
                // PERF: normally removes one integer from memory instead of rescanning
                // and reshuffling every product in the slot.
                ExpirationManager.RecordProductRemoved(__instance);
                LabelExclamationOverlay.QueueSlot(__instance);
            }
            catch { }
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(DisplaySlot.AddProduct))]
        private static void AddProduct_Prefix(DisplaySlot __instance)
        {
            try
            {
                // No shelf-wide SyncShelf here. Existing products are already tracked;
                // the exact newly added product is handled in the postfix.
                var heldLabel = BoxLabelPatch.HeldBoxLabel;

                if (heldLabel != null && heldLabel.BoxKey > 0)
                {
                    int boxKey = heldLabel.BoxKey;

                    if (ExpirationSaveManager.runtimeBoxDates.TryGetValue(boxKey, out List<int> dates) && dates != null && dates.Count > 0)
                    {
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
                // DisplaySlot appends the added item to its native m_Products list.
                // This removes two recursive scans/sorts from every restocker/player add.
                ExpirationManager.RecordProductAdded(__instance, ExpirationManager.GetLastProduct(__instance));
                LabelExclamationOverlay.QueueSlot(__instance);
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError($"[SmartExpiration] Błąd AddProduct_Postfix: {ex.Message}");
            }
        }
    }
}
