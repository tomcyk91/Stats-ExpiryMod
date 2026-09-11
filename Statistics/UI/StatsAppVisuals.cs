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
        private void CacheGameTmpStyle()
        {
            if (_gameStyleCached) return;
            _gameStyleCached = true;

            TextMeshProUGUI src = null;

            var market = FindByPath(_computerRoot, "Screen/Market App");
            if (market != null)
                src = market.GetComponentInChildren<TextMeshProUGUI>(true);

            if (src == null && _desktopCanvas != null)
                src = _desktopCanvas.GetComponentInChildren<TextMeshProUGUI>(true);

            if (src == null && _screen != null)
                src = _screen.GetComponentInChildren<TextMeshProUGUI>(true);

            if (src == null) return;

            _gameFont = src.font;
            _gameColor = src.color;
        }

        private static void SafeSetOutline(TMP_Text t, float width)
        {
            if (t == null) return;

            try
            {
                // jeśli nie ma fonta/materiału, outlineWidth w IL2CPP potrafi wywalić NRE
                if (t.font == null) return;

                // materialForRendering bywa null zanim TMP się w pełni zainicjalizuje
                var m = t.fontMaterial;
                if (m == null) return;

                t.outlineWidth = width;
            }
            catch
            {
                // celowo cisza: to jest "niestabilne" API w IL2CPP
            }
        }

        private void ApplyGameTmp(TextMeshProUGUI tmp, float fontSize, TextAlignmentOptions align)
        {
            if (tmp == null) return;

            if (_gameFont != null) tmp.font = _gameFont;

            tmp.color = _gameColor;
            tmp.fontSize = fontSize;
            tmp.alignment = align;
            tmp.richText = true;
            tmp.enableWordWrapping = true;
        }

        private void AddTileShadow(Transform tile)
        {
            if (tile == null) return;

            var shadow = tile.GetComponent<UnityEngine.UI.Shadow>();
            if (shadow == null)
                shadow = tile.gameObject.AddComponent<UnityEngine.UI.Shadow>();

            shadow.effectColor = StatsAppTheme.Shadow;
            shadow.effectDistance = new Vector2(2f, -2f);
            shadow.useGraphicAlpha = true;
        }

        private void PolishButtonVisual(Button btn, bool isSmall)
        {
            if (btn == null) return;

            var img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = StatsAppTheme.Button;
                img.raycastTarget = true;
            }

            // Delikatna ramka zamiast mocnego, "plastikowego" efektu.
            var outline = btn.GetComponent<Outline>();
            if (outline == null) outline = btn.gameObject.AddComponent<Outline>();
            outline.effectColor = new Color(StatsAppTheme.HeaderBorder.r, StatsAppTheme.HeaderBorder.g, StatsAppTheme.HeaderBorder.b, 0.70f);
            outline.effectDistance = new Vector2(1f, -1f);

            var tr = btn.transform;
            if (tr.Find("Shadow") == null)
            {
                var sh = new GameObject("Shadow");
                sh.transform.SetParent(tr, false);
                sh.transform.SetAsFirstSibling();

                var rt = sh.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(-1f, -2f);
                rt.offsetMax = new Vector2(1f, 1f);

                sh.AddComponent<CanvasRenderer>();
                var simg = sh.AddComponent<Image>();
                simg.raycastTarget = false;
                simg.color = StatsAppTheme.Shadow;
            }

            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
            {
                if (_gameFont != null) tmp.font = _gameFont;
                tmp.fontStyle = FontStyles.Bold;
                tmp.color = StatsAppTheme.TextLight;

                SafeSetOutline(tmp, 0.06f);
                tmp.outlineColor = new Color(0f, 0f, 0f, 0.45f);
            }
        }

        private void MakeCloseButtonRed(Button btn)
        {
            if (btn == null) return;

            var img = btn.GetComponent<Image>();
            if (img != null)
            {
                img.color = StatsAppTheme.Danger;
            }

            var tmp = btn.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null)
            {
                tmp.fontStyle = FontStyles.Bold;
                tmp.color = StatsAppTheme.TextLight;
                SafeSetOutline(tmp, 0.06f);
                tmp.outlineColor = new Color(0f, 0f, 0f, 0.45f);
            }

            var tr = btn.transform;
            var existingShadow = tr.Find("RedShadow");
            if (existingShadow == null)
            {
                var sh = new GameObject("RedShadow");
                sh.transform.SetParent(tr, false);
                sh.transform.SetAsFirstSibling();

                var rt = sh.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(-1f, -2f);
                rt.offsetMax = new Vector2(1f, 1f);

                sh.AddComponent<CanvasRenderer>();
                var simg = sh.AddComponent<Image>();
                simg.raycastTarget = false;
                simg.color = StatsAppTheme.DangerDark;
            }
        }

        /// <summary>
        /// Pass 2b - twarde wymuszenie stylu pojedynczego kafelka.
        /// Robimy to na gotowej instancji, a nie tylko na template, ponieważ w Unity/IL2CPP
        /// komponenty Graphic/TMP potrafią odziedziczyć lub odzyskać starszy tint/material
        /// podczas SetActive / przebudowy layoutu.
        /// </summary>
        private void ForceProfessionalTileVisuals(Transform tile)
        {
            if (tile == null) return;

            // Pass 3: styl produktu stosujemy WYŁĄCZNIE do prawdziwych kafelków produktów.
            // PODSUMOWANIE ma własną strukturę (Title/Value/Details) i nie może dostać
            // automatycznie tworzonego "Icon Surface", bo zasłaniał on zawartość kart.
            if (tile.Find("Product Name") == null || tile.Find("Product Brand") == null)
                return;

            try
            {
                var tileRt = tile.GetComponent<RectTransform>();
                if (tileRt != null)
                    tileRt.sizeDelta = new Vector2(StatsAppTheme.TileWidth,
                        tile.name.StartsWith("ExpirationTile_", StringComparison.Ordinal)
                            ? _expirationTileHeight : StatsAppTheme.TileHeight);

                // ROOT CARD - celowo zerujemy sprite/material, żeby kolor nie był mnożony
                // przez stary asset lub materiał z poprzedniego wyglądu.
                var background = tile.GetComponent<Image>();
                if (background == null)
                {
                    if (tile.GetComponent<CanvasRenderer>() == null)
                        tile.gameObject.AddComponent<CanvasRenderer>();
                    background = tile.gameObject.AddComponent<Image>();
                }

                background.enabled = true;
                background.raycastTarget = false;
                background.sprite = null;
                background.overrideSprite = null;
                background.material = null;
                background.type = Image.Type.Simple;
                background.preserveAspect = false;
                background.color = StatsAppTheme.TileBackground;

                var outline = tile.GetComponent<Outline>();
                if (outline == null) outline = tile.gameObject.AddComponent<Outline>();
                outline.effectColor = StatsAppTheme.TileBorder;
                outline.effectDistance = new Vector2(1f, -1f);
                outline.useGraphicAlpha = false;

                var shadow = tile.GetComponent<UnityEngine.UI.Shadow>();
                if (shadow == null) shadow = tile.gameObject.AddComponent<UnityEngine.UI.Shadow>();
                shadow.effectColor = StatsAppTheme.Shadow;
                shadow.effectDistance = new Vector2(1.5f, -1.5f);
                shadow.useGraphicAlpha = true;

                // ICON SURFACE
                Transform iconSurface = tile.Find("Icon Surface");
                if (iconSurface == null)
                {
                    var go = new GameObject("Icon Surface");
                    go.transform.SetParent(tile, false);
                    iconSurface = go.transform;
                    go.AddComponent<RectTransform>();
                    go.AddComponent<CanvasRenderer>();
                    go.AddComponent<Image>();
                    go.AddComponent<Outline>();
                }

                var iconSurfaceRt = iconSurface.GetComponent<RectTransform>();
                if (iconSurfaceRt != null)
                {
                    iconSurfaceRt.anchorMin = new Vector2(0.035f, 0.10f);
                    iconSurfaceRt.anchorMax = new Vector2(0.300f, 0.90f);
                    iconSurfaceRt.offsetMin = Vector2.zero;
                    iconSurfaceRt.offsetMax = Vector2.zero;
                }

                var iconSurfaceImg = iconSurface.GetComponent<Image>();
                if (iconSurfaceImg != null)
                {
                    iconSurfaceImg.enabled = true;
                    iconSurfaceImg.raycastTarget = false;
                    iconSurfaceImg.sprite = null;
                    iconSurfaceImg.overrideSprite = null;
                    iconSurfaceImg.material = null;
                    iconSurfaceImg.type = Image.Type.Simple;
                    iconSurfaceImg.color = StatsAppTheme.TileIconBackground;
                }

                var iconSurfaceOutline = iconSurface.GetComponent<Outline>();
                if (iconSurfaceOutline == null) iconSurfaceOutline = iconSurface.gameObject.AddComponent<Outline>();
                iconSurfaceOutline.effectColor = StatsAppTheme.TileIconBorder;
                iconSurfaceOutline.effectDistance = new Vector2(1f, -1f);
                iconSurfaceOutline.useGraphicAlpha = false;
                iconSurface.SetAsFirstSibling();

                // SEPARATOR
                Transform separator = tile.Find("Text Separator");
                if (separator == null)
                {
                    var go = new GameObject("Text Separator");
                    go.transform.SetParent(tile, false);
                    separator = go.transform;
                    go.AddComponent<RectTransform>();
                    go.AddComponent<CanvasRenderer>();
                    go.AddComponent<Image>();
                }

                var separatorRt = separator.GetComponent<RectTransform>();
                if (separatorRt != null)
                {
                    separatorRt.anchorMin = new Vector2(0.325f, 0.705f);
                    separatorRt.anchorMax = new Vector2(0.965f, 0.705f);
                    separatorRt.pivot = new Vector2(0.5f, 0.5f);
                    separatorRt.sizeDelta = new Vector2(0f, 1f);
                    separatorRt.offsetMin = new Vector2(separatorRt.offsetMin.x, 0f);
                    separatorRt.offsetMax = new Vector2(separatorRt.offsetMax.x, 0f);
                }

                var separatorImg = separator.GetComponent<Image>();
                if (separatorImg != null)
                {
                    separatorImg.enabled = true;
                    separatorImg.raycastTarget = false;
                    separatorImg.sprite = null;
                    separatorImg.overrideSprite = null;
                    separatorImg.material = null;
                    separatorImg.color = StatsAppTheme.TileSeparator;
                }

                // TITLE
                var nameTr = tile.Find("Product Name");
                if (nameTr != null)
                {
                    var rt = nameTr.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.anchorMin = new Vector2(0.325f, 0.735f);
                        rt.anchorMax = new Vector2(0.965f, 0.925f);
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                    }

                    var tmp = nameTr.GetComponent<TextMeshProUGUI>();
                    if (tmp != null)
                    {
                        if (_gameFont != null) tmp.font = _gameFont;
                        tmp.color = StatsAppTheme.TileTitle;
                        tmp.fontStyle = FontStyles.Bold;
                        tmp.alignment = TextAlignmentOptions.MidlineLeft;
                        tmp.enableAutoSizing = true;
                        tmp.fontSize = 10.5f;
                        tmp.fontSizeMin = 7f;
                        tmp.fontSizeMax = 10.5f;
                        tmp.enableWordWrapping = false;
                        tmp.overflowMode = TextOverflowModes.Ellipsis;
                        tmp.margin = new Vector4(1f, 0f, 2f, 0f);
                        SafeSetOutline(tmp, 0f);
                    }
                }

                // INFO BODY
                var infoTr = tile.Find("Product Brand");
                if (infoTr != null)
                {
                    bool denseStats = SmartExpiration.PluginConfig.ExpiryEnabled &&
                                      tile.name.StartsWith("StatsTile_", StringComparison.Ordinal);

                    var rt = infoTr.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.anchorMin = denseStats ? new Vector2(0.325f, 0.045f) : new Vector2(0.325f, 0.10f);
                        rt.anchorMax = denseStats ? new Vector2(0.965f, 0.690f) : new Vector2(0.965f, 0.675f);
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                    }

                    var tmp = infoTr.GetComponent<TextMeshProUGUI>();
                    if (tmp != null)
                    {
                        if (_gameFont != null) tmp.font = _gameFont;
                        tmp.color = StatsAppTheme.TileText;
                        tmp.fontStyle = FontStyles.Normal;
                        tmp.alignment = TextAlignmentOptions.TopLeft;
                        tmp.enableAutoSizing = true;
                        tmp.fontSize = denseStats ? 7.5f : 8.7f;
                        tmp.fontSizeMin = denseStats ? 5.6f : 6.7f;
                        tmp.fontSizeMax = denseStats ? 7.5f : 8.7f;
                        tmp.lineSpacing = denseStats ? -1f : 0f;
                        tmp.paragraphSpacing = 0f;
                        tmp.enableWordWrapping = false;
                        tmp.overflowMode = TextOverflowModes.Ellipsis;
                        tmp.margin = new Vector4(1f, 1f, 2f, 0f);
                        SafeSetOutline(tmp, 0f);
                        if (tile.name.StartsWith("ExpirationTile_", StringComparison.Ordinal))
                        {
                            // Preserve the multiline expiry layout on every visual refresh.
                            if (rt != null)
                            {
                                rt.anchorMin = new Vector2(0.325f, 0.045f);
                                rt.anchorMax = new Vector2(0.965f, 0.690f);
                            }
                            ConfigureExpirationLocationText(tmp);
                        }
                    }
                }

                // PRODUCT ICON - nie tintujemy sprite produktu.
                var iconTr = tile.Find("Product Icon");
                if (iconTr != null)
                {
                    var rt = iconTr.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.anchorMin = new Vector2(0.050f, 0.17f);
                        rt.anchorMax = new Vector2(0.280f, 0.83f);
                        rt.offsetMin = Vector2.zero;
                        rt.offsetMax = Vector2.zero;
                    }

                    var img = iconTr.GetComponent<Image>();
                    if (img != null)
                    {
                        img.color = Color.white;
                        img.material = null;
                        img.preserveAspect = true;
                        img.raycastTarget = false;
                    }
                }

                // Status ma być tylko cienkim akcentem, nie dużym pasem.
                string[] accentNames = { "StatusBar", "ExpiryAccent", "AnalysisAccent" };
                for (int i = 0; i < accentNames.Length; i++)
                {
                    var accentTr = tile.Find(accentNames[i]);
                    if (accentTr == null) continue;
                    var rt = accentTr.GetComponent<RectTransform>();
                    if (rt == null) continue;
                    rt.anchorMin = new Vector2(0f, 0f);
                    rt.anchorMax = new Vector2(0.012f, 1f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }

                if (tile.GetComponent<RectMask2D>() == null)
                    tile.gameObject.AddComponent<RectMask2D>();
            }
            catch (Exception ex)
            {
                Plugin.DebugLog("[StatsUI] ForceProfessionalTileVisuals: " + ex.Message);
            }
        }

        private bool UsesProfessionalProductTileLayout()
        {
            return _hubMode == HubMode.Stats ||
                   _hubMode == HubMode.Profitability ||
                   _hubMode == HubMode.IceCream ||
                   _hubMode == HubMode.Expiration ||
                   _hubMode == HubMode.Products ||
                   _hubMode == HubMode.Analysis;
        }

        private void ReapplyProfessionalTileStyles()
        {
            if (_tilesContent == null || !UsesProfessionalProductTileLayout()) return;

            for (int i = 0; i < _tilesContent.childCount; i++)
            {
                Transform child = _tilesContent.GetChild(i);
                if (child == null) continue;
                ForceProfessionalTileVisuals(child);
            }
        }

        private void ScheduleProfessionalTileReapply()
        {
            if (!UsesProfessionalProductTileLayout()) return;

            try
            {
                CancelInvoke(nameof(ReapplyProfessionalTileStyles));
                Invoke(nameof(ReapplyProfessionalTileStyles), 0.04f);
            }
            catch
            {
                // Natychmiastowe wymuszenie już zostało wykonane; deferred pass jest tylko zabezpieczeniem.
            }
        }

        private void AdjustProductTileContent(Transform tile)
        {
            if (tile == null) return;

            var background = tile.GetComponent<Image>();
            if (background != null)
                background.color = StatsAppTheme.TileBackground;

            var nameTr = tile.Find("Product Name");
            if (nameTr != null)
            {
                var rt = nameTr.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.325f, 0.71f);
                    rt.anchorMax = new Vector2(0.965f, 0.94f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }

                var tmp = nameTr.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.alignment = TextAlignmentOptions.MidlineLeft;
                    tmp.color = StatsAppTheme.TileTitle;
                    tmp.fontStyle = FontStyles.Bold;
                    tmp.enableAutoSizing = true;
                    tmp.fontSizeMin = 7.5f;
                    tmp.fontSizeMax = 11f;
                    tmp.enableWordWrapping = false;
                    tmp.overflowMode = TextOverflowModes.Ellipsis;
                    tmp.margin = new Vector4(1f, 0f, 2f, 0f);
                    SafeSetOutline(tmp, 0f);
                }
            }

            var infoTr = tile.Find("Product Brand");
            if (infoTr != null)
            {
                var rt = infoTr.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.325f, 0.10f);
                    rt.anchorMax = new Vector2(0.965f, 0.665f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }

                var tmp = infoTr.GetComponent<TextMeshProUGUI>();
                if (tmp != null)
                {
                    tmp.color = StatsAppTheme.TileText;
                    tmp.enableAutoSizing = true;
                    tmp.fontSizeMin = 6.7f;
                    tmp.fontSizeMax = 8.7f;
                    tmp.fontSize = 8.7f;
                    tmp.lineSpacing = 0f;
                    tmp.paragraphSpacing = 0f;
                    tmp.enableWordWrapping = false;
                    tmp.overflowMode = TextOverflowModes.Ellipsis;
                    tmp.alignment = TextAlignmentOptions.TopLeft;
                    tmp.margin = new Vector4(1f, 1f, 2f, 0f);
                    SafeSetOutline(tmp, 0f);
                }
            }

            var iconTr = tile.Find("Product Icon");
            if (iconTr != null)
            {
                var rt = iconTr.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0.050f, 0.17f);
                    rt.anchorMax = new Vector2(0.280f, 0.83f);
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                }
            }

            if (tile.GetComponent<RectMask2D>() == null)
                tile.gameObject.AddComponent<RectMask2D>();
        }

    }
}
