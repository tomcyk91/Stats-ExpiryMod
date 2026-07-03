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
            // Pobieramy obecny dzień z gry
            int currentDay = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;

            // KRYTYCZNA ZMIANA: W Postfixie gra ma już podbity licznik na nowy poranek 
            // (czyli klikając koniec dnia 45, currentDay wynosi już 46).
            // Ustawiamy closedDay na dzień, który właśnie fizycznie minął (czyli 45).
            int closedDay = currentDay > 1 ? currentDay - 1 : 1;

            int totalSpoiledCount = 0;

            Dictionary<int, int> spoiledProductsDaily = new Dictionary<int, int>();

            // PERF: korzystamy ze wspolnego cache slotow zamiast kolejnego pelnego FindObjectsOfType.
            var allSlots = SmartExpiration.SceneSlotCache.GetSlots();

            if (allSlots != null)
            {
                for (int s = 0; s < allSlots.Length; s++)
                {
                    var slot = allSlots[s];
                    if (slot != null && slot.HasProduct)
                    {
                        ExpirationManager.SyncShelf(slot);
                        var productsOnShelf = slot.GetComponentsInChildren<global::Product>(true);

                        List<int> validDates = new List<int>();
                        int expiredCount = 0;

                        foreach (var p in productsOnShelf)
                        {
                            var comp = p.GetComponent<ProductExpirationComponent>();
                            if (comp != null)
                            {
                                // Sprawdzamy termin względem ZAMKNIĘTEGO DNIA (closedDay)
                                // Jeśli produkt psuł się w dniu 46, a zamykamy dzień 45 -> 46 <= 45 (Fałsz, produkt przeżywa na jutro).
                                // Jeśli produkt psuł się w dniu 45, a zamykamy dzień 45 -> 45 <= 45 (Prawda, ląduje w koszu).
                                if (comp.ExpirationDay <= closedDay)
                                    expiredCount++;
                                else
                                    validDates.Add(comp.ExpirationDay);
                            }
                        }

                        if (expiredCount > 0)
                        {
                            int productId = slot.ProductID;
                            if (spoiledProductsDaily.ContainsKey(productId))
                                spoiledProductsDaily[productId] += expiredCount;
                            else
                                spoiledProductsDaily[productId] = expiredCount;

                            for (int i = 0; i < expiredCount; i++)
                            {
                                var poppedProduct = slot.TakeProductFromDisplay();
                                if (poppedProduct != null)
                                {
                                    poppedProduct.transform.SetParent(null);
                                    UnityEngine.Object.Destroy(poppedProduct.gameObject);
                                    totalSpoiledCount++;
                                }
                            }

                            var remainingProducts = ExpirationSaveManager.GetSortedProducts(slot.transform);
                            for (int i = 0; i < remainingProducts.Count && i < validDates.Count; i++)
                            {
                                var rComp = ExpirationManager.EnsureExpiration(remainingProducts[i], slot);
                                if (rComp != null)
                                {
                                    rComp.ExpirationDay = validDates[i];
                                }
                            }

                            ExpirationManager.UpdateMemory(slot);
                        }
                    }
                }
            }

            StatisticMod.Plugin.DebugLog($"[Nocne Sprzątanie] Wyrzucono łącznie {totalSpoiledCount} zepsutych produktów z półek po Dniu {closedDay}.");
            ExpirationSaveManager.SaveData();

            // =========================================================
            // INTEGRACJA: WYSYŁANIE STRAT DO STATISTIC MOD
            // =========================================================
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

                        // Używamy zunifikowanego closedDay dla pewności
                        StatsStore.AddThrownF(closedDay, pid, kgSpoiled, lostValue, true);
                        StatisticMod.Plugin.DebugLog($"[Statystyki Strat] Wyrzucono (WAGA): PID {pid} | {kgSpoiled} kg | Strata: {lostValue} $ | Zapisano dla dnia: {closedDay}");
                    }
                    else
                    {
                        float lostValue = price * count;

                        StatsStore.AddThrownF(closedDay, pid, (float)count, lostValue, false);
                        StatisticMod.Plugin.DebugLog($"[Statystyki Strat] Wyrzucono (SZTUKI): PID {pid} | {count} szt. | Strata: {lostValue} $ | Zapisano dla dnia: {closedDay}");
                    }
                }

                StatsStore.SaveNow();
                StatisticMod.Plugin.DebugLog($"[Statystyki Strat] Pomyślnie zaktualizowano plik statystyk o straty z zakończonego dnia {closedDay}.");
            }

            // =========================================================
            // NOWOŚĆ: KARA PUNKTOWA ZA NIEPOSPRZĄTANIE SKLEPU
            // =========================================================
            if (totalSpoiledCount > 0 && StoreLevelManager.Instance != null)
            {
                StoreLevelManager.Instance.RemovePoint(totalSpoiledCount);
                StatisticMod.Plugin.DebugLog($"[Punkty Sklepu] Odjęto {totalSpoiledCount} punktów za zepsute produkty pozostawione na półkach na noc!");
            }
        }
    }
}