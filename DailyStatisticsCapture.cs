using System;

namespace StatisticMod
{
    /// <summary>
    /// Przechwytuje dokładnie te dane, które gra przekazuje do ekranu
    /// „Podsumowanie dnia”. Prefix działa zanim ekran lub następny dzień
    /// zdążą wyzerować DailyStatisticsData.
    /// </summary>
    public static class DailyStatisticsScreen_ApplyStatistics_Patch
    {
        public static void Prefix(
            int __0, int __1, int __2, int __3, int __4,
            int __5, int __6, int __7,
            float __8, float __9, float __10, float __11,
            float __12, float __13, float __14, float __15,
            float __16, float __17, float __18, float __19)
        {
            try
            {
                // Pozycje parametrów metody DailyStatisticsScreen.ApplyStatistics:
                int day = __0;
                int satisfied = __1;
                int notFound = __2;
                int expensive = __3;
                int harmed = __4;
                int shortChange = __5;
                int totalCustomers = __6;
                int storePoint = __7;

                float checkoutIncome = __8;
                float supplyCosts = __9;
                float upgradeCosts = __10;
                float screenCustomizationCosts = __11;
                float rentCosts = __12;
                float billCosts = __13;
                float vendingIncome = __14;
                float loanIncome = __15;
                float loanPayment = __16;
                float staffPayment = __17;
                float dailyProfit = __18;
                float balance = __19;

                if (day < 1) return;

                DailyStatisticsData raw = null;
                try
                {
                    var manager = DailyStatisticsManager.HasInstance
                        ? DailyStatisticsManager.Instance
                        : null;

                    if (manager != null)
                        raw = manager.DailyStatisticsData;
                }
                catch { }

                var summary = new DailySummaryStats
                {
                    Day = day,
                    Captured = true,

                    SatisfiedCustomerCount = satisfied,
                    CouldntFindProduct = notFound,
                    ExpensiveProducts = expensive,
                    HarmedCustomerCount = harmed,
                    ShortChangeAmount = shortChange,
                    TotalCustomerCount = totalCustomers,
                    StorePoint = storePoint,

                    CheckoutIncome = checkoutIncome,
                    SupplyCosts = supplyCosts,
                    UpgradeCosts = upgradeCosts,

                    // Parametr ekranu może być sumą kilku kosztów personalizacji.
                    // Gdy dostępne są surowe dane managera, zapisujemy osobne pola,
                    // żeby nie podwajać PaintCosts/FloorBoxCosts w obliczeniach.
                    CustomizationCosts = raw != null
                        ? raw.CustomizationCosts
                        : screenCustomizationCosts,

                    BillCosts = billCosts,
                    VendingIncome = vendingIncome,
                    RentCosts = rentCosts,
                    LoanIncome = loanIncome,
                    LoanPayment = loanPayment,
                    StaffPayment = staffPayment,

                    PaintCosts = raw != null ? raw.PaintCosts : 0f,
                    FloorBoxCosts = raw != null ? raw.FloorBoxCosts : 0f,

                    DailyProfit = dailyProfit,
                    Balance = balance
                };

                DailySummaryStore.SetDay(summary);

                Plugin.Log?.LogInfo(
                    $"[DailySummary] Captured day={day}, customers={totalCustomers}, " +
                    $"income={checkoutIncome:0.00}, profit={dailyProfit:0.00}, " +
                    $"file={DailySummaryStore.AbsoluteFilePath}");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("[DailySummary] Capture failed: " + ex.Message);
            }
        }
    }
}
