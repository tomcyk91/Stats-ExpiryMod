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
        private void EnsureStatsApp()
        {
            Transform existing = null;

            if (_desktopCanvas != null)
                existing = FindDirectChild(_desktopCanvas, STATS_APP_NAME);

            if (existing == null && _screen != null)
                existing = FindDirectChild(_screen, STATS_APP_NAME);

            if (existing != null)
            {
                _statsApp = existing.gameObject;
                EnsureGridUI();

                ForceHeaderOnTop();   // ✅ DODAJ
                return;
            }

            var parent = _desktopCanvas ?? _screen;
            if (parent == null) return;

            _statsApp = CreateSimpleFullScreenPanel(parent, STATS_APP_NAME);
            _statsApp.SetActive(false);

            EnsureGridUI();
            BuildStatsTiles();

            ForceHeaderOnTop();       // ✅ DODAJ
        }

        private void ForceHeaderOnTop()
        {
            if (_statsApp == null) return;

            var header = FindDirectChild(_statsApp.transform, "Header");
            header?.SetAsLastSibling(); // ✅ header zawsze nad scroll/body
        }

        private GameObject CreateSimpleFullScreenPanel(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            go.AddComponent<CanvasRenderer>();
            var img = go.AddComponent<Image>();
            img.color = StatsAppTheme.AppFrame;
            img.raycastTarget = true;

            BuildAppBodySurface(go.transform);
            BuildHeaderWithClose(go.transform);

            go.transform.SetAsLastSibling();
            return go;
        }

        private void BuildAppBodySurface(Transform appRoot)
        {
            if (appRoot == null) return;

            var body = new GameObject("BodyPanel");
            body.transform.SetParent(appRoot, false);
            body.transform.SetAsFirstSibling();

            var rt = body.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(StatsAppTheme.OuterLeft, StatsAppTheme.BodyBottom);
            rt.anchorMax = new Vector2(StatsAppTheme.OuterRight, StatsAppTheme.BodyTop);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            body.AddComponent<CanvasRenderer>();
            var image = body.AddComponent<Image>();
            image.color = StatsAppTheme.Body;
            image.raycastTarget = false;

            var outline = body.AddComponent<Outline>();
            outline.effectColor = StatsAppTheme.Border;
            outline.effectDistance = new Vector2(1f, -1f);

            var accent = new GameObject("BodyAccent");
            accent.transform.SetParent(body.transform, false);

            var accentRT = accent.AddComponent<RectTransform>();
            accentRT.anchorMin = new Vector2(0f, 1f);
            accentRT.anchorMax = new Vector2(1f, 1f);
            accentRT.pivot = new Vector2(0.5f, 1f);
            accentRT.sizeDelta = new Vector2(0f, 3f);
            accentRT.anchoredPosition = Vector2.zero;

            accent.AddComponent<CanvasRenderer>();
            var accentImage = accent.AddComponent<Image>();
            accentImage.color = StatsAppTheme.Accent;
            accentImage.raycastTarget = false;
        }

        private void OnCloseClicked() => HideStats();

        private void ShowStats()
        {
            if (_statsApp == null)
                EnsureStatsApp();
            if (_statsApp == null) return;

            HideAllAppsExceptDesktopCanvas();

            _statsApp.SetActive(true);
            _statsApp.transform.SetAsLastSibling();
            _isOpen = true;

            if (!SmartExpiration.PluginConfig.ExpiryEnabled &&
                _hubMode == HubMode.Expiration)
            {
                _hubMode = HubMode.Stats;
            }

            // blokada reload (zostaje)
            StatsStore.SuspendReload = true;

            // ✅ ZAWSZE startuj od "dziś" po otwarciu (dla widoku Statystyk)
            if (_selectedDay < 1)
                _selectedDay = GetCurrentDaySafe();

            RebuildDaysUI();

            UpdateDayLabel();

            RefreshTitleModeText();      // ustawia: STATYSTYKI/TERMINY/PRODUKTY

            RefreshHeaderForMode();

            QueueBuildForHubMode();

            QueueBuildTiles();
        }

        private void HideAllAppsExceptDesktopCanvas()
        {
            for (int i = 0; i < _screen.childCount; i++)
            {
                var child = _screen.GetChild(i);
                if (child.name == "Desktop Canvas") continue;
                child.gameObject.SetActive(false);
            }
        }

        private void HideStats()
        {
            HideProductInfoTooltip();
            HideHeaderDropdown();
            HideChartDropdown();
            _statsApp?.SetActive(false);
            _isOpen = false;

            // 🔥 Reset dnia – przy kolejnym otwarciu będzie "dziś"
            _selectedDay = -1;
        }

        private void EnsureGridUI()
        {
            if (_statsApp == null) return;

            EnsureSidebarNavigation();

            var existing = FindDirectChild(_statsApp.transform, "TilesScroll");
            if (existing != null)
            {
                var existingRT = existing.GetComponent<RectTransform>();
                if (existingRT != null)
                {
                    existingRT.anchorMin = new Vector2(0.035f, StatsAppTheme.ContentBottom);
                    existingRT.anchorMax = new Vector2(0.965f, StatsAppTheme.ContentTop);
                    existingRT.offsetMin = Vector2.zero;
                    existingRT.offsetMax = Vector2.zero;
                }

                RefreshSidebarNavigation();
                if (_sidebarNavigationRoot != null)
                    _sidebarNavigationRoot.transform.SetAsLastSibling();
                return;
            }

            var scrollGO = new GameObject("TilesScroll");
            scrollGO.transform.SetParent(_statsApp.transform, false);
            scrollGO.transform.SetAsLastSibling();

            var scrollRT = scrollGO.AddComponent<RectTransform>();
            // Tiles now use the full width again. Module navigation lives in
            // a collapsible overlay drawer on the left side.
            scrollRT.anchorMin = new Vector2(0.035f, StatsAppTheme.ContentBottom);
            scrollRT.anchorMax = new Vector2(0.965f, StatsAppTheme.ContentTop);
            scrollRT.offsetMin = Vector2.zero;
            scrollRT.offsetMax = Vector2.zero;

            scrollGO.AddComponent<CanvasRenderer>();
            var scrollPanel = scrollGO.AddComponent<Image>();
            scrollPanel.color = StatsAppTheme.Surface;
            scrollPanel.raycastTarget = false;

            var scrollOutline = scrollGO.AddComponent<Outline>();
            scrollOutline.effectColor = StatsAppTheme.Border;
            scrollOutline.effectDistance = new Vector2(1f, -1f);

            var scrollRect = scrollGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 520f;
            scrollRect.decelerationRate = 0.135f;
            scrollRect.inertia = true;

            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(scrollGO.transform, false);

            var viewportRT = viewportGO.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = Vector2.zero;
            viewportRT.offsetMax = Vector2.zero;

            viewportGO.AddComponent<CanvasRenderer>();
            var vpImg = viewportGO.AddComponent<Image>();
            vpImg.color = new Color(1f, 1f, 1f, 0.02f);
            vpImg.raycastTarget = true;

            var vpMask = viewportGO.AddComponent<Mask>();
            vpMask.showMaskGraphic = false;

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);

            var contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0, 1);
            contentRT.anchorMax = new Vector2(1, 1);
            contentRT.pivot = new Vector2(0.5f, 1);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0, 0);
            contentRT.offsetMin = new Vector2(0, contentRT.offsetMin.y);
            contentRT.offsetMax = new Vector2(0, contentRT.offsetMax.y);

            var grid = contentGO.AddComponent<GridLayoutGroup>();
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 3;
            grid.cellSize = new Vector2(StatsAppTheme.TileWidth, StatsAppTheme.TileHeight);
            grid.spacing = new Vector2(10f, 10f);
            grid.padding = new RectOffset(14, 20, 14, 16);
            grid.childAlignment = TextAnchor.UpperLeft;

            scrollRect.viewport = viewportRT;
            scrollRect.content = contentRT;

            // === VERTICAL SCROLLBAR (right side) ===
            var sbGO = new GameObject("Scrollbar Vertical");
            sbGO.transform.SetParent(scrollGO.transform, false);
            sbGO.transform.SetAsLastSibling();

            var sbRT = sbGO.AddComponent<RectTransform>();
            sbRT.anchorMin = new Vector2(1f, 0f);
            sbRT.anchorMax = new Vector2(1f, 1f);
            sbRT.pivot = new Vector2(1f, 1f);

            // szerokość paska i odsunięcie od prawej krawędzi
            sbRT.sizeDelta = new Vector2(15f, 0f);   // było 14f
            sbRT.anchoredPosition = new Vector2(9f, 0f); // dopasuj do mniejszej szerokości

            sbGO.AddComponent<CanvasRenderer>();
            var sbBg = sbGO.AddComponent<Image>();
            sbBg.color = StatsAppTheme.ScrollTrack;

            var sb = sbGO.AddComponent<UnityEngine.UI.Scrollbar>();
            sb.direction = UnityEngine.UI.Scrollbar.Direction.BottomToTop;
            sb.numberOfSteps = 0; // płynnie
            sb.size = 0.2f;       // początkowy rozmiar "thumb"

            // Handle
            var handleGO = new GameObject("Sliding Area");
            handleGO.transform.SetParent(sbGO.transform, false);
            var haRT = handleGO.AddComponent<RectTransform>();
            haRT.anchorMin = Vector2.zero;
            haRT.anchorMax = Vector2.one;
            haRT.offsetMin = new Vector2(2f, 2f);
            haRT.offsetMax = new Vector2(-2f, -2f);

            var handle = new GameObject("Handle");
            handle.transform.SetParent(handleGO.transform, false);
            var hRT = handle.AddComponent<RectTransform>();
            hRT.anchorMin = Vector2.zero;
            hRT.anchorMax = Vector2.one;
            hRT.offsetMin = Vector2.zero;
            hRT.offsetMax = Vector2.zero;

            handle.AddComponent<CanvasRenderer>();
            var hImg = handle.AddComponent<Image>();
            hImg.color = StatsAppTheme.ScrollThumb;

            sb.handleRect = hRT;
            sb.targetGraphic = hImg;

            // Podepnij do ScrollRect
            scrollRect.verticalScrollbar = sb;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scrollRect.verticalScrollbarSpacing = -20f; // kompensuje szerokość paska


            _tilesContent = contentRT;

            _tileTemplate = CreateFallbackTileTemplate();

            RefreshSidebarNavigation();
        }

        private void RecalcGridContentHeight(RectTransform content, GridLayoutGroup grid, int itemCount)
        {
            if (content == null || grid == null) return;

            int cols = Mathf.Max(1, grid.constraintCount);
            int rows = Mathf.CeilToInt(itemCount / (float)cols);

            float cellH = grid.cellSize.y;
            float spacingY = grid.spacing.y;

            float height =
                grid.padding.top +
                grid.padding.bottom +
                rows * cellH +
                Mathf.Max(0, rows - 1) * spacingY;

            var sd = content.sizeDelta;
            sd.y = height;
            content.sizeDelta = sd;
        }

        private GameObject CreateFallbackTileTemplate()
        {
            CacheGameTmpStyle();

            var go = new GameObject("FallbackTile");
            var rt = go.AddComponent<RectTransform>();
            rt.sizeDelta = new Vector2(StatsAppTheme.TileWidth, StatsAppTheme.TileHeight);

            go.AddComponent<CanvasRenderer>();
            var img = go.AddComponent<Image>();
            img.color = StatsAppTheme.TileBackground;
            img.raycastTarget = false;

            // Delikatny cień jest częścią template, więc każdy tryb ma identyczną kartę.
            AddTileShadow(go.transform);

            var outline = go.AddComponent<Outline>();
            outline.effectColor = StatsAppTheme.TileBorder;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;

            // Osobne jasne pole pod ikonę – wizualnie oddziela produkt od danych.
            var iconSurfaceGO = new GameObject("Icon Surface");
            iconSurfaceGO.transform.SetParent(go.transform, false);
            var iconSurfaceRT = iconSurfaceGO.AddComponent<RectTransform>();
            iconSurfaceRT.anchorMin = new Vector2(0.035f, 0.10f);
            iconSurfaceRT.anchorMax = new Vector2(0.300f, 0.90f);
            iconSurfaceRT.offsetMin = Vector2.zero;
            iconSurfaceRT.offsetMax = Vector2.zero;
            iconSurfaceGO.AddComponent<CanvasRenderer>();
            var iconSurfaceImg = iconSurfaceGO.AddComponent<Image>();
            iconSurfaceImg.color = StatsAppTheme.TileIconBackground;
            iconSurfaceImg.raycastTarget = false;
            var iconOutline = iconSurfaceGO.AddComponent<Outline>();
            iconOutline.effectColor = StatsAppTheme.TileIconBorder;
            iconOutline.effectDistance = new Vector2(1f, -1f);
            iconOutline.useGraphicAlpha = false;

            var separatorGO = new GameObject("Text Separator");
            separatorGO.transform.SetParent(go.transform, false);
            var separatorRT = separatorGO.AddComponent<RectTransform>();
            separatorRT.anchorMin = new Vector2(0.325f, 0.705f);
            separatorRT.anchorMax = new Vector2(0.965f, 0.705f);
            separatorRT.pivot = new Vector2(0.5f, 0.5f);
            separatorRT.sizeDelta = new Vector2(0f, 1f);
            separatorGO.AddComponent<CanvasRenderer>();
            var separatorImg = separatorGO.AddComponent<Image>();
            separatorImg.color = StatsAppTheme.TileSeparator;
            separatorImg.raycastTarget = false;

            var nameGO = new GameObject("Product Name");
            nameGO.transform.SetParent(go.transform, false);
            var nrt = nameGO.AddComponent<RectTransform>();
            nrt.anchorMin = new Vector2(0.325f, 0.735f);
            nrt.anchorMax = new Vector2(0.965f, 0.925f);
            nrt.offsetMin = Vector2.zero;
            nrt.offsetMax = Vector2.zero;

            var tmp = nameGO.AddComponent<TextMeshProUGUI>();
            tmp.text = "Produkt";
            tmp.raycastTarget = false;
            ApplyGameTmp(tmp, 10.8f, TextAlignmentOptions.MidlineLeft);
            tmp.fontStyle = FontStyles.Bold;
            tmp.color = StatsAppTheme.TileTitle;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 7.2f;
            tmp.fontSizeMax = 10.8f;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.margin = new Vector4(1f, 0f, 2f, 0f);
            SafeSetOutline(tmp, 0f);

            var infoGO = new GameObject("Product Brand");
            infoGO.transform.SetParent(go.transform, false);
            var irt = infoGO.AddComponent<RectTransform>();
            irt.anchorMin = new Vector2(0.325f, 0.10f);
            irt.anchorMax = new Vector2(0.965f, 0.675f);
            irt.offsetMin = Vector2.zero;
            irt.offsetMax = Vector2.zero;

            var tmp2 = infoGO.AddComponent<TextMeshProUGUI>();
            tmp2.text = "Sprzedane: 0\nPrzychód: 0\nWyrzucone: 0\nStrata: 0";
            tmp2.raycastTarget = false;
            ApplyGameTmp(tmp2, 8.7f, TextAlignmentOptions.TopLeft);
            tmp2.color = StatsAppTheme.TileText;
            tmp2.enableAutoSizing = true;
            tmp2.fontSizeMin = 6.8f;
            tmp2.fontSizeMax = 8.7f;
            tmp2.lineSpacing = 0f;
            tmp2.enableWordWrapping = false;
            tmp2.overflowMode = TextOverflowModes.Ellipsis;
            tmp2.margin = new Vector4(1f, 1f, 2f, 0f);
            SafeSetOutline(tmp2, 0f);

            var iconGO = new GameObject("Product Icon");
            iconGO.transform.SetParent(go.transform, false);
            var icrt = iconGO.AddComponent<RectTransform>();
            icrt.anchorMin = new Vector2(0.050f, 0.17f);
            icrt.anchorMax = new Vector2(0.280f, 0.83f);
            icrt.offsetMin = Vector2.zero;
            icrt.offsetMax = Vector2.zero;

            iconGO.AddComponent<CanvasRenderer>();
            var icImg = iconGO.AddComponent<Image>();
            icImg.color = Color.white;
            icImg.preserveAspect = true;
            icImg.enabled = true;
            icImg.raycastTarget = false;

            var le = go.AddComponent<LayoutElement>();
            le.preferredWidth = StatsAppTheme.TileWidth;
            le.preferredHeight = StatsAppTheme.TileHeight;

            // Nie pozwalamy długim tłumaczeniom wyjść poza kartę.
            if (go.GetComponent<RectMask2D>() == null)
                go.AddComponent<RectMask2D>();

            // Pass 2b: template i każda jego instancja korzystają z dokładnie tej samej
            // wymuszonej warstwy wizualnej. Dzięki temu żaden stary tint / materiał
            // nie może przywrócić granatowego kafelka.
            ForceProfessionalTileVisuals(go.transform);

            go.SetActive(false);
            return go;
        }

        private void ClearTilesOnly()
        {
            HideProductInfoTooltip();
            if (_tilesContent == null) return;

            for (int i = _tilesContent.childCount - 1; i >= 0; i--)
            {
                Transform ch = _tilesContent.GetChild(i);
                if (ch != null)
                    Destroy(ch.gameObject);
            }
        }

        private void ForceTilesLayout(int built)
        {
            if (_tilesContent == null) return;
            var grid = _tilesContent.GetComponent<GridLayoutGroup>();
            if (grid != null) RecalcGridContentHeight(_tilesContent, grid, built);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_tilesContent);
            Canvas.ForceUpdateCanvases();

            if (_hubMode == HubMode.Expiration && grid != null)
            {
                // Measure at the final card width, including translated text that wraps.
                // The expiry body occupies 0.690 - 0.045 of the card height.
                float requiredHeight = StatsAppTheme.TileHeight;
                for (int i = 0; i < _tilesContent.childCount; i++)
                {
                    var tile = _tilesContent.GetChild(i);
                    if (tile == null || !tile.gameObject.activeSelf ||
                        !tile.name.StartsWith("ExpirationTile_", StringComparison.Ordinal)) continue;

                    var info = GetTmpComponent(tile, "Product Brand");
                    if (info != null)
                        requiredHeight = Mathf.Max(requiredHeight, (info.preferredHeight + 4f) / 0.645f);
                }

                _expirationTileHeight = Mathf.Ceil(requiredHeight);
                grid.cellSize = new Vector2(grid.cellSize.x, _expirationTileHeight);
                RecalcGridContentHeight(_tilesContent, grid, built);
                LayoutRebuilder.ForceRebuildLayoutImmediate(_tilesContent);
                Canvas.ForceUpdateCanvases();
            }

            // Pass 3: tylko tryby oparte o kafelki produktów dostają wymuszony Product Tile skin.
            // PODSUMOWANIE i WYKRESY mają własny layout i są stylowane niezależnie.
            if (UsesProfessionalProductTileLayout())
            {
                ReapplyProfessionalTileStyles();
                ScheduleProfessionalTileReapply();
            }
        }

        private void EnterChartsLayout()
        {
            if (_tilesContent == null) return;

            // 1. Wyłączamy scrollowanie, żeby wykres był stabilny
            DisableOuterScrollForCharts();

            // 2. Wyłączamy Grid, bo wykres to jeden duży obiekt, a nie siatka kafelków
            var grid = _tilesContent.GetComponent<GridLayoutGroup>();
            if (grid != null) grid.enabled = false;

            var fitter = _tilesContent.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = false;

            // 3. Resetujemy kontener pod wykres (pełny stretch)
            var rt = _tilesContent.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private void ExitChartsLayout()
        {
            if (_tilesContent == null) return;

            // 1. Przywracamy przewijanie (ScrollRect)
            RestoreOuterScrollAfterCharts();

            // 2. Resetujemy RectTransform kontenera kafelków
            var rt = _tilesContent.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 1); // Góra-Stretch
            rt.anchorMax = new Vector2(1, 1);
            rt.pivot = new Vector2(0.5f, 1);
            rt.anchoredPosition = Vector2.zero;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // 3. Włączamy komponenty odpowiedzialne za kafelki
            var grid = _tilesContent.GetComponent<GridLayoutGroup>();
            if (grid != null) grid.enabled = true;

            var fitter = _tilesContent.GetComponent<ContentSizeFitter>();
            if (fitter != null) fitter.enabled = true;

            // 4. Czyścimy stare śmieci po wykresach (jeśli zostały)
            // UWAGA: Używamy bezpiecznej pętli for dla IL2CPP
            for (int i = _tilesContent.childCount - 1; i >= 0; i--)
            {
                var child = _tilesContent.GetChild(i);
                if (child.name.StartsWith("Charts_Root")) // Usuwamy tylko korzeń wykresu
                    UnityEngine.Object.Destroy(child.gameObject);
            }

            // 5. Wymuszamy natychmiastowe przeliczenie layoutu
            Canvas.ForceUpdateCanvases();
        }

    }
}
