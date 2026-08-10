using System.Collections.Generic;

namespace StatisticMod
{
    public enum MissReason
    {
        None = 0,
        GlobalOutOfStock = 1,
        ShelfEmpty = 2,
        NotDisplayed = 3,
        Other = 4
    }

    public enum PricingAdviceType
    {
        Keep = 0,
        RaiseSlightly = 1,
        LowerSlightly = 2,
        RestockFirst = 3
    }

    public sealed class BusinessAnalysisData
    {
        public int SchemaVersion = 2;

        // Tylko zakończone dni. To właśnie tę listę czyta ekran ANALIZA.
        public List<BusinessDayData> Days = new();

        // Bufor trwającego dnia. Jest zapisywany, aby nie tracić danych po
        // wyjściu z gry, ale nie jest traktowany jako zakończona historia.
        public BusinessDayData OpenDay;
    }

    public sealed class BusinessDayData
    {
        public int Day;
        public int CustomerVisits;
        public int FullySatisfiedCustomers;
        public int CustomersWithMissingProducts;
        public float PotentialRevenueLost;
        public List<BusinessProductLine> Products = new();
    }

    public sealed class BusinessProductLine
    {
        public int ProductId;

        // Klient chciał kupić.
        public int RequestedUnits;
        public float RequestedWeightKg;

        // Klient faktycznie zebrał do koszyka.
        public int PickedUnits;
        public float PickedWeightKg;

        // Pola zgodności ze schematem v1. Po migracji zawsze są równe Picked*.
        public int FulfilledUnits;
        public float FulfilledWeightKg;

        // Sprzedaż potwierdzona przez główny StatsStore.
        public int SoldUnits;
        public float SoldWeightKg;
        public float SoldRevenue;

        // Niezrealizowana część listy klienta.
        public int MissedUnits;
        public float MissedWeightKg;

        // Tylko realnie utracona sprzedaż wynikająca z braku/niewystawienia.
        // MissReason.Other nie zwiększa tej wartości.
        public float MissedRevenue;

        public int GlobalOutOfStockUnits;
        public int ShelfEmptyUnits;
        public int NotDisplayedUnits;
        public int OtherUnfulfilledUnits;

        public bool WasDisplayed;
    }

    internal sealed class DemandResultItem
    {
        public int ProductId;
        public int RequestedUnits;
        public int PickedUnits;
        public int MissedUnits;
        public float PriceAtRequest;
        public bool IsWeight;
        public float KgPerUnit;
        public bool WasDisplayed;
        public MissReason MissReason;
    }

    public sealed class ProductBusinessAnalysisRow
    {
        public int ProductId;
        public bool IsWeight;
        public float KgPerUnit;
        public int DaysInRange;
        public int OfferedDays;

        public int RequestedUnits;
        public float RequestedWeightKg;
        public int PickedUnits;
        public float PickedWeightKg;
        public int MissedUnits;
        public float MissedWeightKg;
        public float MissedRevenue;

        public int GlobalOutOfStockUnits;
        public int ShelfEmptyUnits;
        public int NotDisplayedUnits;
        public int OtherUnfulfilledUnits;

        public int SoldUnits;
        public float SoldWeightKg;
        public float SoldRevenue;

        public float AverageDailyDemandVisible;
        public float RecentAverageVisible;
        public float PreviousAverageVisible;
        public float DemandTrend;

        // Dostępność: zebrano / popyt.
        public float ServiceLevel;

        // Sprzedaż / popyt. Może przekroczyć 100%, jeśli produkt był także
        // sprzedawany kanałem bez ShoppingList, np. zamówieniem online.
        public float SalesToDemandRate;

        // Tylko braki magazynowe, bez OtherUnfulfilled.
        public float MissRate;

        public int ShopStockUnits;
        public int WarehouseStockUnits;
        public int TotalStockUnits;
        public float DaysOfCover;

        public int TransferToShelfUnits;
        public int RecommendedOrderUnits;
        public int RecommendedBoxes;
        public int UnitsPerBox;
        public int ExpirationRiskUnits;

        public float CurrentCost;
        public float CurrentPrice;
        public float MarketPrice;
        public float SuggestedPrice;
        public PricingAdviceType PricingAdvice;
        public float PricingConfidence;

        public float RequestedVisible => IsWeight ? RequestedWeightKg : RequestedUnits;
        public float PickedVisible => IsWeight ? PickedWeightKg : PickedUnits;
        public float FulfilledVisible => PickedVisible;
        public float MissedVisible => IsWeight ? MissedWeightKg : MissedUnits;
        public float SoldVisible => IsWeight ? SoldWeightKg : SoldUnits;
        public float StockMissedVisible => IsWeight
            ? (GlobalOutOfStockUnits + ShelfEmptyUnits + NotDisplayedUnits) * KgPerUnit
            : GlobalOutOfStockUnits + ShelfEmptyUnits + NotDisplayedUnits;
        public float OtherUnfulfilledVisible => IsWeight
            ? OtherUnfulfilledUnits * KgPerUnit
            : OtherUnfulfilledUnits;
        public float ShopStockVisible => IsWeight ? ShopStockUnits * KgPerUnit : ShopStockUnits;
        public float WarehouseStockVisible => IsWeight ? WarehouseStockUnits * KgPerUnit : WarehouseStockUnits;
        public float TotalStockVisible => IsWeight ? TotalStockUnits * KgPerUnit : TotalStockUnits;
    }
}
