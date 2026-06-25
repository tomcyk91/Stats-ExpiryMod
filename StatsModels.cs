using System.Collections.Generic;

namespace StatisticMod
{
    public class StatsData
    {
        public List<DayStats> Days = new();
    }

    public class DayStats
    {
        public int Day;

        public int SoldUnits;
        public float SoldWeightKg;
        public float SoldRevenue;

        public int ThrownUnits;
        public float ThrownWeightKg;
        public float ThrownValue;

        public List<ProductLine> Products = new();
    }

    public class ProductLine
    {
        public int ProductId;

        public int SoldUnits;
        public float SoldWeightKg;
        public float SoldRevenue;

        public int ThrownUnits;
        public float ThrownWeightKg;
        public float ThrownValue;
    }
}
