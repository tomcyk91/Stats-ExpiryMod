using System;
using System.Collections.Generic;
using UnityEngine;
using SmartExpiration;

namespace StatisticMod
{
    public static class BusinessAnalysisService
    {
        public static List<ProductBusinessAnalysisRow> BuildRows(int rangeDays)
        {
            if (rangeDays < 1) rangeDays = 7;

            int currentDay = GetCurrentDay();
            BusinessAnalysisStore.SetCurrentDay(currentDay);

            // Wyłącznie zakończone dni. OpenDay nigdy nie trafia do agregacji.
            int lastClosedDay = currentDay - 1;
            if (lastClosedDay < 1)
                return new List<ProductBusinessAnalysisRow>();

            int startDay = Mathf.Max(1, lastClosedDay - rangeDays + 1);
            int actualDays = lastClosedDay - startDay + 1;

            var map = new Dictionary<int, ProductBusinessAnalysisRow>();
            AggregateClosedDays(map, startDay, lastClosedDay);

            StockSnapshot stock = StockSnapshotService.Capture();
            foreach (var pair in map)
            {
                ProductBusinessAnalysisRow row = pair.Value;
                row.DaysInRange = actualDays;
                row.IsWeight = SalesUnifiedFinal.WeightPerUnit.TryGetValue(row.ProductId, out float kgPerUnit);
                row.KgPerUnit = row.IsWeight ? kgPerUnit : 0f;

                float requestedVisible = row.RequestedVisible;
                float pickedVisible = row.PickedVisible;
                float soldVisible = row.SoldVisible;
                float stockMissedVisible = row.StockMissedVisible;

                row.ServiceLevel = requestedVisible > 0.0001f
                    ? Mathf.Clamp01(pickedVisible / requestedVisible)
                    : 1f;

                row.SalesToDemandRate = requestedVisible > 0.0001f
                    ? soldVisible / requestedVisible
                    : (soldVisible > 0.0001f ? 1f : 0f);

                row.MissRate = requestedVisible > 0.0001f
                    ? Mathf.Clamp01(stockMissedVisible / requestedVisible)
                    : 0f;

                // Dla kanałów bez ShoppingList (np. online, zakupy gracza) sprzedaż
                // jest najlepszym dostępnym przybliżeniem popytu.
                float demandForAverage = requestedVisible > 0.0001f
                    ? requestedVisible
                    : soldVisible;
                row.AverageDailyDemandVisible = demandForAverage / Mathf.Max(1, actualDays);

                row.RecentAverageVisible = GetAverageDemand(
                    row.ProductId,
                    Mathf.Max(1, lastClosedDay - 2),
                    lastClosedDay,
                    row.IsWeight,
                    row.KgPerUnit);

                row.PreviousAverageVisible = GetAverageDemand(
                    row.ProductId,
                    Mathf.Max(1, lastClosedDay - 5),
                    Mathf.Max(1, lastClosedDay - 3),
                    row.IsWeight,
                    row.KgPerUnit);

                row.DemandTrend = CalculateTrend(row.RecentAverageVisible, row.PreviousAverageVisible);

                ProductStockState stockState = stock.Get(row.ProductId);
                row.ShopStockUnits = stockState.ShopUnits;
                row.WarehouseStockUnits = stockState.WarehouseUnits;
                row.TotalStockUnits = stockState.TotalUnits;

                if (row.ProductId == 9999)
                {
                    ApplyIceCreamStandRules(row);
                }
                else
                {
                    CalculateRestock(row, lastClosedDay);
                    CalculatePricing(row);
                }
            }

            return new List<ProductBusinessAnalysisRow>(map.Values);
        }

        private static void AggregateClosedDays(
            Dictionary<int, ProductBusinessAnalysisRow> map,
            int startDay,
            int endDay)
        {
            if (BusinessAnalysisStore.Data?.Days == null) return;

            for (int i = 0; i < BusinessAnalysisStore.Data.Days.Count; i++)
            {
                BusinessDayData day = BusinessAnalysisStore.Data.Days[i];
                if (day == null || day.Day < startDay || day.Day > endDay || day.Products == null)
                    continue;

                for (int p = 0; p < day.Products.Count; p++)
                {
                    BusinessProductLine line = day.Products[p];
                    if (line == null || line.ProductId <= 0) continue;

                    ProductBusinessAnalysisRow row = GetRow(map, line.ProductId);
                    row.RequestedUnits += line.RequestedUnits;
                    row.RequestedWeightKg += line.RequestedWeightKg;
                    row.PickedUnits += line.PickedUnits > 0 ? line.PickedUnits : line.FulfilledUnits;
                    row.PickedWeightKg += line.PickedWeightKg > 0f ? line.PickedWeightKg : line.FulfilledWeightKg;
                    row.MissedUnits += line.MissedUnits;
                    row.MissedWeightKg += line.MissedWeightKg;
                    row.MissedRevenue += line.MissedRevenue;
                    row.GlobalOutOfStockUnits += line.GlobalOutOfStockUnits;
                    row.ShelfEmptyUnits += line.ShelfEmptyUnits;
                    row.NotDisplayedUnits += line.NotDisplayedUnits;
                    row.OtherUnfulfilledUnits += line.OtherUnfulfilledUnits;
                    row.SoldUnits += line.SoldUnits;
                    row.SoldWeightKg += line.SoldWeightKg;
                    row.SoldRevenue += line.SoldRevenue;
                    if (line.WasDisplayed) row.OfferedDays++;
                }
            }
        }

        private static void ApplyIceCreamStandRules(ProductBusinessAnalysisRow row)
        {
            // Stoisko z lodami nie ma zwykłych półek, pudeł, kosztu zakupu ani Pricing.
            row.ShopStockUnits = 0;
            row.WarehouseStockUnits = 0;
            row.TotalStockUnits = 0;
            row.DaysOfCover = 999f;
            row.TransferToShelfUnits = 0;
            row.RecommendedOrderUnits = 0;
            row.RecommendedBoxes = 0;
            row.UnitsPerBox = 1;
            row.ExpirationRiskUnits = 0;

            float averagePrice = row.SoldUnits > 0
                ? row.SoldRevenue / row.SoldUnits
                : 0f;

            row.CurrentCost = 0f;
            row.CurrentPrice = RoundPrice(averagePrice);
            row.MarketPrice = row.CurrentPrice;
            row.SuggestedPrice = row.CurrentPrice;
            row.PricingAdvice = PricingAdviceType.Keep;
            row.PricingConfidence = row.SoldUnits > 0
                ? Mathf.Clamp01(row.SoldUnits / 25f)
                : 0f;
        }

        private static ProductBusinessAnalysisRow GetRow(
            Dictionary<int, ProductBusinessAnalysisRow> map,
            int productId)
        {
            if (!map.TryGetValue(productId, out ProductBusinessAnalysisRow row))
            {
                row = new ProductBusinessAnalysisRow { ProductId = productId };
                map[productId] = row;
            }
            return row;
        }

        private static float GetAverageDemand(
            int productId,
            int fromDay,
            int toDay,
            bool isWeight,
            float kgPerUnit)
        {
            if (toDay < fromDay) return 0f;
            int countDays = toDay - fromDay + 1;
            float sum = 0f;

            for (int dayNumber = fromDay; dayNumber <= toDay; dayNumber++)
            {
                BusinessDayData day = BusinessAnalysisStore.TryGetDay(dayNumber);
                if (day?.Products == null) continue;

                for (int i = 0; i < day.Products.Count; i++)
                {
                    BusinessProductLine line = day.Products[i];
                    if (line == null || line.ProductId != productId) continue;

                    float requested = isWeight ? line.RequestedWeightKg : line.RequestedUnits;
                    float sold = isWeight ? line.SoldWeightKg : line.SoldUnits;
                    sum += requested > 0.0001f ? requested : sold;
                    break;
                }
            }

            return sum / Mathf.Max(1, countDays);
        }

        private static int GetCurrentDay()
        {
            int currentDay = 1;
            try
            {
                var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                if (dcm != null) currentDay = dcm.CurrentDay;
                else if (StatsStore.CurrentDay > 0) currentDay = StatsStore.CurrentDay;
            }
            catch { }
            return Mathf.Max(1, currentDay);
        }

        private static float CalculateTrend(float recent, float previous)
        {
            if (previous <= 0.0001f)
                return recent > 0.0001f ? 1f : 0f;
            return (recent - previous) / previous;
        }

        private static void CalculateRestock(ProductBusinessAnalysisRow row, int analysisEndDay)
        {
            float recent = row.RecentAverageVisible;
            float longAverage = GetAverageDemand(row.ProductId, Mathf.Max(1, analysisEndDay - 13), analysisEndDay, row.IsWeight, row.KgPerUnit);
            float forecastVisible = recent > 0.0001f
                ? recent * 0.65f + longAverage * 0.35f
                : longAverage;

            if (forecastVisible <= 0.0001f) forecastVisible = row.AverageDailyDemandVisible;

            float forecastUnits = row.IsWeight && row.KgPerUnit > 0f
                ? forecastVisible / row.KgPerUnit
                : forecastVisible;

            row.DaysOfCover = forecastUnits > 0.0001f ? row.TotalStockUnits / forecastUnits : 999f;

            int desiredShelfUnits = Mathf.CeilToInt(forecastUnits * 1.5f);
            if (desiredShelfUnits < 1 && forecastUnits > 0f) desiredShelfUnits = 1;
            int shelfGap = Mathf.Max(0, desiredShelfUnits - row.ShopStockUnits);
            row.TransferToShelfUnits = Mathf.Min(row.WarehouseStockUnits, shelfGap);

            float targetUnits = forecastUnits * 4f; // 1 dzień dostawy + 2 dni pokrycia + 1 dzień zapasu bezpieczeństwa

            int shelfLifeDays = 0;
            try { shelfLifeDays = ExpirationCalculator.GetDaysForProduct(null, row.ProductId); } catch { }
            if (shelfLifeDays > 0 && shelfLifeDays < 365 && forecastUnits > 0f)
            {
                float safeUnits = forecastUnits * shelfLifeDays * 0.75f;
                if (targetUnits > safeUnits) targetUnits = safeUnits;
            }

            row.RecommendedOrderUnits = Mathf.Max(0, Mathf.CeilToInt(targetUnits - row.TotalStockUnits));

            int unitsPerBox = 1;
            ProductSO so = null;
            try
            {
                if (Plugin.ProductCache != null && Plugin.ProductCache.TryGetSO(row.ProductId, out so) && so != null)
                    unitsPerBox = Mathf.Max(1, so.ProductAmountOnPurchase);
            }
            catch { }

            row.UnitsPerBox = unitsPerBox;
            row.RecommendedBoxes = row.RecommendedOrderUnits > 0
                ? Mathf.CeilToInt(row.RecommendedOrderUnits / (float)unitsPerBox)
                : 0;

            if (shelfLifeDays > 0 && shelfLifeDays < 365 && forecastUnits > 0f)
            {
                int safeTotal = Mathf.FloorToInt(forecastUnits * shelfLifeDays * 0.75f);
                row.ExpirationRiskUnits = Mathf.Max(0, row.TotalStockUnits + row.RecommendedOrderUnits - safeTotal);
            }
        }

        private static void CalculatePricing(ProductBusinessAnalysisRow row)
        {
            PriceManager pm = null;
            try { pm = PriceManager.HasInstance ? PriceManager.Instance : null; } catch { }
            if (pm == null) return;

            try { row.CurrentCost = pm.CurrentCost(row.ProductId); } catch { }
            try { row.CurrentPrice = pm.SellingPrice(row.ProductId); } catch { }

            Pricing pricing = null;
            try { pricing = pm.GetPrice(row.ProductId); } catch { }
            if (pricing == null)
            {
                try { pricing = pm.GetPriceSetByPlayer(row.ProductId); } catch { }
            }

            if (pricing != null)
            {
                try { row.MarketPrice = pricing.MarketPrice; } catch { }
                try
                {
                    if (row.CurrentPrice <= 0f) row.CurrentPrice = pricing.SellingPrice;
                }
                catch { }
            }

            if (row.MarketPrice <= 0f)
            {
                ProductSO so = null;
                try
                {
                    if (Plugin.ProductCache != null && Plugin.ProductCache.TryGetSO(row.ProductId, out so) && so != null)
                        row.MarketPrice = RoundPrice(row.CurrentCost * (1f + so.OptimumProfitRate / 100f));
                }
                catch { }
            }

            float suggestion = row.MarketPrice > 0f ? row.MarketPrice : row.CurrentPrice;
            PricingAdviceType advice = PricingAdviceType.Keep;

            if (row.MissRate >= 0.15f || row.DaysOfCover < 1f)
            {
                advice = PricingAdviceType.RestockFirst;
            }
            else if (row.DemandTrend >= 0.15f && row.ServiceLevel >= 0.95f && row.DaysOfCover >= 2f && row.DaysOfCover <= 5f)
            {
                suggestion *= 1.03f;
                advice = PricingAdviceType.RaiseSlightly;
            }
            else if (row.DemandTrend <= -0.15f && row.DaysOfCover > 5f)
            {
                suggestion *= 0.95f;
                advice = PricingAdviceType.LowerSlightly;
            }

            float minimum = row.CurrentCost > 0f ? row.CurrentCost * 1.05f : 0f;
            float maximum = 0f;
            try
            {
                if (Plugin.ProductCache != null && Plugin.ProductCache.TryGetSO(row.ProductId, out ProductSO so) && so != null && so.MaxProfitRate > 0f)
                    maximum = row.CurrentCost * (1f + so.MaxProfitRate / 100f);
            }
            catch { }
            if (maximum <= minimum) maximum = Mathf.Max(minimum, suggestion * 1.20f);

            row.SuggestedPrice = RoundPrice(Mathf.Clamp(suggestion, minimum, maximum));
            row.PricingAdvice = advice;

            float evidence = Mathf.Max(row.RequestedVisible, row.SoldVisible);
            float dayFactor = Mathf.Clamp01(row.DaysInRange / 7f);
            float amountFactor = Mathf.Clamp01(evidence / 25f);
            row.PricingConfidence = dayFactor * 0.4f + amountFactor * 0.6f;
        }

        private static float RoundPrice(float value) => Mathf.Round(value * 100f) / 100f;
    }
}
