using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace StatisticMod
{
    public partial class StatsAppManager
    {
        private const float DESKTOP_INSIGHTS_REFRESH = 5.0f;
        private const float DESKTOP_ADVICE_REFRESH = 99999.0f; // v14: heavy refresh is manual/day-change only
        private const float DESKTOP_ANALYSIS_CACHE_SECONDS = 20.0f;
        private const float DESKTOP_EXPIRY_CACHE_SECONDS = 40.0f;
        private const float DESKTOP_ADVICE_RETRY = 1.0f;
        private const string DESKTOP_STATE_FILE = "StatisticMod.desktopinsights.tsv";
        private const string DESKTOP_PROMO_STATE_FILE = "StatisticMod.desktop-promos.tsv";
        private const string DESKTOP_STATE_HEADER = "# StatisticMod DesktopInsights v7";
        private const int DESKTOP_MAX_ALERTS = 6;
        private const int DESKTOP_MAX_STOCK_EXPIRY = 6;
        private const int DESKTOP_MAX_CHANGES = 30;
        private const int DESKTOP_MAX_ADVICE = 16;

        private const float DESKTOP_CONTENT_GAP = 8f;
        private const float DESKTOP_SUMMARY_HEIGHT = 90f;
        private const float DESKTOP_ALERT_ROW_HEIGHT = 40f;
        private const float DESKTOP_STOCK_EXPIRY_ROW_HEIGHT = 36f;
        private const float DESKTOP_CHANGE_LINE_HEIGHT = 24f;
        private const float DESKTOP_ADVICE_ROW_HEIGHT = 42f;

        private GameObject _desktopInsightsPanel;
        private RectTransform _desktopInsightsViewport;
        private RectTransform _desktopInsightsContent;
        private ScrollRect _desktopInsightsScroll;
        private Scrollbar _desktopInsightsScrollbar;

        private GameObject _desktopSummarySurface;
        private GameObject _desktopAlertsSurface;
        private GameObject _desktopStockExpirySurface;
        private GameObject _desktopChangesSurface;
        private GameObject _desktopAdviceSurface;
        private RectTransform _desktopSummarySurfaceRT;
        private RectTransform _desktopAlertsSurfaceRT;
        private RectTransform _desktopStockExpirySurfaceRT;
        private RectTransform _desktopChangesSurfaceRT;
        private RectTransform _desktopAdviceSurfaceRT;

        private TextMeshProUGUI _desktopInsightsSummary;
        private TextMeshProUGUI _desktopInsightsAlerts;
        private TextMeshProUGUI _desktopInsightsStockExpiry;
        private TextMeshProUGUI _desktopInsightsChanges;
        private TextMeshProUGUI _desktopInsightsAdvice;
        private TextMeshProUGUI _desktopInsightsFooter;
        private TextMeshProUGUI _desktopInsightsStatus;

        private Transform _desktopAlertRowsRoot;
        private readonly List<GameObject> _desktopAlertRowObjects = new();
        private Transform _desktopStockExpiryRowsRoot;
        private readonly List<GameObject> _desktopStockExpiryRowObjects = new();
        private Transform _desktopChangeRowsRoot;
        private readonly List<GameObject> _desktopChangeRowObjects = new();
        private Transform _desktopAdviceRowsRoot;
        private readonly List<GameObject> _desktopAdviceRowObjects = new();

        private int _desktopPendingPriceProductId = -1;
        private float _desktopPendingPriceFocusUntil;
        private int _desktopPendingMarketProductId = -1;
        private float _desktopPendingMarketFocusUntil;

        private float _desktopInsightsNextRefresh;
        private float _desktopInsightsNextAdviceRefresh;
        private int _desktopInsightsDay = -1;
        private int _desktopPriceChangeSequence;
        private int _desktopVisibleAlertCount;
        private int _desktopVisibleStockExpiryCount;
        private int _desktopVisibleChangeCount;
        private int _desktopVisibleAdviceCount;
        private string _desktopInsightsSlot = "";
        private bool _desktopAlertsDataReady;
        private bool _desktopStockExpiryDataReady;
        private bool _desktopAdviceDataReady;

        private readonly Dictionary<int, float> _desktopLastObservedPrices = new();
        private readonly Dictionary<int, DesktopPriceChange> _desktopPriceChanges = new();
        private readonly List<DesktopStoreAlert> _desktopAlertsCache = new();
        private readonly List<DesktopStockExpiryRow> _desktopStockExpiryCache = new();
        private readonly List<DesktopPriceAdvice> _desktopAdviceCache = new();

        // v15: only promotions applied from THIS panel are auto-expired.
        // They are stored separately so the user's own promotions are untouched.
        private readonly Dictionary<int, DesktopAppliedPromotion> _desktopAppliedPromotions = new();
        private bool _desktopPromoStateLoaded;
        private string _desktopPromoStateSlot = "";
        private int _desktopPromoCleanupDay = -1;

        // v13 PERF: all three heavy desktop sections share one analysis snapshot.
        // The expiration aggregation is even more expensive, so it has its own cache.
        private List<ProductBusinessAnalysisRow> _desktopBusinessRowsCache;
        private float _desktopBusinessRowsCacheUntil;
        private int _desktopBusinessRowsCacheDay = -1;
        private Dictionary<int, SortedDictionary<int, int>> _desktopExpirationMapCache;
        private float _desktopExpirationMapCacheUntil;
        private int _desktopExpirationMapCacheDay = -1;

        private sealed class DesktopPriceChange
        {
            public int ProductId;
            public float OldPrice;
            public float NewPrice;
            public int Sequence;
            public bool OldPriceKnown = true;
        }

        private sealed class DesktopStoreAlert
        {
            public int ProductId;
            public int Severity; // 2 = krytyczny, 1 = ostrzeżenie
            public float Score;
            public string Title;
            public string Details;
            public bool OpenMarket;
        }

        private sealed class DesktopStockExpiryRow
        {
            public int ProductId;
            public bool IsWeight;
            public float ShopStock;
            public float WarehouseStock;
            public float TotalStock;
            public float DaysOfCover;
            public int NearestDaysLeft = int.MaxValue;
            public int ExpiredUnits;
            public int TodayUnits;
            public int TomorrowUnits;
            public int InTwoDaysUnits;
            public float Score;
        }

        private sealed class DesktopPriceAdvice
        {
            public int ProductId;
            public float CurrentPrice;
            public float SuggestedPrice;
            public float CurrentMargin;
            public float SuggestedMargin;
            public float Score;
            public float Confidence;
            public bool IsPromotion;
            public int PromotionRate;
        }

        private sealed class DesktopAppliedPromotion
        {
            public int ProductId;
            public int AppliedDay;
            public int DiscountRate;
        }

        private void EnsureDesktopInsightsPanel()
        {
            if (_desktopCanvas == null) return;

            if (_desktopInsightsPanel != null)
            {
                SetDesktopInsightsVisible(!_isOpen);
                return;
            }

            Transform existing = FindDirectChild(_desktopCanvas, "StatisticMod.DesktopInsights");
            if (existing != null)
            {
                // Panel jest tworzony runtime. Jeżeli w tej samej scenie został stary układ
                // bez ScrollRect, przebuduj go zamiast próbować podpinać niezgodną strukturę.
                Transform viewport = existing.Find("ScrollArea");
                Transform stockExpiry = existing.Find("ScrollArea/Content/StockExpirySurface");
                if (viewport == null || stockExpiry == null)
                {
                    UnityEngine.Object.Destroy(existing.gameObject);
                }
                else
                {
                    _desktopInsightsPanel = existing.gameObject;
                    BindDesktopInsightsTextReferences(existing);
                    BindDesktopScrollReferences(existing);
                    SetDesktopInsightsVisible(!_isOpen);
                    return;
                }
            }

            CacheGameTmpStyle();

            var root = new GameObject("StatisticMod.DesktopInsights");
            root.transform.SetParent(_desktopCanvas, false);
            _desktopInsightsPanel = root;

            var rt = root.AddComponent<RectTransform>();
            // Prawa część pulpitu. Górę zostawiamy wolną dla STORE STATUS.
            rt.anchorMin = new Vector2(0.695f, 0.060f);
            rt.anchorMax = new Vector2(0.997f, 0.905f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var bg = root.AddComponent<Image>();
            bg.color = new Color(StatsAppTheme.AppFrame.r, StatsAppTheme.AppFrame.g, StatsAppTheme.AppFrame.b, 0.96f);
            bg.raycastTarget = true;

            var outline = root.AddComponent<Outline>();
            outline.effectColor = new Color(StatsAppTheme.HeaderBorder.r, StatsAppTheme.HeaderBorder.g, StatsAppTheme.HeaderBorder.b, 0.95f);
            outline.effectDistance = new Vector2(1f, -1f);

            CreateDesktopInsightsSurface(root.transform, "Header", new Vector2(0.018f, 0.862f), new Vector2(0.982f, 0.982f), StatsAppTheme.Header);

            CreateDesktopInsightsText(
                root.transform,
                "Title",
                Plugin.T("ASYSTENT SKLEPU", "STORE ASSISTANT"),
                new Vector2(0.045f, 0.914f),
                new Vector2(0.955f, 0.969f),
                12.6f,
                TextAlignmentOptions.MidlineLeft,
                StatsAppTheme.TextLight,
                true);

            CreateDesktopInsightsText(
                root.transform,
                "Subtitle",
                Plugin.T("kółko = przewijanie  •  ODŚW. = aktualizuj ciężkie dane", "wheel = scroll  •  REFRESH = update heavy data"),
                new Vector2(0.045f, 0.870f),
                new Vector2(0.955f, 0.916f),
                5.4f,
                TextAlignmentOptions.MidlineLeft,
                new Color(StatsAppTheme.TextLight.r, StatsAppTheme.TextLight.g, StatsAppTheme.TextLight.b, 0.72f),
                false);

            BuildDesktopInsightsScrollArea(root.transform);
            BuildDesktopInsightsContent();
            BuildDesktopInsightsFooter(root.transform);

            ResetDesktopPriceTracking(GetCurrentDaySafe());
            RefreshDesktopInsights(true);
            SetDesktopInsightsVisible(!_isOpen);

            if (_desktopInsightsScroll != null)
                _desktopInsightsScroll.verticalNormalizedPosition = 1f;

            if (Plugin.Log != null)
                Plugin.Log.LogInfo("[DesktopInsights] Panel v15 utworzony: poprawione ceny + promocje jednodniowe.");
        }

        private void BuildDesktopInsightsScrollArea(Transform root)
        {
            var scrollGO = new GameObject("ScrollArea");
            scrollGO.transform.SetParent(root, false);

            var scrollRT = scrollGO.AddComponent<RectTransform>();
            scrollRT.anchorMin = new Vector2(0.025f, 0.075f);
            scrollRT.anchorMax = new Vector2(0.948f, 0.850f);
            scrollRT.offsetMin = Vector2.zero;
            scrollRT.offsetMax = Vector2.zero;
            _desktopInsightsViewport = scrollRT;

            var raycastImage = scrollGO.AddComponent<Image>();
            raycastImage.color = new Color(1f, 1f, 1f, 0.005f);
            raycastImage.raycastTarget = true;

            var mask = scrollGO.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(scrollGO.transform, false);
            var contentRT = contentGO.AddComponent<RectTransform>();
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = new Vector2(0f, 500f);
            _desktopInsightsContent = contentRT;

            var scroll = scrollGO.AddComponent<ScrollRect>();
            scroll.content = contentRT;
            scroll.viewport = scrollRT;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.elasticity = 0.08f;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.scrollSensitivity = 300f;
            _desktopInsightsScroll = scroll;

            // Cienki pasek po prawej stronie pokazuje, że panel można przewijać.
            var barGO = new GameObject("Scrollbar");
            barGO.transform.SetParent(root, false);
            var barRT = barGO.AddComponent<RectTransform>();
            barRT.anchorMin = new Vector2(0.952f, 0.08f);
            barRT.anchorMax = new Vector2(0.974f, 0.847f);
            barRT.offsetMin = Vector2.zero;
            barRT.offsetMax = Vector2.zero;

            var barImage = barGO.AddComponent<Image>();
            barImage.color = new Color(StatsAppTheme.Border.r, StatsAppTheme.Border.g, StatsAppTheme.Border.b, 0.28f);
            barImage.raycastTarget = true;

            var slidingArea = new GameObject("Sliding Area");
            slidingArea.transform.SetParent(barGO.transform, false);
            var slidingRT = slidingArea.AddComponent<RectTransform>();
            slidingRT.anchorMin = Vector2.zero;
            slidingRT.anchorMax = Vector2.one;
            slidingRT.offsetMin = new Vector2(1f, 1f);
            slidingRT.offsetMax = new Vector2(-1f, -1f);

            var handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(slidingArea.transform, false);
            var handleRT = handleGO.AddComponent<RectTransform>();
            handleRT.anchorMin = Vector2.zero;
            handleRT.anchorMax = Vector2.one;
            handleRT.offsetMin = Vector2.zero;
            handleRT.offsetMax = Vector2.zero;

            var handleImage = handleGO.AddComponent<Image>();
            handleImage.color = new Color(StatsAppTheme.Info.r, StatsAppTheme.Info.g, StatsAppTheme.Info.b, 0.72f);
            handleImage.raycastTarget = true;

            var scrollbar = barGO.AddComponent<Scrollbar>();
            scrollbar.handleRect = handleRT;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.numberOfSteps = 0;
            _desktopInsightsScrollbar = scrollbar;

            scroll.verticalScrollbar = scrollbar;
            scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            scroll.verticalScrollbarSpacing = 2f;
        }

        private void BuildDesktopInsightsContent()
        {
            if (_desktopInsightsContent == null) return;

            _desktopSummarySurface = CreateDesktopInsightsSurface(
                _desktopInsightsContent,
                "SummarySurface",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                StatsAppTheme.Surface);
            _desktopSummarySurfaceRT = _desktopSummarySurface.GetComponent<RectTransform>();

            _desktopInsightsSummary = CreateDesktopInsightsInsetText(
                _desktopSummarySurface.transform,
                "SummaryText",
                8.6f,
                TextAlignmentOptions.TopLeft,
                StatsAppTheme.TextDark,
                11f, 9f, 10f, 7f);

            _desktopAlertsSurface = CreateDesktopInsightsSurface(
                _desktopInsightsContent,
                "AlertsSurface",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                StatsAppTheme.Surface);
            _desktopAlertsSurfaceRT = _desktopAlertsSurface.GetComponent<RectTransform>();

            _desktopInsightsAlerts = CreateDesktopInsightsText(
                _desktopAlertsSurface.transform,
                "AlertsText",
                "<b>" + Plugin.T("ALERTY", "ALERTS") + "</b>",
                new Vector2(0.03f, 0.865f),
                new Vector2(0.97f, 0.985f),
                7.2f,
                TextAlignmentOptions.MidlineLeft,
                StatsAppTheme.TextDark,
                false);
            ConfigureDesktopSectionHeader(_desktopInsightsAlerts);

            var alertRowsGO = new GameObject("AlertRows");
            alertRowsGO.transform.SetParent(_desktopAlertsSurface.transform, false);
            _desktopAlertRowsRoot = alertRowsGO.transform;
            var alertRowsRT = alertRowsGO.AddComponent<RectTransform>();
            alertRowsRT.anchorMin = Vector2.zero;
            alertRowsRT.anchorMax = Vector2.one;
            alertRowsRT.offsetMin = Vector2.zero;
            alertRowsRT.offsetMax = Vector2.zero;

            _desktopStockExpirySurface = CreateDesktopInsightsSurface(
                _desktopInsightsContent,
                "StockExpirySurface",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                StatsAppTheme.Surface);
            _desktopStockExpirySurfaceRT = _desktopStockExpirySurface.GetComponent<RectTransform>();

            _desktopInsightsStockExpiry = CreateDesktopInsightsText(
                _desktopStockExpirySurface.transform,
                "StockExpiryText",
                "<b>" + Plugin.T("ZAPASY I TERMINY", "STOCK & EXPIRY") + "</b>",
                new Vector2(0.03f, 0.865f),
                new Vector2(0.97f, 0.985f),
                7.2f,
                TextAlignmentOptions.MidlineLeft,
                StatsAppTheme.TextDark,
                false);
            ConfigureDesktopSectionHeader(_desktopInsightsStockExpiry);

            var stockExpiryRowsGO = new GameObject("StockExpiryRows");
            stockExpiryRowsGO.transform.SetParent(_desktopStockExpirySurface.transform, false);
            _desktopStockExpiryRowsRoot = stockExpiryRowsGO.transform;
            var stockExpiryRowsRT = stockExpiryRowsGO.AddComponent<RectTransform>();
            stockExpiryRowsRT.anchorMin = Vector2.zero;
            stockExpiryRowsRT.anchorMax = Vector2.one;
            stockExpiryRowsRT.offsetMin = Vector2.zero;
            stockExpiryRowsRT.offsetMax = Vector2.zero;

            _desktopChangesSurface = CreateDesktopInsightsSurface(
                _desktopInsightsContent,
                "ChangesSurface",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                StatsAppTheme.Surface);
            _desktopChangesSurfaceRT = _desktopChangesSurface.GetComponent<RectTransform>();

            _desktopInsightsChanges = CreateDesktopInsightsText(
                _desktopChangesSurface.transform,
                "ChangesText",
                "<b>" + Plugin.T("ZMIANY CEN DZISIAJ", "PRICE CHANGES TODAY") + "</b>",
                new Vector2(0.03f, 0.865f),
                new Vector2(0.97f, 0.985f),
                7.2f,
                TextAlignmentOptions.MidlineLeft,
                StatsAppTheme.TextDark,
                false);
            ConfigureDesktopSectionHeader(_desktopInsightsChanges);

            var changeRowsGO = new GameObject("ChangeRows");
            changeRowsGO.transform.SetParent(_desktopChangesSurface.transform, false);
            _desktopChangeRowsRoot = changeRowsGO.transform;
            var changeRowsRT = changeRowsGO.AddComponent<RectTransform>();
            changeRowsRT.anchorMin = Vector2.zero;
            changeRowsRT.anchorMax = Vector2.one;
            changeRowsRT.offsetMin = Vector2.zero;
            changeRowsRT.offsetMax = Vector2.zero;

            _desktopAdviceSurface = CreateDesktopInsightsSurface(
                _desktopInsightsContent,
                "AdviceSurface",
                new Vector2(0f, 1f),
                new Vector2(1f, 1f),
                StatsAppTheme.Surface);
            _desktopAdviceSurfaceRT = _desktopAdviceSurface.GetComponent<RectTransform>();

            _desktopInsightsAdvice = CreateDesktopInsightsText(
                _desktopAdviceSurface.transform,
                "AdviceText",
                "<b>" + Plugin.T("SUGEROWANE CENY I PROMOCJE", "PRICE & PROMOTION SUGGESTIONS") + "</b>",
                new Vector2(0.03f, 0.865f),
                new Vector2(0.97f, 0.985f),
                7.2f,
                TextAlignmentOptions.MidlineLeft,
                StatsAppTheme.TextDark,
                false);
            ConfigureDesktopSectionHeader(_desktopInsightsAdvice);

            var rowsGO = new GameObject("AdviceRows");
            rowsGO.transform.SetParent(_desktopAdviceSurface.transform, false);
            _desktopAdviceRowsRoot = rowsGO.transform;
            var rowsRT = rowsGO.AddComponent<RectTransform>();
            rowsRT.anchorMin = Vector2.zero;
            rowsRT.anchorMax = Vector2.one;
            rowsRT.offsetMin = Vector2.zero;
            rowsRT.offsetMax = Vector2.zero;
        }

        private void BuildDesktopInsightsFooter(Transform root)
        {
            _desktopInsightsStatus = CreateDesktopInsightsText(
                root,
                "StatusText",
                Plugin.T("Dane ciężkie: ręczne odświeżanie", "Heavy data: manual refresh"),
                new Vector2(0.03f, 0.008f),
                new Vector2(0.34f, 0.064f),
                5.5f,
                TextAlignmentOptions.MidlineLeft,
                new Color(StatsAppTheme.TextLight.r, StatsAppTheme.TextLight.g, StatsAppTheme.TextLight.b, 0.68f),
                false);

            CreateDesktopInsightsButton(
                root,
                "RefreshInsightsButton",
                Plugin.T("ODŚW.", "REFRESH"),
                new Vector2(0.35f, 0.008f),
                new Vector2(0.52f, 0.064f),
                StatsAppTheme.Positive,
                RefreshDesktopInsightsManual);

            CreateDesktopInsightsButton(
                root,
                "OpenPricesButton",
                Plugin.T("CENY", "PRICES"),
                new Vector2(0.53f, 0.008f),
                new Vector2(0.75f, 0.064f),
                StatsAppTheme.Info,
                OpenNativePricesApp);

            CreateDesktopInsightsButton(
                root,
                "OpenAnalysisButton",
                Plugin.T("ANALIZA", "ANALYSIS"),
                new Vector2(0.76f, 0.008f),
                new Vector2(0.97f, 0.064f),
                StatsAppTheme.Header,
                OpenDesktopInsightsAnalysis);

            _desktopInsightsFooter = _desktopInsightsStatus;
        }

        private void BindDesktopInsightsTextReferences(Transform root)
        {
            if (root == null) return;

            Transform content = root.Find("ScrollArea/Content");
            if (content != null)
            {
                _desktopInsightsSummary = FindDesktopInsightsText(content, "SummarySurface/SummaryText");
                _desktopInsightsAlerts = FindDesktopInsightsText(content, "AlertsSurface/AlertsText");
                Transform alertRows = content.Find("AlertsSurface/AlertRows");
                if (alertRows != null) _desktopAlertRowsRoot = alertRows;
                _desktopInsightsStockExpiry = FindDesktopInsightsText(content, "StockExpirySurface/StockExpiryText");
                Transform stockExpiryRows = content.Find("StockExpirySurface/StockExpiryRows");
                if (stockExpiryRows != null) _desktopStockExpiryRowsRoot = stockExpiryRows;
                _desktopInsightsChanges = FindDesktopInsightsText(content, "ChangesSurface/ChangesText");
                Transform changeRows = content.Find("ChangesSurface/ChangeRows");
                if (changeRows != null) _desktopChangeRowsRoot = changeRows;
                _desktopInsightsAdvice = FindDesktopInsightsText(content, "AdviceSurface/AdviceText");
                Transform rows = content.Find("AdviceSurface/AdviceRows");
                if (rows != null) _desktopAdviceRowsRoot = rows;
            }

            _desktopInsightsStatus = FindDesktopInsightsText(root, "StatusText");
            _desktopInsightsFooter = _desktopInsightsStatus;
        }

        private void BindDesktopScrollReferences(Transform root)
        {
            if (root == null) return;
            Transform scrollArea = root.Find("ScrollArea");
            if (scrollArea != null)
            {
                _desktopInsightsViewport = scrollArea.GetComponent<RectTransform>();
                _desktopInsightsScroll = scrollArea.GetComponent<ScrollRect>();
                Transform content = scrollArea.Find("Content");
                if (content != null) _desktopInsightsContent = content.GetComponent<RectTransform>();
            }

            Transform sb = root.Find("Scrollbar");
            if (sb != null) _desktopInsightsScrollbar = sb.GetComponent<Scrollbar>();

            if (_desktopInsightsContent != null)
            {
                Transform summary = _desktopInsightsContent.Find("SummarySurface");
                Transform alerts = _desktopInsightsContent.Find("AlertsSurface");
                Transform stockExpiry = _desktopInsightsContent.Find("StockExpirySurface");
                Transform changes = _desktopInsightsContent.Find("ChangesSurface");
                Transform advice = _desktopInsightsContent.Find("AdviceSurface");
                if (summary != null)
                {
                    _desktopSummarySurface = summary.gameObject;
                    _desktopSummarySurfaceRT = summary.GetComponent<RectTransform>();
                }
                if (alerts != null)
                {
                    _desktopAlertsSurface = alerts.gameObject;
                    _desktopAlertsSurfaceRT = alerts.GetComponent<RectTransform>();
                }
                if (stockExpiry != null)
                {
                    _desktopStockExpirySurface = stockExpiry.gameObject;
                    _desktopStockExpirySurfaceRT = stockExpiry.GetComponent<RectTransform>();
                }
                if (changes != null)
                {
                    _desktopChangesSurface = changes.gameObject;
                    _desktopChangesSurfaceRT = changes.GetComponent<RectTransform>();
                }
                if (advice != null)
                {
                    _desktopAdviceSurface = advice.gameObject;
                    _desktopAdviceSurfaceRT = advice.GetComponent<RectTransform>();
                }
            }
        }

        private static TextMeshProUGUI FindDesktopInsightsText(Transform root, string path)
        {
            if (root == null) return null;
            Transform t = root.Find(path);
            return t != null ? t.GetComponent<TextMeshProUGUI>() : null;
        }

        private void TickDesktopInsightsPanel()
        {
            // v15: expire only promotions created by this panel when the day changes.
            try { ClearExpiredDesktopPromotionsForDay(GetCurrentDaySafe()); } catch { }

            if (_desktopCanvas == null) return;
            if (_desktopInsightsPanel == null)
            {
                EnsureDesktopInsightsPanel();
                if (_desktopInsightsPanel == null) return;
            }

            TryFocusPendingPriceProduct();
            TryFocusPendingMarketProduct();

            SetDesktopInsightsVisible(!_isOpen);
            if (_isOpen) return;

            // v13 PERF: the panel GameObject can be active while its computer/desktop parent
            // is hidden. Do not run store-wide scans during normal gameplay.
            if (_desktopInsightsPanel == null || !_desktopInsightsPanel.activeInHierarchy)
                return;

            int day = GetCurrentDaySafe();
            string slot = GetDesktopSlotSafe();

            bool dayOrSlotChanged =
                _desktopInsightsDay != day ||
                !string.Equals(
                    _desktopInsightsSlot,
                    slot,
                    StringComparison.OrdinalIgnoreCase);

            if (dayOrSlotChanged)
            {
                ResetDesktopPriceTracking(day);

                // One heavy refresh per new day is acceptable and keeps the panel
                // useful without generating periodic gameplay hitches.
                RefreshDesktopInsights(true);

                _desktopInsightsNextRefresh =
                    Time.realtimeSinceStartup +
                    DESKTOP_INSIGHTS_REFRESH;

                return;
            }

            if (Time.realtimeSinceStartup < _desktopInsightsNextRefresh)
                return;

            _desktopInsightsNextRefresh =
                Time.realtimeSinceStartup +
                DESKTOP_INSIGHTS_REFRESH;

            // v14 PERF:
            // DO NOT call PollDesktopPriceChanges here. Daily costs change at the
            // day transition and are already rebuilt from PreviousCost -> CurrentCost
            // inside ResetDesktopPriceTracking().
            RefreshDesktopInsights(false);
        }

        private void SetDesktopInsightsVisible(bool visible)
        {
            if (_desktopInsightsPanel == null) return;
            if (_desktopInsightsPanel.activeSelf != visible)
                _desktopInsightsPanel.SetActive(visible);
        }

        private void RefreshDesktopInsightsManual()
        {
            try
            {
                // v14 PERF: only this button (plus day change / initial build)
                // performs the expensive stock + expiry + business scans.
                _desktopBusinessRowsCache = null;
                _desktopBusinessRowsCacheUntil = 0f;
                _desktopBusinessRowsCacheDay = -1;
                _desktopExpirationMapCache = null;
                _desktopExpirationMapCacheUntil = 0f;
                _desktopExpirationMapCacheDay = -1;
                _desktopInsightsNextAdviceRefresh = 0f;

                SetDesktopStatus(
                    Plugin.T("Odświeżam alerty, zapasy i sugestie…", "Refreshing alerts, stock and suggestions…"),
                    false);

                RefreshDesktopInsights(true);

                SetDesktopStatus(
                    Plugin.T("Odświeżono dane panelu.", "Panel data refreshed."),
                    false);
            }
            catch (Exception ex)
            {
                SetDesktopStatus(
                    Plugin.T("Nie udało się odświeżyć panelu.", "Could not refresh panel."),
                    true);

                Plugin.DebugWarning(
                    "[DesktopInsights] Manual refresh failed: " +
                    ex.Message);
            }
        }

        private void OpenDesktopInsightsAnalysis()
        {
            _hubMode = HubMode.Analysis;
            ShowStats();
        }

        private void OpenNativePricesApp()
        {
            try
            {
                Transform shortcuts = _appShortcuts;
                if (shortcuts == null && _computerRoot != null)
                    shortcuts = FindByPath(_computerRoot, "Screen/Desktop Canvas/App Shortcuts");

                if (shortcuts == null)
                {
                    SetDesktopStatus(Plugin.T("Nie znaleziono skrótu CENY.", "PRICES shortcut not found."), true);
                    return;
                }

                Transform priceShortcut = null;

                string[] objectNames =
                {
                    "Price.exe", "Prices.exe", "Pricing.exe", "Price App.exe", "Pricing App.exe"
                };

                for (int n = 0; n < objectNames.Length && priceShortcut == null; n++)
                {
                    Transform candidate = shortcuts.Find(objectNames[n]);
                    if (candidate != null) priceShortcut = candidate;
                }

                if (priceShortcut == null)
                {
                    string[] labels = { "CENY", "PRICES", "PRICE", "PRICING" };
                    for (int l = 0; l < labels.Length && priceShortcut == null; l++)
                    {
                        int idx = FindShortcutIndexByLabel(shortcuts, labels[l]);
                        if (idx >= 0 && idx < shortcuts.childCount)
                            priceShortcut = shortcuts.GetChild(idx);
                    }
                }

                if (priceShortcut == null)
                {
                    // Ostatni fallback: nazwa obiektu zawierająca Price/Pricing.
                    for (int i = 0; i < shortcuts.childCount; i++)
                    {
                        Transform child = shortcuts.GetChild(i);
                        if (child == null || string.IsNullOrEmpty(child.name)) continue;
                        string n = child.name.ToUpperInvariant();
                        if (n.Contains("PRICE") || n.Contains("PRICING"))
                        {
                            priceShortcut = child;
                            break;
                        }
                    }
                }

                if (priceShortcut == null)
                {
                    SetDesktopStatus(Plugin.T("Nie znaleziono skrótu CENY.", "PRICES shortcut not found."), true);
                    return;
                }

                Button button = priceShortcut.GetComponent<Button>();
                if (button == null) button = priceShortcut.GetComponentInChildren<Button>(true);
                if (button == null)
                {
                    SetDesktopStatus(Plugin.T("Skrót CENY nie ma przycisku.", "PRICES shortcut has no button."), true);
                    return;
                }

                SetDesktopStatus(Plugin.T("Otwieram aplikację CENY…", "Opening PRICES app…"), false);
                button.onClick.Invoke();
            }
            catch (Exception ex)
            {
                SetDesktopStatus(Plugin.T("Nie udało się otworzyć CEN.", "Could not open PRICES."), true);
                Plugin.DebugWarning("[DesktopInsights] Open prices failed: " + ex.Message);
            }
        }

        private void OpenNativePricesProduct(int productId)
        {
            if (productId <= 0) return;

            _desktopPendingPriceProductId = productId;
            _desktopPendingPriceFocusUntil = Time.realtimeSinceStartup + 6f;
            SetDesktopStatus(Plugin.T("Otwieram produkt w CENACH…", "Opening product in PRICES…"), false);
            OpenNativePricesApp();

            try
            {
                if (PricingProductViewer.HasInstance && PricingProductViewer.Instance != null)
                    PricingProductViewer.Instance.RefreshUnlockedProducts(productId);
            }
            catch { }
        }

        private void TryFocusPendingPriceProduct()
        {
            int productId = _desktopPendingPriceProductId;
            if (productId <= 0) return;

            if (Time.realtimeSinceStartup > _desktopPendingPriceFocusUntil)
            {
                _desktopPendingPriceProductId = -1;
                SetDesktopStatus(Plugin.T("Nie znalazłem produktu w aktualnym widoku CEN.", "Product was not found in the current PRICES view."), true);
                return;
            }

            try
            {
                PricingItem[] items = UnityEngine.Object.FindObjectsOfType<PricingItem>();
                if (items == null || items.Length == 0) return;

                for (int i = 0; i < items.Length; i++)
                {
                    PricingItem item = items[i];
                    if (item == null || item.PricingData == null || item.PricingData.ProductID != productId)
                        continue;

                    ScrollRect scroll = FindDesktopParentScrollRect(item.transform);
                    RectTransform target = item.GetComponent<RectTransform>();
                    if (scroll != null && target != null)
                        CenterDesktopRectInScroll(scroll, target);

                    try
                    {
                        Selectable selectable = item.GetComponentInChildren<Selectable>(true);
                        if (selectable != null) selectable.Select();
                    }
                    catch { }

                    _desktopPendingPriceProductId = -1;
                    SetDesktopStatus(TrimDesktopProductName(GetProductNameSafe(productId), 24) +
                        Plugin.T(" — otwarto w CENACH", " — opened in PRICES"), false);
                    return;
                }
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning("[DesktopInsights] Focus price product failed: " + ex.Message);
            }
        }

        private void OpenNativeMarketApp()
        {
            try
            {
                Transform shortcuts = _appShortcuts;
                if (shortcuts == null && _computerRoot != null)
                    shortcuts = FindByPath(_computerRoot, "Screen/Desktop Canvas/App Shortcuts");

                if (shortcuts == null)
                {
                    SetDesktopStatus(Plugin.T("Nie znaleziono skrótu RYNEK.", "MARKET shortcut not found."), true);
                    return;
                }

                Transform marketShortcut =
                    shortcuts.Find("Wholesale Market.Exe") ??
                    shortcuts.Find("Market.Exe");

                if (marketShortcut == null)
                {
                    string[] labels = { "RYNEK", "MARKET", "WHOLESALE MARKET" };
                    for (int i = 0; i < labels.Length && marketShortcut == null; i++)
                    {
                        int idx = FindShortcutIndexByLabel(shortcuts, labels[i]);
                        if (idx >= 0 && idx < shortcuts.childCount)
                            marketShortcut = shortcuts.GetChild(idx);
                    }
                }

                if (marketShortcut == null)
                {
                    for (int i = 0; i < shortcuts.childCount; i++)
                    {
                        Transform child = shortcuts.GetChild(i);
                        if (child == null || string.IsNullOrEmpty(child.name)) continue;

                        string n = child.name.ToUpperInvariant();
                        if (n.Contains("MARKET"))
                        {
                            marketShortcut = child;
                            break;
                        }
                    }
                }

                if (marketShortcut == null)
                {
                    SetDesktopStatus(Plugin.T("Nie znaleziono skrótu RYNEK.", "MARKET shortcut not found."), true);
                    return;
                }

                Button button = marketShortcut.GetComponent<Button>();
                if (button == null) button = marketShortcut.GetComponentInChildren<Button>(true);
                if (button == null)
                {
                    SetDesktopStatus(Plugin.T("Skrót RYNEK nie ma przycisku.", "MARKET shortcut has no button."), true);
                    return;
                }

                SetDesktopStatus(Plugin.T("Otwieram RYNEK…", "Opening MARKET…"), false);
                button.onClick.Invoke();
            }
            catch (Exception ex)
            {
                SetDesktopStatus(Plugin.T("Nie udało się otworzyć RYNKU.", "Could not open MARKET."), true);
                Plugin.DebugWarning("[DesktopInsights] Open market failed: " + ex.Message);
            }
        }

        private void OpenNativeMarketProduct(int productId)
        {
            if (productId <= 0) return;

            _desktopPendingMarketProductId = productId;
            _desktopPendingMarketFocusUntil = Time.realtimeSinceStartup + 6f;
            SetDesktopStatus(Plugin.T("Otwieram produkt w RYNKU…", "Opening product in MARKET…"), false);
            OpenNativeMarketApp();

            try
            {
                ProductViewer[] viewers = UnityEngine.Object.FindObjectsOfType<ProductViewer>();
                if (viewers != null)
                {
                    for (int i = 0; i < viewers.Length; i++)
                    {
                        ProductViewer viewer = viewers[i];
                        if (viewer != null && viewer.gameObject.activeInHierarchy)
                            viewer.RefreshFilters();
                    }
                }
            }
            catch { }
        }

        private void TryFocusPendingMarketProduct()
        {
            int productId = _desktopPendingMarketProductId;
            if (productId <= 0) return;

            if (Time.realtimeSinceStartup > _desktopPendingMarketFocusUntil)
            {
                _desktopPendingMarketProductId = -1;
                SetDesktopStatus(Plugin.T("RYNEK otwarty — nie znalazłem kafelka produktu.", "MARKET opened — product tile was not found."), true);
                return;
            }

            try
            {
                string wantedName = GetProductNameSafe(productId);
                if (string.IsNullOrWhiteSpace(wantedName)) return;

                SalesItem[] items = UnityEngine.Object.FindObjectsOfType<SalesItem>();
                if (items == null || items.Length == 0) return;

                for (int i = 0; i < items.Length; i++)
                {
                    SalesItem item = items[i];
                    if (item == null || !item.gameObject.activeInHierarchy) continue;

                    bool match = false;
                    TextMeshProUGUI[] texts = item.GetComponentsInChildren<TextMeshProUGUI>(true);
                    if (texts != null)
                    {
                        for (int t = 0; t < texts.Length; t++)
                        {
                            TextMeshProUGUI label = texts[t];
                            if (label == null || string.IsNullOrWhiteSpace(label.text)) continue;

                            string visible = label.text.Replace("\n", " ").Trim();
                            if (visible.IndexOf(wantedName, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                wantedName.IndexOf(visible, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                match = true;
                                break;
                            }
                        }
                    }

                    if (!match) continue;

                    ScrollRect scroll = FindDesktopParentScrollRect(item.transform);
                    RectTransform target = item.GetComponent<RectTransform>();
                    if (scroll != null && target != null)
                        CenterDesktopRectInScroll(scroll, target);

                    try
                    {
                        Selectable selectable = item.GetComponentInChildren<Selectable>(true);
                        if (selectable != null) selectable.Select();
                    }
                    catch { }

                    _desktopPendingMarketProductId = -1;
                    SetDesktopStatus(
                        TrimDesktopProductName(wantedName, 24) +
                        Plugin.T(" — otwarto w RYNKU", " — opened in MARKET"),
                        false);
                    return;
                }
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning("[DesktopInsights] Focus market product failed: " + ex.Message);
            }
        }

        private static ScrollRect FindDesktopParentScrollRect(Transform start)
        {
            Transform current = start;
            while (current != null)
            {
                ScrollRect scroll = current.GetComponent<ScrollRect>();
                if (scroll != null) return scroll;
                current = current.parent;
            }
            return null;
        }

        private static void CenterDesktopRectInScroll(ScrollRect scroll, RectTransform target)
        {
            if (scroll == null || target == null || scroll.content == null) return;

            RectTransform viewport = scroll.viewport != null ? scroll.viewport : scroll.GetComponent<RectTransform>();
            RectTransform content = scroll.content;
            if (viewport == null || content == null) return;

            Canvas.ForceUpdateCanvases();

            float maxScroll = Mathf.Max(0f, content.rect.height - viewport.rect.height);
            if (maxScroll <= 0.5f)
            {
                scroll.verticalNormalizedPosition = 1f;
                return;
            }

            Vector3 worldCenter = target.TransformPoint(target.rect.center);
            Vector3 localCenter = content.InverseTransformPoint(worldCenter);
            float fromTop = content.rect.yMax - localCenter.y;
            float desired = Mathf.Clamp(fromTop - viewport.rect.height * 0.5f, 0f, maxScroll);
            scroll.StopMovement();
            scroll.verticalNormalizedPosition = 1f - (desired / maxScroll);
            scroll.velocity = Vector2.zero;
        }

        private void ResetDesktopPriceTracking(int day)
        {
            _desktopInsightsDay = Mathf.Max(1, day);
            _desktopInsightsSlot = GetDesktopSlotSafe();
            _desktopPriceChangeSequence = 0;
            _desktopLastObservedPrices.Clear();
            _desktopPriceChanges.Clear();
            _desktopAlertsCache.Clear();
            _desktopStockExpiryCache.Clear();
            _desktopAdviceCache.Clear();
            _desktopAlertsDataReady = false;
            _desktopStockExpiryDataReady = false;
            _desktopAdviceDataReady = false;
            _desktopInsightsNextAdviceRefresh = 0f;

            _desktopBusinessRowsCache = null;
            _desktopBusinessRowsCacheUntil = 0f;
            _desktopBusinessRowsCacheDay = -1;
            _desktopExpirationMapCache = null;
            _desktopExpirationMapCacheUntil = 0f;
            _desktopExpirationMapCacheDay = -1;

            // v6: sekcja „Zmiany cen dzisiaj” śledzi dzienny koszt/rynkową cenę zakupu,
            // a nie cenę sprzedaży ustawioną przez gracza. Gra przechowuje PreviousCost,
            // więc po rozpoczęciu dnia (także po restarcie) możemy odtworzyć dokładnie
            // stara cena -> nowa cena bez zgadywania na podstawie LastChangeDate.
            LoadDesktopInsightsState(_desktopInsightsDay);
            RebuildDesktopDailyCostChanges();
            PrimeDesktopObservedPrices();
            SaveDesktopInsightsState();

            if (Plugin.Log != null)
                Plugin.Log.LogInfo("[DesktopInsights] v6 day=" + _desktopInsightsDay +
                    " costChanges=" + _desktopPriceChanges.Count +
                    " trackedCosts=" + _desktopLastObservedPrices.Count);
        }

        private void RebuildDesktopDailyCostChanges()
        {
            PriceManager pm = GetDesktopPriceManager();
            if (pm == null) return;

            HashSet<int> productIds = GetDesktopTrackedProductIds(pm);
            foreach (int productId in productIds)
            {
                float current = GetDesktopCurrentCost(pm, productId);
                float previous = GetDesktopPreviousCost(pm, productId);
                if (current <= 0.0001f || previous <= 0.0001f) continue;
                if (Mathf.Abs(current - previous) < 0.005f) continue;

                RecordDesktopPriceChange(productId, previous, current);
            }
        }

        private void PrimeDesktopObservedPrices()
        {
            PriceManager pm = GetDesktopPriceManager();
            if (pm == null) return;

            HashSet<int> productIds = GetDesktopTrackedProductIds(pm);
            foreach (int productId in productIds)
            {
                float price = GetDesktopCurrentCost(pm, productId);
                if (price > 0.0001f)
                    _desktopLastObservedPrices[productId] = price;
            }
        }

        private void PollDesktopPriceChanges()
        {
            PriceManager pm = GetDesktopPriceManager();
            if (pm == null) return;

            bool dirty = false;
            HashSet<int> productIds = GetDesktopTrackedProductIds(pm);
            foreach (int productId in productIds)
            {
                // Dzienna zmiana dotyczy kosztu/rynkowej ceny zakupu. Cena sprzedaży gracza
                // jest używana osobno w sekcji rekomendacji marży.
                float current = GetDesktopCurrentCost(pm, productId);
                if (current <= 0.0001f) continue;

                if (!_desktopLastObservedPrices.TryGetValue(productId, out float previous))
                {
                    _desktopLastObservedPrices[productId] = current;
                    dirty = true;
                    continue;
                }

                if (Mathf.Abs(current - previous) < 0.005f) continue;

                RecordDesktopPriceChange(productId, previous, current);
                _desktopLastObservedPrices[productId] = current;
                dirty = true;
            }

            if (dirty) SaveDesktopInsightsState();
        }

        private HashSet<int> GetDesktopTrackedProductIds(PriceManager pm)
        {
            var result = new HashSet<int>();
            EnsureDesktopProductCache();

            if (Plugin.ProductCache != null && Plugin.ProductCache.Count > 0)
            {
                foreach (int productId in Plugin.ProductCache.ById.Keys)
                    if (productId > 0 && productId != 9999) result.Add(productId);
            }

            try
            {
                var prices = pm != null ? pm.m_PricesSetByPlayer : null;
                if (prices != null)
                {
                    for (int i = 0; i < prices.Count; i++)
                    {
                        Pricing pricing = prices[i];
                        if (pricing != null && pricing.ProductID > 0 && pricing.ProductID != 9999)
                            result.Add(pricing.ProductID);
                    }
                }
            }
            catch { }

            return result;
        }

        private void RecordDesktopPriceChange(int productId, float oldPrice, float newPrice, bool oldPriceKnown = true)
        {
            _desktopPriceChangeSequence++;

            if (_desktopPriceChanges.TryGetValue(productId, out DesktopPriceChange existing) && existing != null)
            {
                // Jeżeli po restarcie znaliśmy tylko fakt zmiany z LastChangeDate,
                // pierwsza kolejna zaobserwowana zmiana daje nam już prawidłową cenę wejściową.
                if (!existing.OldPriceKnown && oldPriceKnown)
                {
                    existing.OldPrice = oldPrice;
                    existing.OldPriceKnown = true;
                }
                existing.NewPrice = newPrice;
                existing.Sequence = _desktopPriceChangeSequence;
            }
            else
            {
                _desktopPriceChanges[productId] = new DesktopPriceChange
                {
                    ProductId = productId,
                    OldPrice = oldPrice,
                    NewPrice = newPrice,
                    Sequence = _desktopPriceChangeSequence,
                    OldPriceKnown = oldPriceKnown
                };
            }

        }

        private void RefreshDesktopInsights(bool forceAdvice)
        {
            if (_desktopInsightsSummary == null || _desktopInsightsAlerts == null || _desktopInsightsStockExpiry == null || _desktopInsightsChanges == null || _desktopInsightsAdvice == null)
                return;

            int day = GetCurrentDaySafe();
            DayStats stats = StatsStore.TryGetDay(day);

            float revenue = stats != null ? stats.SoldRevenue : 0f;
            float cost = stats != null ? stats.SoldCost : 0f;
            float profit = revenue - cost;
            float margin = revenue > 0.0001f ? (profit / revenue) * 100f : 0f;
            float soldVisible = 0f;
            if (stats != null)
                soldVisible = stats.SoldUnits + stats.SoldWeightKg;

            var summary = new StringBuilder(256);
            summary.Append("<b>").Append(Plugin.T("DZIEŃ", "DAY")).Append(' ').Append(day).Append("</b>\n");
            summary.Append(Plugin.T("Przychód", "Revenue")).Append(": <color=").Append(StatsAppTheme.PositiveHex).Append("><b>")
                .Append(Plugin.Money(revenue)).Append("</b></color>   ");
            summary.Append(Plugin.T("Zysk brutto", "Gross profit")).Append(": <b>").Append(Plugin.Money(profit)).Append("</b>\n");
            summary.Append(Plugin.T("Marża brutto", "Gross margin")).Append(": <color=")
                .Append(GetDesktopMarginColor(margin)).Append("><b>").Append(margin.ToString("0.0")).Append("%</b></color>   ");
            summary.Append(Plugin.T("Sprzedaż", "Sold")).Append(": <b>").Append(soldVisible.ToString("0.##")).Append("</b>");
            _desktopInsightsSummary.text = summary.ToString();

            RefreshDesktopChangesText();

            if (forceAdvice)
            {
                bool alertsReady = RebuildDesktopAlertsCache();
                RebuildDesktopAlertRows();

                bool stockExpiryReady = RebuildDesktopStockExpiryCache();
                RebuildDesktopStockExpiryRows();

                bool ready = RebuildDesktopAdviceCache();

                // v14: no automatic periodic heavy refresh.
                _desktopInsightsNextAdviceRefresh =
                    Time.realtimeSinceStartup +
                    DESKTOP_ADVICE_REFRESH;

                RebuildDesktopAdviceRows();
            }
            else
            {
                if (string.IsNullOrEmpty(_desktopInsightsStockExpiry.text))
                    RebuildDesktopStockExpiryRows();

                if (string.IsNullOrEmpty(_desktopInsightsAdvice.text))
                    RebuildDesktopAdviceRows();
            }

            LayoutDesktopInsightsContent();
        }

        private void EnsureDesktopInsightsRowRoots()
        {
            if (_desktopAlertsSurface != null && _desktopAlertRowsRoot == null)
            {
                Transform existing = _desktopAlertsSurface.transform.Find("AlertRows");
                if (existing != null) _desktopAlertRowsRoot = existing;
                else
                {
                    var go = new GameObject("AlertRows");
                    go.transform.SetParent(_desktopAlertsSurface.transform, false);
                    var rt = go.AddComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    _desktopAlertRowsRoot = go.transform;
                }
            }

            if (_desktopStockExpirySurface != null && _desktopStockExpiryRowsRoot == null)
            {
                Transform existing = _desktopStockExpirySurface.transform.Find("StockExpiryRows");
                if (existing != null) _desktopStockExpiryRowsRoot = existing;
                else
                {
                    var go = new GameObject("StockExpiryRows");
                    go.transform.SetParent(_desktopStockExpirySurface.transform, false);
                    var rt = go.AddComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    _desktopStockExpiryRowsRoot = go.transform;
                }
            }

            if (_desktopChangesSurface != null && _desktopChangeRowsRoot == null)
            {
                Transform existing = _desktopChangesSurface.transform.Find("ChangeRows");
                if (existing != null) _desktopChangeRowsRoot = existing;
                else
                {
                    var go = new GameObject("ChangeRows");
                    go.transform.SetParent(_desktopChangesSurface.transform, false);
                    var rt = go.AddComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    _desktopChangeRowsRoot = go.transform;
                }
            }

            if (_desktopAdviceSurface != null && _desktopAdviceRowsRoot == null)
            {
                Transform existing = _desktopAdviceSurface.transform.Find("AdviceRows");
                if (existing != null) _desktopAdviceRowsRoot = existing;
                else
                {
                    var go = new GameObject("AdviceRows");
                    go.transform.SetParent(_desktopAdviceSurface.transform, false);
                    var rt = go.AddComponent<RectTransform>();
                    rt.anchorMin = Vector2.zero;
                    rt.anchorMax = Vector2.one;
                    rt.offsetMin = Vector2.zero;
                    rt.offsetMax = Vector2.zero;
                    _desktopAdviceRowsRoot = go.transform;
                }
            }
        }

        private List<ProductBusinessAnalysisRow> GetDesktopBusinessRowsCached(bool force = false)
        {
            int day = GetCurrentDaySafe();
            float now = Time.realtimeSinceStartup;

            if (!force &&
                _desktopBusinessRowsCache != null &&
                _desktopBusinessRowsCacheDay == day &&
                now < _desktopBusinessRowsCacheUntil)
            {
                return _desktopBusinessRowsCache;
            }

            try
            {
                List<ProductBusinessAnalysisRow> rows =
                    BusinessAnalysisService.BuildRows(7);

                if (rows != null)
                {
                    _desktopBusinessRowsCache = rows;
                    _desktopBusinessRowsCacheDay = day;
                    _desktopBusinessRowsCacheUntil =
                        now + DESKTOP_ANALYSIS_CACHE_SECONDS;
                }
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning(
                    "[DesktopInsights] Shared business analysis failed: " +
                    ex.Message);
            }

            return _desktopBusinessRowsCache;
        }

        private Dictionary<int, SortedDictionary<int, int>> GetDesktopExpirationMapCached(bool force = false)
        {
            if (!SmartExpiration.PluginConfig.ExpiryEnabled)
                return null;

            int day = GetCurrentDaySafe();
            float now = Time.realtimeSinceStartup;

            if (!force &&
                _desktopExpirationMapCache != null &&
                _desktopExpirationMapCacheDay == day &&
                now < _desktopExpirationMapCacheUntil)
            {
                return _desktopExpirationMapCache;
            }

            try
            {
                BuildExpirationMaps(
                    out Dictionary<int, SortedDictionary<int, int>> shelfExpirations,
                    out Dictionary<int, SortedDictionary<int, int>> boxExpirations,
                    out Dictionary<int, SortedDictionary<int, int>> globalExpirations);

                _desktopExpirationMapCache = globalExpirations;
                _desktopExpirationMapCacheDay = day;
                _desktopExpirationMapCacheUntil =
                    now + DESKTOP_EXPIRY_CACHE_SECONDS;
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning(
                    "[DesktopInsights] Cached expiration map failed: " +
                    ex.Message);
            }

            return _desktopExpirationMapCache;
        }

        private bool RebuildDesktopStockExpiryCache()
        {
            _desktopStockExpiryCache.Clear();
            _desktopStockExpiryDataReady = false;

            List<ProductBusinessAnalysisRow> rows =
                GetDesktopBusinessRowsCached();

            if (rows == null)
                return false;

            Dictionary<int, SortedDictionary<int, int>> globalExpirations =
                GetDesktopExpirationMapCached();

            for (int i = 0; i < rows.Count; i++)
            {
                ProductBusinessAnalysisRow row = rows[i];
                if (row == null || row.ProductId <= 0) continue;

                var item = new DesktopStockExpiryRow
                {
                    ProductId = row.ProductId,
                    IsWeight = row.IsWeight,
                    ShopStock = row.ShopStockVisible,
                    WarehouseStock = row.WarehouseStockVisible,
                    TotalStock = row.TotalStockVisible,
                    DaysOfCover = row.DaysOfCover,
                    Score = 0f
                };

                if (globalExpirations != null &&
                    globalExpirations.TryGetValue(row.ProductId, out SortedDictionary<int, int> batches) &&
                    batches != null)
                {
                    foreach (KeyValuePair<int, int> kv in batches)
                    {
                        if (kv.Value <= 0) continue;

                        if (kv.Key < item.NearestDaysLeft)
                            item.NearestDaysLeft = kv.Key;

                        if (kv.Key < 0)
                            item.ExpiredUnits += kv.Value;
                        else if (kv.Key == 0)
                            item.TodayUnits += kv.Value;
                        else if (kv.Key == 1)
                            item.TomorrowUnits += kv.Value;
                        else if (kv.Key == 2)
                            item.InTwoDaysUnits += kv.Value;
                    }
                }

                // Priorytet: termin, potem brak / niski zapas.
                if (item.ExpiredUnits > 0)
                    item.Score += 320f + Mathf.Min(40f, item.ExpiredUnits * 4f);
                if (item.TodayUnits > 0)
                    item.Score += 260f + Mathf.Min(35f, item.TodayUnits * 3f);
                if (item.TomorrowUnits > 0)
                    item.Score += 190f + Mathf.Min(30f, item.TomorrowUnits * 2f);
                if (item.InTwoDaysUnits > 0)
                    item.Score += 120f + Mathf.Min(20f, item.InTwoDaysUnits);

                if (row.ForecastDailyVisible > 0.15f)
                {
                    if (item.TotalStock <= 0.001f)
                        item.Score += 220f;
                    else if (row.DaysOfCover < 1f)
                        item.Score += 120f + (1f - Mathf.Clamp01(row.DaysOfCover)) * 30f;
                    else if (row.DaysOfCover < 2f)
                        item.Score += 55f;
                }

                // Sekcja ma pokazywać rzeczy wymagające uwagi, nie pełny katalog.
                if (item.Score > 0.01f)
                    _desktopStockExpiryCache.Add(item);
            }

            _desktopStockExpiryCache.Sort((a, b) =>
            {
                int score = b.Score.CompareTo(a.Score);
                if (score != 0) return score;

                int exp = a.NearestDaysLeft.CompareTo(b.NearestDaysLeft);
                if (exp != 0) return exp;

                return a.TotalStock.CompareTo(b.TotalStock);
            });

            _desktopStockExpiryDataReady = true;
            Plugin.DebugLog(
                "[DesktopInsights] stockExpiry=" +
                _desktopStockExpiryCache.Count);
            return true;
        }

        private void RebuildDesktopStockExpiryRows()
        {
            EnsureDesktopInsightsRowRoots();
            if (_desktopStockExpiryRowsRoot == null || _desktopInsightsStockExpiry == null) return;

            for (int i = _desktopStockExpiryRowObjects.Count - 1; i >= 0; i--)
            {
                GameObject go = _desktopStockExpiryRowObjects[i];
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            _desktopStockExpiryRowObjects.Clear();

            int urgentExpiry = 0;
            int lowStock = 0;
            for (int i = 0; i < _desktopStockExpiryCache.Count; i++)
            {
                DesktopStockExpiryRow row = _desktopStockExpiryCache[i];
                if (row == null) continue;

                if (row.ExpiredUnits > 0 || row.TodayUnits > 0 || row.TomorrowUnits > 0)
                    urgentExpiry++;
                if (row.TotalStock <= 0.001f || row.DaysOfCover < 1f)
                    lowStock++;
            }

            string header = "<b>" + Plugin.T("ZAPASY I TERMINY", "STOCK & EXPIRY") + "</b>";
            if (urgentExpiry > 0)
                header += "  <color=" + StatsAppTheme.WarningHex + "><b>" + urgentExpiry + " " +
                    Plugin.T("pilne", "urgent") + "</b></color>";
            if (lowStock > 0)
                header += "  <color=" + StatsAppTheme.InfoHex + "><b>" + lowStock + " " +
                    Plugin.T("niski zapas", "low stock") + "</b></color>";

            _desktopInsightsStockExpiry.text = header;
            ConfigureDesktopSectionHeader(_desktopInsightsStockExpiry);

            if (_desktopStockExpiryCache.Count == 0)
            {
                _desktopVisibleStockExpiryCount = 0;
                string emptyMessage = _desktopStockExpiryDataReady
                    ? Plugin.T("Brak pilnych problemów z zapasem i terminami.", "No urgent stock or expiry issues.")
                    : Plugin.T("Czekam na dane zapasu i terminów…", "Waiting for stock and expiry data…");

                TextMeshProUGUI empty = CreateDesktopInsightsText(
                    _desktopStockExpiryRowsRoot,
                    "StockExpiryEmpty",
                    "<color=" + StatsAppTheme.MutedHex + ">" + emptyMessage + "</color>",
                    new Vector2(0.03f, 0.30f),
                    new Vector2(0.97f, 0.70f),
                    6.8f,
                    TextAlignmentOptions.MidlineLeft,
                    StatsAppTheme.TextDark,
                    false);
                _desktopStockExpiryRowObjects.Add(empty.gameObject);
                _desktopInsightsStockExpiry.transform.SetAsLastSibling();
                return;
            }

            int count = Mathf.Min(DESKTOP_MAX_STOCK_EXPIRY, _desktopStockExpiryCache.Count);
            _desktopVisibleStockExpiryCount = count;
            for (int i = 0; i < count; i++)
                CreateDesktopStockExpiryRow(_desktopStockExpiryCache[i], i);

            _desktopInsightsStockExpiry.transform.SetAsLastSibling();
        }

        private void CreateDesktopStockExpiryRow(DesktopStockExpiryRow item, int index)
        {
            if (_desktopStockExpiryRowsRoot == null || item == null) return;

            var row = new GameObject("StockExpiryRow_" + item.ProductId);
            row.transform.SetParent(_desktopStockExpiryRowsRoot, false);
            _desktopStockExpiryRowObjects.Add(row);

            var rt = row.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.025f, 1f);
            rt.anchorMax = new Vector2(0.975f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, DESKTOP_STOCK_EXPIRY_ROW_HEIGHT - 2f);
            rt.anchoredPosition = new Vector2(0f, -(32f + index * DESKTOP_STOCK_EXPIRY_ROW_HEIGHT));

            var bg = row.AddComponent<Image>();
            bg.color = index % 2 == 0
                ? new Color(StatsAppTheme.Info.r, StatsAppTheme.Info.g, StatsAppTheme.Info.b, 0.045f)
                : new Color(1f, 1f, 1f, 0.001f);
            bg.raycastTarget = false;

            CreateDesktopMarketProductLink(
                row.transform,
                "ProductLink",
                TrimDesktopProductName(GetProductNameSafe(item.ProductId), 16),
                new Vector2(0.015f, 0.10f),
                new Vector2(0.31f, 0.90f),
                item.ProductId,
                6.7f);

            string unit = item.IsWeight ? " kg" : "";
            string stockText =
                Plugin.T("sklep", "shop") + " <b>" + item.ShopStock.ToString(item.IsWeight ? "0.##" : "0") + unit + "</b>" +
                "  •  " + Plugin.T("mag.", "wh.") + " <b>" +
                item.WarehouseStock.ToString(item.IsWeight ? "0.##" : "0") + unit + "</b>";

            CreateDesktopInsightsText(
                row.transform,
                "StockInfo",
                stockText,
                new Vector2(0.32f, 0.08f),
                new Vector2(0.67f, 0.92f),
                5.9f,
                TextAlignmentOptions.MidlineLeft,
                StatsAppTheme.TextDark,
                false);

            string expiryText;
            string expiryColor;

            if (item.ExpiredUnits > 0)
            {
                expiryColor = StatsAppTheme.NegativeHex;
                expiryText = Plugin.T("po terminie", "expired") + " <b>" + item.ExpiredUnits + "</b>";
            }
            else if (item.TodayUnits > 0)
            {
                expiryColor = StatsAppTheme.NegativeHex;
                expiryText = Plugin.T("dzisiaj", "today") + " <b>" + item.TodayUnits + "</b>";
            }
            else if (item.TomorrowUnits > 0)
            {
                expiryColor = StatsAppTheme.WarningHex;
                expiryText = Plugin.T("jutro", "tomorrow") + " <b>" + item.TomorrowUnits + "</b>";
            }
            else if (item.InTwoDaysUnits > 0)
            {
                expiryColor = StatsAppTheme.WarningHex;
                expiryText = Plugin.T("za 2 dni", "in 2 days") + " <b>" + item.InTwoDaysUnits + "</b>";
            }
            else if (item.TotalStock <= 0.001f)
            {
                expiryColor = StatsAppTheme.NegativeHex;
                expiryText = Plugin.T("BRAK TOWARU", "OUT OF STOCK");
            }
            else if (item.DaysOfCover < 2f)
            {
                expiryColor = StatsAppTheme.InfoHex;
                expiryText = Plugin.T("zapas", "cover") + " <b>" + item.DaysOfCover.ToString("0.0") + " d</b>";
            }
            else if (item.NearestDaysLeft != int.MaxValue)
            {
                expiryColor = StatsAppTheme.MutedHex;
                expiryText = Plugin.T("termin", "expiry") + " <b>" + item.NearestDaysLeft + " d</b>";
            }
            else
            {
                expiryColor = StatsAppTheme.MutedHex;
                expiryText = Plugin.T("termin OK", "expiry OK");
            }

            CreateDesktopInsightsText(
                row.transform,
                "ExpiryInfo",
                "<color=" + expiryColor + ">" + expiryText + "</color>",
                new Vector2(0.68f, 0.08f),
                new Vector2(0.99f, 0.92f),
                5.9f,
                TextAlignmentOptions.MidlineRight,
                StatsAppTheme.TextDark,
                false);
        }

        private bool RebuildDesktopAlertsCache()
        {
            _desktopAlertsCache.Clear();
            _desktopAlertsDataReady = false;

            List<ProductBusinessAnalysisRow> rows =
                GetDesktopBusinessRowsCached();

            if (rows == null) return false;

            var bestByProduct = new Dictionary<int, DesktopStoreAlert>();

            for (int i = 0; i < rows.Count; i++)
            {
                ProductBusinessAnalysisRow row = rows[i];
                if (row == null || row.ProductId <= 0 || row.ProductId == 9999) continue;

                float cost = row.CurrentCost;
                float price = row.CurrentPrice;
                float margin = (price > 0.0001f && cost > 0.0001f)
                    ? ((price - cost) / price) * 100f
                    : 0f;

                if (price > 0.0001f && cost > 0.0001f && price < cost - 0.005f)
                {
                    AddDesktopAlertCandidate(bestByProduct, new DesktopStoreAlert
                    {
                        ProductId = row.ProductId,
                        Severity = 2,
                        Score = 120f + Mathf.Abs(margin),
                        Title = Plugin.T("SPRZEDAŻ PONIŻEJ KOSZTU", "SELLING BELOW COST"),
                        Details = Plugin.T("cena", "price") + " " + Plugin.Money(price) +
                            "  •  " + Plugin.T("koszt", "cost") + " " + Plugin.Money(cost) +
                            "  •  " + Plugin.T("marża", "margin") + " " + margin.ToString("0.0") + "%"
                    });
                }

                if (row.TotalStockUnits <= 0 && row.ForecastDailyVisible > 0.15f)
                {
                    AddDesktopAlertCandidate(bestByProduct, new DesktopStoreAlert
                    {
                        ProductId = row.ProductId,
                        Severity = 2,
                        Score = 110f + Mathf.Min(25f, row.ForecastDailyVisible),
                        Title = Plugin.T("BRAK TOWARU", "OUT OF STOCK"),
                        OpenMarket = true,
                        Details = Plugin.T("stan", "stock") + " 0  •  " +
                            Plugin.T("prognoza", "forecast") + " " + row.ForecastDailyVisible.ToString("0.##") +
                            Plugin.T("/dzień", "/day")
                    });
                }

                if (row.MissRate >= 0.15f && row.StockMissedVisible > 0.0001f)
                {
                    AddDesktopAlertCandidate(bestByProduct, new DesktopStoreAlert
                    {
                        ProductId = row.ProductId,
                        Severity = 1,
                        Score = 85f + row.MissRate * 40f + Mathf.Min(20f, row.MissedRevenue),
                        Title = Plugin.T("UTRACONA SPRZEDAŻ", "LOST SALES"),
                        OpenMarket = true,
                        Details = Plugin.T("braki", "miss rate") + " " + (row.MissRate * 100f).ToString("0") + "%  •  " +
                            Plugin.T("utracone", "lost") + " " + Plugin.Money(row.MissedRevenue)
                    });
                }

                if (row.ForecastDailyVisible > 0.15f && row.DaysOfCover < 1f)
                {
                    string orderText = row.RecommendedOrderUnits > 0
                        ? "  •  " + Plugin.T("zamów", "order") + " " + row.RecommendedOrderUnits
                        : string.Empty;

                    AddDesktopAlertCandidate(bestByProduct, new DesktopStoreAlert
                    {
                        ProductId = row.ProductId,
                        Severity = 1,
                        Score = 75f + (1f - Mathf.Clamp01(row.DaysOfCover)) * 20f,
                        Title = Plugin.T("ZAPAS PONIŻEJ 1 DNIA", "LESS THAN 1 DAY OF STOCK"),
                        OpenMarket = true,
                        Details = Plugin.T("pokrycie", "cover") + " " + row.DaysOfCover.ToString("0.0") +
                            Plugin.T(" dnia", " day") + orderText
                    });
                }

                if (price > 0.0001f && cost > 0.0001f && price >= cost && margin < 5f)
                {
                    AddDesktopAlertCandidate(bestByProduct, new DesktopStoreAlert
                    {
                        ProductId = row.ProductId,
                        Severity = 1,
                        Score = 60f + (5f - margin) * 4f,
                        Title = Plugin.T("BARDZO NISKA MARŻA", "VERY LOW MARGIN"),
                        Details = Plugin.T("marża", "margin") + " " + margin.ToString("0.0") + "%  •  " +
                            Plugin.T("cena", "price") + " " + Plugin.Money(price)
                    });
                }
            }

            foreach (DesktopStoreAlert alert in bestByProduct.Values)
                _desktopAlertsCache.Add(alert);

            _desktopAlertsCache.Sort((a, b) =>
            {
                int sev = b.Severity.CompareTo(a.Severity);
                if (sev != 0) return sev;
                return b.Score.CompareTo(a.Score);
            });

            _desktopAlertsDataReady = true;
            if (Plugin.Log != null)
                Plugin.Log.LogInfo("[DesktopInsights] alerts=" + _desktopAlertsCache.Count);
            return true;
        }

        private static void AddDesktopAlertCandidate(
            Dictionary<int, DesktopStoreAlert> bestByProduct,
            DesktopStoreAlert candidate)
        {
            if (bestByProduct == null || candidate == null || candidate.ProductId <= 0) return;

            if (!bestByProduct.TryGetValue(candidate.ProductId, out DesktopStoreAlert current) ||
                current == null ||
                candidate.Severity > current.Severity ||
                (candidate.Severity == current.Severity && candidate.Score > current.Score))
            {
                bestByProduct[candidate.ProductId] = candidate;
            }
        }

        private void RebuildDesktopAlertRows()
        {
            EnsureDesktopInsightsRowRoots();
            if (_desktopAlertRowsRoot == null || _desktopInsightsAlerts == null) return;

            for (int i = _desktopAlertRowObjects.Count - 1; i >= 0; i--)
            {
                GameObject go = _desktopAlertRowObjects[i];
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            _desktopAlertRowObjects.Clear();

            int critical = 0;
            int warnings = 0;
            for (int i = 0; i < _desktopAlertsCache.Count; i++)
            {
                DesktopStoreAlert alert = _desktopAlertsCache[i];
                if (alert == null) continue;
                if (alert.Severity >= 2) critical++;
                else warnings++;
            }

            string header = "<b>" + Plugin.T("ALERTY", "ALERTS") + "</b>";
            if (critical > 0)
                header += "  <color=" + StatsAppTheme.NegativeHex + "><b>" + critical + " " + Plugin.T("kryt.", "crit.") + "</b></color>";
            if (warnings > 0)
                header += "  <color=" + StatsAppTheme.WarningHex + "><b>" + warnings + " " + Plugin.T("uwag", "warn") + "</b></color>";
            _desktopInsightsAlerts.text = header;
            ConfigureDesktopSectionHeader(_desktopInsightsAlerts);

            if (_desktopAlertsCache.Count == 0)
            {
                _desktopVisibleAlertCount = 0;
                string emptyMessage = _desktopAlertsDataReady
                    ? Plugin.T("Brak pilnych alertów.", "No urgent alerts.")
                    : Plugin.T("Czekam na dane analizy…", "Waiting for analysis data…");

                TextMeshProUGUI empty = CreateDesktopInsightsText(
                    _desktopAlertRowsRoot,
                    "AlertsEmpty",
                    "<color=" + StatsAppTheme.MutedHex + ">" + emptyMessage + "</color>",
                    new Vector2(0.03f, 0.30f),
                    new Vector2(0.97f, 0.70f),
                    7.0f,
                    TextAlignmentOptions.MidlineLeft,
                    StatsAppTheme.TextDark,
                    false);
                _desktopAlertRowObjects.Add(empty.gameObject);
                _desktopInsightsAlerts.transform.SetAsLastSibling();
                return;
            }

            int count = Mathf.Min(DESKTOP_MAX_ALERTS, _desktopAlertsCache.Count);
            _desktopVisibleAlertCount = count;
            for (int i = 0; i < count; i++)
                CreateDesktopAlertRow(_desktopAlertsCache[i], i);

            _desktopInsightsAlerts.transform.SetAsLastSibling();
        }

        private void CreateDesktopAlertRow(DesktopStoreAlert alert, int index)
        {
            if (_desktopAlertRowsRoot == null || alert == null) return;

            var row = new GameObject("AlertRow_" + alert.ProductId);
            row.transform.SetParent(_desktopAlertRowsRoot, false);
            _desktopAlertRowObjects.Add(row);

            var rt = row.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.025f, 1f);
            rt.anchorMax = new Vector2(0.975f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, DESKTOP_ALERT_ROW_HEIGHT - 3f);
            rt.anchoredPosition = new Vector2(0f, -(34f + index * DESKTOP_ALERT_ROW_HEIGHT));

            var bg = row.AddComponent<Image>();
            Color baseColor = alert.Severity >= 2 ? StatsAppTheme.Negative : StatsAppTheme.Warning;
            bg.color = new Color(baseColor.r, baseColor.g, baseColor.b, alert.Severity >= 2 ? 0.12f : 0.10f);
            bg.raycastTarget = false;

            if (alert.OpenMarket)
            {
                CreateDesktopMarketProductLink(
                    row.transform,
                    "ProductLink",
                    TrimDesktopProductName(GetProductNameSafe(alert.ProductId), 17),
                    new Vector2(0.015f, 0.10f),
                    new Vector2(0.34f, 0.90f),
                    alert.ProductId,
                    6.8f);
            }
            else
            {
                CreateDesktopProductLink(
                    row.transform,
                    "ProductLink",
                    TrimDesktopProductName(GetProductNameSafe(alert.ProductId), 17),
                    new Vector2(0.015f, 0.10f),
                    new Vector2(0.34f, 0.90f),
                    alert.ProductId,
                    6.8f);
            }

            string color = alert.Severity >= 2 ? StatsAppTheme.NegativeHex : StatsAppTheme.WarningHex;
            string text = "<color=" + color + "><b>" + alert.Title + "</b></color>\n" +
                "<size=85%>" + alert.Details + "</size>";

            CreateDesktopInsightsText(
                row.transform,
                "AlertInfo",
                text,
                new Vector2(0.36f, 0.05f),
                new Vector2(0.99f, 0.95f),
                6.2f,
                TextAlignmentOptions.MidlineLeft,
                StatsAppTheme.TextDark,
                false);
        }

        private void RefreshDesktopChangesText()
        {
            EnsureDesktopInsightsRowRoots();
            if (_desktopInsightsChanges == null || _desktopChangeRowsRoot == null) return;

            for (int i = _desktopChangeRowObjects.Count - 1; i >= 0; i--)
            {
                GameObject go = _desktopChangeRowObjects[i];
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            _desktopChangeRowObjects.Clear();

            var changes = new List<DesktopPriceChange>(_desktopPriceChanges.Values);
            changes.Sort((a, b) => b.Sequence.CompareTo(a.Sequence));

            string header = "<b>" + Plugin.T("ZMIANY CEN DZISIAJ", "PRICE CHANGES TODAY") + "</b>";
            if (changes.Count > 0)
                header += "  <color=" + StatsAppTheme.InfoHex + ">(" + changes.Count + ")</color>";
            _desktopInsightsChanges.text = header;
            ConfigureDesktopSectionHeader(_desktopInsightsChanges);

            if (changes.Count == 0)
            {
                _desktopVisibleChangeCount = 0;
                TextMeshProUGUI empty = CreateDesktopInsightsText(
                    _desktopChangeRowsRoot,
                    "ChangesEmpty",
                    "<color=" + StatsAppTheme.MutedHex + ">" +
                    Plugin.T("Brak zarejestrowanych zmian cen.", "No recorded price changes.") +
                    "</color>",
                    new Vector2(0.03f, 0.42f),
                    new Vector2(0.97f, 0.72f),
                    7.4f,
                    TextAlignmentOptions.TopLeft,
                    StatsAppTheme.TextDark,
                    false);
                _desktopChangeRowObjects.Add(empty.gameObject);
                if (_desktopInsightsChanges != null) _desktopInsightsChanges.transform.SetAsLastSibling();
                return;
            }

            int count = Mathf.Min(DESKTOP_MAX_CHANGES, changes.Count);
            _desktopVisibleChangeCount = count;
            for (int i = 0; i < count; i++)
                CreateDesktopChangeRow(changes[i], i);

            if (changes.Count > count)
            {
                TextMeshProUGUI more = CreateDesktopInsightsText(
                    _desktopChangeRowsRoot,
                    "ChangesMore",
                    "<color=" + StatsAppTheme.MutedHex + ">+" + (changes.Count - count) + " " +
                    Plugin.T("kolejnych zmian", "more changes") + "</color>",
                    new Vector2(0.03f, 0f),
                    new Vector2(0.97f, 0f),
                    8.0f,
                    TextAlignmentOptions.TopLeft,
                    StatsAppTheme.TextDark,
                    false);
                RectTransform moreRT = more.GetComponent<RectTransform>();
                moreRT.anchorMin = new Vector2(0.03f, 1f);
                moreRT.anchorMax = new Vector2(0.97f, 1f);
                moreRT.pivot = new Vector2(0.5f, 1f);
                moreRT.sizeDelta = new Vector2(0f, DESKTOP_CHANGE_LINE_HEIGHT);
                moreRT.anchoredPosition = new Vector2(0f, -(32f + count * DESKTOP_CHANGE_LINE_HEIGHT));
                _desktopChangeRowObjects.Add(more.gameObject);
            }

            if (_desktopInsightsChanges != null) _desktopInsightsChanges.transform.SetAsLastSibling();
        }

        private void CreateDesktopChangeRow(DesktopPriceChange change, int index)
        {
            if (_desktopChangeRowsRoot == null || change == null) return;

            var row = new GameObject("ChangeRow_" + change.ProductId);
            row.transform.SetParent(_desktopChangeRowsRoot, false);
            _desktopChangeRowObjects.Add(row);

            var rt = row.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.025f, 1f);
            rt.anchorMax = new Vector2(0.975f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, DESKTOP_CHANGE_LINE_HEIGHT - 2f);
            rt.anchoredPosition = new Vector2(0f, -(32f + index * DESKTOP_CHANGE_LINE_HEIGHT));

            var rowBg = row.AddComponent<Image>();
            rowBg.color = new Color(StatsAppTheme.SurfaceAlt.r, StatsAppTheme.SurfaceAlt.g, StatsAppTheme.SurfaceAlt.b, index % 2 == 0 ? 0.34f : 0.18f);
            rowBg.raycastTarget = false;

            int productId = change.ProductId;
            CreateDesktopProductLink(
                row.transform,
                "ProductLink",
                TrimDesktopProductName(GetProductNameSafe(productId), 17),
                new Vector2(0.012f, 0.08f),
                new Vector2(0.37f, 0.92f),
                productId,
                6.8f);

            float delta = change.NewPrice - change.OldPrice;
            string color = delta >= 0f ? StatsAppTheme.PositiveHex : StatsAppTheme.NegativeHex;
            string priceText = Plugin.Money(change.OldPrice) + " → <b>" + Plugin.Money(change.NewPrice) + "</b>" +
                "  <color=" + color + ">" + (delta >= 0f ? "+" : "-") + Plugin.Money(Mathf.Abs(delta)) + "</color>";

            CreateDesktopInsightsText(
                row.transform,
                "PriceInfo",
                priceText,
                new Vector2(0.385f, 0.08f),
                new Vector2(0.995f, 0.92f),
                6.6f,
                TextAlignmentOptions.MidlineLeft,
                StatsAppTheme.TextDark,
                false);
        }

        private bool RebuildDesktopAdviceCache()
        {
            _desktopAdviceCache.Clear();
            _desktopAdviceDataReady = false;

            PriceManager pm = GetDesktopPriceManager();
            if (pm == null) return false;

            EnsureDesktopProductCache();

            var productIds = new HashSet<int>();
            if (Plugin.ProductCache != null && Plugin.ProductCache.Count > 0)
            {
                foreach (int productId in Plugin.ProductCache.ById.Keys)
                    if (productId > 0 && productId != 9999) productIds.Add(productId);
            }

            // Po restarcie ProductVisualCache może być gotowy później niż PriceManager.
            // Lista cen gracza jest więc drugim, niezależnym źródłem ID produktów.
            try
            {
                var prices = pm.m_PricesSetByPlayer;
                if (prices != null)
                {
                    for (int i = 0; i < prices.Count; i++)
                    {
                        Pricing pricing = prices[i];
                        if (pricing != null && pricing.ProductID > 0 && pricing.ProductID != 9999)
                            productIds.Add(pricing.ProductID);
                    }
                }
            }
            catch { }

            if (productIds.Count == 0) return false;

            var analysisById = new Dictionary<int, ProductBusinessAnalysisRow>();
            List<ProductBusinessAnalysisRow> rows =
                GetDesktopBusinessRowsCached();

            if (rows != null)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    ProductBusinessAnalysisRow row = rows[i];
                    if (row != null && row.ProductId > 0)
                        analysisById[row.ProductId] = row;
                }
            }

            foreach (int productId in productIds)
            {
                float cost = GetDesktopCurrentCost(pm, productId);
                float price = GetDesktopSellingPrice(pm, productId);
                if (cost <= 0.0001f || price <= 0.0001f) continue;

                ProductBusinessAnalysisRow row = null;
                analysisById.TryGetValue(productId, out row);
                if (row != null && row.PricingAdvice == PricingAdviceType.RestockFirst)
                    continue;

                // A product already discounted by the player/game should not receive
                // another price/promo recommendation from this panel.
                if (GetDesktopDiscountRate(pm, productId) > 0)
                    continue;

                float currentMargin = ((price - cost) / price) * 100f;
                float confidence = row != null ? row.PricingConfidence : 0.25f;

                int promotionRate = GetDesktopPromotionSuggestionRate(
                    pm,
                    productId,
                    cost,
                    price,
                    row);

                if (promotionRate > 0)
                {
                    float promoPrice =
                        Mathf.Round(price * (1f - promotionRate / 100f) * 100f) / 100f;

                    if (promoPrice > 0.0001f)
                    {
                        float promoMargin = ((promoPrice - cost) / promoPrice) * 100f;
                        float promoScore = 18f + confidence * 10f;

                        if (row != null)
                        {
                            promoScore += Mathf.Clamp(row.DaysOfCover - 4f, 0f, 12f) * 1.5f;
                            promoScore += Mathf.Clamp(-row.DemandTrend, 0f, 1f) * 18f;
                            promoScore += Mathf.Min(20f, row.ExpirationRiskUnits * 2f);
                        }

                        _desktopAdviceCache.Add(new DesktopPriceAdvice
                        {
                            ProductId = productId,
                            CurrentPrice = price,
                            SuggestedPrice = promoPrice,
                            CurrentMargin = currentMargin,
                            SuggestedMargin = promoMargin,
                            Score = promoScore,
                            Confidence = confidence,
                            IsPromotion = true,
                            PromotionRate = promotionRate
                        });

                        continue;
                    }
                }

                float suggested = GetDesktopSuggestedPrice(pm, productId, cost, price, row);
                if (suggested <= 0.0001f)
                    continue;

                // Do not clutter the panel with tiny permanent corrections.
                float priceGapPct = Mathf.Abs(suggested - price) / price * 100f;
                if (priceGapPct < 2f)
                    continue;

                float suggestedMargin = ((suggested - cost) / suggested) * 100f;
                float marginGap = Mathf.Abs(suggestedMargin - currentMargin);

                float priceScore = marginGap * 1.8f + priceGapPct + confidence * 8f;
                if (price < cost * 1.05f) priceScore += 30f;
                if (row != null && row.PricingAdvice != PricingAdviceType.Keep) priceScore += 8f;

                _desktopAdviceCache.Add(new DesktopPriceAdvice
                {
                    ProductId = productId,
                    CurrentPrice = price,
                    SuggestedPrice = suggested,
                    CurrentMargin = currentMargin,
                    SuggestedMargin = suggestedMargin,
                    Score = priceScore,
                    Confidence = confidence,
                    IsPromotion = false,
                    PromotionRate = 0
                });
            }

            _desktopAdviceCache.Sort((a, b) => b.Score.CompareTo(a.Score));
            _desktopAdviceDataReady = true;
            if (Plugin.Log != null)
            {
                int promoCount = 0;
                for (int i = 0; i < _desktopAdviceCache.Count; i++)
                {
                    DesktopPriceAdvice candidate = _desktopAdviceCache[i];
                    if (candidate != null && candidate.IsPromotion)
                        promoCount++;
                }

                Plugin.Log.LogInfo(
                    "[DesktopInsights] advice scan products=" + productIds.Count +
                    " suggestions=" + _desktopAdviceCache.Count +
                    " promos=" + promoCount);
            }
            return true;
        }

        private void RebuildDesktopAdviceRows()
        {
            EnsureDesktopInsightsRowRoots();
            if (_desktopAdviceRowsRoot == null || _desktopInsightsAdvice == null) return;

            for (int i = _desktopAdviceRowObjects.Count - 1; i >= 0; i--)
            {
                GameObject go = _desktopAdviceRowObjects[i];
                if (go != null) UnityEngine.Object.Destroy(go);
            }
            _desktopAdviceRowObjects.Clear();

            _desktopInsightsAdvice.text = "<b>" + Plugin.T("SUGEROWANE CENY I PROMOCJE", "PRICE & PROMOTION SUGGESTIONS") + "</b>";
            ConfigureDesktopSectionHeader(_desktopInsightsAdvice);

            if (_desktopAdviceCache.Count == 0)
            {
                _desktopVisibleAdviceCount = 0;
                string emptyMessage = _desktopAdviceDataReady
                    ? Plugin.T("Brak produktów wymagających teraz korekty ceny.", "No products currently need a price correction.")
                    : Plugin.T("Czekam na dane cenowe gry… ponawiam za chwilę.", "Waiting for game pricing data… retrying shortly.");
                var empty = CreateDesktopInsightsText(
                    _desktopAdviceRowsRoot,
                    "AdviceEmpty",
                    "<color=" + StatsAppTheme.MutedHex + ">" + emptyMessage + "</color>",
                    new Vector2(0.03f, 0.48f),
                    new Vector2(0.97f, 0.72f),
                    7.4f,
                    TextAlignmentOptions.TopLeft,
                    StatsAppTheme.TextDark,
                    false);
                _desktopAdviceRowObjects.Add(empty.gameObject);
                if (_desktopInsightsAdvice != null) _desktopInsightsAdvice.transform.SetAsLastSibling();
                return;
            }

            int count = Mathf.Min(DESKTOP_MAX_ADVICE, _desktopAdviceCache.Count);
            _desktopVisibleAdviceCount = count;

            for (int i = 0; i < count; i++)
            {
                DesktopPriceAdvice advice = _desktopAdviceCache[i];
                CreateDesktopAdviceRow(advice, i);
            }

            if (_desktopInsightsAdvice != null) _desktopInsightsAdvice.transform.SetAsLastSibling();
        }

        private void CreateDesktopAdviceRow(DesktopPriceAdvice advice, int index)
        {
            if (_desktopAdviceRowsRoot == null || advice == null) return;

            var row = new GameObject("AdviceRow_" + advice.ProductId);
            row.transform.SetParent(_desktopAdviceRowsRoot, false);
            _desktopAdviceRowObjects.Add(row);

            var rt = row.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.025f, 1f);
            rt.anchorMax = new Vector2(0.975f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, DESKTOP_ADVICE_ROW_HEIGHT - 3f);
            rt.anchoredPosition = new Vector2(0f, -(36f + index * DESKTOP_ADVICE_ROW_HEIGHT));

            var image = row.AddComponent<Image>();
            image.color = new Color(StatsAppTheme.SurfaceAlt.r, StatsAppTheme.SurfaceAlt.g, StatsAppTheme.SurfaceAlt.b, 0.72f);
            image.raycastTarget = false;

            bool raise = advice.SuggestedPrice > advice.CurrentPrice;
            string priceColor = advice.IsPromotion
                ? StatsAppTheme.WarningHex
                : (raise ? StatsAppTheme.PositiveHex : StatsAppTheme.WarningHex);

            int productId = advice.ProductId;
            CreateDesktopProductLink(
                row.transform,
                "ProductLink",
                TrimDesktopProductName(GetProductNameSafe(productId), 18),
                new Vector2(0.02f, 0.52f),
                new Vector2(0.73f, 0.94f),
                productId,
                7.1f);

            string text;
            if (advice.IsPromotion)
            {
                text =
                    "<color=" + StatsAppTheme.WarningHex + "><b>" +
                    Plugin.T("PROMOCJA", "PROMOTION") + " -" +
                    advice.PromotionRate + "%</b></color>   " +
                    Plugin.Money(advice.CurrentPrice) + " → <b>" +
                    Plugin.Money(advice.SuggestedPrice) + "</b>   " +
                    Plugin.T("marża po promo", "promo margin") + " " +
                    advice.SuggestedMargin.ToString("0.0") + "%";
            }
            else
            {
                text =
                    Plugin.Money(advice.CurrentPrice) + " → <color=" +
                    priceColor + "><b>" + Plugin.Money(advice.SuggestedPrice) +
                    "</b></color>   " +
                    Plugin.T("marża", "margin") + " " +
                    advice.CurrentMargin.ToString("0.0") + "% → " +
                    advice.SuggestedMargin.ToString("0.0") + "%";
            }

            CreateDesktopInsightsText(
                row.transform,
                "Info",
                text,
                new Vector2(0.02f, 0.06f),
                new Vector2(0.75f, 0.54f),
                advice.IsPromotion ? 6.2f : 6.8f,
                TextAlignmentOptions.MidlineLeft,
                StatsAppTheme.TextDark,
                false);

            if (advice.IsPromotion)
            {
                int promoRate = advice.PromotionRate;
                CreateDesktopInsightsButton(
                    row.transform,
                    "ApplyPromotion",
                    "-" + promoRate + "%",
                    new Vector2(0.77f, 0.16f),
                    new Vector2(0.975f, 0.84f),
                    StatsAppTheme.Warning,
                    () => ApplyDesktopSuggestedPromotion(productId));
            }
            else
            {
                CreateDesktopInsightsButton(
                    row.transform,
                    "ApplyPrice",
                    Plugin.T("USTAW", "APPLY"),
                    new Vector2(0.77f, 0.16f),
                    new Vector2(0.975f, 0.84f),
                    raise ? StatsAppTheme.Positive : StatsAppTheme.Warning,
                    () => ApplyDesktopSuggestedPrice(productId));
            }
        }

        private void ApplyDesktopSuggestedPrice(int productId)
        {
            DesktopPriceAdvice advice = null;
            for (int i = 0; i < _desktopAdviceCache.Count; i++)
            {
                DesktopPriceAdvice candidate = _desktopAdviceCache[i];
                if (candidate != null && candidate.ProductId == productId)
                {
                    advice = candidate;
                    break;
                }
            }

            if (advice == null)
            {
                SetDesktopStatus(Plugin.T("Sugestia jest już nieaktualna — odświeżam.", "Suggestion is stale — refreshing."), true);
                RefreshDesktopInsights(true);
                return;
            }

            if (advice.IsPromotion)
            {
                ApplyDesktopSuggestedPromotion(productId);
                return;
            }

            // Bezpośredni zapis ceny tylko w single-player. W co-op użytkownik może użyć CENY,
            // żeby zachować natywną ścieżkę synchronizacji gry.
            string sceneName = string.Empty;
            try { sceneName = SceneManager.GetActiveScene().name; } catch { }
            if (!string.Equals(sceneName, "Main Scene", StringComparison.OrdinalIgnoreCase))
            {
                SetDesktopStatus(Plugin.T("W multiplayer użyj aplikacji CENY.", "In multiplayer use the PRICES app."), true);
                OpenNativePricesApp();
                return;
            }

            PriceManager pm = GetDesktopPriceManager();
            if (pm == null)
            {
                SetDesktopStatus(Plugin.T("PriceManager nie jest jeszcze gotowy.", "PriceManager is not ready yet."), true);
                return;
            }

            float oldPrice = GetDesktopSellingPrice(pm, productId);
            float newPrice = Mathf.Round(advice.SuggestedPrice * 100f) / 100f;
            if (newPrice <= 0.0001f)
            {
                SetDesktopStatus(Plugin.T("Nieprawidłowa sugerowana cena.", "Invalid suggested price."), true);
                return;
            }

            try
            {
                Pricing pricing = null;
                try { pricing = pm.GetPriceSetByPlayer(productId); } catch { pricing = null; }

                if (pricing == null)
                {
                    // Sprawdzona ścieżka PriceManager: brakujący wpis można zasiać Pricing(id, price).
                    pricing = new Pricing(productId, newPrice);
                    if (pm.m_PricesSetByPlayer != null)
                        pm.m_PricesSetByPlayer.Add(pricing);
                }

                if (pricing == null)
                {
                    SetDesktopStatus(Plugin.T("Nie udało się utworzyć wpisu ceny.", "Could not create price entry."), true);
                    return;
                }

                pricing.Price = newPrice;
                int day = GetCurrentDaySafe();
                if (day > 0) pricing.LastChangeDate = day;

                RefreshDesktopPriceVisuals(productId);

                // Nie dopisujemy tej operacji do „Zmian cen dzisiaj”. Ta sekcja v6
                // pokazuje wyłącznie dzienne zmiany kosztu zakupu (PreviousCost -> CurrentCost).

                string name = TrimDesktopProductName(GetProductNameSafe(productId), 22);
                SetDesktopStatus(name + ": " + Plugin.Money(oldPrice) + " → " + Plugin.Money(newPrice), false);

                _desktopInsightsNextAdviceRefresh = 0f;
                RefreshDesktopInsights(true);
            }
            catch (Exception ex)
            {
                SetDesktopStatus(Plugin.T("Nie udało się zmienić ceny.", "Could not change price."), true);
                Plugin.DebugWarning("[DesktopInsights] Apply suggested price failed for " + productId + ": " + ex.Message);
            }
        }

        private void RefreshDesktopPriceVisuals(int productId)
        {
            try
            {
                var slots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
                if (slots != null)
                {
                    for (int i = 0; i < slots.Length; i++)
                    {
                        DisplaySlot slot = slots[i];
                        if (slot == null) continue;

                        try
                        {
                            if (!slot.HasProduct) continue;
                            ProductSO so = slot.PeekProductSO();
                            if (so == null || so.ID != productId) continue;
                            try { slot.PricingChanged(productId); } catch { }
                            slot.SetPriceTag();
                        }
                        catch { }
                    }
                }
            }
            catch { }

            try
            {
                var vendingSlots = UnityEngine.Object.FindObjectsOfType<VendingSlot>();
                if (vendingSlots != null)
                {
                    for (int i = 0; i < vendingSlots.Length; i++)
                    {
                        VendingSlot slot = vendingSlots[i];
                        if (slot == null) continue;
                        try
                        {
                            if (slot.ProductID != productId) continue;
                            slot.SetPriceTag();
                        }
                        catch { }
                    }
                }
            }
            catch { }

            try
            {
                if (PricingProductViewer.HasInstance && PricingProductViewer.Instance != null)
                    PricingProductViewer.Instance.RefreshUnlockedProducts(productId);
            }
            catch { }
        }

        private void SetDesktopStatus(string message, bool warning)
        {
            if (_desktopInsightsStatus == null) return;
            string color = warning ? StatsAppTheme.WarningHex : StatsAppTheme.PositiveHex;
            _desktopInsightsStatus.text = "<color=" + color + "><b>" + message + "</b></color>";
        }

        private static void ConfigureDesktopSectionHeader(TextMeshProUGUI header)
        {
            if (header == null) return;
            RectTransform rt = header.GetComponent<RectTransform>();
            if (rt == null) return;

            // Stała wysokość nagłówka zamiast anchorów zależnych od wysokości sekcji.
            // Dzięki temu nagłówek pozostaje zawsze przy górnej krawędzi i nie wpada pod wiersze.
            rt.anchorMin = new Vector2(0.03f, 1f);
            rt.anchorMax = new Vector2(0.97f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, 26f);
            rt.anchoredPosition = new Vector2(0f, -3f);
            header.alignment = TextAlignmentOptions.MidlineLeft;
            header.enableWordWrapping = false;
        }

        private void LayoutDesktopInsightsContent()
        {
            if (_desktopInsightsContent == null ||
                _desktopSummarySurfaceRT == null ||
                _desktopAlertsSurfaceRT == null ||
                _desktopStockExpirySurfaceRT == null ||
                _desktopChangesSurfaceRT == null ||
                _desktopAdviceSurfaceRT == null)
                return;

            ConfigureDesktopSectionHeader(_desktopInsightsAlerts);
            ConfigureDesktopSectionHeader(_desktopInsightsStockExpiry);
            ConfigureDesktopSectionHeader(_desktopInsightsChanges);
            ConfigureDesktopSectionHeader(_desktopInsightsAdvice);
            if (_desktopInsightsAlerts != null) _desktopInsightsAlerts.transform.SetAsLastSibling();
            if (_desktopInsightsStockExpiry != null) _desktopInsightsStockExpiry.transform.SetAsLastSibling();
            if (_desktopInsightsChanges != null) _desktopInsightsChanges.transform.SetAsLastSibling();
            if (_desktopInsightsAdvice != null) _desktopInsightsAdvice.transform.SetAsLastSibling();

            float top = 0f;

            SetDesktopTopRect(_desktopSummarySurfaceRT, top, DESKTOP_SUMMARY_HEIGHT);
            top += DESKTOP_SUMMARY_HEIGHT + DESKTOP_CONTENT_GAP;

            int alertRows = _desktopVisibleAlertCount > 0 ? _desktopVisibleAlertCount : 1;
            float alertsHeight = 34f + alertRows * DESKTOP_ALERT_ROW_HEIGHT;
            alertsHeight = Mathf.Max(66f, alertsHeight);
            SetDesktopTopRect(_desktopAlertsSurfaceRT, top, alertsHeight);
            top += alertsHeight + DESKTOP_CONTENT_GAP;

            int stockExpiryRows = _desktopVisibleStockExpiryCount > 0 ? _desktopVisibleStockExpiryCount : 1;
            float stockExpiryHeight = 32f + stockExpiryRows * DESKTOP_STOCK_EXPIRY_ROW_HEIGHT;
            stockExpiryHeight = Mathf.Max(62f, stockExpiryHeight);
            SetDesktopTopRect(_desktopStockExpirySurfaceRT, top, stockExpiryHeight);
            top += stockExpiryHeight + DESKTOP_CONTENT_GAP;

            int changeLines = _desktopVisibleChangeCount > 0 ? _desktopVisibleChangeCount : 1;
            float changesHeight = 34f + changeLines * DESKTOP_CHANGE_LINE_HEIGHT;
            if (_desktopPriceChanges.Count > DESKTOP_MAX_CHANGES)
                changesHeight += DESKTOP_CHANGE_LINE_HEIGHT;
            changesHeight = Mathf.Max(60f, changesHeight);

            SetDesktopTopRect(_desktopChangesSurfaceRT, top, changesHeight);
            top += changesHeight + DESKTOP_CONTENT_GAP;

            int adviceRows = Mathf.Max(1, _desktopVisibleAdviceCount);
            float adviceHeight = 38f + adviceRows * DESKTOP_ADVICE_ROW_HEIGHT;
            adviceHeight = Mathf.Max(82f, adviceHeight);

            SetDesktopTopRect(_desktopAdviceSurfaceRT, top, adviceHeight);
            top += adviceHeight + 8f;

            _desktopInsightsContent.sizeDelta = new Vector2(0f, top);

            if (_desktopInsightsScrollbar != null)
                _desktopInsightsScrollbar.gameObject.SetActive(top > GetDesktopViewportHeight() + 2f);
        }

        private float GetDesktopViewportHeight()
        {
            if (_desktopInsightsViewport == null) return 0f;
            try { return _desktopInsightsViewport.rect.height; }
            catch { return 0f; }
        }

        private static void SetDesktopTopRect(RectTransform rt, float top, float height)
        {
            if (rt == null) return;
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.anchoredPosition = new Vector2(0f, -top);
            rt.sizeDelta = new Vector2(0f, height);
        }

        private float GetDesktopSuggestedPrice(
            PriceManager pm,
            int productId,
            float cost,
            float currentPrice,
            ProductBusinessAnalysisRow row)
        {
            // No analysis => do not invent a permanent price correction.
            if (row == null || currentPrice <= 0.0001f)
                return currentPrice;

            // Falling demand is represented as a temporary promotion. Keep and
            // RestockFirst also leave the permanent selling price alone.
            if (row.PricingAdvice == PricingAdviceType.LowerSlightly ||
                row.PricingAdvice == PricingAdviceType.RestockFirst ||
                row.PricingAdvice == PricingAdviceType.Keep)
            {
                return currentPrice;
            }

            float suggested = row.SuggestedPrice > 0.0001f
                ? row.SuggestedPrice
                : currentPrice;

            Pricing pricing = null;
            try { pricing = pm.GetPrice(productId); } catch { }
            if (pricing == null)
            {
                try { pricing = pm.GetPriceSetByPlayer(productId); } catch { }
            }

            float marketPrice = row.MarketPrice;
            if (marketPrice <= 0.0001f && pricing != null)
            {
                try { marketPrice = pricing.MarketPrice; } catch { }
            }

            ProductSO so = null;
            try
            {
                if (Plugin.ProductCache != null)
                    Plugin.ProductCache.TryGetSO(productId, out so);
            }
            catch { so = null; }

            if (marketPrice <= 0.0001f &&
                so != null &&
                so.OptimumProfitRate > 0f)
            {
                marketPrice = cost * (1f + so.OptimumProfitRate / 100f);
            }

            // Permanent recommendations may not be below MarketPrice.
            float minimum = Mathf.Max(
                cost * 1.05f,
                marketPrice > 0.0001f ? marketPrice : 0f);

            float maximum = 0f;
            if (so != null && so.MaxProfitRate > 0f)
                maximum = cost * (1f + so.MaxProfitRate / 100f);

            if (maximum <= minimum)
                maximum = Mathf.Max(minimum, currentPrice * 1.20f);

            suggested = Mathf.Clamp(suggested, minimum, maximum);

            // A RaiseSlightly recommendation must never turn into a decrease.
            if (row.PricingAdvice == PricingAdviceType.RaiseSlightly)
                suggested = Mathf.Max(suggested, currentPrice);

            return Mathf.Round(suggested * 100f) / 100f;
        }

        private int GetDesktopPromotionSuggestionRate(
            PriceManager pm,
            int productId,
            float cost,
            float currentPrice,
            ProductBusinessAnalysisRow row)
        {
            if (pm == null ||
                row == null ||
                currentPrice <= 0.0001f ||
                cost <= 0.0001f)
            {
                return 0;
            }

            // Never promote something that is currently hard to keep in stock.
            if (row.PricingAdvice == PricingAdviceType.RestockFirst ||
                row.MissRate >= 0.10f ||
                row.DaysOfCover < 2.5f)
            {
                return 0;
            }

            if (GetDesktopDiscountRate(pm, productId) > 0)
                return 0;

            float evidence = Mathf.Max(
                row.RequestedVisible,
                row.SoldVisible);

            if (row.DaysInRange < 2 ||
                evidence < 5f ||
                row.PricingConfidence < 0.15f)
            {
                return 0;
            }

            bool decliningOverstock =
                row.DemandTrend <= -0.15f &&
                row.DaysOfCover > 5f;

            bool largeOverstock =
                row.DaysOfCover >= 8f &&
                row.DemandTrend < 0.05f;

            bool expiryPressure =
                row.ExpirationRiskUnits > 0 &&
                row.DaysOfCover >= 3f;

            if (!decliningOverstock &&
                !largeOverstock &&
                !expiryPressure)
            {
                return 0;
            }

            // If the base price is already below market, do not discount it even
            // further unless there is a real expiration/overstock pressure.
            if (row.MarketPrice > 0.0001f &&
                currentPrice < row.MarketPrice * 0.98f &&
                !expiryPressure)
            {
                return 0;
            }

            int rate = 10;

            if (decliningOverstock ||
                row.DaysOfCover >= 10f)
            {
                rate = 15;
            }

            if (row.DemandTrend <= -0.35f ||
                row.DaysOfCover >= 14f ||
                row.ExpirationRiskUnits >= 3)
            {
                rate = 20;
            }

            if ((row.DemandTrend <= -0.55f &&
                 row.DaysOfCover >= 10f) ||
                row.DaysOfCover >= 20f)
            {
                rate = 25;
            }

            // Safety floor: even a temporary promo must retain >= 5% gross margin.
            float safePrice = cost * 1.05f;
            if (safePrice >= currentPrice)
                return 0;

            float maxPercent =
                (1f - safePrice / currentPrice) * 100f;

            int safeRate =
                Mathf.FloorToInt(maxPercent / 5f) * 5;

            rate = Mathf.Min(rate, safeRate);
            if (rate < 5)
                return 0;

            return Mathf.Clamp(rate, 5, 25);
        }

        private static int GetDesktopDiscountRate(
            PriceManager pm,
            int productId)
        {
            if (pm == null || productId <= 0)
                return 0;

            try
            {
                Pricing pricing =
                    pm.GetPriceSetByPlayer(productId);

                if (pricing != null)
                    return Mathf.Max(0, pricing.DiscountRate);
            }
            catch { }

            try
            {
                Pricing pricing =
                    pm.GetPrice(productId);

                if (pricing != null)
                    return Mathf.Max(0, pricing.DiscountRate);
            }
            catch { }

            return 0;
        }

        private void ApplyDesktopSuggestedPromotion(int productId)
        {
            DesktopPriceAdvice advice = null;

            for (int i = 0; i < _desktopAdviceCache.Count; i++)
            {
                DesktopPriceAdvice candidate = _desktopAdviceCache[i];
                if (candidate != null &&
                    candidate.ProductId == productId &&
                    candidate.IsPromotion)
                {
                    advice = candidate;
                    break;
                }
            }

            if (advice == null || advice.PromotionRate <= 0)
            {
                SetDesktopStatus(
                    Plugin.T(
                        "Sugestia promocji jest już nieaktualna — odświeżam.",
                        "Promotion suggestion is stale — refreshing."),
                    true);

                RefreshDesktopInsights(true);
                return;
            }

            string sceneName = string.Empty;
            try { sceneName = SceneManager.GetActiveScene().name; } catch { }

            if (!string.Equals(
                    sceneName,
                    "Main Scene",
                    StringComparison.OrdinalIgnoreCase))
            {
                SetDesktopStatus(
                    Plugin.T(
                        "Promocję z panelu można ustawić tylko w single-player.",
                        "Panel promotions can only be applied in single-player."),
                    true);
                return;
            }

            PriceManager pm = GetDesktopPriceManager();
            if (pm == null)
            {
                SetDesktopStatus(
                    Plugin.T(
                        "PriceManager nie jest jeszcze gotowy.",
                        "PriceManager is not ready yet."),
                    true);
                return;
            }

            int rate = Mathf.Clamp(advice.PromotionRate, 5, 25);
            float currentPrice = GetDesktopSellingPrice(pm, productId);
            float cost = GetDesktopCurrentCost(pm, productId);

            if (currentPrice <= 0.0001f || cost <= 0.0001f)
            {
                SetDesktopStatus(
                    Plugin.T(
                        "Brak poprawnych danych ceny dla promocji.",
                        "Invalid pricing data for promotion."),
                    true);
                return;
            }

            float estimatedPromo =
                Mathf.Round(currentPrice * (1f - rate / 100f) * 100f) / 100f;

            if (estimatedPromo < cost * 1.05f - 0.005f)
            {
                SetDesktopStatus(
                    Plugin.T(
                        "Promocja zeszłaby zbyt blisko kosztu zakupu.",
                        "Promotion would go too close to wholesale cost."),
                    true);
                return;
            }

            try
            {
                Pricing pricing = null;
                try { pricing = pm.GetPriceSetByPlayer(productId); } catch { }

                if (pricing == null)
                {
                    // Same shipped PriceManager path already used by USTAW.
                    pricing = new Pricing(productId, currentPrice);
                    if (pm.m_PricesSetByPlayer != null)
                        pm.m_PricesSetByPlayer.Add(pricing);
                }

                if (pricing == null)
                {
                    SetDesktopStatus(
                        Plugin.T(
                            "Nie udało się utworzyć wpisu promocji.",
                            "Could not create promotion pricing record."),
                        true);
                    return;
                }

                pricing.DiscountRate = rate;

                int day = GetCurrentDaySafe();
                if (day > 0)
                    pricing.LastChangeDate = day;

                AddDesktopDiscountedProductFlag(productId);
                RememberDesktopAppliedPromotion(productId, day, rate);

                RefreshDesktopPriceVisuals(productId);

                float actualPromo = estimatedPromo;
                try
                {
                    float gamePromo = pm.DiscountedPrice(productId);
                    if (gamePromo > 0.0001f &&
                        gamePromo < currentPrice)
                    {
                        actualPromo = gamePromo;
                    }
                }
                catch { }

                string name =
                    TrimDesktopProductName(
                        GetProductNameSafe(productId),
                        20);

                SetDesktopStatus(
                    name + ": " +
                    Plugin.T("PROMO", "PROMO") + " -" +
                    rate + "%  " +
                    Plugin.Money(currentPrice) + " → " +
                    Plugin.Money(actualPromo),
                    false);

                _desktopInsightsNextAdviceRefresh = 0f;
                RefreshDesktopInsights(true);
            }
            catch (Exception ex)
            {
                SetDesktopStatus(
                    Plugin.T(
                        "Nie udało się ustawić promocji.",
                        "Could not apply promotion."),
                    true);

                Plugin.DebugWarning(
                    "[DesktopInsights] Apply promotion failed for " +
                    productId + ": " + ex.Message);
            }
        }

        private static void AddDesktopDiscountedProductFlag(int productId)
        {
            if (productId <= 0)
                return;

            try
            {
                ProductLicenseManager manager =
                    ProductLicenseManager.HasInstance
                        ? ProductLicenseManager.Instance
                        : null;

                if (manager == null ||
                    manager.m_DiscountedProducts == null)
                {
                    return;
                }

                if (!manager.m_DiscountedProducts.Contains(productId))
                    manager.m_DiscountedProducts.Add(productId);
            }
            catch { }
        }

        private static void RemoveDesktopDiscountedProductFlag(int productId)
        {
            if (productId <= 0)
                return;

            try
            {
                ProductLicenseManager manager =
                    ProductLicenseManager.HasInstance
                        ? ProductLicenseManager.Instance
                        : null;

                if (manager == null ||
                    manager.m_DiscountedProducts == null)
                {
                    return;
                }

                while (manager.m_DiscountedProducts.Contains(productId))
                    manager.m_DiscountedProducts.Remove(productId);
            }
            catch { }
        }

        private static string GetDesktopPromoStateFilePath()
        {
            return Path.Combine(
                Application.persistentDataPath,
                GetDesktopSlotSafe(),
                DESKTOP_PROMO_STATE_FILE);
        }

        private void EnsureDesktopPromoStateLoaded()
        {
            string slot = GetDesktopSlotSafe();

            if (_desktopPromoStateLoaded &&
                string.Equals(
                    _desktopPromoStateSlot,
                    slot,
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            _desktopAppliedPromotions.Clear();
            _desktopPromoStateLoaded = true;
            _desktopPromoStateSlot = slot;
            _desktopPromoCleanupDay = -1;

            try
            {
                string path = GetDesktopPromoStateFilePath();
                if (!File.Exists(path))
                    return;

                string[] lines = File.ReadAllLines(path);

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line) ||
                        line[0] == '#')
                    {
                        continue;
                    }

                    string[] parts = line.Split('\t');
                    if (parts.Length < 4 || parts[0] != "PROMO")
                        continue;

                    if (!int.TryParse(
                            parts[1],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int productId) ||
                        !int.TryParse(
                            parts[2],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int day) ||
                        !int.TryParse(
                            parts[3],
                            NumberStyles.Integer,
                            CultureInfo.InvariantCulture,
                            out int rate))
                    {
                        continue;
                    }

                    if (productId <= 0 || day <= 0 || rate <= 0)
                        continue;

                    _desktopAppliedPromotions[productId] =
                        new DesktopAppliedPromotion
                        {
                            ProductId = productId,
                            AppliedDay = day,
                            DiscountRate = rate
                        };
                }
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning(
                    "[DesktopInsights] Promo state load failed: " +
                    ex.Message);
            }
        }

        private void SaveDesktopPromoState()
        {
            try
            {
                string path = GetDesktopPromoStateFilePath();
                string dir = Path.GetDirectoryName(path);

                if (!string.IsNullOrEmpty(dir))
                    Directory.CreateDirectory(dir);

                var sb = new StringBuilder(512);
                sb.AppendLine("# StatisticMod Desktop Promotions v1");

                var ids = new List<int>(_desktopAppliedPromotions.Keys);
                ids.Sort();

                for (int i = 0; i < ids.Count; i++)
                {
                    DesktopAppliedPromotion promo =
                        _desktopAppliedPromotions[ids[i]];

                    if (promo == null ||
                        promo.ProductId <= 0 ||
                        promo.AppliedDay <= 0 ||
                        promo.DiscountRate <= 0)
                    {
                        continue;
                    }

                    sb.Append("PROMO\t")
                        .Append(promo.ProductId.ToString(CultureInfo.InvariantCulture))
                        .Append('\t')
                        .Append(promo.AppliedDay.ToString(CultureInfo.InvariantCulture))
                        .Append('\t')
                        .Append(promo.DiscountRate.ToString(CultureInfo.InvariantCulture))
                        .AppendLine();
                }

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);

                if (File.Exists(path))
                    File.Delete(path);

                File.Move(tmp, path);
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning(
                    "[DesktopInsights] Promo state save failed: " +
                    ex.Message);
            }
        }

        private void RememberDesktopAppliedPromotion(
            int productId,
            int day,
            int rate)
        {
            if (productId <= 0 || day <= 0 || rate <= 0)
                return;

            EnsureDesktopPromoStateLoaded();

            _desktopAppliedPromotions[productId] =
                new DesktopAppliedPromotion
                {
                    ProductId = productId,
                    AppliedDay = day,
                    DiscountRate = rate
                };

            SaveDesktopPromoState();
        }

        private void ClearExpiredDesktopPromotionsForDay(int currentDay)
        {
            if (currentDay <= 0)
                return;

            EnsureDesktopPromoStateLoaded();

            if (_desktopAppliedPromotions.Count == 0)
            {
                _desktopPromoCleanupDay = currentDay;
                return;
            }

            if (_desktopPromoCleanupDay == currentDay)
                return;

            PriceManager pm = GetDesktopPriceManager();
            if (pm == null)
                return;

            var expiredIds = new List<int>();

            foreach (KeyValuePair<int, DesktopAppliedPromotion> pair
                     in _desktopAppliedPromotions)
            {
                DesktopAppliedPromotion promo = pair.Value;
                if (promo == null || promo.AppliedDay == currentDay)
                    continue;

                bool removeDiscountFlag = false;

                try
                {
                    Pricing pricing =
                        pm.GetPriceSetByPlayer(promo.ProductId);

                    if (pricing != null)
                    {
                        // If the player changed -15% to e.g. -20%, preserve it.
                        // If our rate is still there, expire it. If the game already
                        // reset it to 0 at day transition, only remove the stale flag.
                        if (pricing.DiscountRate == promo.DiscountRate)
                        {
                            pricing.DiscountRate = 0;
                            removeDiscountFlag = true;
                        }
                        else if (pricing.DiscountRate <= 0)
                        {
                            removeDiscountFlag = true;
                        }
                    }
                }
                catch { }

                if (removeDiscountFlag)
                {
                    RemoveDesktopDiscountedProductFlag(promo.ProductId);
                    RefreshDesktopPriceVisuals(promo.ProductId);
                }

                // Whether cleared or manually overridden, it is no longer OUR
                // current-day promo and must leave the tracking file.
                expiredIds.Add(promo.ProductId);
            }

            for (int i = 0; i < expiredIds.Count; i++)
                _desktopAppliedPromotions.Remove(expiredIds[i]);

            if (expiredIds.Count > 0)
            {
                SaveDesktopPromoState();

                if (Plugin.Log != null)
                {
                    Plugin.Log.LogInfo(
                        "[DesktopInsights] wygaszono/zwolniono promocje z poprzedniego dnia: " +
                        expiredIds.Count);
                }
            }

            _desktopPromoCleanupDay = currentDay;
        }

        private static PriceManager GetDesktopPriceManager()
        {
            try
            {
                PriceManager pm = PriceManager.Instance;
                if (pm != null) return pm;
            }
            catch { }

            try { return UnityEngine.Object.FindFirstObjectByType<PriceManager>(); }
            catch { return null; }
        }

        private static float GetDesktopSellingPrice(PriceManager pm, int productId)
        {
            if (pm == null) return 0f;
            try
            {
                float value = pm.SellingPrice(productId);
                if (value > 0.0001f) return value;
            }
            catch { }

            try
            {
                Pricing pricing = pm.GetPriceSetByPlayer(productId);
                if (pricing != null && pricing.Price > 0.0001f) return pricing.Price;
            }
            catch { }

            try
            {
                Pricing pricing = pm.GetPrice(productId);
                if (pricing != null)
                {
                    if (pricing.Price > 0.0001f) return pricing.Price;
                    float value = pricing.SellingPrice;
                    if (value > 0.0001f) return value;
                }
            }
            catch { }

            return 0f;
        }

        private static float GetDesktopPreviousCost(PriceManager pm, int productId)
        {
            if (pm == null) return 0f;

            try
            {
                Pricing pricing = pm.GetPrice(productId);
                if (pricing != null)
                {
                    float value = pricing.PreviousCost;
                    if (value > 0.0001f) return value;
                }
            }
            catch { }

            try
            {
                Pricing pricing = pm.GetPriceSetByPlayer(productId);
                if (pricing != null)
                {
                    float value = pricing.PreviousCost;
                    if (value > 0.0001f) return value;
                }
            }
            catch { }

            return 0f;
        }

        private static float GetDesktopCurrentCost(PriceManager pm, int productId)
        {
            if (pm == null) return 0f;
            try
            {
                float value = pm.CurrentCost(productId);
                if (value > 0.0001f) return value;
            }
            catch { }

            try
            {
                Pricing pricing = pm.GetPrice(productId);
                if (pricing != null)
                {
                    float value = pricing.CurrentCost;
                    if (value > 0.0001f) return value;
                    value = pricing.AvgCost;
                    if (value > 0.0001f) return value;
                }
            }
            catch { }

            return 0f;
        }

        private static string GetDesktopSlotSafe()
        {
            try
            {
                string slot = StatsStore.CurrentSlot;
                return string.IsNullOrWhiteSpace(slot) ? "slot_0" : slot.Trim();
            }
            catch { return "slot_0"; }
        }

        private static string GetDesktopStateFilePath()
        {
            return Path.Combine(Application.persistentDataPath, GetDesktopSlotSafe(), DESKTOP_STATE_FILE);
        }

        private void LoadDesktopInsightsState(int day)
        {
            try
            {
                string path = GetDesktopStateFilePath();
                if (!File.Exists(path)) return;

                string[] lines = File.ReadAllLines(path);
                int storedDay = -1;
                bool v6Snapshot = false;

                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i];
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith(DESKTOP_STATE_HEADER, StringComparison.Ordinal))
                        v6Snapshot = true;
                    if (line[0] == '#') continue;

                    string[] parts = line.Split('\t');
                    if (parts.Length >= 2 && parts[0] == "DAY")
                    {
                        int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out storedDay);
                    }
                    else if (v6Snapshot && parts.Length >= 3 && parts[0] == "OBSERVED")
                    {
                        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int productId)) continue;
                        if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float price)) continue;
                        if (productId > 0 && productId != 9999 && price > 0.0001f)
                            _desktopLastObservedPrices[productId] = price;
                    }
                }

                // Zmiany są dzienne. Snapshot cen jest ponad dniami i służy do dokładnego
                // wykrycia zmiany po restarcie albo przy rozpoczęciu nowego dnia.
                if (v6Snapshot && storedDay == day)
                {
                    for (int i = 0; i < lines.Length; i++)
                    {
                        string line = lines[i];
                        if (string.IsNullOrWhiteSpace(line) || line[0] == '#') continue;
                        string[] parts = line.Split('\t');
                        if (parts.Length < 6 || parts[0] != "CHANGE") continue;

                        if (!int.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int productId)) continue;
                        if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out float oldPrice)) continue;
                        if (!float.TryParse(parts[3], NumberStyles.Float, CultureInfo.InvariantCulture, out float newPrice)) continue;
                        if (!int.TryParse(parts[4], NumberStyles.Integer, CultureInfo.InvariantCulture, out int sequence)) sequence = 0;
                        bool oldKnown = parts[5] == "1";

                        if (!oldKnown) continue;

                        _desktopPriceChanges[productId] = new DesktopPriceChange
                        {
                            ProductId = productId,
                            OldPrice = oldPrice,
                            NewPrice = newPrice,
                            Sequence = sequence,
                            OldPriceKnown = true
                        };
                        if (sequence > _desktopPriceChangeSequence) _desktopPriceChangeSequence = sequence;
                    }
                }

                Plugin.DebugLog("[DesktopInsights] v6 state: day=" + storedDay +
                    ", changes=" + _desktopPriceChanges.Count +
                    ", observed=" + _desktopLastObservedPrices.Count);
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning("[DesktopInsights] Load state failed: " + ex.Message);
            }
        }

        private void SaveDesktopInsightsState()
        {
            try
            {
                string path = GetDesktopStateFilePath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                var sb = new StringBuilder(8192);
                sb.AppendLine(DESKTOP_STATE_HEADER);
                sb.Append("DAY\t").Append(_desktopInsightsDay.ToString(CultureInfo.InvariantCulture)).AppendLine();

                var observedIds = new List<int>(_desktopLastObservedPrices.Keys);
                observedIds.Sort();
                for (int i = 0; i < observedIds.Count; i++)
                {
                    int productId = observedIds[i];
                    float price = _desktopLastObservedPrices[productId];
                    if (productId <= 0 || productId == 9999 || price <= 0.0001f) continue;
                    sb.Append("OBSERVED\t")
                        .Append(productId.ToString(CultureInfo.InvariantCulture)).Append('\t')
                        .Append(price.ToString("R", CultureInfo.InvariantCulture)).AppendLine();
                }

                var changes = new List<DesktopPriceChange>(_desktopPriceChanges.Values);
                changes.Sort((a, b) => a.Sequence.CompareTo(b.Sequence));
                for (int i = 0; i < changes.Count; i++)
                {
                    DesktopPriceChange c = changes[i];
                    if (c == null || !c.OldPriceKnown) continue;
                    sb.Append("CHANGE\t")
                        .Append(c.ProductId.ToString(CultureInfo.InvariantCulture)).Append('\t')
                        .Append(c.OldPrice.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(c.NewPrice.ToString("R", CultureInfo.InvariantCulture)).Append('\t')
                        .Append(c.Sequence.ToString(CultureInfo.InvariantCulture)).Append('\t')
                        .Append("1").AppendLine();
                }

                string tmp = path + ".tmp";
                File.WriteAllText(tmp, sb.ToString(), Encoding.UTF8);
                File.Move(tmp, path, true);
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning("[DesktopInsights] Save state failed: " + ex.Message);
            }
        }

        private void EnsureDesktopProductCache()
        {
            if (Plugin.ProductCache == null) return;
            if (Plugin.ProductCache.Count > 0) return;

            try
            {
                var idm = UnityEngine.Object.FindFirstObjectByType<global::IDManager>();
                if (idm != null) Plugin.ProductCache.Build(idm);
            }
            catch { }
        }

        private static string GetDesktopMarginColor(float margin)
        {
            if (margin < 5f) return StatsAppTheme.NegativeHex;
            if (margin < 15f) return StatsAppTheme.WarningHex;
            return StatsAppTheme.PositiveHex;
        }

        private static string TrimDesktopProductName(string value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return "-";
            value = value.Trim();
            if (value.Length <= maxLength) return value;
            return value.Substring(0, Mathf.Max(1, maxLength - 1)).TrimEnd() + "…";
        }

        private GameObject CreateDesktopInsightsSurface(
            Transform parent,
            string name,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color color)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(StatsAppTheme.Border.r, StatsAppTheme.Border.g, StatsAppTheme.Border.b, 0.75f);
            outline.effectDistance = new Vector2(1f, -1f);
            return go;
        }

        private TextMeshProUGUI CreateDesktopInsightsInsetText(
            Transform parent,
            string name,
            float fontSize,
            TextAlignmentOptions alignment,
            Color color,
            float left,
            float top,
            float right,
            float bottom)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(left, bottom);
            rt.offsetMax = new Vector2(-right, -top);

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = string.Empty;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = color;
            tmp.richText = true;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;
            if (_gameFont != null) tmp.font = _gameFont;
            return tmp;
        }

        private TextMeshProUGUI CreateDesktopInsightsText(
            Transform parent,
            string name,
            string value,
            Vector2 anchorMin,
            Vector2 anchorMax,
            float fontSize,
            TextAlignmentOptions alignment,
            Color color,
            bool bold)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.text = bold ? "<b>" + value + "</b>" : value;
            tmp.fontSize = fontSize;
            tmp.alignment = alignment;
            tmp.color = color;
            tmp.richText = true;
            tmp.enableWordWrapping = true;
            tmp.raycastTarget = false;
            if (_gameFont != null) tmp.font = _gameFont;
            return tmp;
        }

        private Button CreateDesktopProductLink(
            Transform parent,
            string name,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            int productId,
            float fontSize = 8.4f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.001f);
            image.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.88f, 0.96f, 1f, 1f);
            colors.pressedColor = new Color(0.75f, 0.90f, 1f, 1f);
            button.colors = colors;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener((UnityAction)(() => OpenNativePricesProduct(productId)));

            TextMeshProUGUI text = CreateDesktopInsightsText(
                go.transform,
                "Label",
                "<u><b>" + label + "</b></u>",
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                fontSize,
                TextAlignmentOptions.MidlineLeft,
                StatsAppTheme.Info,
                false);
            text.raycastTarget = false;
            return button;
        }

        private Button CreateDesktopMarketProductLink(
            Transform parent,
            string name,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            int productId,
            float fontSize = 8.4f)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = new Color(1f, 1f, 1f, 0.001f);
            image.raycastTarget = true;

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(0.88f, 0.96f, 1f, 1f);
            colors.pressedColor = new Color(0.75f, 0.90f, 1f, 1f);
            button.colors = colors;
            button.onClick.RemoveAllListeners();
            button.onClick.AddListener((UnityAction)(() => OpenNativeMarketProduct(productId)));

            TextMeshProUGUI text = CreateDesktopInsightsText(
                go.transform,
                "Label",
                "<u><b>" + label + "</b></u>",
                new Vector2(0f, 0f),
                new Vector2(1f, 1f),
                fontSize,
                TextAlignmentOptions.MidlineLeft,
                StatsAppTheme.Info,
                false);
            text.raycastTarget = false;
            return button;
        }

        private Button CreateDesktopInsightsButton(
            Transform parent,
            string name,
            string label,
            Vector2 anchorMin,
            Vector2 anchorMax,
            Color background,
            Action onClick)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            var image = go.AddComponent<Image>();
            image.color = background;
            image.raycastTarget = true;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = new Color(StatsAppTheme.Border.r, StatsAppTheme.Border.g, StatsAppTheme.Border.b, 0.85f);
            outline.effectDistance = new Vector2(1f, -1f);

            var button = go.AddComponent<Button>();
            button.targetGraphic = image;
            button.transition = Selectable.Transition.ColorTint;
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(1f, 1f, 1f, 0.88f);
            colors.pressedColor = new Color(0.78f, 0.78f, 0.78f, 1f);
            button.colors = colors;
            button.onClick.RemoveAllListeners();
            if (onClick != null)
                button.onClick.AddListener((UnityAction)(() => onClick()));

            CreateDesktopInsightsText(
                go.transform,
                "Label",
                label,
                new Vector2(0.04f, 0.06f),
                new Vector2(0.96f, 0.94f),
                8.0f,
                TextAlignmentOptions.Midline,
                StatsAppTheme.TextLight,
                true);

            return button;
        }
    }
}
