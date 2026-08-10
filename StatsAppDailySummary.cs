using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StatisticMod
{
    /// <summary>
    /// Widok PODSUMOWANIE: kafelki z finalnymi danymi dnia przechwyconymi
    /// z DailyStatisticsScreen.ApplyStatistics.
    /// </summary>
    public partial class StatsAppManager
    {
        private int GetLatestCapturedSummaryDay()
        {
            List<DailySummaryStats> days = DailySummaryStore.GetDaysSnapshot();
            for (int i = days.Count - 1; i >= 0; i--)
            {
                DailySummaryStats day = days[i];
                if (day != null && day.Captured && day.Day > 0)
                    return day.Day;
            }

            return Mathf.Max(1, GetCurrentDaySafe() - 1);
        }

        private bool HasCapturedSummaryDay(int day)
        {
            DailySummaryStats summary = DailySummaryStore.TryGetDay(day);
            return summary != null && summary.Captured;
        }

        private int FindSummaryDay(int fromDay, int direction)
        {
            List<DailySummaryStats> days = DailySummaryStore.GetDaysSnapshot();
            if (direction < 0)
            {
                for (int i = days.Count - 1; i >= 0; i--)
                {
                    DailySummaryStats day = days[i];
                    if (day != null && day.Captured && day.Day < fromDay)
                        return day.Day;
                }
            }
            else
            {
                for (int i = 0; i < days.Count; i++)
                {
                    DailySummaryStats day = days[i];
                    if (day != null && day.Captured && day.Day > fromDay)
                        return day.Day;
                }
            }

            return -1;
        }

        private void EnterDailySummaryMode()
        {
            _selectedDay = GetLatestCapturedSummaryDay();
            RefreshDailySummaryDayControls();
        }

        private void MoveDailySummaryDay(int direction)
        {
            int target = FindSummaryDay(_selectedDay, direction);
            if (target < 1) return;

            _selectedDay = target;
            RefreshDailySummaryDayControls();
            QueueBuildForHubMode();
        }

        private void RefreshDailySummaryDayControls()
        {
            if (_dayLabelTmp != null)
            {
                _dayLabelTmp.text = HasCapturedSummaryDay(_selectedDay)
                    ? $"{Plugin.T("DZIEŃ", "DAY")} {_selectedDay}"
                    : Plugin.T("BRAK DANYCH", "NO DATA");
            }

            if (_prevDayBtn != null)
                _prevDayBtn.interactable = FindSummaryDay(_selectedDay, -1) > 0;

            if (_nextDayBtn != null)
                _nextDayBtn.interactable = FindSummaryDay(_selectedDay, 1) > 0;
        }

        private void BuildDailySummaryTiles()
        {
            if (_tilesContent == null) return;

            ExitChartsLayout();
            ClearTilesOnly();

            // Przy pierwszym wejściu wybierz najnowszy zapisany dzień.
            // Nie resetujemy dnia podczas późniejszego przechodzenia strzałkami.
            if (!HasCapturedSummaryDay(_selectedDay))
                _selectedDay = GetLatestCapturedSummaryDay();

            RefreshDailySummaryDayControls();

            DailySummaryStats summary = DailySummaryStore.TryGetDay(_selectedDay);
            if (summary == null || !summary.Captured)
            {
                CreateDailySummaryCard(
                    Plugin.T("BRAK PODSUMOWANIA", "NO SUMMARY"),
                    Plugin.T("Brak danych", "No data"),
                    Plugin.T(
                        "Podsumowanie pojawi się po zakończeniu dnia.",
                        "The summary will appear after the day is finished."),
                    new Color(0.65f, 0.72f, 0.80f, 1f));

                ForceTilesLayout(1);
                return;
            }

            int built = 0;
            int totalCustomers = Math.Max(0, summary.TotalCustomerCount);
            int satisfied = Math.Max(0, summary.SatisfiedCustomerCount);
            float satisfaction = totalCustomers > 0
                ? satisfied * 100f / totalCustomers
                : 0f;

            int productProblems =
                Math.Max(0, summary.CouldntFindProduct) +
                Math.Max(0, summary.ExpensiveProducts);

            int serviceProblems =
                Math.Max(0, summary.ShortChangeAmount) +
                Math.Max(0, summary.HarmedCustomerCount);

            float otherIncome = summary.VendingIncome + summary.LoanIncome;
            float rentAndBills = Math.Abs(summary.RentCosts) + Math.Abs(summary.BillCosts);
            float investments =
                Math.Abs(summary.UpgradeCosts) +
                Math.Abs(summary.CustomizationCosts) +
                Math.Abs(summary.PaintCosts) +
                Math.Abs(summary.FloorBoxCosts);

            Color customersColor = new Color(0.25f, 0.72f, 1f, 1f);
            Color positiveColor = new Color(0.35f, 0.90f, 0.48f, 1f);
            Color warningColor = new Color(1f, 0.66f, 0.20f, 1f);
            Color negativeColor = new Color(1f, 0.32f, 0.28f, 1f);
            Color neutralColor = new Color(0.72f, 0.64f, 1f, 1f);

            CreateDailySummaryCard(
                Plugin.T("KLIENCI", "CUSTOMERS"),
                totalCustomers.ToString("N0"),
                $"{Plugin.T("Zadowoleni", "Satisfied")}: {satisfied:N0}",
                customersColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("SATYSFAKCJA", "SATISFACTION"),
                satisfaction.ToString("0.0") + "%",
                $"{satisfied:N0} / {totalCustomers:N0}",
                satisfaction >= 80f ? positiveColor : warningColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("PROBLEMY Z PRODUKTAMI", "PRODUCT ISSUES"),
                productProblems.ToString("N0"),
                $"{Plugin.T("Brak", "Not found")}: {summary.CouldntFindProduct:N0}  •  " +
                $"{Plugin.T("Za drogo", "Too expensive")}: {summary.ExpensiveProducts:N0}",
                productProblems > 0 ? warningColor : positiveColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("BŁĘDY OBSŁUGI", "SERVICE ISSUES"),
                serviceProblems.ToString("N0"),
                $"{Plugin.T("Reszta", "Short change")}: {summary.ShortChangeAmount:N0}  •  " +
                $"{Plugin.T("Poszkodowani", "Harmed")}: {summary.HarmedCustomerCount:N0}",
                serviceProblems > 0 ? negativeColor : positiveColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("PUNKTY SKLEPU", "STORE POINTS"),
                summary.StorePoint.ToString("N0"),
                Plugin.T("Wynik uzyskany tego dnia", "Points earned that day"),
                neutralColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("DOCHÓD Z KAS", "CHECKOUT INCOME"),
                FormatSummaryMoney(summary.CheckoutIncome),
                Plugin.T("Sprzedaż przy kasach", "Checkout sales"),
                positiveColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("POZOSTAŁE WPŁYWY", "OTHER INCOME"),
                FormatSummaryMoney(otherIncome),
                $"{Plugin.T("Automaty", "Vending")}: {FormatSummaryMoney(summary.VendingIncome)}  •  " +
                $"{Plugin.T("Pożyczki", "Loans")}: {FormatSummaryMoney(summary.LoanIncome)}",
                positiveColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("ŁĄCZNE WPŁYWY", "TOTAL INCOME"),
                FormatSummaryMoney(summary.TotalIncome),
                Plugin.T("Kasy + automaty + pożyczki", "Checkout + vending + loans"),
                positiveColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("ZAOPATRZENIE", "SUPPLIES"),
                FormatSummaryMoney(Math.Abs(summary.SupplyCosts)),
                Plugin.T("Koszt zamówionego towaru", "Cost of ordered stock"),
                negativeColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("PERSONEL", "STAFF"),
                FormatSummaryMoney(Math.Abs(summary.StaffPayment)),
                Plugin.T("Wynagrodzenia pracowników", "Employee wages"),
                negativeColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("CZYNSZ I RACHUNKI", "RENT AND BILLS"),
                FormatSummaryMoney(rentAndBills),
                $"{Plugin.T("Czynsz", "Rent")}: {FormatSummaryMoney(Math.Abs(summary.RentCosts))}  •  " +
                $"{Plugin.T("Rachunki", "Bills")}: {FormatSummaryMoney(Math.Abs(summary.BillCosts))}",
                negativeColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("INWESTYCJE", "INVESTMENTS"),
                FormatSummaryMoney(investments),
                $"{Plugin.T("Ulepszenia", "Upgrades")}: {FormatSummaryMoney(Math.Abs(summary.UpgradeCosts))}  •  " +
                $"{Plugin.T("Wystrój", "Customization")}: {FormatSummaryMoney(Math.Abs(summary.CustomizationCosts))}",
                warningColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("SPŁATA POŻYCZKI", "LOAN PAYMENT"),
                FormatSummaryMoney(Math.Abs(summary.LoanPayment)),
                Plugin.T("Rata zapłacona tego dnia", "Payment made that day"),
                negativeColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("ŁĄCZNE WYDATKI", "TOTAL EXPENSES"),
                FormatSummaryMoney(summary.TotalExpenses),
                Plugin.T("Suma wszystkich zapisanych kosztów", "Sum of all recorded costs"),
                negativeColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("ZYSK DNIA", "DAILY PROFIT"),
                FormatSummaryMoney(summary.DailyProfit),
                $"{Plugin.T("Wyliczenie moda", "Mod calculation")}: {FormatSummaryMoney(summary.CalculatedProfit)}",
                summary.DailyProfit >= 0f ? positiveColor : negativeColor);
            built++;

            CreateDailySummaryCard(
                Plugin.T("SALDO", "BALANCE"),
                FormatSummaryMoney(summary.Balance),
                Plugin.T("Stan konta po zakończeniu dnia", "Account balance after the day"),
                summary.Balance >= 0f ? customersColor : negativeColor);
            built++;

            ForceTilesLayout(built);
        }

        private string FormatSummaryMoney(float value)
        {
            return value.ToString("N2") + Plugin.T(" zł", " $");
        }

        private void CreateDailySummaryCard(
            string title,
            string value,
            string details,
            Color accent)
        {
            var card = new GameObject("DailySummaryCard");
            card.transform.SetParent(_tilesContent, false);

            var cardRT = card.AddComponent<RectTransform>();
            cardRT.sizeDelta = new Vector2(205f, 90f);

            card.AddComponent<CanvasRenderer>();
            var background = card.AddComponent<Image>();
            background.color = new Color(0.055f, 0.176f, 0.271f, 1f);
            background.raycastTarget = false;

            var layout = card.AddComponent<LayoutElement>();
            layout.preferredWidth = 205f;
            layout.preferredHeight = 90f;

            var shadow = new GameObject("Shadow");
            shadow.transform.SetParent(card.transform, false);
            shadow.transform.SetAsFirstSibling();
            var shadowRT = shadow.AddComponent<RectTransform>();
            shadowRT.anchorMin = Vector2.zero;
            shadowRT.anchorMax = Vector2.one;
            shadowRT.offsetMin = new Vector2(-3f, -5f);
            shadowRT.offsetMax = new Vector2(3f, 3f);
            shadow.AddComponent<CanvasRenderer>();
            var shadowImage = shadow.AddComponent<Image>();
            shadowImage.color = new Color(0f, 0f, 0f, 0.24f);
            shadowImage.raycastTarget = false;

            var accentGO = new GameObject("Accent");
            accentGO.transform.SetParent(card.transform, false);
            var accentRT = accentGO.AddComponent<RectTransform>();
            accentRT.anchorMin = new Vector2(0f, 0f);
            accentRT.anchorMax = new Vector2(0.025f, 1f);
            accentRT.offsetMin = Vector2.zero;
            accentRT.offsetMax = Vector2.zero;
            accentGO.AddComponent<CanvasRenderer>();
            var accentImage = accentGO.AddComponent<Image>();
            accentImage.color = accent;
            accentImage.raycastTarget = false;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(card.transform, false);
            var titleRT = titleGO.AddComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0.07f, 0.68f);
            titleRT.anchorMax = new Vector2(0.96f, 0.95f);
            titleRT.offsetMin = Vector2.zero;
            titleRT.offsetMax = Vector2.zero;
            var titleTMP = titleGO.AddComponent<TextMeshProUGUI>();
            titleTMP.text = title;
            titleTMP.fontSize = 11f;
            titleTMP.fontStyle = FontStyles.Bold;
            titleTMP.alignment = TextAlignmentOptions.Left;
            titleTMP.color = new Color32(255, 245, 220, 255);
            titleTMP.enableAutoSizing = true;
            titleTMP.fontSizeMin = 8f;
            titleTMP.fontSizeMax = 11f;
            titleTMP.enableWordWrapping = false;
            titleTMP.overflowMode = TextOverflowModes.Ellipsis;
            titleTMP.raycastTarget = false;
            if (_gameFont != null) titleTMP.font = _gameFont;
            SafeSetOutline(titleTMP, 0.08f);

            var valueGO = new GameObject("Value");
            valueGO.transform.SetParent(card.transform, false);
            var valueRT = valueGO.AddComponent<RectTransform>();
            valueRT.anchorMin = new Vector2(0.07f, 0.29f);
            valueRT.anchorMax = new Vector2(0.96f, 0.70f);
            valueRT.offsetMin = Vector2.zero;
            valueRT.offsetMax = Vector2.zero;
            var valueTMP = valueGO.AddComponent<TextMeshProUGUI>();
            valueTMP.text = value;
            valueTMP.fontSize = 20f;
            valueTMP.fontStyle = FontStyles.Bold;
            valueTMP.alignment = TextAlignmentOptions.Left;
            valueTMP.color = accent;
            valueTMP.enableAutoSizing = true;
            valueTMP.fontSizeMin = 12f;
            valueTMP.fontSizeMax = 20f;
            valueTMP.enableWordWrapping = false;
            valueTMP.overflowMode = TextOverflowModes.Ellipsis;
            valueTMP.raycastTarget = false;
            if (_gameFont != null) valueTMP.font = _gameFont;
            SafeSetOutline(valueTMP, 0.10f);

            var detailsGO = new GameObject("Details");
            detailsGO.transform.SetParent(card.transform, false);
            var detailsRT = detailsGO.AddComponent<RectTransform>();
            detailsRT.anchorMin = new Vector2(0.07f, 0.05f);
            detailsRT.anchorMax = new Vector2(0.96f, 0.30f);
            detailsRT.offsetMin = Vector2.zero;
            detailsRT.offsetMax = Vector2.zero;
            var detailsTMP = detailsGO.AddComponent<TextMeshProUGUI>();
            detailsTMP.text = details;
            detailsTMP.fontSize = 8.8f;
            detailsTMP.alignment = TextAlignmentOptions.Left;
            detailsTMP.color = new Color32(225, 225, 225, 225);
            detailsTMP.enableAutoSizing = true;
            detailsTMP.fontSizeMin = 7f;
            detailsTMP.fontSizeMax = 8.8f;
            detailsTMP.enableWordWrapping = false;
            detailsTMP.overflowMode = TextOverflowModes.Ellipsis;
            detailsTMP.raycastTarget = false;
            if (_gameFont != null) detailsTMP.font = _gameFont;

            card.SetActive(true);
        }
    }
}
