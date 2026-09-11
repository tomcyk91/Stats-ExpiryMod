using System;

namespace StatisticMod
{
    /// <summary>
    /// Finalne dane ekranu podsumowania jednego zakończonego dnia.
    /// Osobny plik klasy pozwala zachować istniejący StatsModels.cs bez nadpisywania.
    /// </summary>
    public class DailySummaryStats
    {
        public int Day;
        public bool Captured;

        public int SatisfiedCustomerCount;
        public int CouldntFindProduct;
        public int ExpensiveProducts;
        public int ShortChangeAmount;
        public int HarmedCustomerCount;
        public int TotalCustomerCount;
        public int StorePoint;

        public float CheckoutIncome;
        public float SupplyCosts;
        public float UpgradeCosts;
        public float CustomizationCosts;
        public float BillCosts;
        public float VendingIncome;
        public float RentCosts;
        public float LoanIncome;
        public float LoanPayment;
        public float StaffPayment;
        public float PaintCosts;
        public float FloorBoxCosts;

        public float DailyProfit;
        public float Balance;

        public float TotalIncome => CheckoutIncome + VendingIncome + LoanIncome;

        public float TotalExpenses =>
            Math.Abs(SupplyCosts) +
            Math.Abs(UpgradeCosts) +
            Math.Abs(CustomizationCosts) +
            Math.Abs(BillCosts) +
            Math.Abs(RentCosts) +
            Math.Abs(LoanPayment) +
            Math.Abs(StaffPayment) +
            Math.Abs(PaintCosts) +
            Math.Abs(FloorBoxCosts);

        public float CalculatedProfit => TotalIncome - TotalExpenses;
    }
}
