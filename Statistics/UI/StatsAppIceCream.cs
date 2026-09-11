using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StatisticMod
{
    public partial class StatsAppManager
    {
        private enum IceCreamViewMode
        {
            Summary = 0,
            Cones = 1,
            Flavours = 2,
            Combinations = 3
        }

        private sealed class IceCreamConeAggregate
        {
            public int ProductId;
            public int SoldCount;
            public float Revenue;
            public float Cost;
            public int CostedCount;
        }

        private sealed class IceCreamFlavourAggregate
        {
            public int ProductId;
            public int Scoops;
            public int IceCreamCount;
        }

        private struct IceCreamRecipePart
        {
            public int ProductId;
            public int Count;
        }

        private IceCreamViewMode _iceCreamView = IceCreamViewMode.Summary;
        private int _iceCreamLiveSignature = int.MinValue;

        private string GetIceCreamViewLabel(IceCreamViewMode mode)
        {
            return mode switch
            {
                IceCreamViewMode.Summary => Plugin.T("PODSUMOWANIE", "SUMMARY"),
                IceCreamViewMode.Cones => Plugin.T("ROŻKI", "CONES"),
                IceCreamViewMode.Flavours => Plugin.T("SMAKI", "FLAVOURS"),
                IceCreamViewMode.Combinations => Plugin.T("KOMBINACJE", "COMBINATIONS"),
                _ => "?"
            };
        }

        private void RefreshIceCreamLive()
        {
            DayStats ds = StatsStore.TryGetDay(_selectedDay);
            int signature = ComputeIceCreamSignature(ds);
            if (signature == _iceCreamLiveSignature) return;

            BuildIceCreamTiles();
        }

        private int ComputeIceCreamSignature(DayStats ds)
        {
            unchecked
            {
                int hash = 17;
                if (ds?.IceCreamSales == null) return hash;

                hash = (hash * 31) + ds.IceCreamSales.Count;
                for (int i = 0; i < ds.IceCreamSales.Count; i++)
                {
                    IceCreamSaleLine line = ds.IceCreamSales[i];
                    if (line == null) continue;
                    hash = (hash * 31) + line.SoldCount;
                    hash = (hash * 31) + Mathf.RoundToInt(line.SoldRevenue * 100f);
                    hash = (hash * 31) + Mathf.RoundToInt(line.SoldCost * 100f);
                }
                return hash;
            }
        }

        private void BuildIceCreamTiles()
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

                if (Plugin.ProductCache != null && Plugin.ProductCache.Count == 0)
                {
                    var idm = UnityEngine.Object.FindFirstObjectByType<global::IDManager>();
                    if (idm != null) Plugin.ProductCache.Build(idm);
                }

                DayStats ds = StatsStore.TryGetDay(_selectedDay);
                List<IceCreamSaleLine> sales = ds?.IceCreamSales;
                int built = 0;

                if (sales == null || sales.Count == 0)
                {
                    CreateIceCreamTile(
                        "IceCream_NoData",
                        Plugin.T("Lody", "Ice Cream"),
                        9999,
                        Plugin.T("Brak szczegółowych danych o sprzedaży lodów dla tego dnia.", "No detailed ice cream sales data for this day."),
                        false,
                        0f,
                        0f);
                    built = 1;
                }
                else
                {
                    switch (_iceCreamView)
                    {
                        case IceCreamViewMode.Cones:
                            built = BuildIceCreamConeTiles(sales);
                            break;
                        case IceCreamViewMode.Flavours:
                            built = BuildIceCreamFlavourTiles(sales);
                            break;
                        case IceCreamViewMode.Combinations:
                            built = BuildIceCreamCombinationTiles(sales);
                            break;
                        case IceCreamViewMode.Summary:
                        default:
                            built = BuildIceCreamSummaryTiles(sales);
                            break;
                    }
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

                _iceCreamLiveSignature = ComputeIceCreamSignature(ds);
                ReapplyProfessionalTileStyles();
                ScheduleProfessionalTileReapply();
                UpdateDayHeaderUI();
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError("[StatsUI] BuildIceCreamTiles failed: " + ex);
            }
        }

        private int BuildIceCreamSummaryTiles(List<IceCreamSaleLine> sales)
        {
            int sold = 0;
            int costed = 0;
            int totalScoops = 0;
            float revenue = 0f;
            float cost = 0f;

            var cones = BuildConeAggregates(sales);
            var flavours = BuildFlavourAggregates(sales);

            IceCreamSaleLine bestCombination = null;
            float bestCombinationProfit = float.MinValue;

            for (int i = 0; i < sales.Count; i++)
            {
                IceCreamSaleLine line = sales[i];
                if (line == null || line.SoldCount <= 0) continue;

                sold += line.SoldCount;
                costed += line.CostedCount;
                totalScoops += line.ScoopCount * line.SoldCount;
                revenue += line.SoldRevenue;
                cost += line.SoldCost;

                if (line.CostedCount >= line.SoldCount)
                {
                    float profit = line.SoldRevenue - line.SoldCost;
                    if (bestCombination == null || profit > bestCombinationProfit)
                    {
                        bestCombination = line;
                        bestCombinationProfit = profit;
                    }
                }
            }

            bool complete = sold > 0 && costed >= sold;
            float profitTotal = complete ? revenue - cost : 0f;
            float margin = complete && revenue > 0.0001f ? (profitTotal / revenue) * 100f : 0f;
            float avgScoops = sold > 0 ? totalScoops / (float)sold : 0f;

            string summaryText =
                $"{Plugin.T("Sprzedane", "Sold")}: <b>{sold}</b> {Plugin.T("szt.", "pcs")}\n" +
                $"{Plugin.T("Przychód", "Revenue")}: <color={StatsAppTheme.PositiveHex}><b>{Plugin.Money(revenue)}</b></color>\n" +
                $"{Plugin.T("Koszt", "Cost")}: " + (complete ? $"<color={StatsAppTheme.WarningHex}><b>{Plugin.Money(cost)}</b></color>" : "—") + "\n" +
                $"{Plugin.T("Zysk / Marża", "Profit / Margin")}: " + (complete ? $"<b>{Plugin.Money(profitTotal)} / {margin:0.0}%</b>" : "—");

            CreateIceCreamTile("IceCream_Summary", Plugin.T("Lody — podsumowanie", "Ice Cream — summary"), 9999, summaryText, complete, profitTotal, margin);

            IceCreamConeAggregate topCone = null;
            foreach (var pair in cones)
            {
                if (topCone == null || pair.Value.SoldCount > topCone.SoldCount)
                    topCone = pair.Value;
            }

            IceCreamFlavourAggregate topFlavour = null;
            foreach (var pair in flavours)
            {
                if (topFlavour == null || pair.Value.Scoops > topFlavour.Scoops)
                    topFlavour = pair.Value;
            }

            string usageText =
                $"{Plugin.T("Łącznie gałek", "Total scoops")}: <b>{totalScoops}</b>\n" +
                $"{Plugin.T("Śr. gałek / lód", "Avg scoops / ice cream")}: <b>{avgScoops:0.00}</b>\n" +
                $"{Plugin.T("Najpopularniejszy rożek", "Top cone")}: <b>{(topCone != null ? GetProductNameSafe(topCone.ProductId) : "—")}</b>\n" +
                $"{Plugin.T("Najpopularniejszy smak", "Top flavour")}: <b>{(topFlavour != null ? GetProductNameSafe(topFlavour.ProductId) : "—")}</b>";

            CreateIceCreamTile("IceCream_Usage", Plugin.T("Zużycie składników", "Ingredient usage"), 9999, usageText, true, 0f, 100f);

            if (bestCombination != null)
            {
                float comboProfit = bestCombination.SoldRevenue - bestCombination.SoldCost;
                float comboMargin = bestCombination.SoldRevenue > 0.0001f ? (comboProfit / bestCombination.SoldRevenue) * 100f : 0f;
                string comboText =
                    $"{FormatIceCreamRecipe(bestCombination.Recipe)}\n" +
                    $"{Plugin.T("Sprzedane", "Sold")}: <b>{bestCombination.SoldCount}</b> | {Plugin.T("Gałki", "Scoops")}: <b>{bestCombination.ScoopCount}</b>\n" +
                    $"{Plugin.T("Przychód", "Revenue")}: <b>{Plugin.Money(bestCombination.SoldRevenue)}</b>\n" +
                    $"{Plugin.T("Zysk / Marża", "Profit / Margin")}: <b>{Plugin.Money(comboProfit)} / {comboMargin:0.0}%</b>";

                CreateIceCreamTile(
                    "IceCream_BestCombo",
                    Plugin.T("Najbardziej dochodowa kombinacja", "Most profitable combination"),
                    bestCombination.ConeProductId > 0 ? bestCombination.ConeProductId : 9999,
                    comboText,
                    true,
                    comboProfit,
                    comboMargin);
            }

            return bestCombination != null ? 3 : 2;
        }

        private int BuildIceCreamConeTiles(List<IceCreamSaleLine> sales)
        {
            var map = BuildConeAggregates(sales);
            var rows = new List<IceCreamConeAggregate>(map.Values);

            rows.Sort((a, b) =>
            {
                bool ak = a.CostedCount >= a.SoldCount && a.SoldCount > 0;
                bool bk = b.CostedCount >= b.SoldCount && b.SoldCount > 0;
                if (ak != bk) return ak ? -1 : 1;
                float ap = ak ? a.Revenue - a.Cost : float.MinValue;
                float bp = bk ? b.Revenue - b.Cost : float.MinValue;
                int cmp = ap.CompareTo(bp);
                if (cmp == 0) cmp = a.SoldCount.CompareTo(b.SoldCount);
                return cmp * (_sortAsc ? 1 : -1);
            });

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                bool complete = row.SoldCount > 0 && row.CostedCount >= row.SoldCount;
                float profit = complete ? row.Revenue - row.Cost : 0f;
                float margin = complete && row.Revenue > 0.0001f ? (profit / row.Revenue) * 100f : 0f;

                string body =
                    $"{Plugin.T("Lody", "Ice creams")}: <b>{row.SoldCount}</b>\n" +
                    $"{Plugin.T("Przychód", "Revenue")}: <color={StatsAppTheme.PositiveHex}><b>{Plugin.Money(row.Revenue)}</b></color>\n" +
                    $"{Plugin.T("Koszt", "Cost")}: " + (complete ? $"<b>{Plugin.Money(row.Cost)}</b>" : "—") + "\n" +
                    $"{Plugin.T("Zysk / Marża", "Profit / Margin")}: " + (complete ? $"<b>{Plugin.Money(profit)} / {margin:0.0}%</b>" : "—");

                CreateIceCreamTile("IceCream_Cone_" + row.ProductId, GetProductNameSafe(row.ProductId), row.ProductId, body, complete, profit, margin);
            }

            return rows.Count;
        }

        private int BuildIceCreamFlavourTiles(List<IceCreamSaleLine> sales)
        {
            var map = BuildFlavourAggregates(sales);
            var rows = new List<IceCreamFlavourAggregate>(map.Values);

            int totalScoops = 0;
            for (int i = 0; i < rows.Count; i++) totalScoops += rows[i].Scoops;

            rows.Sort((a, b) =>
            {
                int cmp = a.Scoops.CompareTo(b.Scoops);
                if (cmp == 0) cmp = a.IceCreamCount.CompareTo(b.IceCreamCount);
                if (cmp == 0) cmp = a.ProductId.CompareTo(b.ProductId);
                return cmp * (_sortAsc ? 1 : -1);
            });

            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                float share = totalScoops > 0 ? (row.Scoops / (float)totalScoops) * 100f : 0f;
                float avg = row.IceCreamCount > 0 ? row.Scoops / (float)row.IceCreamCount : 0f;

                string body =
                    $"{Plugin.T("Zużyte gałki", "Scoops used")}: <b>{row.Scoops}</b>\n" +
                    $"{Plugin.T("Lody z tym smakiem", "Ice creams with flavour")}: <b>{row.IceCreamCount}</b>\n" +
                    $"{Plugin.T("Udział w gałkach", "Share of scoops")}: <b>{share:0.0}%</b>\n" +
                    $"{Plugin.T("Śr. gałek w takim lodzie", "Avg scoops when used")}: <b>{avg:0.00}</b>";

                CreateIceCreamTile("IceCream_Flavour_" + row.ProductId, GetProductNameSafe(row.ProductId), row.ProductId, body, true, 0f, share);
            }

            return rows.Count;
        }

        private int BuildIceCreamCombinationTiles(List<IceCreamSaleLine> sales)
        {
            var rows = new List<IceCreamSaleLine>();
            for (int i = 0; i < sales.Count; i++)
                if (sales[i] != null && sales[i].SoldCount > 0) rows.Add(sales[i]);

            rows.Sort((a, b) =>
            {
                bool ak = a.CostedCount >= a.SoldCount;
                bool bk = b.CostedCount >= b.SoldCount;
                if (ak != bk) return ak ? -1 : 1;

                float ap = ak ? a.SoldRevenue - a.SoldCost : float.MinValue;
                float bp = bk ? b.SoldRevenue - b.SoldCost : float.MinValue;
                int cmp = ap.CompareTo(bp);
                if (cmp == 0) cmp = a.SoldCount.CompareTo(b.SoldCount);
                return cmp * (_sortAsc ? 1 : -1);
            });

            for (int i = 0; i < rows.Count; i++)
            {
                IceCreamSaleLine line = rows[i];
                bool complete = line.SoldCount > 0 && line.CostedCount >= line.SoldCount;
                float profit = complete ? line.SoldRevenue - line.SoldCost : 0f;
                float margin = complete && line.SoldRevenue > 0.0001f ? (profit / line.SoldRevenue) * 100f : 0f;

                string topping = line.ToppingIndex >= 0 ? line.ToppingIndex.ToString() : "—";
                string body =
                    $"{FormatIceCreamRecipe(line.Recipe)}\n" +
                    $"{Plugin.T("Sprzedane", "Sold")}: <b>{line.SoldCount}</b> | {Plugin.T("Gałki", "Scoops")}: <b>{line.ScoopCount}</b> | {Plugin.T("Topping", "Topping")}: <b>{topping}</b>\n" +
                    $"{Plugin.T("Przychód / Koszt", "Revenue / Cost")}: <b>{Plugin.Money(line.SoldRevenue)} / {(complete ? Plugin.Money(line.SoldCost) : "—")}</b>\n" +
                    $"{Plugin.T("Zysk / Marża", "Profit / Margin")}: " + (complete ? $"<b>{Plugin.Money(profit)} / {margin:0.0}%</b>" : "—");

                string title = GetProductNameSafe(line.ConeProductId);
                CreateIceCreamTile("IceCream_Combo_" + i, title, line.ConeProductId > 0 ? line.ConeProductId : 9999, body, complete, profit, margin);
            }

            return rows.Count;
        }

        private Dictionary<int, IceCreamConeAggregate> BuildConeAggregates(List<IceCreamSaleLine> sales)
        {
            var map = new Dictionary<int, IceCreamConeAggregate>();
            for (int i = 0; i < sales.Count; i++)
            {
                IceCreamSaleLine line = sales[i];
                if (line == null || line.ConeProductId <= 0 || line.SoldCount <= 0) continue;

                if (!map.TryGetValue(line.ConeProductId, out IceCreamConeAggregate row))
                {
                    row = new IceCreamConeAggregate { ProductId = line.ConeProductId };
                    map[line.ConeProductId] = row;
                }

                row.SoldCount += line.SoldCount;
                row.Revenue += line.SoldRevenue;
                row.Cost += line.SoldCost;
                row.CostedCount += line.CostedCount;
            }
            return map;
        }

        private Dictionary<int, IceCreamFlavourAggregate> BuildFlavourAggregates(List<IceCreamSaleLine> sales)
        {
            var map = new Dictionary<int, IceCreamFlavourAggregate>();
            for (int i = 0; i < sales.Count; i++)
            {
                IceCreamSaleLine line = sales[i];
                if (line == null || line.SoldCount <= 0) continue;

                List<IceCreamRecipePart> parts = ParseIceCreamRecipe(line.Recipe);
                for (int j = 0; j < parts.Count; j++)
                {
                    IceCreamRecipePart part = parts[j];
                    if (part.ProductId <= 0 || part.Count <= 0) continue;

                    if (!map.TryGetValue(part.ProductId, out IceCreamFlavourAggregate row))
                    {
                        row = new IceCreamFlavourAggregate { ProductId = part.ProductId };
                        map[part.ProductId] = row;
                    }

                    row.Scoops += part.Count * line.SoldCount;
                    row.IceCreamCount += line.SoldCount;
                }
            }
            return map;
        }

        private List<IceCreamRecipePart> ParseIceCreamRecipe(string recipe)
        {
            var result = new List<IceCreamRecipePart>();
            if (string.IsNullOrWhiteSpace(recipe)) return result;

            string[] entries = recipe.Split(',');
            for (int i = 0; i < entries.Length; i++)
            {
                string entry = entries[i]?.Trim();
                if (string.IsNullOrEmpty(entry)) continue;

                int x = entry.IndexOf('x');
                if (x <= 0 || x >= entry.Length - 1) continue;

                if (!int.TryParse(entry.Substring(0, x), out int pid)) continue;
                if (!int.TryParse(entry.Substring(x + 1), out int count)) continue;
                if (pid <= 0 || count <= 0) continue;

                result.Add(new IceCreamRecipePart { ProductId = pid, Count = count });
            }
            return result;
        }

        private string FormatIceCreamRecipe(string recipe)
        {
            List<IceCreamRecipePart> parts = ParseIceCreamRecipe(recipe);
            if (parts.Count == 0) return Plugin.T("Brak smaków", "No flavours");

            string text = string.Empty;
            for (int i = 0; i < parts.Count; i++)
            {
                if (i > 0) text += " + ";
                text += GetProductNameSafe(parts[i].ProductId) + " x" + parts[i].Count;
            }
            return text;
        }

        private void CreateIceCreamTile(
            string objectName,
            string title,
            int iconProductId,
            string body,
            bool profitabilityKnown,
            float profit,
            float margin)
        {
            GameObject tile = UnityEngine.Object.Instantiate(_tileTemplate, _tilesContent, false);
            tile.name = objectName;

            SetTmpText(tile.transform, "Product Name", title);

            Sprite icon = null;
            if (Plugin.ProductCache != null)
                Plugin.ProductCache.TryGet(iconProductId, out _, out icon);

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
                TextMeshProUGUI tmp = infoTr.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.text = body ?? string.Empty;
                    AttachIceCreamInfoTooltipZones(tile.transform, tmp, body ?? string.Empty);
                }
            }

            try
            {
                AddTileShadow(tile.transform);
                AddIceCreamStatusBar(tile.transform, profitabilityKnown, profit, margin);
                ApplyStatsPremiumTypography(tile.transform);
            }
            catch { }

            DisableRaycastOnAllTMP(tile.transform);
            ForceProfessionalTileVisuals(tile.transform);
            tile.SetActive(true);
        }

        private void AddIceCreamStatusBar(Transform tile, bool known, float profit, float margin)
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

            if (!known)
                img.color = StatsAppTheme.Warning;
            else if (profit < -0.001f || margin < 0f)
                img.color = StatsAppTheme.Negative;
            else if (margin < 10f)
                img.color = StatsAppTheme.Warning;
            else
                img.color = StatsAppTheme.Positive;
        }
    }
}
