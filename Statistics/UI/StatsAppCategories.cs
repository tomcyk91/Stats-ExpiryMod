using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StatisticMod
{
    public partial class StatsAppManager
    {
        private enum CategoryBucket
        {
            Edible = 0,
            Drink = 1,
            Cleaning = 2,
            Book = 3,
            Clothing = 4,
            Electronics = 5,
            Hardware = 6,
            Kitchen = 7,
            Bakery = 8,
            Oven = 9,
            IceCream = 10,
            Other = 11
        }

        private enum CategorySortMode
        {
            GrossProfit = 0,
            SoldRevenue = 1,
            Margin = 2,
            MissedRevenue = 3,
            Name = 4
        }

        private sealed class CategoryStatsRow
        {
            public CategoryBucket Bucket;
            public HashSet<int> ProductIds = new HashSet<int>();

            public int SoldUnits;
            public float SoldWeightKg;
            public float SoldRevenue;

            public float SoldCost;
            public bool ProfitabilityComplete = true;

            public int RequestedUnits;
            public float RequestedWeightKg;
            public int MissedUnits;
            public float MissedWeightKg;
            public float MissedRevenue;

            public int RepresentativeProductId;
            public float RepresentativeRevenue;

            public float GrossProfit =>
                ProfitabilityComplete ? SoldRevenue - SoldCost : 0f;

            public float Margin =>
                ProfitabilityComplete && SoldRevenue > 0.0001f
                    ? (GrossProfit / SoldRevenue) * 100f
                    : 0f;

            public bool HasActivity =>
                SoldUnits > 0 ||
                SoldWeightKg > 0.0001f ||
                SoldRevenue > 0.0001f ||
                RequestedUnits > 0 ||
                RequestedWeightKg > 0.0001f ||
                MissedUnits > 0 ||
                MissedWeightKg > 0.0001f ||
                MissedRevenue > 0.0001f;
        }

        private CategorySortMode _categorySortMode = CategorySortMode.GrossProfit;
        private int _categoryLiveSignature = int.MinValue;

        private string GetCategorySortLabel(CategorySortMode mode)
        {
            return mode switch
            {
                CategorySortMode.GrossProfit => Plugin.T("ZYSK", "PROFIT"),
                CategorySortMode.SoldRevenue => Plugin.T("PRZYCHÓD", "REVENUE"),
                CategorySortMode.Margin => Plugin.T("MARŻA", "MARGIN"),
                CategorySortMode.MissedRevenue => Plugin.T("UTRACONA SPRZEDAŻ", "MISSED SALES"),
                CategorySortMode.Name => Plugin.T("NAZWA", "NAME"),
                _ => "?"
            };
        }

        private string GetCategoryName(CategoryBucket bucket)
        {
            return bucket switch
            {
                CategoryBucket.Edible => Plugin.T("ŻYWNOŚĆ", "EDIBLE"),
                CategoryBucket.Drink => Plugin.T("NAPOJE", "DRINKS"),
                CategoryBucket.Cleaning => Plugin.T("CHEMIA", "CLEANING"),
                CategoryBucket.Book => Plugin.T("KSIĄŻKI", "BOOKS"),
                CategoryBucket.Clothing => Plugin.T("ODZIEŻ", "CLOTHING"),
                CategoryBucket.Electronics => Plugin.T("ELEKTRONIKA", "ELECTRONICS"),
                CategoryBucket.Hardware => Plugin.T("NARZĘDZIA", "HARDWARE"),
                CategoryBucket.Kitchen => Plugin.T("KUCHNIA", "KITCHEN"),
                CategoryBucket.Bakery => Plugin.T("PIEKARNIA", "BAKERY"),
                CategoryBucket.Oven => Plugin.T("WYPIEKI", "OVEN"),
                CategoryBucket.IceCream => Plugin.T("LODY", "ICE CREAM"),
                _ => Plugin.T("INNE", "OTHER")
            };
        }

        private CategoryBucket GetCategoryBucket(int productId)
        {
            if (productId == 9999)
                return CategoryBucket.IceCream;

            try
            {
                if (Plugin.ProductCache != null &&
                    Plugin.ProductCache.TryGetSO(productId, out ProductSO so) &&
                    so != null)
                {
                    var category = so.Category;

                    if (category == ProductSO.ProductCategory.EDIBLE) return CategoryBucket.Edible;
                    if (category == ProductSO.ProductCategory.DRINK) return CategoryBucket.Drink;
                    if (category == ProductSO.ProductCategory.CLEANING) return CategoryBucket.Cleaning;
                    if (category == ProductSO.ProductCategory.BOOK) return CategoryBucket.Book;
                    if (category == ProductSO.ProductCategory.CLOTHING) return CategoryBucket.Clothing;
                    if (category == ProductSO.ProductCategory.ELECTRONICS) return CategoryBucket.Electronics;
                    if (category == ProductSO.ProductCategory.HARDWARE) return CategoryBucket.Hardware;
                    if (category == ProductSO.ProductCategory.KITCHEN) return CategoryBucket.Kitchen;
                    if (category == ProductSO.ProductCategory.BAKERY) return CategoryBucket.Bakery;
                    if (category == ProductSO.ProductCategory.OVEN) return CategoryBucket.Oven;
                    if (category == ProductSO.ProductCategory.ICE_CREAM) return CategoryBucket.IceCream;
                }
            }
            catch { }

            return CategoryBucket.Other;
        }

        private BusinessDayData GetCategoryBusinessDay(int day)
        {
            BusinessDayData result = null;

            try
            {
                result = BusinessAnalysisStore.TryGetDay(day);
                if (result != null) return result;

                BusinessDayData open = BusinessAnalysisStore.GetOpenDay();
                if (open != null && open.Day == day)
                    return open;
            }
            catch { }

            return null;
        }

        private List<CategoryStatsRow> BuildCategoryRows(int day)
        {
            if (Plugin.ProductCache != null && Plugin.ProductCache.Count == 0)
            {
                var idm = UnityEngine.Object.FindFirstObjectByType<global::IDManager>();
                if (idm != null) Plugin.ProductCache.Build(idm);
            }

            var map = new Dictionary<CategoryBucket, CategoryStatsRow>();

            CategoryStatsRow GetRow(CategoryBucket bucket)
            {
                if (!map.TryGetValue(bucket, out CategoryStatsRow row))
                {
                    row = new CategoryStatsRow { Bucket = bucket };
                    map[bucket] = row;
                }
                return row;
            }

            DayStats stats = StatsStore.TryGetDay(day);
            if (stats?.Products != null)
            {
                for (int i = 0; i < stats.Products.Count; i++)
                {
                    ProductLine p = stats.Products[i];
                    if (p == null || p.ProductId <= 0) continue;

                    bool hasStats =
                        p.SoldUnits > 0 ||
                        p.SoldWeightKg > 0.0001f ||
                        p.SoldRevenue > 0.0001f;

                    if (!hasStats) continue;

                    CategoryStatsRow row = GetRow(GetCategoryBucket(p.ProductId));
                    row.ProductIds.Add(p.ProductId);

                    bool weighted = IsWeightProduct(p.ProductId) || p.SoldWeightKg > 0.0001f;
                    if (weighted) row.SoldWeightKg += Mathf.Max(0f, p.SoldWeightKg);
                    else row.SoldUnits += Mathf.Max(0, p.SoldUnits);

                    row.SoldRevenue += Mathf.Max(0f, p.SoldRevenue);

                    if (HasCompleteProfitabilityData(p))
                    {
                        row.SoldCost += Mathf.Max(0f, p.SoldCost);
                    }
                    else if (hasStats)
                    {
                        row.ProfitabilityComplete = false;
                    }

                    if (p.SoldRevenue > row.RepresentativeRevenue)
                    {
                        row.RepresentativeRevenue = p.SoldRevenue;
                        row.RepresentativeProductId = p.ProductId;
                    }
                }
            }

            BusinessDayData demandDay = GetCategoryBusinessDay(day);
            if (demandDay?.Products != null)
            {
                for (int i = 0; i < demandDay.Products.Count; i++)
                {
                    BusinessProductLine p = demandDay.Products[i];
                    if (p == null || p.ProductId <= 0) continue;

                    bool hasDemand =
                        p.RequestedUnits > 0 ||
                        p.RequestedWeightKg > 0.0001f ||
                        p.MissedUnits > 0 ||
                        p.MissedWeightKg > 0.0001f ||
                        p.MissedRevenue > 0.0001f;

                    if (!hasDemand) continue;

                    CategoryStatsRow row = GetRow(GetCategoryBucket(p.ProductId));
                    row.ProductIds.Add(p.ProductId);

                    bool weighted =
                        IsWeightProduct(p.ProductId) ||
                        p.RequestedWeightKg > 0.0001f ||
                        p.MissedWeightKg > 0.0001f;

                    if (weighted)
                    {
                        row.RequestedWeightKg += Mathf.Max(0f, p.RequestedWeightKg);
                        row.MissedWeightKg += Mathf.Max(0f, p.MissedWeightKg);
                    }
                    else
                    {
                        row.RequestedUnits += Mathf.Max(0, p.RequestedUnits);
                        row.MissedUnits += Mathf.Max(0, p.MissedUnits);
                    }

                    row.MissedRevenue += Mathf.Max(0f, p.MissedRevenue);
                }
            }

            var rows = new List<CategoryStatsRow>();
            foreach (var kv in map)
            {
                if (kv.Value != null && kv.Value.HasActivity)
                    rows.Add(kv.Value);
            }

            rows.Sort(CompareCategoryRows);
            return rows;
        }

        private int CompareCategoryRows(CategoryStatsRow a, CategoryStatsRow b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;

            bool requiresProfit =
                _categorySortMode == CategorySortMode.GrossProfit ||
                _categorySortMode == CategorySortMode.Margin;

            if (requiresProfit && a.ProfitabilityComplete != b.ProfitabilityComplete)
                return a.ProfitabilityComplete ? -1 : 1;

            int cmp = _categorySortMode switch
            {
                CategorySortMode.SoldRevenue => a.SoldRevenue.CompareTo(b.SoldRevenue),
                CategorySortMode.Margin => a.Margin.CompareTo(b.Margin),
                CategorySortMode.MissedRevenue => a.MissedRevenue.CompareTo(b.MissedRevenue),
                CategorySortMode.Name => string.Compare(
                    GetCategoryName(a.Bucket),
                    GetCategoryName(b.Bucket),
                    false,
                    ModLocalization.CurrentCulture),
                _ => a.GrossProfit.CompareTo(b.GrossProfit)
            };

            if (cmp == 0)
                cmp = ((int)a.Bucket).CompareTo((int)b.Bucket);

            return cmp * (_sortAsc ? 1 : -1);
        }

        private string FormatCategoryQuantity(int units, float kg)
        {
            bool hasUnits = units > 0;
            bool hasKg = kg > 0.0001f;

            if (hasUnits && hasKg)
                return $"{units} {Plugin.T("szt.", "pcs")} + {kg:0.00} kg";

            if (hasKg)
                return $"{kg:0.00} kg";

            return $"{units} {Plugin.T("szt.", "pcs")}";
        }

        private string BuildCategoryInfoText(CategoryStatsRow row)
        {
            string dash = $"<color={StatsAppTheme.MutedHex}><b>—</b></color>";
            string sold = FormatCategoryQuantity(row.SoldUnits, row.SoldWeightKg);
            string demand = FormatCategoryQuantity(row.RequestedUnits, row.RequestedWeightKg);
            string missed = FormatCategoryQuantity(row.MissedUnits, row.MissedWeightKg);

            string costText = row.ProfitabilityComplete
                ? $"<color={StatsAppTheme.WarningHex}><b>{Plugin.Money(row.SoldCost)}</b></color>"
                : dash;

            string profitColor =
                row.GrossProfit >= 0f ? StatsAppTheme.PositiveHex : StatsAppTheme.NegativeHex;

            string profitText = row.ProfitabilityComplete
                ? $"<color={profitColor}><b>{Plugin.Money(row.GrossProfit)}</b></color>"
                : dash;

            string marginText = row.ProfitabilityComplete
                ? $"<color={StatsAppTheme.PurpleHex}><b>{row.Margin:0.0}%</b></color>"
                : dash;

            return
                $"{Plugin.T("Sprz.", "Sold")}: <color={StatsAppTheme.InfoHex}><b>{sold}</b></color>\n" +
                $"{Plugin.T("Produkty", "Products")}: <b>{row.ProductIds.Count}</b>\n" +
                $"{Plugin.T("Przychód", "Revenue")}: <color={StatsAppTheme.PositiveHex}><b>{Plugin.Money(row.SoldRevenue)}</b></color>\n" +
                $"{Plugin.T("Koszt", "Cost")}: {costText}\n" +
                $"{Plugin.T("Zysk", "Profit")}: {profitText}\n" +
                $"{Plugin.T("Marża", "Margin")}: {marginText}\n" +
                $"{Plugin.T("Popyt", "Demand")}: <color={StatsAppTheme.InfoHex}><b>{demand}</b></color>\n" +
                $"{Plugin.T("Braki", "Missed")}: <color={StatsAppTheme.WarningHex}><b>{missed}</b></color>\n" +
                $"{Plugin.T("Utracony przychód", "Lost revenue")}: <color={StatsAppTheme.NegativeHex}><b>{Plugin.Money(row.MissedRevenue)}</b></color>";
        }

        private void ConfigureCategoryInfoText(TextMeshProUGUI tmp)
        {
            if (tmp == null) return;

            tmp.enableAutoSizing = false;
            tmp.fontSize = 6.7f;
            tmp.lineSpacing = 0f;
            tmp.paragraphSpacing = 0f;
            tmp.enableWordWrapping = false;
            tmp.maxVisibleLines = 9;
            tmp.maxVisibleCharacters = int.MaxValue;
            tmp.maxVisibleWords = int.MaxValue;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.margin = new Vector4(1f, 1f, 1f, 1f);
            tmp.color = StatsAppTheme.TileText;

            RectTransform rt = tmp.rectTransform;
            if (rt == null) return;

            rt.anchorMin = new Vector2(0.325f, 0.035f);
            rt.anchorMax = new Vector2(0.985f, 0.700f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.ForceUpdateRectTransforms();

            float availableWidth = rt.rect.width - tmp.margin.x - tmp.margin.z - 1f;
            float availableHeight = rt.rect.height - tmp.margin.y - tmp.margin.w - 1f;
            if (availableWidth <= 0f || availableHeight <= 0f) return;

            for (int attempt = 0; attempt < 4; attempt++)
            {
                Vector2 preferred = tmp.GetPreferredValues(tmp.text, Mathf.Infinity, Mathf.Infinity);
                float scale = Mathf.Min(
                    availableWidth / Mathf.Max(0.001f, preferred.x),
                    availableHeight / Mathf.Max(0.001f, preferred.y));
                if (scale >= 1f) break;

                tmp.fontSize *= scale * 0.98f;
            }

            tmp.ForceMeshUpdate();
        }

        private void AlignCategoryTooltipZones(TextMeshProUGUI tmp)
        {
            if (tmp == null || tmp.textInfo.lineCount != 9) return;

            Transform root = tmp.transform.Find("CategoryMetricTooltipZones");
            if (root == null || root.childCount != 9) return;

            Rect textRect = tmp.rectTransform.rect;
            if (textRect.height <= 0f) return;

            for (int lineIndex = 0; lineIndex < 9; lineIndex++)
            {
                RectTransform zone = root.GetChild(lineIndex).GetComponent<RectTransform>();
                if (zone == null) continue;

                TMP_LineInfo line = tmp.textInfo.lineInfo[lineIndex];
                float top = lineIndex == 0 ? textRect.yMax :
                    (tmp.textInfo.lineInfo[lineIndex - 1].descender + line.ascender) * 0.5f;
                float bottom = lineIndex == 8 ? textRect.yMin :
                    (line.descender + tmp.textInfo.lineInfo[lineIndex + 1].ascender) * 0.5f;

                zone.anchorMin = new Vector2(0f, Mathf.Clamp01((bottom - textRect.yMin) / textRect.height));
                zone.anchorMax = new Vector2(1f, Mathf.Clamp01((top - textRect.yMin) / textRect.height));
                zone.offsetMin = Vector2.zero;
                zone.offsetMax = Vector2.zero;
            }
        }

        private void AddCategoryStatusBar(Transform tile, CategoryStatsRow row)
        {
            if (tile == null || row == null) return;

            Transform existing = tile.Find("CategoryStatusBar");
            Image image = existing != null ? existing.GetComponent<Image>() : null;

            if (image == null)
            {
                var go = new GameObject("CategoryStatusBar");
                go.transform.SetParent(tile, false);

                var rt = go.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0.018f, 1f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                go.AddComponent<CanvasRenderer>();
                image = go.AddComponent<Image>();
                image.raycastTarget = false;
            }

            if (!row.ProfitabilityComplete)
                image.color = StatsAppTheme.Warning;
            else if (row.GrossProfit < -0.001f || row.Margin < 0f)
                image.color = StatsAppTheme.Negative;
            else if (row.MissedRevenue > row.SoldRevenue * 0.10f && row.MissedRevenue > 0.01f)
                image.color = StatsAppTheme.Warning;
            else
                image.color = StatsAppTheme.Positive;
        }

        private void AttachCategoryStatsTooltipZones(
            Transform tile,
            TextMeshProUGUI infoTmp)
        {
            if (tile == null || infoTmp == null) return;

            Transform root =
                BeginMetricTooltipRoot(
                    infoTmp,
                    "CategoryMetricTooltipZones");

            if (root == null) return;

            RectTransform tileRT = tile.GetComponent<RectTransform>();

            CreateMetricLineZone(root, tileRT, "CategorySold", 0, 9, 0.00f, 1.00f,
                Plugin.T("Sprzedane w kategorii", "Category sold"),
                Plugin.T(
                    "Łączna ilość sprzedana w wybranym dniu dla wszystkich produktów tej kategorii. Sztuki i kilogramy są pokazywane osobno, ponieważ nie można ich poprawnie zsumować w jedną jednostkę.",
                    "Total quantity sold on the selected day across all products in this category. Pieces and kilograms are shown separately because they cannot be meaningfully combined into one unit."));

            CreateMetricLineZone(root, tileRT, "CategoryProducts", 1, 9, 0.00f, 1.00f,
                Plugin.T("Produkty", "Products"),
                Plugin.T(
                    "Liczba różnych produktów tej kategorii, które miały sprzedaż lub zarejestrowany popyt w wybranym dniu.",
                    "Number of distinct products in this category that had sales or recorded demand on the selected day."));

            CreateMetricLineZone(root, tileRT, "CategoryRevenue", 2, 9, 0.00f, 1.00f,
                Plugin.T("Przychód kategorii", "Category revenue"),
                Plugin.T(
                    "Suma przychodu ze sprzedaży wszystkich produktów tej kategorii w wybranym dniu.",
                    "Sum of revenue from all products in this category on the selected day."));

            CreateMetricLineZone(root, tileRT, "CategoryCost", 3, 9, 0.00f, 1.00f,
                Plugin.T("Koszt sprzedanego towaru", "Cost of goods sold"),
                Plugin.T(
                    "Łączny koszt zakupowy sprzedanych produktów tej kategorii, zapisany w chwili sprzedaży. Jeśli choć część historycznej sprzedaży nie ma kosztu, mod pokazuje — zamiast liczyć błędną marżę.",
                    "Total purchase cost of sold products in this category, captured at the time of sale. If any historical sale lacks cost data, the mod shows — instead of calculating a misleading margin."));

            CreateMetricLineZone(root, tileRT, "CategoryProfit", 4, 9, 0.00f, 1.00f,
                Plugin.T("Zysk brutto kategorii", "Category gross profit"),
                Plugin.T(
                    "Przychód kategorii minus koszt sprzedanego towaru. Nie obejmuje pensji, czynszu, rachunków ani innych kosztów prowadzenia sklepu.",
                    "Category revenue minus cost of goods sold. It does not include wages, rent, bills, or other operating expenses."));

            CreateMetricLineZone(root, tileRT, "CategoryMargin", 5, 9, 0.00f, 1.00f,
                Plugin.T("Marża kategorii", "Category margin"),
                Plugin.T(
                    "Łączny zysk brutto kategorii / łączny przychód × 100%. Jest to marża ważona rzeczywistą sprzedażą, a nie prosta średnia procentów poszczególnych produktów.",
                    "Total category gross profit / total category revenue × 100%. This is a sales-weighted margin, not a simple average of individual product percentages."));

            CreateMetricLineZone(root, tileRT, "CategoryDemand", 6, 9, 0.00f, 1.00f,
                Plugin.T("Popyt kategorii", "Category demand"),
                Plugin.T(
                    "Łączna ilość, której klienci chcieli w wybranym dniu dla produktów tej kategorii. Obejmuje również niezrealizowany popyt. W starszych dniach zaimportowanych przed modułem Analiza popyt może być przybliżony sprzedażą.",
                    "Total quantity customers wanted on the selected day for products in this category, including unfulfilled demand. For older days imported before the Analysis module, demand may be approximated from sales."));

            CreateMetricLineZone(root, tileRT, "CategoryMissed", 7, 9, 0.00f, 1.00f,
                Plugin.T("Braki", "Missed demand"),
                Plugin.T(
                    "Niezrealizowana ilość popytu w tej kategorii. Sztuki i kilogramy są prezentowane osobno.",
                    "Unfulfilled demand quantity in this category. Pieces and kilograms are displayed separately."));

            CreateMetricLineZone(root, tileRT, "CategoryLostRevenue", 8, 9, 0.00f, 1.00f,
                Plugin.T("Utracony przychód", "Lost revenue"),
                Plugin.T(
                    "Szacowany przychód utracony z powodu realnych braków magazynowych lub niewystawienia produktów tej kategorii. Zdarzenia oznaczone jako „inne” nie zwiększają tej kwoty.",
                    "Estimated revenue lost due to real stock/display shortages for products in this category. Events classified as 'other' do not increase this value."));
        }

        private int GetCategoryLiveSignature()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 31 + _selectedDay;

                DayStats stats = StatsStore.TryGetDay(_selectedDay);
                if (stats?.Products != null)
                {
                    for (int i = 0; i < stats.Products.Count; i++)
                    {
                        ProductLine p = stats.Products[i];
                        if (p == null) continue;
                        hash = hash * 31 + p.ProductId;
                        hash = hash * 31 + p.SoldUnits;
                        hash = hash * 31 + Mathf.RoundToInt(p.SoldWeightKg * 100f);
                        hash = hash * 31 + Mathf.RoundToInt(p.SoldRevenue * 100f);
                        hash = hash * 31 + Mathf.RoundToInt(p.SoldCost * 100f);
                    }
                }

                BusinessDayData demand = GetCategoryBusinessDay(_selectedDay);
                if (demand?.Products != null)
                {
                    for (int i = 0; i < demand.Products.Count; i++)
                    {
                        BusinessProductLine p = demand.Products[i];
                        if (p == null) continue;
                        hash = hash * 31 + p.ProductId;
                        hash = hash * 31 + p.RequestedUnits;
                        hash = hash * 31 + p.MissedUnits;
                        hash = hash * 31 + Mathf.RoundToInt(p.RequestedWeightKg * 100f);
                        hash = hash * 31 + Mathf.RoundToInt(p.MissedWeightKg * 100f);
                        hash = hash * 31 + Mathf.RoundToInt(p.MissedRevenue * 100f);
                    }
                }

                return hash;
            }
        }

        private void RefreshCategoryStatsLive()
        {
            if (_hubMode != HubMode.Categories) return;

            int signature = GetCategoryLiveSignature();
            if (signature == _categoryLiveSignature) return;

            BuildCategoryTiles();
        }

        private void BuildCategoryTiles()
        {
            try
            {
                if (_tilesContent == null || _tileTemplate == null)
                    return;

                ClearTilesOnly();
                EnsureSelectedDayInitialized();

                List<CategoryStatsRow> rows =
                    BuildCategoryRows(_selectedDay);

                int built = 0;
                var categoryInfoTexts = new List<TextMeshProUGUI>();

                for (int i = 0; i < rows.Count; i++)
                {
                    CategoryStatsRow row = rows[i];
                    if (row == null) continue;

                    GameObject tile =
                        UnityEngine.Object.Instantiate(
                            _tileTemplate,
                            _tilesContent,
                            false);

                    tile.name = "CategoryTile_" + row.Bucket;

                    TextMeshProUGUI title =
                        GetTmpComponent(
                            tile.transform,
                            "Product Name");

                    if (title != null)
                    {
                        title.text = GetCategoryName(row.Bucket);
                        title.enableAutoSizing = true;
                        title.fontSizeMin = 7f;
                        title.fontSizeMax = 13f;
                        title.enableWordWrapping = false;
                        title.overflowMode = TextOverflowModes.Ellipsis;
                        title.alignment = TextAlignmentOptions.Left;
                    }

                    Transform iconTr =
                        tile.transform.Find("Product Icon");

                    if (iconTr != null)
                    {
                        Image image = iconTr.GetComponent<Image>();
                        if (image != null)
                        {
                            Sprite sprite = null;

                            if (row.RepresentativeProductId > 0 &&
                                Plugin.ProductCache != null)
                            {
                                Plugin.ProductCache.TryGet(
                                    row.RepresentativeProductId,
                                    out _,
                                    out sprite);
                            }

                            image.sprite = sprite;
                            image.enabled = sprite != null;
                            image.preserveAspect = true;
                        }
                    }

                    TextMeshProUGUI infoTmp =
                        GetTmpComponent(
                            tile.transform,
                            "Product Brand");

                    if (infoTmp != null)
                    {
                        infoTmp.text = BuildCategoryInfoText(row);
                        categoryInfoTexts.Add(infoTmp);
                    }

                    AdjustProductTileContent(tile.transform);

                    try
                    {
                        AddTileShadow(tile.transform);
                        AddCategoryStatusBar(tile.transform, row);
                        ApplyStatsPremiumTypography(tile.transform);
                    }
                    catch { }

                    DisableRaycastOnAllTMP(tile.transform);
                    ForceProfessionalTileVisuals(tile.transform);
                    tile.SetActive(true);
                    built++;
                }

                ForceTilesLayout(built);

                foreach (TextMeshProUGUI infoTmp in categoryInfoTexts)
                {
                    ConfigureCategoryInfoText(infoTmp);
                    AttachCategoryStatsTooltipZones(infoTmp.transform.parent, infoTmp);
                    AlignCategoryTooltipZones(infoTmp);
                }

                UpdateDayHeaderUI();
                _categoryLiveSignature = GetCategoryLiveSignature();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError(
                    "[CategoryStats] Build failed: " + ex);
            }
        }
    }
}
