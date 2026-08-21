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
            Pricing = 3
        }

        private AnalysisViewMode _analysisView = AnalysisViewMode.Demand;
        private int _analysisRangeDays = 7;

        private void CycleAnalysisView()
        {
            _analysisView = (AnalysisViewMode)(((int)_analysisView + 1) % 4);
        }

        private string GetAnalysisSortLabel()
        {
            return _analysisView switch
            {
                AnalysisViewMode.Demand => Plugin.T("POPYT", "DEMAND"),
                AnalysisViewMode.MissedSales => Plugin.T("UTRACONA SPRZ.", "MISSED SALES"),
                AnalysisViewMode.Restock => Plugin.T("UZUPEŁNIANIE", "RESTOCK"),
                AnalysisViewMode.Pricing => Plugin.T("CENY", "PRICING"),
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

        private string BuildAnalysisText(ProductBusinessAnalysisRow row)
        {
            string unit = row.IsWeight ? "kg" : Plugin.T("szt.", "pcs");
            string requested = FormatVisible(row.RequestedVisible, row.IsWeight);
            string picked = FormatVisible(row.PickedVisible, row.IsWeight);
            string sold = FormatVisible(row.SoldVisible, row.IsWeight);
            string stockMissed = FormatVisible(row.StockMissedVisible, row.IsWeight);
            string otherMissed = FormatVisible(row.OtherUnfulfilledVisible, row.IsWeight);

            switch (_analysisView)
            {
                case AnalysisViewMode.Demand:
                    return
                        $"{Plugin.T("Popyt", "Demand")}: <color=#297CA6>{requested} {unit}</color>\n" +
                        $"{Plugin.T("Zebr.", "Picked")}: <color=#308B58>{picked} {unit}</color> | {Plugin.T("Sprzed.", "Sold")}: <color=#C2771A>{sold} {unit}</color>\n" +
                        $"{Plugin.T("Dost.", "Avail.")}: <color={ServiceColor(row.ServiceLevel)}>{row.ServiceLevel * 100f:0.0}%</color> | {Plugin.T("Sprz./popyt", "Sold/dem.")}: {row.SalesToDemandRate * 100f:0.0}%\n" +
                        $"{Plugin.T("Śr./dzień", "Avg/day")}: <color=#C2771A>{row.AverageDailyDemandVisible:0.00} {unit}</color>";

                case AnalysisViewMode.MissedSales:
                    return
                        $"{Plugin.T("Brak zapasu", "Stock miss")}: <color=#C2771A>{stockMissed} {unit}</color> | <color=#C2433A>{row.MissedRevenue:0.00} $</color>\n" +
                        $"{Plugin.T("Brak wszędzie", "Out of stock")}: <color=#C2433A>{FormatReason(row.GlobalOutOfStockUnits, row)} {unit}</color>\n" +
                        $"{Plugin.T("Pusta półka", "Empty shelf")}: <color=#C2771A>{FormatReason(row.ShelfEmptyUnits, row)} {unit}</color> | {Plugin.T("Niewyst.", "Not shown")}: <color=#9A7B16>{FormatReason(row.NotDisplayedUnits, row)} {unit}</color>\n" +
                        $"{Plugin.T("Inne (bez straty $)", "Other (no lost $)")}: <color=#718492>{otherMissed} {unit}</color>";

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
                        $"{Plugin.T("Koszt", "Cost")}: <color=#C2771A>{row.CurrentCost:0.00} $</color> | {Plugin.T("Cena", "Price")}: <color=#308B58>{row.CurrentPrice:0.00} $</color>\n" +
                        $"{Plugin.T("Rynkowa", "Market")}: <color=#297CA6>{row.MarketPrice:0.00} $</color>\n" +
                        $"{Plugin.T("Sugestia", "Suggestion")}: <color={AdviceColor(row.PricingAdvice)}>{row.SuggestedPrice:0.00} $</color>\n" +
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
            text.fontSizeMin = 5.8f;
            text.fontSizeMax = 7.8f;
            text.fontSize = 7.8f;
            text.lineSpacing = -1f;
            text.paragraphSpacing = 0f;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.margin = new Vector4(1f, 1f, 2f, 0f);
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

            if (_analysisView == AnalysisViewMode.MissedSales && row.StockMissedVisible > 0.0001f)
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
