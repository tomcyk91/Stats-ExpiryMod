using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StatisticMod
{
    public partial class StatsAppManager
    {
        private enum AnalysisViewMode
        {
            Demand = 0,
            MissedSales = 1,
            Restock = 2,
            Pricing = 3,
            ExpiryRisk = 4
        }

        private sealed class ExpiryRiskDisplayRow
        {
            public int ProductId;
            public bool IsWeight;
            public float KgPerUnit;
            public int TotalTrackedUnits;
            public int ExpiredUnits;
            public int DueWithin1Units;
            public int DueWithin3Units;
            public int DueWithin7Units;
            public int NearestDays;
            public float CurrentCost;
            public float AtRiskValue3Days;
        }

        private AnalysisViewMode _analysisView = AnalysisViewMode.Demand;
        private int _analysisRangeDays = 7;

        private void CycleAnalysisView()
        {
            _analysisView = (AnalysisViewMode)(((int)_analysisView + 1) % 5);
        }

        private string GetAnalysisSortLabel()
        {
            return _analysisView switch
            {
                AnalysisViewMode.Demand => Plugin.T("POPYT", "DEMAND"),
                AnalysisViewMode.MissedSales => Plugin.T("UTRACONA SPRZ.", "MISSED SALES"),
                AnalysisViewMode.Restock => Plugin.T("UZUPEŁNIANIE", "RESTOCK"),
                AnalysisViewMode.Pricing => Plugin.T("CENY", "PRICING"),
                AnalysisViewMode.ExpiryRisk => Plugin.T("RYZYKO TERMINU", "EXPIRY RISK"),
                _ => Plugin.T("POPYT", "DEMAND")
            };
        }

        private void BuildAnalysisTilesNow()
        {
            ExitChartsLayout();
            ClearTilesOnly();
            if (_titleTmp != null)
                _titleTmp.text = Plugin.T("ANALIZA", "ANALYSIS");

            if (Plugin.ProductCache != null && Plugin.ProductCache.Count == 0)
            {
                var idm = UnityEngine.Object.FindFirstObjectByType<global::IDManager>();
                if (idm != null) Plugin.ProductCache.Build(idm);
            }

            if (_analysisView == AnalysisViewMode.ExpiryRisk)
            {
                BuildExpiryRiskTilesNow();
                return;
            }

            List<ProductBusinessAnalysisRow> rows = BusinessAnalysisService.BuildRows(_analysisRangeDays);
            var culture = new System.Globalization.CultureInfo("pl-PL");
            int dir = _sortAsc ? 1 : -1;

            rows.Sort((a, b) =>
            {
                int cmp = _analysisView switch
                {
                    AnalysisViewMode.Demand => Mathf.Max(a.RequestedVisible, a.SoldVisible).CompareTo(Mathf.Max(b.RequestedVisible, b.SoldVisible)),
                    AnalysisViewMode.MissedSales => a.MissedRevenue.CompareTo(b.MissedRevenue),
                    AnalysisViewMode.Restock => GetRestockScore(a).CompareTo(GetRestockScore(b)),
                    AnalysisViewMode.Pricing => Mathf.Abs(a.SuggestedPrice - a.CurrentPrice).CompareTo(Mathf.Abs(b.SuggestedPrice - b.CurrentPrice)),
                    _ => string.Compare(GetProductNameSafe(a.ProductId), GetProductNameSafe(b.ProductId), culture, System.Globalization.CompareOptions.IgnoreCase)
                };
                if (cmp == 0) cmp = a.ProductId.CompareTo(b.ProductId);
                return cmp * dir;
            });

            int built = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                ProductBusinessAnalysisRow row = rows[i];
                if (row == null || row.ProductId <= 0) continue;
                if (row.RequestedVisible <= 0.0001f && row.SoldVisible <= 0.0001f) continue;

                string name = GetProductNameSafe(row.ProductId);
                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    bool nameMatch = name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool idMatch = row.ProductId.ToString().IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!nameMatch && !idMatch) continue;
                }

                Sprite icon = null;
                if (Plugin.ProductCache != null)
                    Plugin.ProductCache.TryGet(row.ProductId, out _, out icon);

                GameObject tile = Instantiate(_tileTemplate, _tilesContent, false);
                tile.name = "AnalysisTile_" + row.ProductId;
                tile.SetActive(true);
                DisableGameScriptsOnTile(tile.transform);

                TextMeshProUGUI nameTmp = GetTmpComponent(tile.transform, "Product Name");
                if (nameTmp != null)
                {
                    nameTmp.text = FormatTileProductName(name, row.ProductId, false);
                    ConfigureAnalysisTitleText(nameTmp);
                }

                TextMeshProUGUI infoTmp = GetTmpComponent(tile.transform, "Product Brand");
                if (infoTmp != null)
                {
                    infoTmp.text = BuildAnalysisText(row);
                    ConfigureAnalysisInfoText(infoTmp);
                }

                Transform iconTr = tile.transform.Find("Product Icon");
                if (iconTr != null)
                {
                    Image img = iconTr.GetComponent<Image>();
                    if (img != null)
                    {
                        img.sprite = icon;
                        img.enabled = icon != null;
                        img.preserveAspect = true;
                    }
                }

                EnsureAnalysisTileClip(tile);
                AdjustAnalysisTile(tile.transform);
                AddAnalysisAccent(tile.transform, row);
                DisableRaycastOnAllTMP(tile.transform);

                // Globalne tooltipy objaśniają wszystkie metryki ANALIZY:
                // popyt, utraconą sprzedaż, uzupełnianie i ceny.
                if (infoTmp != null)
                    AttachAnalysisInfoTooltipZones(tile.transform, infoTmp);

                built++;
            }

            if (built == 0)
            {
                GameObject empty = Instantiate(_tileTemplate, _tilesContent, false);
                empty.name = "AnalysisEmpty";
                empty.SetActive(true);
                DisableGameScriptsOnTile(empty.transform);
                TextMeshProUGUI emptyName = GetTmpComponent(empty.transform, "Product Name");
                if (emptyName != null) emptyName.text = Plugin.T("BRAK DANYCH", "NO DATA");
                TextMeshProUGUI emptyInfo = GetTmpComponent(empty.transform, "Product Brand");
                if (emptyInfo != null)
                    emptyInfo.text = Plugin.T("Dane pojawią się po zakończeniu pierwszego dnia.", "Data will appear after the first day is completed.");
                Transform icon = empty.transform.Find("Product Icon");
                if (icon != null) icon.gameObject.SetActive(false);
                EnsureAnalysisTileClip(empty);
                ConfigureAnalysisTitleText(emptyName);
                ConfigureAnalysisInfoText(emptyInfo);
                AdjustAnalysisTile(empty.transform);
                built = 1;
            }

            ForceTilesLayout(built);
        }


        private void BuildExpiryRiskTilesNow()
        {
            Dictionary<int, SortedDictionary<int, int>> expirationMap = BuildGlobalExpirationMap();

            var rows = new List<ExpiryRiskDisplayRow>();
            if (expirationMap != null)
            {
                foreach (var pair in expirationMap)
                {
                    int productId = pair.Key;
                    SortedDictionary<int, int> batches = pair.Value;
                    if (productId <= 0 || batches == null || batches.Count == 0)
                        continue;

                    var row = new ExpiryRiskDisplayRow
                    {
                        ProductId = productId,
                        NearestDays = int.MaxValue,
                        CurrentCost = Mathf.Max(0f, GetCurrentCost(productId))
                    };

                    row.IsWeight = SalesUnifiedFinal.WeightPerUnit.TryGetValue(productId, out float kgPerUnit);
                    row.KgPerUnit = row.IsWeight ? kgPerUnit : 0f;

                    foreach (var kv in batches)
                    {
                        int daysLeft = kv.Key;
                        int count = Mathf.Max(0, kv.Value);
                        if (count <= 0) continue;

                        row.TotalTrackedUnits += count;
                        if (daysLeft < row.NearestDays)
                            row.NearestDays = daysLeft;

                        if (daysLeft < 0)
                        {
                            row.ExpiredUnits += count;
                        }
                        else
                        {
                            if (daysLeft <= 1) row.DueWithin1Units += count;
                            if (daysLeft <= 3) row.DueWithin3Units += count;
                            if (daysLeft <= 7) row.DueWithin7Units += count;
                        }
                    }

                    if (row.TotalTrackedUnits <= 0)
                        continue;

                    int atRiskUnits = row.ExpiredUnits + row.DueWithin3Units;
                    row.AtRiskValue3Days = atRiskUnits * row.CurrentCost;
                    rows.Add(row);
                }
            }

            var culture = new System.Globalization.CultureInfo("pl-PL");
            int dir = _sortAsc ? 1 : -1;

            rows.Sort((a, b) =>
            {
                int cmp = GetExpiryRiskScore(a).CompareTo(GetExpiryRiskScore(b));
                if (cmp == 0)
                {
                    cmp = string.Compare(
                        GetProductNameSafe(a.ProductId),
                        GetProductNameSafe(b.ProductId),
                        culture,
                        System.Globalization.CompareOptions.IgnoreCase);
                }
                if (cmp == 0) cmp = a.ProductId.CompareTo(b.ProductId);
                return cmp * dir;
            });

            int built = 0;
            for (int i = 0; i < rows.Count; i++)
            {
                ExpiryRiskDisplayRow row = rows[i];
                string name = GetProductNameSafe(row.ProductId);

                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    bool nameMatch = name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool idMatch = row.ProductId.ToString().IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!nameMatch && !idMatch) continue;
                }

                Sprite icon = null;
                if (Plugin.ProductCache != null)
                    Plugin.ProductCache.TryGet(row.ProductId, out _, out icon);

                GameObject tile = Instantiate(_tileTemplate, _tilesContent, false);
                tile.name = "ExpiryRiskTile_" + row.ProductId;
                tile.SetActive(true);
                DisableGameScriptsOnTile(tile.transform);

                TextMeshProUGUI nameTmp = GetTmpComponent(tile.transform, "Product Name");
                if (nameTmp != null)
                {
                    nameTmp.text = FormatTileProductName(name, row.ProductId, false);
                    ConfigureAnalysisTitleText(nameTmp);
                }

                TextMeshProUGUI infoTmp = GetTmpComponent(tile.transform, "Product Brand");
                if (infoTmp != null)
                {
                    infoTmp.text = BuildExpiryRiskText(row);
                    ConfigureExpiryRiskInfoText(infoTmp);
                }

                Transform iconTr = tile.transform.Find("Product Icon");
                if (iconTr != null)
                {
                    Image img = iconTr.GetComponent<Image>();
                    if (img != null)
                    {
                        img.sprite = icon;
                        img.enabled = icon != null;
                        img.preserveAspect = true;
                    }
                }

                EnsureAnalysisTileClip(tile);
                AdjustAnalysisTile(tile.transform);
                AddExpiryRiskAccent(tile.transform, row);
                DisableRaycastOnAllTMP(tile.transform);

                if (infoTmp != null)
                    AttachAnalysisInfoTooltipZones(tile.transform, infoTmp);

                built++;
            }

            if (built == 0)
            {
                GameObject empty = Instantiate(_tileTemplate, _tilesContent, false);
                empty.name = "ExpiryRiskEmpty";
                empty.SetActive(true);
                DisableGameScriptsOnTile(empty.transform);

                TextMeshProUGUI emptyName = GetTmpComponent(empty.transform, "Product Name");
                if (emptyName != null)
                    emptyName.text = Plugin.T("BRAK DANYCH TERMINÓW", "NO EXPIRY DATA");

                TextMeshProUGUI emptyInfo = GetTmpComponent(empty.transform, "Product Brand");
                if (emptyInfo != null)
                    emptyInfo.text = Plugin.T(
                        "Brak produktów z aktywnym śledzeniem dat ważności.",
                        "No products currently have active expiry-date tracking.");

                Transform icon = empty.transform.Find("Product Icon");
                if (icon != null) icon.gameObject.SetActive(false);

                EnsureAnalysisTileClip(empty);
                ConfigureAnalysisTitleText(emptyName);
                ConfigureAnalysisInfoText(emptyInfo);
                AdjustAnalysisTile(empty.transform);
                built = 1;
            }

            ForceTilesLayout(built);
        }

        private string BuildExpiryRiskText(ExpiryRiskDisplayRow row)
        {
            string total = FormatExpiryQuantity(row.TotalTrackedUnits, row);
            string expired = FormatExpiryQuantity(row.ExpiredUnits, row);
            string due1 = FormatExpiryQuantity(row.DueWithin1Units, row);
            string due3 = FormatExpiryQuantity(row.DueWithin3Units, row);
            string due7 = FormatExpiryQuantity(row.DueWithin7Units, row);

            return
                $"{Plugin.T("Stan", "Stock")}: <color=#297CA6>{total}</color> | {Plugin.T("Najbl.", "Nearest")}: <color={NearestExpiryColor(row.NearestDays)}>{FormatNearestExpiry(row.NearestDays)}</color>\n" +
                $"{Plugin.T("Po term.", "Expired")}: <color={ExpiryCountColor(row.ExpiredUnits, true)}>{expired}</color> | ≤1{Plugin.T("d", "d")}: <color={ExpiryCountColor(row.DueWithin1Units, false)}>{due1}</color>\n" +
                $"≤3{Plugin.T("d", "d")}: <color={ExpiryWindowColor(row.DueWithin3Units, 3)}>{due3}</color> | ≤7{Plugin.T("d", "d")}: <color={ExpiryWindowColor(row.DueWithin7Units, 7)}>{due7}</color>\n" +
                $"{Plugin.T("Ryzyko wartości", "Value risk")} ≤3{Plugin.T("d", "d")}: <color=#C2771A>{Plugin.Money(row.AtRiskValue3Days)}</color>\n" +
                $"{Plugin.T("Ocena", "Risk")}: <color={ExpiryRiskColor(row)}>{GetExpiryRiskLabel(row)}</color>";
        }

        private static string FormatExpiryQuantity(int count, ExpiryRiskDisplayRow row)
        {
            if (row != null && row.IsWeight && row.KgPerUnit > 0f)
                return (count * row.KgPerUnit).ToString("0.00") + " kg";

            return count.ToString() + " " + Plugin.T("szt.", "pcs");
        }

        private static float GetExpiryRiskScore(ExpiryRiskDisplayRow row)
        {
            if (row == null) return 0f;

            // Priorytet sortowania: towar już przeterminowany, następnie ≤1d,
            // ≤3d i ≤7d. Wartość zagrożona rozstrzyga podobne przypadki.
            return
                row.ExpiredUnits * 1000000f +
                row.DueWithin1Units * 10000f +
                row.DueWithin3Units * 100f +
                row.DueWithin7Units +
                Mathf.Min(999f, row.AtRiskValue3Days * 0.01f);
        }

        private static string FormatNearestExpiry(int days)
        {
            if (days == int.MaxValue) return "—";
            if (days < 0) return Plugin.T("PO TERMINIE", "EXPIRED");
            if (days == 0) return Plugin.T("DZIŚ", "TODAY");
            if (days == 1) return Plugin.T("JUTRO", "TOMORROW");
            return Plugin.T($"ZA {days} DNI", $"IN {days} DAYS");
        }

        private static string NearestExpiryColor(int days)
        {
            if (days <= 0) return StatsAppTheme.NegativeHex;
            if (days <= 3) return StatsAppTheme.WarningHex;
            if (days <= 7) return StatsAppTheme.InfoHex;
            return StatsAppTheme.PositiveHex;
        }

        private static string ExpiryCountColor(int count, bool expired)
        {
            if (count <= 0) return StatsAppTheme.PositiveHex;
            return expired ? StatsAppTheme.NegativeHex : StatsAppTheme.WarningHex;
        }

        private static string ExpiryWindowColor(int count, int days)
        {
            if (count <= 0) return StatsAppTheme.PositiveHex;
            return days <= 3 ? StatsAppTheme.WarningHex : StatsAppTheme.InfoHex;
        }

        private static string GetExpiryRiskLabel(ExpiryRiskDisplayRow row)
        {
            if (row == null) return "—";
            if (row.ExpiredUnits > 0) return Plugin.T("KRYTYCZNE", "CRITICAL");
            if (row.DueWithin1Units > 0) return Plugin.T("PILNE", "URGENT");
            if (row.DueWithin3Units > 0) return Plugin.T("WYSOKIE", "HIGH");
            if (row.DueWithin7Units > 0) return Plugin.T("UWAGA", "WATCH");
            return Plugin.T("NISKIE", "LOW");
        }

        private static string ExpiryRiskColor(ExpiryRiskDisplayRow row)
        {
            if (row == null) return StatsAppTheme.InfoHex;
            if (row.ExpiredUnits > 0) return StatsAppTheme.NegativeHex;
            if (row.DueWithin1Units > 0 || row.DueWithin3Units > 0) return StatsAppTheme.WarningHex;
            if (row.DueWithin7Units > 0) return StatsAppTheme.InfoHex;
            return StatsAppTheme.PositiveHex;
        }

        private void AddExpiryRiskAccent(Transform tile, ExpiryRiskDisplayRow row)
        {
            if (tile == null) return;

            var bar = new GameObject("AnalysisAccent");
            bar.transform.SetParent(tile, false);

            RectTransform rt = bar.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0.018f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            bar.AddComponent<CanvasRenderer>();
            Image image = bar.AddComponent<Image>();

            if (row != null && row.ExpiredUnits > 0)
                image.color = StatsAppTheme.Negative;
            else if (row != null && (row.DueWithin1Units > 0 || row.DueWithin3Units > 0))
                image.color = StatsAppTheme.Warning;
            else if (row != null && row.DueWithin7Units > 0)
                image.color = StatsAppTheme.Info;
            else
                image.color = StatsAppTheme.Positive;

            image.raycastTarget = false;
        }

        private string BuildAnalysisText(ProductBusinessAnalysisRow row)
        {
            string unit = row.IsWeight ? "kg" : Plugin.T("szt.", "pcs");
            string requested = FormatVisible(row.RequestedVisible, row.IsWeight);
            string picked = FormatVisible(row.PickedVisible, row.IsWeight);
            string sold = FormatVisible(row.SoldVisible, row.IsWeight);
            string stockMissed = FormatVisible(row.StockMissedVisible, row.IsWeight);
            string otherMissed = FormatVisible(row.OtherUnfulfilledVisible, row.IsWeight);

            string otherNoLostLabel = Plugin.T("Inne (bez straty $)", "Other (no lost $)");
            string currencyUnit = Plugin.CurrencyUnitLabel;
            if (!string.IsNullOrEmpty(currencyUnit))
                otherNoLostLabel = otherNoLostLabel.Replace("$", currencyUnit);
            else
                otherNoLostLabel = otherNoLostLabel.Replace(" $", string.Empty).Replace("$", string.Empty);

            switch (_analysisView)
            {
                case AnalysisViewMode.Demand:
                    return
                        $"{Plugin.T("Popyt", "Demand")}: <color=#297CA6>{requested} {unit}</color> | {Plugin.T("Sprz.", "Sold")}: <color=#C2771A>{sold} {unit}</color>\n" +
                        $"{Plugin.T("Zebr.", "Picked")}: <color=#308B58>{picked} {unit}</color> | {Plugin.T("Dost.", "Avail.")}: <color={ServiceColor(row.ServiceLevel)}>{row.ServiceLevel * 100f:0.0}%</color>\n" +
                        $"{Plugin.T("Śr.", "Avg.")}: {row.AverageDailyDemandVisible:0.00} | {Plugin.T("Ost.3", "Last 3")}: <color=#297CA6>{row.RecentAverageVisible:0.00}</color> {unit}\n" +
                        $"{Plugin.T("Poprz.3", "Prev. 3")}: {row.PreviousAverageVisible:0.00} | {Plugin.T("Trend", "Trend")}: <color={TrendColor(row.DemandTrend)}>{TrendArrow(row.DemandTrend)} {FormatTrend(row.DemandTrend)}</color>\n" +
                        $"{Plugin.T("Prog.", "Fcst.")}: <color=#C2771A>{row.ForecastDailyVisible:0.00}/{Plugin.T("d", "d")}</color> | <color=#C2771A>{row.Forecast3DayVisible:0.00}/3{Plugin.T("d", "d")}</color>";

                case AnalysisViewMode.MissedSales:
                    return
                        $"{Plugin.T("Brak zapasu", "Stock miss")}: <color=#C2771A>{stockMissed} {unit}</color> | <color=#C2433A>{Plugin.Money(row.MissedRevenue)}</color>\n" +
                        $"{Plugin.T("Brak wszędzie", "Out of stock")}: <color=#C2433A>{FormatReason(row.GlobalOutOfStockUnits, row)} {unit}</color>\n" +
                        $"{Plugin.T("Pusta półka", "Empty shelf")}: <color=#C2771A>{FormatReason(row.ShelfEmptyUnits, row)} {unit}</color> | {Plugin.T("Niewyst.", "Not shown")}: <color=#9A7B16>{FormatReason(row.NotDisplayedUnits, row)} {unit}</color>\n" +
                        $"{otherNoLostLabel}: <color=#718492>{otherMissed} {unit}</color>";

                case AnalysisViewMode.Restock:
                    string shop = FormatVisible(row.ShopStockVisible, row.IsWeight);
                    string warehouse = FormatVisible(row.WarehouseStockVisible, row.IsWeight);
                    string transfer = FormatReason(row.TransferToShelfUnits, row);
                    string order = FormatReason(row.RecommendedOrderUnits, row);
                    return
                        $"{Plugin.T("Sklep", "Shop")}: <color=#297CA6>{shop} {unit}</color> | {Plugin.T("Mag.", "Wh.")}: <color=#C2771A>{warehouse} {unit}</color>\n" +
                        $"{Plugin.T("Pokrycie", "Cover")}: <color={CoverColor(row.DaysOfCover)}>{FormatCover(row.DaysOfCover)}</color>\n" +
                        $"{Plugin.T("Na półkę", "To shelf")}: <color=#308B58>{transfer} {unit}</color>\n" +
                        $"{Plugin.T("Zamów", "Order")}: <color=#C2771A>{order} {unit}</color> ({row.RecommendedBoxes} {Plugin.T("kart.", "boxes")})";

                case AnalysisViewMode.Pricing:
                    return
                        $"{Plugin.T("Koszt", "Cost")}: <color=#C2771A>{Plugin.Money(row.CurrentCost)}</color> | {Plugin.T("Cena", "Price")}: <color=#308B58>{Plugin.Money(row.CurrentPrice)}</color>\n" +
                        $"{Plugin.T("Rynkowa", "Market")}: <color=#297CA6>{Plugin.Money(row.MarketPrice)}</color>\n" +
                        $"{Plugin.T("Sugestia", "Suggestion")}: <color={AdviceColor(row.PricingAdvice)}>{Plugin.Money(row.SuggestedPrice)}</color>\n" +
                        $"{GetAdviceText(row)} | {Plugin.T("Pewn.", "Conf.")}: {GetConfidenceText(row.PricingConfidence)}";
            }

            return string.Empty;
        }

        private static void ConfigureAnalysisTitleText(TextMeshProUGUI text)
        {
            if (text == null) return;

            text.enableAutoSizing = true;
            text.fontSizeMin = 7.5f;
            text.fontSizeMax = 11f;
            text.fontSize = 11f;
            text.fontStyle = FontStyles.Bold;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.margin = new Vector4(1f, 0f, 2f, 0f);
            text.color = StatsAppTheme.TileTitle;
        }

        private static void ConfigureAnalysisInfoText(TextMeshProUGUI text)
        {
            if (text == null) return;

            text.enableAutoSizing = true;
            text.fontSizeMin = 5.4f;
            text.fontSizeMax = 7.4f;
            text.fontSize = 7.4f;
            text.lineSpacing = -2f;
            text.paragraphSpacing = 0f;
            text.enableWordWrapping = false;
            // Ellipsis kończył cały tekst po przepełnieniu pierwszej linii.
            // Karta ma RectMask2D, więc Overflow jest bezpieczny i pozwala
            // wyświetlić wszystkie jawne linie statystyk.
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.margin = new Vector4(1f, 1f, 2f, 0f);
            text.color = StatsAppTheme.TileText;
        }

        private static void ConfigureExpiryRiskInfoText(TextMeshProUGUI text)
        {
            if (text == null) return;

            text.enableAutoSizing = true;
            text.fontSizeMin = 5.2f;
            text.fontSizeMax = 6.8f;
            text.fontSize = 6.8f;
            text.lineSpacing = -1.5f;
            text.paragraphSpacing = 0f;
            text.enableWordWrapping = false;
            text.maxVisibleLines = 8;
            text.overflowMode = TextOverflowModes.Overflow;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.margin = new Vector4(1f, 0f, 1f, 0f);
            text.color = StatsAppTheme.TileText;
        }

        private static void EnsureAnalysisTileClip(GameObject tile)
        {
            if (tile == null) return;

            // Zabezpieczenie końcowe: nawet przy bardzo długim tłumaczeniu lub
            // nietypowej wartości tekst nigdy nie zostanie narysowany poza kartą.
            if (tile.GetComponent<RectMask2D>() == null)
                tile.AddComponent<RectMask2D>();
        }

        private static float GetRestockScore(ProductBusinessAnalysisRow row)
        {
            return row.RecommendedOrderUnits * 10f + row.TransferToShelfUnits + (row.MissRate * 100f);
        }

        private static string FormatVisible(float value, bool weight)
            => weight ? value.ToString("0.00") : Mathf.RoundToInt(value).ToString();

        private static string FormatReason(int rawUnits, ProductBusinessAnalysisRow row)
        {
            if (row.IsWeight) return (rawUnits * row.KgPerUnit).ToString("0.00");
            return rawUnits.ToString();
        }

        private static string FormatCover(float days)
        {
            if (days >= 998f) return "∞";
            return days.ToString("0.0") + " " + Plugin.T("dni", "days");
        }

        private static string ServiceColor(float service)
        {
            if (service >= 0.95f) return StatsAppTheme.PositiveHex;
            if (service >= 0.80f) return StatsAppTheme.WarningHex;
            return StatsAppTheme.NegativeHex;
        }

        private static string TrendColor(float trend)
        {
            if (trend >= 0.10f) return StatsAppTheme.PositiveHex;
            if (trend <= -0.10f) return StatsAppTheme.WarningHex;
            return StatsAppTheme.InfoHex;
        }

        private static string TrendArrow(float trend)
        {
            if (trend >= 0.10f) return "↑";
            if (trend <= -0.10f) return "↓";
            return "→";
        }

        private static string FormatTrend(float trend)
        {
            float percent = trend * 100f;
            return percent > 0.05f ? $"+{percent:0.0}%" : $"{percent:0.0}%";
        }

        private static string CoverColor(float days)
        {
            if (days < 1f) return StatsAppTheme.NegativeHex;
            if (days < 2f) return StatsAppTheme.WarningHex;
            return StatsAppTheme.PositiveHex;
        }

        private static string AdviceColor(PricingAdviceType advice)
        {
            return advice switch
            {
                PricingAdviceType.RaiseSlightly => StatsAppTheme.PositiveHex,
                PricingAdviceType.LowerSlightly => StatsAppTheme.WarningHex,
                PricingAdviceType.RestockFirst => StatsAppTheme.WarningHex,
                _ => StatsAppTheme.InfoHex
            };
        }

        private static string GetAdviceText(ProductBusinessAnalysisRow row)
        {
            return row.PricingAdvice switch
            {
                PricingAdviceType.RaiseSlightly => Plugin.T("Lekka podwyżka", "Small increase"),
                PricingAdviceType.LowerSlightly => Plugin.T("Lekka obniżka", "Small decrease"),
                PricingAdviceType.RestockFirst => Plugin.T("Najpierw uzupełnij zapas", "Restock first"),
                _ => Plugin.T("Pozostaw cenę", "Keep price")
            };
        }

        private static string GetConfidenceText(float confidence)
        {
            if (confidence >= 0.70f) return Plugin.T("wysoka", "high");
            if (confidence >= 0.35f) return Plugin.T("średnia", "medium");
            return Plugin.T("niska", "low");
        }

        private void AttachDemandInfoTooltipZones(Transform tile, TextMeshProUGUI infoTmp)
        {
            if (tile == null || infoTmp == null || _analysisView != AnalysisViewMode.Demand)
                return;

            Transform old = infoTmp.transform.Find("DemandTooltipZones");
            if (old != null)
                UnityEngine.Object.Destroy(old.gameObject);

            var root = new GameObject("DemandTooltipZones");
            root.transform.SetParent(infoTmp.transform, false);

            RectTransform rootRT = root.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;

            RectTransform tileRT = tile.GetComponent<RectTransform>();

            // Wiersz 1: Popyt | Sprz.
            CreateProductInfoTooltipZone(root.transform, tileRT, "Demand",
                new Vector2(0.00f, 0.80f), new Vector2(0.24f, 1.00f),
                Plugin.T("Popyt", "Demand"),
                Plugin.T(
                    "Łączna ilość, której klienci chcieli w wybranym zakresie dni. Obejmuje także popyt niezrealizowany.",
                    "Total quantity customers wanted during the selected day range. It also includes unfulfilled demand."));

            CreateProductInfoTooltipZone(root.transform, tileRT, "Sold",
                new Vector2(0.50f, 0.80f), new Vector2(0.73f, 1.00f),
                Plugin.T("Sprz. = Sprzedane", "Sold"),
                Plugin.T(
                    "Ilość faktycznie sprzedana klientom w wybranym zakresie dni.",
                    "Quantity actually sold to customers during the selected day range."));

            // Wiersz 2: Zebr. | Dost.
            CreateProductInfoTooltipZone(root.transform, tileRT, "Picked",
                new Vector2(0.00f, 0.60f), new Vector2(0.24f, 0.80f),
                Plugin.T("Zebr. = Zebrane", "Picked"),
                Plugin.T(
                    "Ilość, którą klienci znaleźli i zabrali z ekspozycji. To etap realizacji popytu przed finalną sprzedażą.",
                    "Quantity customers found and picked from displays. This is the demand-fulfilment stage before the final sale."));

            CreateProductInfoTooltipZone(root.transform, tileRT, "Availability",
                new Vector2(0.50f, 0.60f), new Vector2(0.73f, 0.80f),
                Plugin.T("Dost. = Dostępność", "Avail. = Availability"),
                Plugin.T(
                    "Poziom obsługi = Zebrane / Popyt. 100% oznacza, że cały zgłoszony popyt został pokryty.",
                    "Service level = Picked / Demand. 100% means all recorded demand was fulfilled."));

            // Wiersz 3: Śr. | Ost.3
            CreateProductInfoTooltipZone(root.transform, tileRT, "Average",
                new Vector2(0.00f, 0.40f), new Vector2(0.19f, 0.60f),
                Plugin.T("Śr. = Średnia dzienna", "Avg. = Daily average"),
                Plugin.T(
                    "Średni dzienny popyt w aktualnie wybranym zakresie, np. 7 dni.",
                    "Average daily demand in the currently selected range, for example 7 days."));

            CreateProductInfoTooltipZone(root.transform, tileRT, "Last3",
                new Vector2(0.44f, 0.40f), new Vector2(0.72f, 0.60f),
                Plugin.T("Ost.3 = Ostatnie 3 dni", "Last 3 days"),
                Plugin.T(
                    "Średni popyt z ostatnich 3 zakończonych dni sprzedażowych.",
                    "Average demand from the last 3 completed sales days."));

            // Wiersz 4: Poprz.3 | Trend
            CreateProductInfoTooltipZone(root.transform, tileRT, "Previous3",
                new Vector2(0.00f, 0.20f), new Vector2(0.33f, 0.40f),
                Plugin.T("Poprz.3 = Poprzednie 3 dni", "Prev. 3 days"),
                Plugin.T(
                    "Średni popyt z 3 dni bezpośrednio poprzedzających ostatnie 3 zakończone dni.",
                    "Average demand from the 3 days immediately preceding the last 3 completed days."));

            CreateProductInfoTooltipZone(root.transform, tileRT, "Trend",
                new Vector2(0.46f, 0.20f), new Vector2(0.72f, 0.40f),
                Plugin.T("Trend popytu", "Demand trend"),
                Plugin.T(
                    "Zmiana między Ost.3 i Poprz.3: (Ost.3 - Poprz.3) / Poprz.3. ↑ od +10%, ↓ od -10%, → pomiędzy.",
                    "Change between Last 3 and Prev. 3: (Last 3 - Prev. 3) / Prev. 3. ↑ from +10%, ↓ from -10%, → in between."));

            // Wiersz 5: Prog.
            CreateProductInfoTooltipZone(root.transform, tileRT, "Forecast",
                new Vector2(0.00f, 0.00f), new Vector2(0.24f, 0.20f),
                Plugin.T("Prog. = Prognoza popytu", "Fcst. = Demand forecast"),
                Plugin.T(
                    "Prognoza dzienna = 65% średniej z ostatnich 3 dni + 35% średniej z ostatnich 14 dni. Druga wartość pokazuje prognozę na 3 dni.",
                    "Daily forecast = 65% of the last-3-day average + 35% of the last-14-day average. The second value is the 3-day forecast."));
        }

        private void AdjustAnalysisTile(Transform tile)
        {
            if (tile == null) return;

            Image background = tile.GetComponent<Image>();
            if (background != null)
                background.color = StatsAppTheme.TileBackground;

            Transform name = tile.Find("Product Name");
            if (name != null)
            {
                RectTransform rt = name.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.325f, 0.71f);
                    rt.anchorMax = new Vector2(0.965f, 0.94f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
            }

            Transform info = tile.Find("Product Brand");
            if (info != null)
            {
                RectTransform rt = info.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.325f, 0.08f);
                    rt.anchorMax = new Vector2(0.965f, 0.665f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
            }

            Transform icon = tile.Find("Product Icon");
            if (icon != null)
            {
                RectTransform rt = icon.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.050f, 0.17f);
                    rt.anchorMax = new Vector2(0.280f, 0.83f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
            }
        }

        private void AddAnalysisAccent(Transform tile, ProductBusinessAnalysisRow row)
        {
            if (tile == null) return;

            var bar = new GameObject("AnalysisAccent");
            bar.transform.SetParent(tile, false);
            RectTransform rt = bar.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(0.018f, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            bar.AddComponent<CanvasRenderer>();
            Image image = bar.AddComponent<Image>();
            image.raycastTarget = false;

            if (_analysisView == AnalysisViewMode.Demand && row.DemandTrend <= -0.10f)
                image.color = StatsAppTheme.Warning;
            else if (_analysisView == AnalysisViewMode.Demand && row.DemandTrend >= 0.10f)
                image.color = StatsAppTheme.Positive;
            else if (_analysisView == AnalysisViewMode.Demand)
                image.color = StatsAppTheme.Info;
            else if (_analysisView == AnalysisViewMode.MissedSales && row.StockMissedVisible > 0.0001f)
                image.color = StatsAppTheme.Negative;
            else if (_analysisView == AnalysisViewMode.Restock && (row.RecommendedOrderUnits > 0 || row.TransferToShelfUnits > 0))
                image.color = StatsAppTheme.Warning;
            else if (_analysisView == AnalysisViewMode.Pricing && row.PricingAdvice != PricingAdviceType.Keep)
                image.color = StatsAppTheme.Info;
            else
                image.color = StatsAppTheme.Positive;
        }
    }
}
