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
        private float GetCurrentPrice(int productId)
        {
            if (_priceManager == null) _priceManager = UnityEngine.Object.FindObjectOfType<PriceManager>();
            if (_priceManager == null) return 0f;

            try
            {
                return _priceManager.SellingPrice(productId);
            }
            catch
            {
                return 0f;
            }
        }

        private float GetCurrentCost(int productId)
        {
            if (_priceManager == null) _priceManager = UnityEngine.Object.FindObjectOfType<PriceManager>();
            if (_priceManager == null) return 0f;

            try
            {
                return _priceManager.CurrentCost(productId);
            }
            catch
            {
                // Gra rzuciła błędem (np. brak ID w cenniku) - zwracamy 0
                return 0f;
            }
        }

        private void BuildAllProductsTilesNow()
        {
            ClearTilesOnly();

            var cache = Plugin.ProductCache;
            if (cache == null || cache.Count == 0) return;

            var shopStock = new System.Collections.Generic.Dictionary<int, int>();
            var warehouseStock = new System.Collections.Generic.Dictionary<int, int>();

            // IL2CPP PERF FIX: Skan natywny po indeksie zamiast alokacji foreach
            var allSlots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
            if (allSlots != null)
            {
                for (int i = 0; i < allSlots.Count; i++)
                {
                    var ds = allSlots[i];
                    if (ds != null && ds.ProductID > 0)
                    {
                        shopStock.TryGetValue(ds.ProductID, out int current);
                        shopStock[ds.ProductID] = current + ds.ProductCount;
                    }
                }
            }

            var allBoxes = UnityEngine.Object.FindObjectsOfType<Box>();
            if (allBoxes != null)
            {
                for (int i = 0; i < allBoxes.Count; i++)
                {
                    var box = allBoxes[i];
                    if (box == null) continue;

                    try
                    {
                        var data = box.Data;
                        if (data != null && data.ProductID > 0)
                        {
                            warehouseStock.TryGetValue(data.ProductID, out int current);
                            warehouseStock[data.ProductID] = current + box.ProductCount;
                        }
                    }
                    catch { }
                }
            }

            var ids = new List<int>(cache.ById.Keys);
            var polishCulture = new System.Globalization.CultureInfo("pl-PL");

            ids.Sort((a, b) =>
            {
                int dir = _sortAsc ? 1 : -1;
                int cmp = 0;

                switch (_simpleSort)
                {
                    case SimpleSortMode.Name:
                        string nameA = cache.NameById.GetValueOrDefault(a) ?? "";
                        string nameB = cache.NameById.GetValueOrDefault(b) ?? "";
                        cmp = string.Compare(nameA, nameB, polishCulture, System.Globalization.CompareOptions.IgnoreCase);
                        break;

                    case SimpleSortMode.ProductId:
                        cmp = a.CompareTo(b);
                        break;

                    case SimpleSortMode.PriceBuy:
                        cmp = GetCurrentCost(a).CompareTo(GetCurrentCost(b));
                        break;

                    case SimpleSortMode.PriceSell:
                        cmp = GetCurrentPrice(a).CompareTo(GetCurrentPrice(b));
                        break;

                    case SimpleSortMode.TotalStock:
                        int stockA = shopStock.GetValueOrDefault(a) + warehouseStock.GetValueOrDefault(a);
                        int stockB = shopStock.GetValueOrDefault(b) + warehouseStock.GetValueOrDefault(b);
                        cmp = stockA.CompareTo(stockB);
                        break;

                    case SimpleSortMode.TotalValue:
                        float valA = (shopStock.GetValueOrDefault(a) + warehouseStock.GetValueOrDefault(a)) * GetCurrentCost(a);
                        float valB = (shopStock.GetValueOrDefault(b) + warehouseStock.GetValueOrDefault(b)) * GetCurrentCost(b);
                        cmp = valA.CompareTo(valB);
                        break;

                    case SimpleSortMode.NearestExpiry:
                        cmp = a.CompareTo(b);
                        break;
                }

                if (cmp == 0) cmp = a.CompareTo(b);
                return cmp * dir;
            });

            int built = 0;
            for (int idIdx = 0; idIdx < ids.Count; idIdx++)
            {
                int pid = ids[idIdx];
                float sellPrice = GetCurrentPrice(pid);

                if (_onlyWithPrice && (sellPrice <= 0.001f || !IsProductUnlocked(pid))) continue;

                if (!cache.TryGet(pid, out var name, out var icon)) continue;

                if (!string.IsNullOrEmpty(_searchFilter))
                {
                    // ZERO GARBAGE FIX: Szybkie przeszukiwanie bez alokowania nowych stringów (.ToLower)
                    bool matchesName = name.IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;
                    bool matchesId = pid.ToString().IndexOf(_searchFilter, StringComparison.OrdinalIgnoreCase) >= 0;

                    if (!matchesName && !matchesId) continue;
                }

                var tile = Instantiate(_tileTemplate, _tilesContent, false);
                tile.SetActive(true);
                DisableGameScriptsOnTile(tile.transform);
                tile.name = "Product_" + pid;

                var nameTmp = GetTmpComponent(tile.transform, "Product Name");
                if (nameTmp != null)
                {
                    nameTmp.text = FormatTileProductName(name, pid, true);
                    nameTmp.enableAutoSizing = true;
                    nameTmp.fontSizeMin = 8f;
                    nameTmp.fontSizeMax = 13f;
                    nameTmp.enableWordWrapping = false;
                }

                var infoTmp = GetTmpComponent(tile.transform, "Product Brand");
                if (infoTmp != null)
                {
                    int sQty = shopStock.GetValueOrDefault(pid);
                    int wQty = warehouseStock.GetValueOrDefault(pid);
                    int totalQty = sQty + wQty;
                    float buyP = GetCurrentCost(pid);
                    float sellP = GetCurrentPrice(pid);

                    bool isWeight = SalesUnifiedFinal.WeightPerUnit.TryGetValue(pid, out float kgPerUnit);

                    string sStockStr, wStockStr;
                    if (isWeight)
                    {
                        sStockStr = (sQty * kgPerUnit).ToString("N2") + " kg";
                        wStockStr = (wQty * kgPerUnit).ToString("N2") + " kg";
                    }
                    else
                    {
                        string unit = Plugin.T("szt.", "pcs");
                        sStockStr = sQty.ToString("N0") + " " + unit;
                        wStockStr = wQty.ToString("N0") + " " + unit;
                    }

                    float totalCostValue = totalQty * buyP;
                    float totalSalesValue = totalQty * sellP;

                    string buyShort = Plugin.BuyShortLabel;
                    string sellShort = Plugin.SellShortLabel;
                    string shopShort = Plugin.ShopShortLabel;
                    string warehouseShort = Plugin.WarehouseShortLabel;

                    infoTmp.text =
                        $"{Plugin.T("Cena", "Price")}: <color={StatsAppTheme.WarningHex}><b>{buyShort}: {Plugin.Money(buyP)}</b></color> | <color={StatsAppTheme.PositiveHex}><b>{sellShort}: {Plugin.Money(sellP)}</b></color>\n" +
                        $"{Plugin.T("Stan", "Stock")}: <color={StatsAppTheme.InfoHex}><b>{shopShort}: {sStockStr}</b></color> | <color={StatsAppTheme.WarningHex}><b>{warehouseShort}: {wStockStr}</b></color>\n" +
                        $"{Plugin.T("Wartość", "Value")} {buyShort}: <color={StatsAppTheme.PurpleHex}><b>{Plugin.Money(totalCostValue)}</b></color> | {sellShort}: <color={StatsAppTheme.PositiveHex}><b>{Plugin.Money(totalSalesValue)}</b></color>";

                    infoTmp.fontSize = 8.7f;
                    infoTmp.lineSpacing = 0f;

                    AttachProductInfoTooltipZones(tile.transform, infoTmp);
                }

                var iconTr = tile.transform.Find("Product Icon");
                if (iconTr != null)
                {
                    var img = iconTr.GetComponent<UnityEngine.UI.Image>();
                    if (img != null) { img.sprite = icon; img.enabled = (icon != null); img.preserveAspect = true; }
                }

                AdjustProductTileContent(tile.transform);
                ForceProfessionalTileVisuals(tile.transform);
                built++;
            }
            ForceTilesLayout(built);
        }

        private (int shop, int warehouse) GetStock(int productId)
        {
            int shopCount = 0;
            int warehouseCount = 0;

            // 1. Zliczanie na półkach w sklepie (DisplaySlot)
            var displays = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
            foreach (var display in displays)
            {
                if (display != null && display.ProductID == productId)
                {
                    shopCount += display.ProductCount;
                }
            }

            // 2. Zliczanie w magazynie - omijamy regały i skanujemy bezpośrednio KARTONY (Box)
            var allBoxes = UnityEngine.Object.FindObjectsOfType<Box>();
            foreach (var box in allBoxes)
            {
                if (box != null && box.Data != null && box.Data.ProductID == productId)
                {
                    warehouseCount += box.ProductCount;
                }
            }

            return (shopCount, warehouseCount);
        }

        private bool IsProductUnlocked(int productId)
        {
            // Stoisko z lodami jest produktem syntetycznym i nie ma natywnej licencji produktu.
            if (productId == 9999) return true;

            if (ProductLicenseManager.Instance == null) return false;

            try
            {
                // Wykorzystujemy natywną metodę gry do sprawdzenia statusu licencji
                return ProductLicenseManager.Instance.IsProductLicenseUnlocked(productId);
            }
            catch
            {
                // W razie błędu (np. brak licencji dla tego ID w bazie), ukrywamy produkt
                return false;
            }
        }

    }
}
