using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace StatisticMod
{
    /// <summary>
    /// Rozszerzenie zakładki WYKRESY o drugi zakres danych:
    /// PRODUKT (dotychczasowe wykresy) oraz SKLEP (podsumowania dnia).
    /// </summary>
    public partial class StatsAppManager
    {
        private enum ChartScope
        {
            Product,
            Store
        }

        private enum SummaryMetric
        {
            TotalCustomers,
            SatisfiedCustomers,
            SatisfactionRate,
            CouldntFindProduct,
            ExpensiveProducts,
            ShortChangeAmount,
            HarmedCustomers,
            StorePoint,
            CheckoutIncome,
            VendingIncome,
            LoanIncome,
            TotalIncome,
            SupplyCosts,
            StaffPayment,
            RentCosts,
            BillCosts,
            UpgradeCosts,
            CustomizationCosts,
            LoanPayment,
            PaintCosts,
            FloorBoxCosts,
            TotalExpenses,
            DailyProfit,
            Balance
        }

        private ChartScope _chartScope = ChartScope.Product;
        private SummaryMetric _summaryMetric = SummaryMetric.TotalCustomers;
        private int _summaryMetricCycleIndex;

        private readonly SummaryMetric[] _summaryMetricCycle =
        {
            SummaryMetric.TotalCustomers,
            SummaryMetric.SatisfiedCustomers,
            SummaryMetric.SatisfactionRate,
            SummaryMetric.CouldntFindProduct,
            SummaryMetric.ExpensiveProducts,
            SummaryMetric.ShortChangeAmount,
            SummaryMetric.HarmedCustomers,
            SummaryMetric.StorePoint,
            SummaryMetric.CheckoutIncome,
            SummaryMetric.VendingIncome,
            SummaryMetric.LoanIncome,
            SummaryMetric.TotalIncome,
            SummaryMetric.SupplyCosts,
            SummaryMetric.StaffPayment,
            SummaryMetric.RentCosts,
            SummaryMetric.BillCosts,
            SummaryMetric.UpgradeCosts,
            SummaryMetric.CustomizationCosts,
            SummaryMetric.LoanPayment,
            SummaryMetric.PaintCosts,
            SummaryMetric.FloorBoxCosts,
            SummaryMetric.TotalExpenses,
            SummaryMetric.DailyProfit,
            SummaryMetric.Balance
        };

        private Button _chartScopeBtn;
        private TextMeshProUGUI _chartScopeLabel;
        private GameObject _chartProductPickerRoot;

        private void BuildChartScopeButton(Transform parent, int height)
        {
            if (parent == null) return;

            var btnGO = new GameObject("ChartScope");
            btnGO.transform.SetParent(parent, false);

            var le = btnGO.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleHeight = 0;

            btnGO.AddComponent<Image>().color = new Color(0f, 0f, 0.3774f, 1f);
            StretchToParent(btnGO);

            _chartScopeBtn = btnGO.AddComponent<Button>();
            PolishButtonVisual(_chartScopeBtn, true);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(btnGO.transform, false);

            _chartScopeLabel = textGO.AddComponent<TextMeshProUGUI>();
            _chartScopeLabel.fontSize = 12;
            _chartScopeLabel.fontStyle = FontStyles.Bold;
            _chartScopeLabel.alignment = TextAlignmentOptions.Center;
            _chartScopeLabel.color = Color.white;
            _chartScopeLabel.enableAutoSizing = true;
            _chartScopeLabel.fontSizeMin = 8;
            _chartScopeLabel.fontSizeMax = 12;
            if (_gameFont != null) _chartScopeLabel.font = _gameFont;

            var rt = textGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(4f, 0f);
            rt.offsetMax = new Vector2(-4f, 0f);

            _chartScopeBtn.onClick.AddListener((UnityAction)(() =>
            {
                SetChartScope(_chartScope == ChartScope.Product
                    ? ChartScope.Store
                    : ChartScope.Product);
            }));

            UpdateChartScopeLabel();
        }

        private void SetChartScope(ChartScope scope)
        {
            _chartScope = scope;

            if (_productDropPanel != null)
                _productDropPanel.SetActive(false);
            if (_dropdownBlocker != null)
                _dropdownBlocker.SetActive(false);

            UpdateChartScopeVisuals();
            UpdateCategoryCycleLabel();
            UpdateChartHeader();
            RefreshChart();
        }

        private void UpdateChartScopeVisuals()
        {
            UpdateChartScopeLabel();

            if (_chartProductPickerRoot != null)
                _chartProductPickerRoot.SetActive(_chartScope == ChartScope.Product);

            try
            {
                if (_chartProductPickerRoot != null && _chartProductPickerRoot.transform.parent != null)
                {
                    var rowRt = _chartProductPickerRoot.transform.parent.GetComponent<RectTransform>();
                    if (rowRt != null)
                        LayoutRebuilder.ForceRebuildLayoutImmediate(rowRt);
                }
            }
            catch { }
        }

        private void UpdateChartScopeLabel()
        {
            if (_chartScopeLabel == null) return;

            string value = _chartScope == ChartScope.Product
                ? Plugin.T("PRODUKT", "PRODUCT")
                : Plugin.T("SKLEP", "STORE");

            _chartScopeLabel.text = $"{Plugin.T("Widok", "View")}: {value}";
        }

        private void OnChartCategoryClicked()
        {
            if (_chartScope == ChartScope.Store)
            {
                _summaryMetricCycleIndex++;
                if (_summaryMetricCycleIndex >= _summaryMetricCycle.Length)
                    _summaryMetricCycleIndex = 0;

                _summaryMetric = _summaryMetricCycle[_summaryMetricCycleIndex];
            }
            else
            {
                _metricCycleIndex++;
                if (_metricCycleIndex >= _metricCycle.Length)
                    _metricCycleIndex = 0;

                _chartMetric = _metricCycle[_metricCycleIndex];
            }

            UpdateCategoryCycleLabel();
            UpdateChartHeader();
            RefreshChart();
        }

        private string GetActiveChartMetricLabel()
        {
            return _chartScope == ChartScope.Store
                ? GetSummaryMetricLabel(_summaryMetric)
                : GetMetricLabel(_chartMetric);
        }

        private string GetChartHeaderText()
        {
            if (_chartScope == ChartScope.Store)
            {
                return $"{Plugin.T("Wyniki sklepu w czasie", "Store results over time")} — " +
                       GetSummaryMetricLabel(_summaryMetric);
            }

            return $"{Plugin.T("Wyniki produktu w czasie", "Product results over time")} — " +
                   GetMetricLabel(_chartMetric);
        }

        private string GetSummaryMetricLabel(SummaryMetric metric)
        {
            return metric switch
            {
                SummaryMetric.TotalCustomers => Plugin.T("Łączna liczba klientów", "Total customers"),
                SummaryMetric.SatisfiedCustomers => Plugin.T("Zadowoleni klienci", "Satisfied customers"),
                SummaryMetric.SatisfactionRate => Plugin.T("Poziom zadowolenia", "Satisfaction rate"),
                SummaryMetric.CouldntFindProduct => Plugin.T("Nie znaleziono produktów", "Products not found"),
                SummaryMetric.ExpensiveProducts => Plugin.T("Produkty za drogie", "Products too expensive"),
                SummaryMetric.ShortChangeAmount => Plugin.T("Niepoprawna reszta", "Incorrect change"),
                SummaryMetric.HarmedCustomers => Plugin.T("Poszkodowani klienci", "Harmed customers"),
                SummaryMetric.StorePoint => Plugin.T("Punkty sklepu", "Store points"),
                SummaryMetric.CheckoutIncome => Plugin.T("Dochód z kas", "Checkout income"),
                SummaryMetric.VendingIncome => Plugin.T("Dochód z automatów", "Vending income"),
                SummaryMetric.LoanIncome => Plugin.T("Wpływy z pożyczki", "Loan income"),
                SummaryMetric.TotalIncome => Plugin.T("Łączne wpływy", "Total income"),
                SummaryMetric.SupplyCosts => Plugin.T("Zaopatrzenie", "Supply costs"),
                SummaryMetric.StaffPayment => Plugin.T("Personel", "Staff payment"),
                SummaryMetric.RentCosts => Plugin.T("Wynajem", "Rent"),
                SummaryMetric.BillCosts => Plugin.T("Rachunki", "Bills"),
                SummaryMetric.UpgradeCosts => Plugin.T("Ulepszenia", "Upgrades"),
                SummaryMetric.CustomizationCosts => Plugin.T("Modernizacja", "Customization"),
                SummaryMetric.LoanPayment => Plugin.T("Spłata pożyczki", "Loan payment"),
                SummaryMetric.PaintCosts => Plugin.T("Malowanie", "Paint costs"),
                SummaryMetric.FloorBoxCosts => Plugin.T("Podłogi", "Floor costs"),
                SummaryMetric.TotalExpenses => Plugin.T("Łączne wydatki", "Total expenses"),
                SummaryMetric.DailyProfit => Plugin.T("Zysk dnia", "Daily profit"),
                SummaryMetric.Balance => Plugin.T("Saldo", "Balance"),
                _ => Plugin.T("Wynik", "Result")
            };
        }

        private void RefreshStoreSummaryChart()
        {
            List<DailySummaryStats> captured = DailySummaryStore.GetDaysSnapshot();
            if (captured == null || captured.Count == 0)
            {
                SetStoreLegendNoData();
                return;
            }

            int lastDay = captured[captured.Count - 1].Day;
            int fromDay = Math.Max(1, lastDay - (_chartDaysRange - 1));

            var days = new List<int>();
            var values = new List<float>();

            float maxPositive = 0f;
            float maxNegativeAbs = 0f;

            for (int day = fromDay; day <= lastDay; day++)
            {
                DailySummaryStats summary = DailySummaryStore.TryGetDay(day);
                float value = summary != null && summary.Captured
                    ? GetSummaryMetricValue(summary, _summaryMetric)
                    : 0f;

                days.Add(day);
                values.Add(value);

                if (value >= 0f)
                    maxPositive = Mathf.Max(maxPositive, value);
                else
                    maxNegativeAbs = Mathf.Max(maxNegativeAbs, Mathf.Abs(value));
            }

            if (days.Count == 0)
            {
                SetStoreLegendNoData();
                return;
            }

            RectTransform rtC = _chartBarsContainer.GetComponent<RectTransform>();
            float width = Mathf.Max(1f, rtC.rect.width);
            float height = Mathf.Max(120f, rtC.rect.height);
            float groupWidth = width / days.Count;
            float barWidth = Mathf.Clamp(groupWidth * 0.45f, 8f, 30f);

            Color positiveColor = GetSummaryMetricColor(_summaryMetric);
            Color negativeColor = new Color(0.95f, 0.18f, 0.12f, 0.90f);

            bool hasNegative = maxNegativeAbs > 0.0001f;
            if (!hasNegative)
            {
                float maxValue = Mathf.Max(0.1f, maxPositive);
                float usableHeight = Mathf.Max(30f, (height - 38f) * 0.70f);

                for (int i = 0; i < days.Count; i++)
                {
                    float xCenter = (i + 0.5f) * groupWidth;
                    string label = FormatSummaryMetricValue(_summaryMetric, values[i]);
                    DrawBar(xCenter, 22f, barWidth, usableHeight, values[i], maxValue, positiveColor, label);
                    DrawDayLabel(xCenter, days[i], groupWidth);
                }
            }
            else
            {
                DrawSignedStoreBars(
                    days,
                    values,
                    groupWidth,
                    barWidth,
                    height,
                    maxPositive,
                    maxNegativeAbs,
                    positiveColor,
                    negativeColor);
            }

            UpdateStoreSummaryLegend();
        }

        private void DrawSignedStoreBars(
            List<int> days,
            List<float> values,
            float groupWidth,
            float barWidth,
            float chartHeight,
            float maxPositive,
            float maxNegativeAbs,
            Color positiveColor,
            Color negativeColor)
        {
            float bottom = 22f;
            float usableHeight = Mathf.Max(70f, chartHeight - 55f);
            float safePositive = Mathf.Max(0.1f, maxPositive);
            float safeNegative = Mathf.Max(0.1f, maxNegativeAbs);
            float totalScale = safePositive + safeNegative;

            float negativeHeight = usableHeight * (safeNegative / totalScale);
            float positiveHeight = usableHeight - negativeHeight;
            float zeroY = bottom + negativeHeight;

            DrawStoreZeroAxis(zeroY);

            for (int i = 0; i < days.Count; i++)
            {
                float xCenter = (i + 0.5f) * groupWidth;
                float value = values[i];
                float barHeight;
                Color color;
                bool negative = value < 0f;

                if (negative)
                {
                    barHeight = (Mathf.Abs(value) / safeNegative) * negativeHeight;
                    color = negativeColor;
                }
                else
                {
                    barHeight = (value / safePositive) * positiveHeight;
                    color = positiveColor;
                }

                barHeight = Mathf.Max(2f, barHeight);
                DrawSignedStoreBar(xCenter, zeroY, barWidth, barHeight, negative, color);

                float labelY = negative
                    ? zeroY - barHeight - 20f
                    : zeroY + barHeight + 2f;

                DrawStoreValueLabel(
                    xCenter,
                    labelY,
                    Mathf.Max(70f, groupWidth * 0.95f),
                    FormatSummaryMetricValue(_summaryMetric, value));

                DrawDayLabel(xCenter, days[i], groupWidth);
            }
        }

        private void DrawSignedStoreBar(
            float x,
            float zeroY,
            float width,
            float height,
            bool negative,
            Color color)
        {
            var barGO = new GameObject(negative ? "Bar_Negative" : "Bar_Positive");
            barGO.transform.SetParent(_chartBarsContainer, false);

            var image = barGO.AddComponent<Image>();
            image.color = color;

            var rt = barGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = negative ? new Vector2(0.5f, 1f) : new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(x, zeroY);
            rt.sizeDelta = new Vector2(width, height);
        }

        private void DrawStoreZeroAxis(float y)
        {
            var axisGO = new GameObject("ZeroAxis");
            axisGO.transform.SetParent(_chartBarsContainer, false);
            axisGO.transform.SetAsFirstSibling();

            var image = axisGO.AddComponent<Image>();
            image.color = new Color(0f, 0f, 0.25f, 0.55f);

            var rt = axisGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = new Vector2(0f, y);
            rt.sizeDelta = new Vector2(0f, 2f);
        }

        private void DrawStoreValueLabel(float x, float y, float width, string text)
        {
            var labelGO = new GameObject("StoreValueLabel");
            labelGO.transform.SetParent(_chartBarsContainer, false);

            var textComponent = labelGO.AddComponent<TextMeshProUGUI>();
            textComponent.text = text;
            textComponent.enableAutoSizing = true;
            textComponent.fontSizeMin = 4f;
            textComponent.fontSizeMax = 8f;
            textComponent.enableWordWrapping = false;
            textComponent.alignment = TextAlignmentOptions.Center;
            textComponent.color = new Color(0f, 0f, 0.3774f, 1f);
            if (_gameFont != null) textComponent.font = _gameFont;

            var rt = labelGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = new Vector2(x, y);
            rt.sizeDelta = new Vector2(width, 20f);
        }

        private float GetSummaryMetricValue(DailySummaryStats summary, SummaryMetric metric)
        {
            if (summary == null) return 0f;

            return metric switch
            {
                SummaryMetric.TotalCustomers => summary.TotalCustomerCount,
                SummaryMetric.SatisfiedCustomers => summary.SatisfiedCustomerCount,
                SummaryMetric.SatisfactionRate => summary.TotalCustomerCount > 0
                    ? summary.SatisfiedCustomerCount * 100f / summary.TotalCustomerCount
                    : 0f,
                SummaryMetric.CouldntFindProduct => summary.CouldntFindProduct,
                SummaryMetric.ExpensiveProducts => summary.ExpensiveProducts,
                SummaryMetric.ShortChangeAmount => summary.ShortChangeAmount,
                SummaryMetric.HarmedCustomers => summary.HarmedCustomerCount,
                SummaryMetric.StorePoint => summary.StorePoint,
                SummaryMetric.CheckoutIncome => summary.CheckoutIncome,
                SummaryMetric.VendingIncome => summary.VendingIncome,
                SummaryMetric.LoanIncome => summary.LoanIncome,
                SummaryMetric.TotalIncome => summary.TotalIncome,
                SummaryMetric.SupplyCosts => Mathf.Abs(summary.SupplyCosts),
                SummaryMetric.StaffPayment => Mathf.Abs(summary.StaffPayment),
                SummaryMetric.RentCosts => Mathf.Abs(summary.RentCosts),
                SummaryMetric.BillCosts => Mathf.Abs(summary.BillCosts),
                SummaryMetric.UpgradeCosts => Mathf.Abs(summary.UpgradeCosts),
                SummaryMetric.CustomizationCosts => Mathf.Abs(summary.CustomizationCosts),
                SummaryMetric.LoanPayment => Mathf.Abs(summary.LoanPayment),
                SummaryMetric.PaintCosts => Mathf.Abs(summary.PaintCosts),
                SummaryMetric.FloorBoxCosts => Mathf.Abs(summary.FloorBoxCosts),
                SummaryMetric.TotalExpenses => summary.TotalExpenses,
                SummaryMetric.DailyProfit => summary.DailyProfit,
                SummaryMetric.Balance => summary.Balance,
                _ => 0f
            };
        }

        private Color GetSummaryMetricColor(SummaryMetric metric)
        {
            return metric switch
            {
                SummaryMetric.TotalCustomers => new Color(0.20f, 0.60f, 1f, 0.88f),
                SummaryMetric.SatisfiedCustomers => new Color(0.20f, 0.82f, 0.35f, 0.88f),
                SummaryMetric.SatisfactionRate => new Color(0.20f, 0.82f, 0.35f, 0.88f),
                SummaryMetric.CouldntFindProduct => new Color(1f, 0.40f, 0.18f, 0.88f),
                SummaryMetric.ExpensiveProducts => new Color(1f, 0.58f, 0.12f, 0.88f),
                SummaryMetric.ShortChangeAmount => new Color(0.90f, 0.22f, 0.18f, 0.88f),
                SummaryMetric.HarmedCustomers => new Color(0.90f, 0.15f, 0.15f, 0.88f),
                SummaryMetric.StorePoint => new Color(0.55f, 0.35f, 0.95f, 0.88f),
                SummaryMetric.CheckoutIncome => new Color(0.20f, 0.80f, 0.35f, 0.88f),
                SummaryMetric.VendingIncome => new Color(0.25f, 0.70f, 0.45f, 0.88f),
                SummaryMetric.LoanIncome => new Color(0.30f, 0.65f, 0.80f, 0.88f),
                SummaryMetric.TotalIncome => new Color(0.10f, 0.72f, 0.32f, 0.90f),
                SummaryMetric.DailyProfit => new Color(0.12f, 0.75f, 0.30f, 0.90f),
                SummaryMetric.Balance => new Color(0.15f, 0.55f, 0.90f, 0.90f),
                _ => new Color(0.95f, 0.28f, 0.18f, 0.88f)
            };
        }

        private string FormatSummaryMetricValue(SummaryMetric metric, float value)
        {
            if (metric == SummaryMetric.SatisfactionRate)
                return value.ToString("N1") + "%";

            if (metric == SummaryMetric.StorePoint)
                return value.ToString("N0") + " " + Plugin.T("pkt", "pts");

            if (IsSummaryMoneyMetric(metric))
                return value.ToString("N2") + Plugin.T(" zł", " $");

            return value.ToString("N0");
        }

        private bool IsSummaryMoneyMetric(SummaryMetric metric)
        {
            return metric == SummaryMetric.CheckoutIncome ||
                   metric == SummaryMetric.VendingIncome ||
                   metric == SummaryMetric.LoanIncome ||
                   metric == SummaryMetric.TotalIncome ||
                   metric == SummaryMetric.SupplyCosts ||
                   metric == SummaryMetric.StaffPayment ||
                   metric == SummaryMetric.RentCosts ||
                   metric == SummaryMetric.BillCosts ||
                   metric == SummaryMetric.UpgradeCosts ||
                   metric == SummaryMetric.CustomizationCosts ||
                   metric == SummaryMetric.LoanPayment ||
                   metric == SummaryMetric.PaintCosts ||
                   metric == SummaryMetric.FloorBoxCosts ||
                   metric == SummaryMetric.TotalExpenses ||
                   metric == SummaryMetric.DailyProfit ||
                   metric == SummaryMetric.Balance;
        }

        private void UpdateStoreSummaryLegend()
        {
            if (_legendTextTmp == null && _chartLegendTmp == null) return;

            List<DailySummaryStats> captured = DailySummaryStore.GetDaysSnapshot();
            if (captured == null || captured.Count == 0)
            {
                SetStoreLegendNoData();
                return;
            }

            int lastDay = captured[captured.Count - 1].Day;
            int fromDay = Math.Max(1, lastDay - (_chartDaysRange - 1));

            float value;
            string prefix;

            if (_summaryMetric == SummaryMetric.Balance)
            {
                DailySummaryStats last = DailySummaryStore.TryGetDay(lastDay);
                value = last != null ? last.Balance : 0f;
                prefix = Plugin.T("Ostatnia wartość", "Latest value");
            }
            else if (_summaryMetric == SummaryMetric.SatisfactionRate)
            {
                int totalCustomers = 0;
                int totalSatisfied = 0;

                for (int day = fromDay; day <= lastDay; day++)
                {
                    DailySummaryStats summary = DailySummaryStore.TryGetDay(day);
                    if (summary == null || !summary.Captured) continue;
                    totalCustomers += summary.TotalCustomerCount;
                    totalSatisfied += summary.SatisfiedCustomerCount;
                }

                value = totalCustomers > 0
                    ? totalSatisfied * 100f / totalCustomers
                    : 0f;
                prefix = Plugin.T("Średnia ważona", "Weighted average");
            }
            else
            {
                value = 0f;
                for (int day = fromDay; day <= lastDay; day++)
                {
                    DailySummaryStats summary = DailySummaryStore.TryGetDay(day);
                    if (summary == null || !summary.Captured) continue;
                    value += GetSummaryMetricValue(summary, _summaryMetric);
                }

                prefix = Plugin.T("Razem", "Total");
            }

            Color color = value < 0f
                ? new Color(0.95f, 0.18f, 0.12f, 0.95f)
                : GetSummaryMetricColor(_summaryMetric);

            if (_legendSwatchImg != null)
                _legendSwatchImg.color = color;

            string hex = ColorUtility.ToHtmlStringRGB(color);
            string text = $"{GetSummaryMetricLabel(_summaryMetric)} — {prefix}: " +
                          $"<color=#{hex}>{FormatSummaryMetricValue(_summaryMetric, value)}</color>";

            if (_legendTextTmp != null) _legendTextTmp.text = text;
            if (_chartLegendTmp != null) _chartLegendTmp.text = text;
        }

        private void SetStoreLegendNoData()
        {
            string text = Plugin.T(
                "Brak zapisanych podsumowań dnia.",
                "No saved daily summaries.");

            if (_legendTextTmp != null) _legendTextTmp.text = text;
            if (_chartLegendTmp != null) _chartLegendTmp.text = text;
            if (_legendSwatchImg != null) _legendSwatchImg.color = Color.clear;
        }
    }
}
