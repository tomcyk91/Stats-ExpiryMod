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
        private void EnsureProductInfoTooltip()
        {
            if (_productInfoTooltipGO != null || _statsApp == null) return;

            _productInfoTooltipGO = new GameObject("ProductInfoTooltip");
            _productInfoTooltipGO.transform.SetParent(_statsApp.transform, false);

            _productInfoTooltipRT = _productInfoTooltipGO.AddComponent<RectTransform>();
            _productInfoTooltipRT.anchorMin = new Vector2(0.5f, 0.5f);
            _productInfoTooltipRT.anchorMax = new Vector2(0.5f, 0.5f);
            _productInfoTooltipRT.pivot = new Vector2(0.5f, 0.5f);
            // Smaller tooltip so it covers less of the product grid.
            _productInfoTooltipRT.sizeDelta = new Vector2(200f, 58f);

            var canvas = _productInfoTooltipGO.AddComponent<Canvas>();
            canvas.overrideSorting = true;
            canvas.sortingOrder = 32500;

            var group = _productInfoTooltipGO.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = false;
            group.blocksRaycasts = false;

            _productInfoTooltipGO.AddComponent<CanvasRenderer>();
            var bg = _productInfoTooltipGO.AddComponent<Image>();
            bg.color = StatsAppTheme.DropdownBackground;
            bg.raycastTarget = false;

            var outline = _productInfoTooltipGO.AddComponent<Outline>();
            outline.effectColor = StatsAppTheme.HeaderBorder;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(_productInfoTooltipGO.transform, false);
            var titleRT = titleGO.AddComponent<RectTransform>();
            titleRT.anchorMin = new Vector2(0f, 0.54f);
            titleRT.anchorMax = new Vector2(1f, 0.94f);
            titleRT.offsetMin = new Vector2(7f, 0f);
            titleRT.offsetMax = new Vector2(-7f, 0f);

            _productInfoTooltipTitle = titleGO.AddComponent<TextMeshProUGUI>();
            _productInfoTooltipTitle.raycastTarget = false;
            _productInfoTooltipTitle.alignment = TextAlignmentOptions.MidlineLeft;
            _productInfoTooltipTitle.fontStyle = FontStyles.Bold;
            _productInfoTooltipTitle.fontSize = 9.5f;
            _productInfoTooltipTitle.enableAutoSizing = true;
            _productInfoTooltipTitle.fontSizeMin = 7f;
            _productInfoTooltipTitle.fontSizeMax = 9.5f;
            _productInfoTooltipTitle.enableWordWrapping = false;
            _productInfoTooltipTitle.overflowMode = TextOverflowModes.Ellipsis;
            _productInfoTooltipTitle.color = StatsAppTheme.TextLight;
            if (_gameFont != null) _productInfoTooltipTitle.font = _gameFont;

            var bodyGO = new GameObject("Body");
            bodyGO.transform.SetParent(_productInfoTooltipGO.transform, false);
            var bodyRT = bodyGO.AddComponent<RectTransform>();
            bodyRT.anchorMin = new Vector2(0f, 0.08f);
            bodyRT.anchorMax = new Vector2(1f, 0.56f);
            bodyRT.offsetMin = new Vector2(7f, 0f);
            bodyRT.offsetMax = new Vector2(-7f, 0f);

            _productInfoTooltipBody = bodyGO.AddComponent<TextMeshProUGUI>();
            _productInfoTooltipBody.raycastTarget = false;
            _productInfoTooltipBody.alignment = TextAlignmentOptions.TopLeft;
            _productInfoTooltipBody.fontSize = 7.5f;
            _productInfoTooltipBody.enableAutoSizing = true;
            _productInfoTooltipBody.fontSizeMin = 5.8f;
            _productInfoTooltipBody.fontSizeMax = 7.5f;
            _productInfoTooltipBody.enableWordWrapping = true;
            _productInfoTooltipBody.color = StatsAppTheme.TextLight;
            if (_gameFont != null) _productInfoTooltipBody.font = _gameFont;

            _productInfoTooltipGO.SetActive(false);
        }

        private void ShowProductInfoTooltip(RectTransform source, RectTransform tileRT, string title, string body)
        {
            if (source == null || _statsApp == null) return;

            EnsureProductInfoTooltip();
            if (_productInfoTooltipGO == null || _productInfoTooltipRT == null) return;

            _productInfoTooltipTitle.text = title ?? string.Empty;
            _productInfoTooltipBody.text = body ?? string.Empty;

            // Globalne tooltipy zawierają dłuższe objaśnienia niż pierwotna legenda PRODUKTÓW.
            // Dopasuj wysokość popupu do długości treści, ale zachowaj kompaktowy rozmiar.
            int bodyLength = string.IsNullOrEmpty(body) ? 0 : body.Length;
            float tooltipHeight = bodyLength > 210 ? 94f : bodyLength > 135 ? 82f : bodyLength > 80 ? 70f : 60f;
            _productInfoTooltipRT.sizeDelta = new Vector2(236f, tooltipHeight);

            var rootRT = _statsApp.GetComponent<RectTransform>();
            if (rootRT != null)
            {
                float halfW = _productInfoTooltipRT.sizeDelta.x * 0.5f;
                float halfH = _productInfoTooltipRT.sizeDelta.y * 0.5f;
                Rect rr = rootRT.rect;

                // Use the whole tile to decide which side the tooltip should use.
                // Right-column cards open the tooltip on their LEFT side.
                RectTransform referenceRT = tileRT != null ? tileRT : source;
                Vector3 refCenterWorld = referenceRT.TransformPoint(referenceRT.rect.center);
                Vector3 refCenterLocal = rootRT.InverseTransformPoint(refCenterWorld);

                Vector3 sourceLeftWorld = source.TransformPoint(new Vector3(source.rect.xMin, source.rect.center.y, 0f));
                Vector3 sourceRightWorld = source.TransformPoint(new Vector3(source.rect.xMax, source.rect.center.y, 0f));
                Vector3 sourceCenterWorld = source.TransformPoint(source.rect.center);

                Vector3 sourceLeftLocal = rootRT.InverseTransformPoint(sourceLeftWorld);
                Vector3 sourceRightLocal = rootRT.InverseTransformPoint(sourceRightWorld);
                Vector3 sourceCenterLocal = rootRT.InverseTransformPoint(sourceCenterWorld);

                // Third/right column starts roughly after 2/3 of the app width.
                float rightColumnThreshold = rr.xMin + rr.width * 0.66f;
                bool placeOnLeft = refCenterLocal.x >= rightColumnThreshold;

                float desiredX = placeOnLeft
                    ? sourceLeftLocal.x - halfW - 6f
                    : sourceRightLocal.x + halfW + 6f;

                float x = Mathf.Clamp(desiredX, rr.xMin + halfW + 4f, rr.xMax - halfW - 4f);
                float y = Mathf.Clamp(sourceCenterLocal.y, rr.yMin + halfH + 4f, rr.yMax - halfH - 4f);

                _productInfoTooltipRT.anchoredPosition = new Vector2(x, y);
            }

            _productInfoTooltipGO.transform.SetAsLastSibling();
            _productInfoTooltipGO.SetActive(true);
        }

        private void HideProductInfoTooltip()
        {
            if (_productInfoTooltipGO != null)
                _productInfoTooltipGO.SetActive(false);
        }

        private void AddTooltipEvent(EventTrigger trigger, EventTriggerType eventType, UnityAction<BaseEventData> action)
        {
            if (trigger == null || action == null) return;

            var entry = new EventTrigger.Entry { eventID = eventType };
            entry.callback.AddListener(action);
            trigger.triggers.Add(entry);
        }

        private void CreateProductInfoTooltipZone(Transform parent, RectTransform tileRT, string objectName,
            Vector2 anchorMin, Vector2 anchorMax, string title, string body)
        {
            if (parent == null) return;

            var go = new GameObject(objectName);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            go.AddComponent<CanvasRenderer>();
            var hit = go.AddComponent<Image>();
            hit.color = new Color(1f, 1f, 1f, 0.001f);
            hit.raycastTarget = true;

            var trigger = go.AddComponent<EventTrigger>();
            AddTooltipEvent(trigger, EventTriggerType.PointerEnter,
                (UnityAction<BaseEventData>)((_) => ShowProductInfoTooltip(rt, tileRT, title, body)));
            AddTooltipEvent(trigger, EventTriggerType.PointerExit,
                (UnityAction<BaseEventData>)((_) => HideProductInfoTooltip()));
        }

        private void AttachProductInfoTooltipZones(Transform tile, TextMeshProUGUI infoTmp)
        {
            if (tile == null || infoTmp == null) return;

            Transform existing = infoTmp.transform.Find("TooltipZones");
            if (existing != null)
                UnityEngine.Object.Destroy(existing.gameObject);

            var root = new GameObject("TooltipZones");
            root.transform.SetParent(infoTmp.transform, false);
            var rootRT = root.AddComponent<RectTransform>();
            rootRT.anchorMin = Vector2.zero;
            rootRT.anchorMax = Vector2.one;
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;

            string buyLabel = Plugin.BuyShortLabel;
            string sellLabel = Plugin.SellShortLabel;
            string shopLabel = Plugin.ShopShortLabel;
            string warehouseLabel = Plugin.WarehouseShortLabel;
            RectTransform tileRT = tile.GetComponent<RectTransform>();

            CreateProductInfoTooltipZone(root.transform, tileRT, "BuyPrice",
                new Vector2(0f, 0.666f), new Vector2(0.53f, 1f),
                $"{buyLabel} = {Plugin.T("Cena zakupu", "Buy price")}",
                Plugin.T("Cena zakupu jednej sztuki produktu.", "Purchase price per product unit."));

            CreateProductInfoTooltipZone(root.transform, tileRT, "SellPrice",
                new Vector2(0.53f, 0.666f), new Vector2(1f, 1f),
                $"{sellLabel} = {Plugin.T("Cena sprzedaży", "Sell price")}",
                Plugin.T("Aktualna cena sprzedaży jednej sztuki produktu.", "Current selling price per product unit."));

            CreateProductInfoTooltipZone(root.transform, tileRT, "ShopStock",
                new Vector2(0f, 0.333f), new Vector2(0.53f, 0.666f),
                $"{shopLabel} = {Plugin.T("Sklep", "Shop")}",
                Plugin.T("Ilość produktu obecnie znajdująca się w sklepie.", "Quantity currently located in the shop."));

            CreateProductInfoTooltipZone(root.transform, tileRT, "WarehouseStock",
                new Vector2(0.53f, 0.333f), new Vector2(1f, 0.666f),
                $"{warehouseLabel} = {Plugin.T("Magazyn", "Warehouse")}",
                Plugin.T("Ilość produktu obecnie znajdująca się w magazynie.", "Quantity currently located in the warehouse."));

            CreateProductInfoTooltipZone(root.transform, tileRT, "BuyValue",
                new Vector2(0f, 0f), new Vector2(0.53f, 0.333f),
                $"{buyLabel} = {Plugin.T("Wartość wg ceny zakupu", "Value at buy price")}",
                Plugin.T("Łączna wartość aktualnego zapasu według ceny zakupu.", "Total current stock value at the buy price."));

            CreateProductInfoTooltipZone(root.transform, tileRT, "SellValue",
                new Vector2(0.53f, 0f), new Vector2(1f, 0.333f),
                $"{sellLabel} = {Plugin.T("Wartość detaliczna", "Retail value")}",
                Plugin.T("Potencjalna wartość aktualnego zapasu według bieżącej ceny sprzedaży. To nie jest faktyczny przychód.",
                    "Potential value of current stock at the current selling price. This is not actual revenue."));
        }

    }
}
