using System;
using UnityEngine;

namespace StatisticMod
{
    /// <summary>
    /// Checkout / sales-channel analytics. Physical checkout data is captured
    /// from completed checkout payments. Online orders are counted when a
    /// delivered OrderListData reaches the customer. Vending purchases are
    /// shown only when the vending-machine DLC is active.
    /// </summary>
    public partial class StatsAppManager
    {
        private float _nextCheckoutAnalyticsRefresh;

        private void RefreshCheckoutAnalyticsLive()
        {
            if (_hubMode != HubMode.Checkouts) return;
            if (_selectedDay != GetCurrentDaySafe()) return;
            if (Time.realtimeSinceStartup < _nextCheckoutAnalyticsRefresh) return;

            _nextCheckoutAnalyticsRefresh = Time.realtimeSinceStartup + 0.75f;
            BuildCheckoutAnalyticsTiles();
        }

        private void BuildCheckoutAnalyticsTiles()
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

            CustomerBasketStore.DayData d = CustomerBasketStore.TryGetDay(_selectedDay);
            bool isCurrentDay = _selectedDay == currentDay;
            bool hasRow = d != null;
            bool showZero = isCurrentDay && !hasRow;

            bool hasVendingDlc = SalesUnifiedFinal.IsVendingDlcActive();
            bool extendedComplete = d != null && d.ExtendedChannelsComplete;
            bool paymentReliable = d != null && d.PaymentMethodReliable;
            bool hasChannelRevenue = d != null && d.ChannelRevenueComplete;

            int checkoutTx = hasRow ? Math.Max(0, d.Transactions) : 0;
            float checkoutRevenue = hasRow ? Mathf.Max(0f, d.CheckoutRevenue) : 0f;

            int onlineTx = hasRow ? Math.Max(0, d.OnlineOrderTransactions) : 0;
            float onlineRevenue = hasRow ? Mathf.Max(0f, d.OnlineOrderRevenue) : 0f;

            int vendingTx = hasRow ? Math.Max(0, d.VendingTransactions) : 0;
            float vendingRevenue = hasRow ? Mathf.Max(0f, d.VendingRevenue) : 0f;

            // For a completed day the game's own DailyStatistics summary is the
            // final authority. It is fed by the same MoneyManager transition as
            // our runtime tracker and also repairs historical rows produced by the
            // old CollectedMoney-based vending implementation.
            DailySummaryStats nativeSummary = DailySummaryStore.TryGetDay(_selectedDay);
            bool hasNativeSummary = nativeSummary != null && nativeSummary.Captured;
            if (hasVendingDlc && hasNativeSummary)
                vendingRevenue = Mathf.Max(0f, nativeSummary.VendingIncome);

            int allTx = checkoutTx + onlineTx + (hasVendingDlc ? vendingTx : 0);
            float allRevenue = checkoutRevenue + onlineRevenue + (hasVendingDlc ? vendingRevenue : 0f);

            // On completed days use the native final sales-income totals so the
            // KASY screen and PODSUMOWANIE screen cannot disagree because of a
            // stale/custom channel sidecar. Loans are intentionally excluded here:
            // this card represents sales revenue, not financing inflows.
            if (hasNativeSummary)
            {
                allRevenue = Mathf.Max(0f, nativeSummary.CheckoutIncome) +
                             (hasVendingDlc ? Mathf.Max(0f, nativeSummary.VendingIncome) : 0f);
            }

            Color info = StatsAppTheme.Info;
            Color positive = StatsAppTheme.Positive;
            Color warning = StatsAppTheme.Warning;
            Color purple = StatsAppTheme.Purple;
            string dash = "—";

            int built = 0;

            bool canShowAllChannels = showZero || (hasRow && extendedComplete);

            CreateDailySummaryCard(
                Plugin.T("WSZYSTKIE TRANSAKCJE", "ALL TRANSACTIONS"),
                canShowAllChannels ? allTx.ToString("N0") : dash,
                canShowAllChannels
                    ? (hasVendingDlc
                        ? Plugin.T("Kasy, zamówienia online i automaty vendingowe", "Checkouts, online orders and vending machines")
                        : Plugin.T("Kasy i zamówienia online", "Checkouts and online orders"))
                    : Plugin.T("Dane kanałów sprzedaży dostępne od tej wersji", "Sales-channel data available from this version"),
                info);
            built++;

            CreateDailySummaryCard(
                Plugin.T("ŁĄCZNY PRZYCHÓD", "TOTAL REVENUE"),
                canShowAllChannels ? Plugin.Money(allRevenue) : dash,
                canShowAllChannels
                    ? Plugin.T("Przychód ze wszystkich śledzonych kanałów sprzedaży", "Revenue from all tracked sales channels")
                    : Plugin.T("Dane kanałów sprzedaży dostępne od tej wersji", "Sales-channel data available from this version"),
                positive);
            built++;

            float checkoutAvg = checkoutTx > 0 ? checkoutRevenue / checkoutTx : 0f;
            CreateDailySummaryCard(
                Plugin.T("TRANSAKCJE KASOWE", "CHECKOUT TRANSACTIONS"),
                hasRow || showZero ? checkoutTx.ToString("N0") : dash,
                hasRow || showZero
                    ? $"{Plugin.T("PRZYCHÓD KAS", "CHECKOUT REVENUE")}: {Plugin.Money(checkoutRevenue)}   " +
                      $"• {Plugin.T("ŚREDNI KOSZYK", "AVERAGE BASKET")}: {Plugin.Money(checkoutAvg)}"
                    : Plugin.T("Brak danych o transakcjach", "No transaction data"),
                info);
            built++;

            int cardTx = hasRow ? Math.Max(0, d.CardTransactions) : 0;
            int cashTx = hasRow ? Math.Max(0, d.CashTransactions) : 0;
            int selfTx = hasRow ? Math.Max(0, d.SelfCheckoutTransactions) : 0;
            int cashierTx = hasRow ? Math.Max(0, d.CashierCheckoutTransactions) : 0;
            int playerTx = hasRow ? Math.Max(0, d.PlayerCheckoutTransactions) : 0;
            int regularTx = cashierTx + playerTx;
            int paymentTrackedTx = hasRow
                ? Math.Max(0, d.PaymentTrackedTransactions)
                : 0;

            float cardRevenue = hasRow ? Mathf.Max(0f, d.CardRevenue) : 0f;
            float cashRevenue = hasRow ? Mathf.Max(0f, d.CashRevenue) : 0f;
            float selfRevenue = hasRow ? Mathf.Max(0f, d.SelfCheckoutRevenue) : 0f;
            float cashierRevenue = hasRow ? Mathf.Max(0f, d.CashierCheckoutRevenue) : 0f;
            float playerRevenue = hasRow ? Mathf.Max(0f, d.PlayerCheckoutRevenue) : 0f;
            float regularRevenue = cashierRevenue + playerRevenue;

            if (paymentReliable || showZero)
            {
                CreateCheckoutChannelCard(
                    Plugin.T("KARTA", "CARD"),
                    cardTx,
                    paymentTrackedTx,
                    cardRevenue,
                    hasRow || showZero,
                    hasChannelRevenue || showZero,
                    purple);
            }
            else
            {
                CreateDailySummaryCard(
                    Plugin.T("KARTA", "CARD"),
                    dash,
                    Plugin.T("Dokładne dane metod płatności dostępne od tej wersji", "Accurate payment-method data available from this version"),
                    purple);
            }
            built++;

            if (paymentReliable || showZero)
            {
                CreateCheckoutChannelCard(
                    Plugin.T("GOTÓWKA", "CASH"),
                    cashTx,
                    paymentTrackedTx,
                    cashRevenue,
                    hasRow || showZero,
                    hasChannelRevenue || showZero,
                    warning);
            }
            else
            {
                CreateDailySummaryCard(
                    Plugin.T("GOTÓWKA", "CASH"),
                    dash,
                    Plugin.T("Dokładne dane metod płatności dostępne od tej wersji", "Accurate payment-method data available from this version"),
                    warning);
            }
            built++;

            CreateCheckoutChannelCard(
                Plugin.T("SELF-CHECKOUT", "SELF CHECKOUT"),
                selfTx,
                checkoutTx,
                selfRevenue,
                hasRow || showZero,
                hasChannelRevenue || showZero,
                info);
            built++;

            CreateCheckoutChannelCard(
                Plugin.T("KASJER", "CASHIER"),
                cashierTx,
                checkoutTx,
                cashierRevenue,
                hasRow || showZero,
                hasChannelRevenue || showZero,
                positive);
            built++;

            CreateCheckoutChannelCard(
                Plugin.T("GRACZ", "PLAYER"),
                playerTx,
                checkoutTx,
                playerRevenue,
                hasRow || showZero,
                hasChannelRevenue || showZero,
                purple);
            built++;

            CreateCheckoutChannelCard(
                Plugin.T("KASA TRADYCYJNA", "REGULAR CHECKOUT"),
                regularTx,
                checkoutTx,
                regularRevenue,
                hasRow || showZero,
                hasChannelRevenue || showZero,
                positive);
            built++;

            if (extendedComplete || showZero || onlineTx > 0)
            {
                CreateSalesChannelCard(
                    Plugin.T("ZAMÓWIENIA ONLINE", "ONLINE ORDERS"),
                    onlineTx,
                    allTx,
                    onlineRevenue,
                    hasRow || showZero,
                    positive);
            }
            else
            {
                CreateDailySummaryCard(
                    Plugin.T("ZAMÓWIENIA ONLINE", "ONLINE ORDERS"),
                    dash,
                    Plugin.T("Dane kanałów sprzedaży dostępne od tej wersji", "Sales-channel data available from this version"),
                    positive);
            }
            built++;

            // The vending section is completely hidden for players who do not
            // own / have the vending-machine DLC active.
            if (hasVendingDlc)
            {
                if (extendedComplete || showZero || vendingTx > 0)
                {
                    CreateSalesChannelCard(
                        Plugin.T("AUTOMATY VENDINGOWE", "VENDING MACHINES"),
                        vendingTx,
                        allTx,
                        vendingRevenue,
                        hasRow || showZero,
                        warning);
                }
                else
                {
                    CreateDailySummaryCard(
                        Plugin.T("AUTOMATY VENDINGOWE", "VENDING MACHINES"),
                        dash,
                        Plugin.T("Dane kanałów sprzedaży dostępne od tej wersji", "Sales-channel data available from this version"),
                        warning);
                }
                built++;
            }

            ForceTilesLayout(built);
        }

        private void CreateCheckoutChannelCard(
            string title,
            int transactions,
            int totalTransactions,
            float revenue,
            bool hasTransactionData,
            bool hasRevenueData,
            Color accent)
        {
            string dash = "—";

            if (!hasTransactionData)
            {
                CreateDailySummaryCard(
                    title,
                    dash,
                    Plugin.T("Brak danych o transakcjach", "No transaction data"),
                    accent);
                return;
            }

            float share = totalTransactions > 0
                ? transactions * 100f / totalTransactions
                : 0f;

            string value = totalTransactions > 0
                ? $"{transactions:N0}  ({share:0.0}%)"
                : "0  (0.0%)";

            string detail;
            if (hasRevenueData)
            {
                float avg = transactions > 0 ? revenue / transactions : 0f;
                detail =
                    $"{Plugin.T("PRZYCHÓD", "REVENUE")}: {Plugin.Money(revenue)}   " +
                    $"• {Plugin.T("ŚREDNI KOSZYK", "AVERAGE BASKET")}: {Plugin.Money(avg)}";
            }
            else
            {
                detail =
                    $"{Plugin.T("PRZYCHÓD", "REVENUE")}: —   " +
                    $"• {Plugin.T("Dane przychodów dostępne od tej wersji", "Revenue split available from this version")}";
            }

            CreateDailySummaryCard(title, value, detail, accent);
        }

        private void CreateSalesChannelCard(
            string title,
            int transactions,
            int allTransactions,
            float revenue,
            bool hasData,
            Color accent)
        {
            if (!hasData)
            {
                CreateDailySummaryCard(
                    title,
                    "—",
                    Plugin.T("Brak danych o transakcjach", "No transaction data"),
                    accent);
                return;
            }

            float share = allTransactions > 0
                ? transactions * 100f / allTransactions
                : 0f;
            float avg = transactions > 0 ? revenue / transactions : 0f;

            string value = allTransactions > 0
                ? $"{transactions:N0}  ({share:0.0}%)"
                : "0  (0.0%)";

            string detail =
                $"{Plugin.T("PRZYCHÓD", "REVENUE")}: {Plugin.Money(revenue)}   " +
                $"• {Plugin.T("ŚREDNIA TRANSAKCJA", "AVERAGE TRANSACTION")}: {Plugin.Money(avg)}";

            CreateDailySummaryCard(title, value, detail, accent);
        }
    }
}
