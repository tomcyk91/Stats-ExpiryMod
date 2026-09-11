using System;
using UnityEngine;

namespace StatisticMod
{
    /// <summary>
    /// Customer & Basket KPI view.
    /// Customer-service counters come from the game's DailyStatisticsData,
    /// while basket/transaction metrics are captured at completed checkout.
    /// </summary>
    public partial class StatsAppManager
    {
        private float _nextCustomerKpiRefresh;

        private sealed class CustomerKpiSnapshot
        {
            public int Day;
            public int Customers;
            public int Satisfied;
            public int CouldntFind;
            public int TooExpensive;
            public float CheckoutIncome;

            public int Transactions;
            public float BasketItems;
            public float BasketRevenue;
            public bool HasBasketData;
        }

        private void RefreshCustomerBasketLive()
        {
            if (_hubMode != HubMode.Customers) return;
            if (_selectedDay != GetCurrentDaySafe()) return;
            if (Time.realtimeSinceStartup < _nextCustomerKpiRefresh) return;

            _nextCustomerKpiRefresh = Time.realtimeSinceStartup + 0.75f;
            BuildCustomerBasketTiles();
        }

        private CustomerKpiSnapshot GetCustomerKpiSnapshot(int day)
        {
            var result = new CustomerKpiSnapshot { Day = Mathf.Max(1, day) };

            // Current day: use the live counters that the game itself updates.
            if (day == GetCurrentDaySafe())
            {
                try
                {
                    var manager = DailyStatisticsManager.HasInstance
                        ? DailyStatisticsManager.Instance
                        : null;
                    DailyStatisticsData live = manager != null
                        ? manager.DailyStatisticsData
                        : null;

                    if (live != null)
                    {
                        result.Customers = Math.Max(0, live.TotalCustomerCount);
                        result.Satisfied = Math.Max(0, live.SatisfiedCustomerCount);
                        result.CouldntFind = Math.Max(0, live.CouldntFindProduct);
                        result.TooExpensive = Math.Max(0, live.ExpensiveProducts);
                        result.CheckoutIncome = Mathf.Max(0f, live.CheckoutIncome);
                    }
                }
                catch { }
            }
            else
            {
                // Completed historical day: use the exact snapshot captured from
                // DailyStatisticsScreen.ApplyStatistics before the game clears it.
                DailySummaryStats summary = DailySummaryStore.TryGetDay(day);
                if (summary != null && summary.Captured)
                {
                    result.Customers = Math.Max(0, summary.TotalCustomerCount);
                    result.Satisfied = Math.Max(0, summary.SatisfiedCustomerCount);
                    result.CouldntFind = Math.Max(0, summary.CouldntFindProduct);
                    result.TooExpensive = Math.Max(0, summary.ExpensiveProducts);
                    result.CheckoutIncome = Mathf.Max(0f, summary.CheckoutIncome);
                }
            }

            CustomerBasketStore.DayData basket = CustomerBasketStore.TryGetDay(day);
            if (basket != null && basket.Transactions > 0)
            {
                result.Transactions = Math.Max(0, basket.Transactions);
                result.BasketItems = Mathf.Max(0f, basket.Items);
                result.BasketRevenue = Mathf.Max(0f, basket.CheckoutRevenue);
                result.HasBasketData = true;
            }

            return result;
        }

        private void BuildCustomerBasketTiles()
        {
            if (_tilesContent == null) return;

            ExitChartsLayout();
            ClearTilesOnly();
            EnsureSelectedDayInitialized();

            int currentDay = GetCurrentDaySafe();
            if (_selectedDay < 1) _selectedDay = currentDay;
            if (_selectedDay > currentDay) _selectedDay = currentDay;

            RebuildDaysUI();
            UpdateDayLabel();

            CustomerKpiSnapshot s = GetCustomerKpiSnapshot(_selectedDay);

            Color info = StatsAppTheme.Info;
            Color positive = StatsAppTheme.Positive;
            Color warning = StatsAppTheme.Warning;
            Color negative = StatsAppTheme.Negative;
            Color purple = StatsAppTheme.Purple;

            string dash = "—";
            int built = 0;

            bool isCurrentDay = _selectedDay == currentDay;

            CreateDailySummaryCard(
                Plugin.T("TRANSAKCJE", "TRANSACTIONS"),
                s.HasBasketData ? s.Transactions.ToString("N0") : (isCurrentDay ? "0" : dash),
                s.HasBasketData || isCurrentDay
                    ? Plugin.T("Zakończone transakcje przy kasach", "Completed checkout transactions")
                    : Plugin.T("Brak danych o transakcjach", "No transaction data"),
                info);
            built++;

            float avgBasket = s.HasBasketData && s.Transactions > 0
                ? s.BasketRevenue / s.Transactions
                : 0f;
            CreateDailySummaryCard(
                Plugin.T("ŚREDNI KOSZYK", "AVERAGE BASKET"),
                s.HasBasketData ? FormatSummaryMoney(avgBasket) : dash,
                s.HasBasketData
                    ? $"{FormatSummaryMoney(s.BasketRevenue)} / {s.Transactions:N0} {Plugin.T("transakcji", "transactions")}"
                    : Plugin.T("Brak danych o transakcjach", "No transaction data"),
                positive);
            built++;

            float avgItems = s.HasBasketData && s.Transactions > 0
                ? s.BasketItems / s.Transactions
                : 0f;
            CreateDailySummaryCard(
                Plugin.T("SZTUK NA KOSZYK", "ITEMS PER BASKET"),
                s.HasBasketData ? avgItems.ToString("0.0") : dash,
                s.HasBasketData
                    ? $"{s.BasketItems:0.0} {Plugin.T("sztuk", "items")} / {s.Transactions:N0} {Plugin.T("transakcji", "transactions")}"
                    : Plugin.T("Brak danych o transakcjach", "No transaction data"),
                purple);
            built++;

            bool hasCustomers = s.Customers > 0;
            float revenuePerCustomer = hasCustomers
                ? s.CheckoutIncome / s.Customers
                : 0f;
            CreateDailySummaryCard(
                Plugin.T("PRZYCHÓD / KLIENT", "REVENUE PER CUSTOMER"),
                hasCustomers ? FormatSummaryMoney(revenuePerCustomer) : dash,
                hasCustomers
                    ? $"{FormatSummaryMoney(s.CheckoutIncome)} / {s.Customers:N0} {Plugin.T("klientów", "customers")}"
                    : Plugin.T("Brak danych", "No data"),
                positive);
            built++;

            CreateDailySummaryCard(
                Plugin.T("KLIENCI", "CUSTOMERS"),
                s.Customers.ToString("N0"),
                Plugin.T("Klienci zarejestrowani przez grę", "Customers recorded by the game"),
                info);
            built++;

            bool hasCustomerRate = s.Customers > 0;
            float satisfaction = hasCustomerRate
                ? s.Satisfied * 100f / s.Customers
                : 0f;
            Color satisfactionColor = !hasCustomerRate
                ? info
                : satisfaction >= 80f ? positive
                : satisfaction >= 60f ? warning
                : negative;

            CreateDailySummaryCard(
                Plugin.T("SATYSFAKCJA", "SATISFACTION"),
                hasCustomerRate ? satisfaction.ToString("0.0") + "%" : dash,
                hasCustomerRate ? $"{s.Satisfied:N0} / {s.Customers:N0}" : Plugin.T("Brak danych", "No data"),
                satisfactionColor);
            built++;

            float notFoundRate = hasCustomerRate
                ? s.CouldntFind * 100f / s.Customers
                : 0f;
            Color notFoundColor = !hasCustomerRate
                ? info
                : notFoundRate <= 3f ? positive
                : notFoundRate <= 10f ? warning
                : negative;

            CreateDailySummaryCard(
                Plugin.T("BRAK PRODUKTU", "NOT FOUND RATE"),
                hasCustomerRate ? notFoundRate.ToString("0.0") + "%" : dash,
                hasCustomerRate ? $"{s.CouldntFind:N0} / {s.Customers:N0}" : Plugin.T("Brak danych", "No data"),
                notFoundColor);
            built++;

            float tooExpensiveRate = hasCustomerRate
                ? s.TooExpensive * 100f / s.Customers
                : 0f;
            Color tooExpensiveColor = !hasCustomerRate
                ? info
                : tooExpensiveRate <= 3f ? positive
                : tooExpensiveRate <= 10f ? warning
                : negative;

            CreateDailySummaryCard(
                Plugin.T("ZA DROGO", "TOO EXPENSIVE RATE"),
                hasCustomerRate ? tooExpensiveRate.ToString("0.0") + "%" : dash,
                hasCustomerRate ? $"{s.TooExpensive:N0} / {s.Customers:N0}" : Plugin.T("Brak danych", "No data"),
                tooExpensiveColor);
            built++;

            ForceTilesLayout(built);
        }
    }
}
