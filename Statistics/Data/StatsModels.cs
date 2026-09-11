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

        // Profitability is recorded at the moment of sale so historical margins
        // do not change when wholesale costs change later.
        public float SoldCost;
        public int CostedUnits;
        public float CostedWeightKg;

        public int ThrownUnits;
        public float ThrownWeightKg;
        public float ThrownValue;

        public List<ProductLine> Products = new();
        public List<IceCreamSaleLine> IceCreamSales = new();
    }


    public class IceCreamSaleLine
    {
        public int ConeProductId;
        public int ToppingIndex;
        public int ScoopCount;
        public string Recipe = string.Empty;

        public int SoldCount;
        public float SoldRevenue;
        public float SoldCost;
        public int CostedCount;
    }

    public class ProductLine
    {
        public int ProductId;

        public int SoldUnits;
        public float SoldWeightKg;
        public float SoldRevenue;

        // Profitability is recorded at the moment of sale so historical margins
        // do not change when wholesale costs change later.
        public float SoldCost;
        public int CostedUnits;
        public float CostedWeightKg;

        public int ThrownUnits;
        public float ThrownWeightKg;
        public float ThrownValue;
    }
}
