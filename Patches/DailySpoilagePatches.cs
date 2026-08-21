using HarmonyLib;
using System.Collections.Generic;
using UnityEngine;
using StatisticMod;

namespace SmartExpiration.Patches
{
    [HarmonyPatch(typeof(DayCycleManager))]
    internal static class DailySpoilagePatches
    {
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(DayCycleManager), "FinishTheDay") != null;
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(DayCycleManager.FinishTheDay))]
        private static void FinishTheDay_Postfix()
        {
            int currentDay = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;
            int closedDay = currentDay > 1 ? currentDay - 1 : 1;

            int totalSpoiledCount = 0;
            Dictionary<int, int> spoiledProductsDaily = new Dictionary<int, int>();

            // A new display can have been bought during the day. One explicit full scan here
            // is acceptable and guarantees the nightly cleanup sees every current shelf.
            SmartExpiration.SceneSlotCache.InvalidateSlots();
            var allSlots = SmartExpiration.SceneSlotCache.GetSlots();

            if (allSlots != null)
            {
                for (int s = 0; s < allSlots.Length; s++)
                {
                    var slot = allSlots[s];
                    if (slot == null || !slot.HasProduct) continue;

                    int productId = slot.ProductID;
                    int removedFromSlot = 0;

                    // DisplaySlot can safely remove only the native last product. The helper
                    // swaps expiration metadata so that this last native product represents
                    // an actually expired unit before every TakeProductFromDisplay call.
                    while (slot.HasProduct &&
                           ExpirationManager.PrepareExpiredProductForNativeTake(slot, closedDay, out int expiredDate))
                    {
                        var poppedProduct = slot.TakeProductFromDisplay();
                        if (poppedProduct == null) break;

                        poppedProduct.transform.SetParent(null);
                        UnityEngine.Object.Destroy(poppedProduct.gameObject);
                        removedFromSlot++;
                        totalSpoiledCount++;
                    }

                    if (removedFromSlot > 0)
                    {
                        if (spoiledProductsDaily.ContainsKey(productId))
                            spoiledProductsDaily[productId] += removedFromSlot;
                        else
                            spoiledProductsDaily[productId] = removedFromSlot;

                        LabelExclamationOverlay.QueueSlot(slot);
                    }
                }
            }

            // Do not let a nightly internal removal leave a shelf->box clipboard value alive.
            BoxLabelPatch.ClipboardDate = -1;
            BoxLabelPatch.ClipboardFrame = -1;

            StatisticMod.Plugin.DebugLog($"[Nocne Sprzątanie] Wyrzucono łącznie {totalSpoiledCount} zepsutych produktów z półek po Dniu {closedDay}.");
            ExpirationSaveManager.SaveData();

            if (spoiledProductsDaily.Count > 0)
            {
                foreach (var kvp in spoiledProductsDaily)
                {
                    int pid = kvp.Key;
                    int count = kvp.Value;
                    float price = (PriceManager.Instance != null) ? PriceManager.Instance.SellingPrice(pid) : 0f;

                    if (SalesUnifiedFinal.WeightPerUnit != null && SalesUnifiedFinal.WeightPerUnit.TryGetValue(pid, out float weightPerUnit))
                    {
                        float kgSpoiled = count * weightPerUnit;
                        float lostValue = price * kgSpoiled;
                        StatsStore.AddThrownF(closedDay, pid, kgSpoiled, lostValue, true);
                    }
                    else
                    {
                        float lostValue = price * count;
                        StatsStore.AddThrown(closedDay, pid, count, lostValue);
                    }
                }

                StatsStore.SaveNow();
                StatisticMod.Plugin.DebugLog($"[Statystyki Strat] Pomyślnie zaktualizowano plik statystyk o straty z zakończonego dnia {closedDay}.");
            }

            if (totalSpoiledCount > 0 && StoreLevelManager.Instance != null)
            {
                StoreLevelManager.Instance.RemovePoint(totalSpoiledCount);
                StatisticMod.Plugin.DebugLog($"[Punkty Sklepu] Odjęto {totalSpoiledCount} punktów za zepsute produkty pozostawione na półkach na noc!");
            }
        }
    }
}
