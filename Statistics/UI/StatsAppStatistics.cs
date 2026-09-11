using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using PG;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static UnityEngine.UI.ContentSizeFitter;
using SmartExpiration;

namespace StatisticMod
{
    public partial class StatsAppManager : FakeMonoBehaviour
    {
        private void RefreshStatsLive()
        {
            if (_tilesContent == null) return;

            var ds = StatsStore.TryGetDay(_selectedDay);
            if (ds == null || ds.Products == null) return;

            // 1. Sortujemy kopię danych
            var sorted = new List<ProductLine>(ds.Products);
            int dir = _sortAsc ? 1 : -1;
            var polishCulture = new System.Globalization.CultureInfo("pl-PL");

            sorted.Sort((a, b) =>
            {
                int cmp = 0;
                switch (_statsSortMode)
                {
                    case StatsSortMode.Name:
                        cmp = string.Compare(GetProductNameSafe(a.ProductId), GetProductNameSafe(b.ProductId), polishCulture, System.Globalization.CompareOptions.IgnoreCase);
                        break;
                    case StatsSortMode.ProductId:
                        cmp = a.ProductId.CompareTo(b.ProductId);
                        break;
                    case StatsSortMode.SoldUnits:
                        cmp = GetSoldVisibleValue(a).CompareTo(GetSoldVisibleValue(b));
                        break;
                    case StatsSortMode.SoldRevenue:
                        cmp = GetRevenueVisibleValue(a).CompareTo(GetRevenueVisibleValue(b));
                        break;
                    case StatsSortMode.ThrownUnits:
                        cmp = GetThrownVisibleValue(a).CompareTo(GetThrownVisibleValue(b));
                        break;
                    case StatsSortMode.ThrownValue:
                        cmp = GetLossVisibleValue(a).CompareTo(GetLossVisibleValue(b));
                        break;
                }
                if (cmp == 0) cmp = a.ProductId.CompareTo(b.ProductId);
                return cmp * dir;
            });

            // 2. Aktualizujemy kolejność istniejących kafelków
            for (int i = 0; i < sorted.Count; i++)
            {
                var p = sorted[i];
                // Szukamy kafelka po nazwie (którą nadaliśmy w BuildStatsTiles)
                Transform tile = _tilesContent.Find("StatsTile_" + p.ProductId);

                if (tile != null)
                {
                    // SetSiblingIndex zmienia pozycję w pionowej liście/siatce
                    tile.SetSiblingIndex(i);

                    // 3. Od razu aktualizujemy tekst (żeby kwoty rosły "w oczach")
                    if (_infoTmpByPid.TryGetValue(p.ProductId, out var tmp))
                    {
                        UpdateTileText(tmp, p);
                    }
                }
            }
        }

        private void UpdateTileText(TextMeshProUGUI tmp, ProductLine p)
        {
            if (tmp == null || p == null) return;

            bool expiryEnabled = SmartExpiration.PluginConfig.ExpiryEnabled;
            bool useKg = IsWeightProduct(p.ProductId) || p.SoldWeightKg > 0.0001f;

            float soldVal = GetSoldVisibleValue(p);
            float revenue = GetRevenueVisibleValue(p);
            float share = GetRevenueSharePercent(p);

            string unitSuffix = useKg ? "kg" : Plugin.T("szt.", "pcs");
            string soldValTxt = useKg ? $"{soldVal:0.000}" : $"{Mathf.RoundToInt(soldVal)}";

            string newText =
                $"{Plugin.T("Sprzedane", "Sold")}: <color={StatsAppTheme.InfoHex}><b>{soldValTxt} {unitSuffix}</b></color>\n" +
                $"{Plugin.T("Przychód", "Revenue")}: <color={StatsAppTheme.PositiveHex}><b>{Plugin.Money(revenue)}</b></color>\n" +
                $"{Plugin.T("Trend", "Trend")}: {FormatSalesTrendRichText(p)}\n" +
                $"{Plugin.T("Udział", "Share")}: <color={StatsAppTheme.PurpleHex}><b>{share:0.0}%</b></color>";

            if (expiryEnabled)
            {
                float thrownVal = GetThrownVisibleValue(p);
                float loss = GetLossVisibleValue(p);
                string thrownValTxt = useKg ? $"{thrownVal:0.000}" : $"{Mathf.RoundToInt(thrownVal)}";

                newText +=
                    $"\n{Plugin.T("Wyrzucone", "Wasted")}: <color={StatsAppTheme.WarningHex}><b>{thrownValTxt} {unitSuffix}</b></color>" +
                    $"\n{Plugin.T("Strata", "Loss")}: <color={StatsAppTheme.NegativeHex}><b>{Plugin.Money(loss)}</b></color>";
            }

            if (tmp.text != newText)
                tmp.text = newText;
        }

        private bool HasVisibleStatsActivity(ProductLine p)
        {
            if (p == null) return false;

            bool hasSales = p.SoldUnits != 0 || p.SoldWeightKg > 0.0001f;
            if (hasSales) return true;

            if (!SmartExpiration.PluginConfig.ExpiryEnabled)
                return false;

            return p.ThrownUnits != 0 || p.ThrownWeightKg > 0.0001f;
        }

        private ProductLine FindProductLineForDay(int day, int productId)
        {
            if (day < 1) return null;

            var ds = StatsStore.TryGetDay(day);
            if (ds == null || ds.Products == null) return null;

            for (int i = 0; i < ds.Products.Count; i++)
            {
                var line = ds.Products[i];
                if (line != null && line.ProductId == productId)
                    return line;
            }

            return null;
        }

        private bool TryGetSalesTrendPercent(ProductLine p, out float percent)
        {
            percent = 0f;
            if (p == null || _selectedDay <= 1) return false;

            ProductLine previous = FindProductLineForDay(_selectedDay - 1, p.ProductId);
            if (previous == null) return false;

            float previousSold = GetSoldVisibleValue(previous);
            if (previousSold <= 0.0001f) return false;

            float currentSold = GetSoldVisibleValue(p);
            percent = ((currentSold - previousSold) / previousSold) * 100f;
            return true;
        }

        private float GetRevenueSharePercent(ProductLine p)
        {
            if (p == null) return 0f;

            var ds = StatsStore.TryGetDay(_selectedDay);
            if (ds == null || ds.SoldRevenue <= 0.0001f) return 0f;

            return Mathf.Max(0f, (GetRevenueVisibleValue(p) / ds.SoldRevenue) * 100f);
        }

        private string FormatSalesTrendRichText(ProductLine p)
        {
            if (!TryGetSalesTrendPercent(p, out float trend))
                return $"<color={StatsAppTheme.MutedHex}><b>—</b></color>";

            string value = trend.ToString("+0.0;-0.0;0.0") + "%";
            string color = trend > 0.05f
                ? StatsAppTheme.PositiveHex
                : trend < -0.05f
                    ? StatsAppTheme.NegativeHex
                    : StatsAppTheme.InfoHex;

            return $"<color={color}><b>{value}</b></color>";
        }

        private void BuildStatsTiles()
        {
            try
            {
                Plugin.DebugLog($"[{DateTime.Now:HH:mm:ss.fff}] [StatsUI] BuildStatsTiles START");

                if (_tilesContent == null) return;

                // 1. ⚡ NATYCHMIASTOWE CZYSZCZENIE (Kluczowe przy freeze'ach)
                // DestroyImmediate jest bezpieczniejsze w środowisku IL2CPP przy dużych przebudowach UI,
                // bo nie zostawia "duchów" obiektów do końca klatki.
                for (int i = _tilesContent.childCount - 1; i >= 0; i--)
                {
                    var child = _tilesContent.GetChild(i).gameObject;
                    if (child != null)
                    {
                        UnityEngine.Object.DestroyImmediate(child);
                    }
                }

                _infoTmpByPid.Clear();

                EnsureSelectedDayInitialized();
                int day = _selectedDay;
                var ds = StatsStore.TryGetDay(day);

                if (ds == null || ds.Products == null || ds.Products.Count == 0)
                {
                    Plugin.DebugLog($"[{DateTime.Now:HH:mm:ss.fff}] [StatsUI] Brak danych dla dnia {day}");
                    UpdateDayHeaderUI();
                    return;
                }

                // 2. REBUILD CACHE (Jeśli potrzebny)
                if (Plugin.ProductCache != null && Plugin.ProductCache.Count == 0)
                {
                    var idm = UnityEngine.Object.FindFirstObjectByType<global::IDManager>();
                    if (idm != null) Plugin.ProductCache.Build(idm);
                }

                // 3. SORTOWANIE
                var products = new List<ProductLine>(ds.Products);
                var polishCulture = new System.Globalization.CultureInfo("pl-PL");
                int dir = _sortAsc ? 1 : -1;

                products.Sort((a, b) =>
                {
                    int cmp = 0;
                    switch (_statsSortMode)
                    {
                        case StatsSortMode.Name:
                            cmp = string.Compare(GetProductNameSafe(a.ProductId), GetProductNameSafe(b.ProductId), polishCulture, System.Globalization.CompareOptions.IgnoreCase);
                            break;
                        case StatsSortMode.ProductId:
                            cmp = a.ProductId.CompareTo(b.ProductId);
                            break;
                        case StatsSortMode.SoldUnits:
                            cmp = GetSoldVisibleValue(a).CompareTo(GetSoldVisibleValue(b));
                            break;
                        case StatsSortMode.SoldRevenue:
                            cmp = GetRevenueVisibleValue(a).CompareTo(GetRevenueVisibleValue(b));
                            break;
                        case StatsSortMode.ThrownUnits:
                            cmp = GetThrownVisibleValue(a).CompareTo(GetThrownVisibleValue(b));
                            break;
                        case StatsSortMode.ThrownValue:
                            cmp = GetLossVisibleValue(a).CompareTo(GetLossVisibleValue(b));
                            break;
                    }
                    if (cmp == 0) cmp = a.ProductId.CompareTo(b.ProductId);
                    return cmp * dir;
                });

                // 4. BUDOWANIE KAFELKÓW
                int built = 0;
                for (int i = 0; i < products.Count; i++)
                {
                    var p = products[i];
                    if (p == null) continue;

                    // Pomiń puste wpisy
                    if (!HasVisibleStatsActivity(p))
                        continue;

                    // Instantiate
                    var tile = UnityEngine.Object.Instantiate(_tileTemplate, _tilesContent, false);
                    tile.name = "StatsTile_" + p.ProductId;

                    // Dane produktu
                    string title = $"Produkt #{p.ProductId}";
                    Sprite icon = null;

                    if (Plugin.ProductCache != null && Plugin.ProductCache.TryGet(p.ProductId, out var n, out var sp))
                    {
                        if (!string.IsNullOrEmpty(n)) title = n;
                        icon = sp;
                    }

                    title = FormatTileProductName(title, p.ProductId, false);
                    SetTmpText(tile.transform, "Product Name", title);

                    // Ikona
                    var iconTr = tile.transform.Find("Product Icon");
                    if (iconTr != null)
                    {
                        var img = iconTr.GetComponent<UnityEngine.UI.Image>();
                        if (img != null)
                        {
                            img.preserveAspect = true;
                            img.sprite = icon;
                            img.enabled = (img.sprite != null);
                        }
                    }

                    // Tekst info (Brand / Stats)
                    var infoTr = tile.transform.Find("Product Brand");
                    if (infoTr != null)
                    {
                        var infoTmp = infoTr.GetComponent<TextMeshProUGUI>();
                        if (infoTmp != null)
                        {
                            _infoTmpByPid[p.ProductId] = infoTmp;
                            UpdateTileText(infoTmp, p);
                            AttachStatsInfoTooltipZones(tile.transform, infoTmp);
                        }
                    }

                    // Wizualia Premium (Shadows/Bars)
                    try
                    {
                        AddTileShadow(tile.transform);
                        AddStatsStatusBar(tile.transform, p);
                        ApplyStatsPremiumTypography(tile.transform);
                    }
                    catch { /* Ignorujemy błędy wizualne, by nie przerwać pętli */ }

                    DisableRaycastOnAllTMP(tile.transform);
                    ForceProfessionalTileVisuals(tile.transform);
                    tile.SetActive(true);
                    built++;
                }

                // 5. ⚡ AKTUALIZACJA WYSOKOŚCI (Bez ForceUpdate)
                // Obliczamy wysokość ręcznie, aby ScrollRect wiedział ile ma przewijać,
                // ale nie wymuszamy przebudowy całego Canvasa (to zapobiega Alt+F4).
                var grid = _tilesContent.GetComponent<UnityEngine.UI.GridLayoutGroup>();
                if (grid != null)
                {
                    float rows = Mathf.Ceil(built / (float)grid.constraintCount);
                    float newHeight = grid.padding.top + grid.padding.bottom + (rows * grid.cellSize.y) + (Mathf.Max(0, rows - 1) * grid.spacing.y);

                    var rt = _tilesContent.GetComponent<RectTransform>();
                    if (rt != null) rt.sizeDelta = new Vector2(rt.sizeDelta.x, newHeight);
                }

                // Pass 2b: drugi etap wymuszenia po zbudowaniu całej siatki.
                ReapplyProfessionalTileStyles();
                ScheduleProfessionalTileReapply();

                UpdateDayHeaderUI();
                Plugin.Log.LogWarning($"[{DateTime.Now:HH:mm:ss.fff}] [StatsUI] BuildStatsTiles END - Sukces (Zbudowano: {built})");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[{DateTime.Now:HH:mm:ss.fff}] [StatsUI] BuildStatsTiles CRASH: {e.Message}");
            }
        }

        private void AddStatsStatusBar(Transform tile, ProductLine p)
        {
            if (tile == null) return;

            Transform existing = tile.Find("StatusBar");
            Image img = existing != null ? existing.GetComponent<Image>() : null;

            if (img == null)
            {
                var bar = new GameObject("StatusBar");
                bar.transform.SetParent(tile, false);

                var brt = bar.AddComponent<RectTransform>();
                brt.anchorMin = new Vector2(0f, 0f);
                brt.anchorMax = new Vector2(0.018f, 1f);
                brt.offsetMin = Vector2.zero;
                brt.offsetMax = Vector2.zero;

                bar.AddComponent<CanvasRenderer>();
                img = bar.AddComponent<Image>();
                img.raycastTarget = false;
            }

            if (!SmartExpiration.PluginConfig.ExpiryEnabled)
            {
                img.color = StatsAppTheme.Positive;
                return;
            }

            float revenue = (float)p.SoldRevenue;
            float loss = (float)p.ThrownValue;
            float ratio = (revenue <= 0.01f) ? (loss > 0.01f ? 999f : 0f) : (loss / revenue);

            if (p.ThrownUnits <= 0 && p.ThrownWeightKg <= 0.0001f)
                img.color = StatsAppTheme.Positive;
            else if (ratio >= 0.50f)
                img.color = StatsAppTheme.Negative;
            else
                img.color = StatsAppTheme.Warning;
        }

        private void ApplyStatsPremiumTypography(Transform tile)
        {
            var name = tile.Find("Product Name")?.GetComponent<TextMeshProUGUI>();
            if (name != null)
            {
                name.fontSize = 11.5f;
                name.fontStyle = FontStyles.Bold;
                if (_gameFont != null) name.font = _gameFont;
                name.color = StatsAppTheme.TileTitle;
                SafeSetOutline(name, 0f);
                name.enableAutoSizing = true;
                name.fontSizeMin = 8f;
                name.fontSizeMax = 11.5f;
                name.enableWordWrapping = false;
                name.overflowMode = TextOverflowModes.Ellipsis;
                name.alignment = TextAlignmentOptions.MidlineLeft;
            }

            var info = tile.Find("Product Brand")?.GetComponent<TextMeshProUGUI>();
            if (info != null)
            {
                bool denseStats = SmartExpiration.PluginConfig.ExpiryEnabled &&
                                  tile.name.StartsWith("StatsTile_", StringComparison.Ordinal);

                info.fontSize = denseStats ? 7.5f : 8.7f;
                info.fontStyle = FontStyles.Normal;
                if (_gameFont != null) info.font = _gameFont;
                info.color = StatsAppTheme.TileText;
                SafeSetOutline(info, 0f);
                info.enableAutoSizing = true;
                info.fontSizeMin = denseStats ? 5.6f : 6.8f;
                info.fontSizeMax = denseStats ? 7.5f : 8.7f;
                info.lineSpacing = denseStats ? -1f : 0f;
                info.enableWordWrapping = false;
                info.overflowMode = TextOverflowModes.Ellipsis;
                info.alignment = TextAlignmentOptions.TopLeft;
            }
        }

        private float GetSoldVisibleValue(ProductLine p)
        {
            if (p == null) return 0f;
            bool useKg = IsWeightProduct(p.ProductId) || p.SoldWeightKg > 0.0001f;
            return useKg ? p.SoldWeightKg : p.SoldUnits;
        }

        private float GetThrownVisibleValue(ProductLine p)
        {
            if (p == null) return 0f;
            bool useKg = IsWeightProduct(p.ProductId) || p.SoldWeightKg > 0.0001f;

            if (useKg)
            {
                // Jeśli mamy zapisaną wagę - używamy jej
                if (p.ThrownWeightKg > 0.0001f) return p.ThrownWeightKg;

                // Jeśli wagi brak, ale jest strata ($), wyliczamy kg wstecz (Strata / Koszt)
                float cost = GetCurrentCost(p.ProductId);
                if (cost > 0.01f && p.ThrownValue > 0.01f) return p.ThrownValue / cost;

                return 0f;
            }
            return p.ThrownUnits;
        }

        private float GetRevenueVisibleValue(ProductLine p)
        {
            if (p == null) return 0f;
            bool useKg = IsWeightProduct(p.ProductId) || p.SoldWeightKg > 0.0001f;
            if (useKg)
            {
                float price = GetCurrentPrice(p.ProductId);
                float live = p.SoldWeightKg * price;
                return (live > 0.001f) ? live : p.SoldRevenue; // Fallback do zapisu jeśli cena=0
            }
            return p.SoldRevenue;
        }

        private float GetLossVisibleValue(ProductLine p)
        {
            if (p == null) return 0f;
            bool useKg = IsWeightProduct(p.ProductId) || p.SoldWeightKg > 0.0001f;
            if (useKg)
            {
                float cost = GetCurrentCost(p.ProductId);
                float live = p.ThrownWeightKg * cost;
                return (live > 0.001f) ? live : p.ThrownValue; // Fallback do zapisu
            }
            return p.ThrownValue;
        }

    }
}
