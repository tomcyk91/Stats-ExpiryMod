using System;
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StatisticMod
{
    public partial class StatsAppManager
    {
        private enum ProfitSortMode
        {
            GrossProfit = 0,
            Margin = 1,
            SoldRevenue = 2,
            SoldCost = 3,
            Name = 4,
            ProductId = 5
        }

        private ProfitSortMode _profitSortMode = ProfitSortMode.GrossProfit;

        private bool HasProfitabilityActivity(ProductLine p)
        {
            if (p == null) return false;
            return p.SoldUnits > 0 || p.SoldWeightKg > 0.0001f || p.SoldRevenue > 0.0001f;
        }

        /// <summary>
        /// Profitability is only considered complete when the cost basis was captured
        /// for the whole recorded sale quantity. Legacy days therefore show a dash
        /// instead of an incorrect 100% margin.
        /// </summary>
        private bool HasCompleteProfitabilityData(ProductLine p)
        {
            if (p == null || !HasProfitabilityActivity(p)) return false;

            bool useKg = IsWeightProduct(p.ProductId) || p.SoldWeightKg > 0.0001f;
            if (useKg)
            {
                if (p.SoldWeightKg <= 0.0001f) return false;
                float tolerance = Mathf.Max(0.001f, p.SoldWeightKg * 0.001f);
                return p.CostedWeightKg + tolerance >= p.SoldWeightKg;
            }

            if (p.SoldUnits <= 0) return false;
            return p.CostedUnits >= p.SoldUnits;
        }

        private bool TryGetProfitability(ProductLine p, out float revenue, out float cost, out float grossProfit, out float marginPercent)
        {
            revenue = p != null ? Mathf.Max(0f, p.SoldRevenue) : 0f;
            cost = p != null ? Mathf.Max(0f, p.SoldCost) : 0f;
            grossProfit = 0f;
            marginPercent = 0f;

            if (!HasCompleteProfitabilityData(p)) return false;

            grossProfit = revenue - cost;
            if (revenue > 0.0001f)
                marginPercent = (grossProfit / revenue) * 100f;

            return true;
        }

        private string GetProfitSortLabel(ProfitSortMode mode)
        {
            return mode switch
            {
                ProfitSortMode.GrossProfit => Plugin.T("ZYSK", "PROFIT"),
                ProfitSortMode.Margin => Plugin.T("MARŻA", "MARGIN"),
                ProfitSortMode.SoldRevenue => Plugin.T("PRZYCHÓD", "REVENUE"),
                ProfitSortMode.SoldCost => Plugin.T("KOSZT", "COST"),
                ProfitSortMode.Name => Plugin.T("NAZWA", "NAME"),
                ProfitSortMode.ProductId => "ID",
                _ => "?"
            };
        }

        private void UpdateProfitabilityTileText(TextMeshProUGUI tmp, ProductLine p)
        {
            if (tmp == null || p == null) return;

            float revenue = Mathf.Max(0f, p.SoldRevenue);
            bool complete = TryGetProfitability(p, out _, out float cost, out float grossProfit, out float margin);

            string dash = $"<color={StatsAppTheme.MutedHex}><b>—</b></color>";
            string profitColor = grossProfit >= 0f ? StatsAppTheme.PositiveHex : StatsAppTheme.NegativeHex;
            string marginColor = margin >= 0f ? StatsAppTheme.PurpleHex : StatsAppTheme.NegativeHex;

            // Revenue and cost are totals for the sold quantity, not unit prices.
            // Showing the quantity directly in the labels makes this obvious when
            // comparing this view with the current unit prices in the Products tab.
            bool useKg = IsWeightProduct(p.ProductId) || p.SoldWeightKg > 0.0001f;
            string soldAmount = useKg
                ? $"{p.SoldWeightKg:0.000} kg"
                : $"{p.SoldUnits} {Plugin.T("szt.", "pcs")}";

            string newText =
                $"{Plugin.T("Przychód", "Revenue")} ({soldAmount}): <color={StatsAppTheme.PositiveHex}><b>{Plugin.Money(revenue)}</b></color>\n" +
                $"{Plugin.T("Koszt", "Cost")} ({soldAmount}): " + (complete
                    ? $"<color={StatsAppTheme.WarningHex}><b>{Plugin.Money(cost)}</b></color>"
                    : dash) + "\n" +
                $"{Plugin.T("Zysk brutto", "Gross profit")}: " + (complete
                    ? $"<color={profitColor}><b>{Plugin.Money(grossProfit)}</b></color>"
                    : dash) + "\n" +
                $"{Plugin.T("Marża", "Margin")}: " + (complete
                    ? $"<color={marginColor}><b>{margin:0.0}%</b></color>"
                    : dash);

            if (tmp.text != newText)
                tmp.text = newText;
        }

        private int CompareProfitabilityProducts(ProductLine a, ProductLine b)
        {
            bool aKnown = TryGetProfitability(a, out float aRevenue, out float aCost, out float aProfit, out float aMargin);
            bool bKnown = TryGetProfitability(b, out float bRevenue, out float bCost, out float bProfit, out float bMargin);

            // Unknown legacy profitability always stays at the bottom for cost/profit/margin sorting.
            bool requiresCost = _profitSortMode == ProfitSortMode.GrossProfit ||
                                _profitSortMode == ProfitSortMode.Margin ||
                                _profitSortMode == ProfitSortMode.SoldCost;
            if (requiresCost && aKnown != bKnown)
                return aKnown ? -1 : 1;

            int cmp = 0;
            switch (_profitSortMode)
            {
                case ProfitSortMode.Name:
                    cmp = string.Compare(
                        GetProductNameSafe(a.ProductId),
                        GetProductNameSafe(b.ProductId),
                        false,
                        ModLocalization.CurrentCulture);
                    break;
                case ProfitSortMode.ProductId:
                    cmp = a.ProductId.CompareTo(b.ProductId);
                    break;
                case ProfitSortMode.SoldRevenue:
                    cmp = aRevenue.CompareTo(bRevenue);
                    break;
                case ProfitSortMode.SoldCost:
                    cmp = aCost.CompareTo(bCost);
                    break;
                case ProfitSortMode.Margin:
                    cmp = aMargin.CompareTo(bMargin);
                    break;
                case ProfitSortMode.GrossProfit:
                default:
                    cmp = aProfit.CompareTo(bProfit);
                    break;
            }

            if (cmp == 0) cmp = a.ProductId.CompareTo(b.ProductId);
            return cmp * (_sortAsc ? 1 : -1);
        }

        private void AddProfitabilityStatusBar(Transform tile, ProductLine p)
        {
            if (tile == null) return;

            Transform existing = tile.Find("StatusBar");
            Image img = existing != null ? existing.GetComponent<Image>() : null;
            if (img == null)
            {
                var bar = new GameObject("StatusBar");
                bar.transform.SetParent(tile, false);

                var rt = bar.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0.018f, 1f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                bar.AddComponent<CanvasRenderer>();
                img = bar.AddComponent<Image>();
                img.raycastTarget = false;
            }

            if (!TryGetProfitability(p, out _, out _, out float profit, out float margin))
            {
                img.color = StatsAppTheme.Warning;
                return;
            }

            if (profit < -0.001f || margin < 0f)
                img.color = StatsAppTheme.Negative;
            else if (margin < 10f)
                img.color = StatsAppTheme.Warning;
            else
                img.color = StatsAppTheme.Positive;
        }

        private void RefreshProfitabilityLive()
        {
            if (_tilesContent == null) return;

            DayStats ds = StatsStore.TryGetDay(_selectedDay);
            if (ds == null || ds.Products == null) return;

            var sorted = new List<ProductLine>();
            for (int i = 0; i < ds.Products.Count; i++)
            {
                ProductLine line = ds.Products[i];
                if (HasProfitabilityActivity(line)) sorted.Add(line);
            }
            sorted.Sort(CompareProfitabilityProducts);

            for (int i = 0; i < sorted.Count; i++)
            {
                ProductLine p = sorted[i];
                Transform tile = _tilesContent.Find("ProfitTile_" + p.ProductId);
                if (tile == null) continue;

                tile.SetSiblingIndex(i);
                if (_infoTmpByPid.TryGetValue(p.ProductId, out TextMeshProUGUI tmp))
                    UpdateProfitabilityTileText(tmp, p);
            }
        }

        private void BuildProfitabilityTiles()
        {
            try
            {
                if (_tilesContent == null) return;

                for (int i = _tilesContent.childCount - 1; i >= 0; i--)
                {
                    GameObject child = _tilesContent.GetChild(i).gameObject;
                    if (child != null) UnityEngine.Object.DestroyImmediate(child);
                }

                _infoTmpByPid.Clear();
                EnsureSelectedDayInitialized();

                int day = _selectedDay;
                DayStats ds = StatsStore.TryGetDay(day);
                if (ds == null || ds.Products == null || ds.Products.Count == 0)
                {
                    UpdateDayHeaderUI();
                    return;
                }

                if (Plugin.ProductCache != null && Plugin.ProductCache.Count == 0)
                {
                    var idm = UnityEngine.Object.FindFirstObjectByType<global::IDManager>();
                    if (idm != null) Plugin.ProductCache.Build(idm);
                }

                var products = new List<ProductLine>();
                for (int i = 0; i < ds.Products.Count; i++)
                {
                    ProductLine line = ds.Products[i];
                    if (HasProfitabilityActivity(line)) products.Add(line);
                }
                products.Sort(CompareProfitabilityProducts);

                int built = 0;
                for (int i = 0; i < products.Count; i++)
                {
                    ProductLine p = products[i];
                    if (p == null) continue;

                    GameObject tile = UnityEngine.Object.Instantiate(_tileTemplate, _tilesContent, false);
                    tile.name = "ProfitTile_" + p.ProductId;

                    string title = $"Product #{p.ProductId}";
                    Sprite icon = null;
                    if (Plugin.ProductCache != null && Plugin.ProductCache.TryGet(p.ProductId, out string name, out Sprite sprite))
                    {
                        if (!string.IsNullOrEmpty(name)) title = name;
                        icon = sprite;
                    }

                    title = FormatTileProductName(title, p.ProductId, false);
                    SetTmpText(tile.transform, "Product Name", title);

                    Transform iconTr = tile.transform.Find("Product Icon");
                    if (iconTr != null)
                    {
                        Image img = iconTr.GetComponent<Image>();
                        if (img != null)
                        {
                            img.preserveAspect = true;
                            img.sprite = icon;
                            img.enabled = img.sprite != null;
                        }
                    }

                    Transform infoTr = tile.transform.Find("Product Brand");
                    if (infoTr != null)
                    {
                        TextMeshProUGUI infoTmp = infoTr.GetComponent<TextMeshProUGUI>();
                        if (infoTmp != null)
                        {
                            _infoTmpByPid[p.ProductId] = infoTmp;
                            UpdateProfitabilityTileText(infoTmp, p);
                            AttachProfitabilityInfoTooltipZones(tile.transform, infoTmp);
                        }
                    }

                    try
                    {
                        AddTileShadow(tile.transform);
                        AddProfitabilityStatusBar(tile.transform, p);
                        ApplyStatsPremiumTypography(tile.transform);
                    }
                    catch { }

                    DisableRaycastOnAllTMP(tile.transform);
                    ForceProfessionalTileVisuals(tile.transform);
                    tile.SetActive(true);
                    built++;
                }

                GridLayoutGroup grid = _tilesContent.GetComponent<GridLayoutGroup>();
                if (grid != null)
                {
                    float rows = Mathf.Ceil(built / (float)grid.constraintCount);
                    float newHeight = grid.padding.top + grid.padding.bottom +
                                      (rows * grid.cellSize.y) +
                                      (Mathf.Max(0, rows - 1) * grid.spacing.y);
                    RectTransform rt = _tilesContent.GetComponent<RectTransform>();
                    if (rt != null) rt.sizeDelta = new Vector2(rt.sizeDelta.x, newHeight);
                }

                ReapplyProfessionalTileStyles();
                ScheduleProfessionalTileReapply();
                UpdateDayHeaderUI();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[StatsUI] BuildProfitabilityTiles failed: " + ex);
            }
        }
    }
}
