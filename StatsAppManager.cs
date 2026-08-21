using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using PG;
using System;
using System.ComponentModel;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using static UnityEngine.UI.ContentSizeFitter;
using SmartExpiration;

namespace StatisticMod
{
    public partial class StatsAppManager : FakeMonoBehaviour
    {
        private const string STATS_APP_NAME = "Statistics App";
        private const string STATS_SHORTCUT_NAME = "Statystyki.Exe";

        internal static StatsAppManager _instance;

        private Transform _computerRoot;
        private Transform _screen;
        private Transform _desktopCanvas;
        private Transform _appShortcuts;

        private GameObject _statsApp;
        private Button _statsShortcutButton;

        private bool _installedForThisComputer;
        private int _computerInstanceId;

        private RectTransform _tilesContent;
        private GameObject _tileTemplate;

        private TMP_FontAsset _gameFont;
        private Color _gameColor = Color.white;
        private bool _gameStyleCached;

        private int _selectedDay = -1;

        private TextMeshProUGUI _dayLabelTmp;
        private Button _prevDayBtn;
        private Button _nextDayBtn;
        private bool _shortcutPlaced;

        private readonly System.Collections.Generic.Dictionary<int, TextMeshProUGUI> _infoTmpByPid
        = new();

        private int _lastRefreshDay = -1;
        private float _nextUiRefresh;

        private float _refreshTimer = 0f;
        private const float REFRESH_RATE = 2.0f; // odświeżanie co 2 sekundy
        private enum StatsSortMode
        {
            Name = 0,
            ProductId = 1,
            SoldRevenue = 2,
            ThrownValue = 3,
            ThrownUnits = 4,
            SoldUnits = 5
        }
               

        private enum SimpleSortMode
        {
            Name,
            ProductId,
            NearestExpiry,
            PriceBuy,
            PriceSell,
            TotalStock,
            TotalValue
        }
                        
        private SimpleSortMode _simpleSort = SimpleSortMode.Name;
        private bool _sortAsc = false;
        
        private TextMeshProUGUI _sortLabelTmp;
        private TextMeshProUGUI _sortDirTmp;
        private Button _sortModeBtn;
        private Button _sortDirBtn;
        private bool _isOpen;
        private bool _buildQueued;

        private Button _titleModeBtn;
        private TextMeshProUGUI _titleTmp;

        private bool _onlyWithPrice = true; // Domyślnie pokazujemy tylko produkty z ceną
        private Button _filterAvailableBtn;
        private TextMeshProUGUI _filterAvailableLabel;        
        private enum HubMode { Stats, Expiration, Products, DailySummary, Analysis, Charts }
        private HubMode _hubMode = HubMode.Stats;

        private GameObject _daySelectorGO;
        private StatsSortMode _statsSortMode = StatsSortMode.SoldRevenue;
        public enum ProductSortMode { Name, ID, StockTotal, StockShop, StockWarehouse, PriceBuy, PriceSell }
        private ProductSortMode _currentSortMode = ProductSortMode.Name;

        private GameObject _searchBarGO;
        private TMPro.TMP_InputField _searchInputField;
        private string _searchFilter = "";

        private GridLayoutGroup _tilesGrid;
        private ScrollRect _tilesScroll;

        private bool _savedGridEnabled;
        private bool _savedScrollEnabled;
        private bool _savedScrollVertical;
        private bool _savedScrollHorizontal;

        private static readonly HashSet<int> WeightProductIDs = new()
        {
            165, 166, 167, 168, 169, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188
        };
        [HideFromIl2Cpp]
        public static void InstanceTryInstall()
        {
            if (_instance == null)
            {
                var go = new GameObject("StatisticMod.StatsAppManager");
                UnityEngine.Object.DontDestroyOnLoad(go);

                // Uruchamiamy to poza radarem Unity!
                _instance = new StatsAppManager();
                _instance.gameObject = go;
            }

            _instance.TryInstall();
        }

        // Zmiana nazwy i zabezpieczenie ręczne
        public void ManualUpdate()
        {
            if (!_isOpen || _hubMode != HubMode.Stats) return;

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= REFRESH_RATE)
            {
                _refreshTimer = 0f;
                RefreshStatsLive();
            }
        }

        private bool IsWeightProduct(int productId)
        {
            return WeightProductIDs.Contains(productId);
        }
                

        public void TryInstall()
        {
            // PERF: StatsRunner calls this every 5 seconds. Once the app is installed,
            // do not scan the whole scene for ComputerOperatingSystem again.
            if (_installedForThisComputer && _computerRoot != null && _statsApp != null && _statsShortcutButton != null)
                return;

            var os = UnityEngine.Object.FindObjectOfType<ComputerOperatingSystem>(true);
            if (os == null) return;

            int id = os.gameObject.GetInstanceID();
            if (_installedForThisComputer && id == _computerInstanceId && _statsApp != null && _statsShortcutButton != null)
                return;

            _computerInstanceId = id;
            _installedForThisComputer = true;

            _computerRoot = os.transform;

            _screen = FindByPath(_computerRoot, "Screen");
            _desktopCanvas = FindByPath(_computerRoot, "Screen/Desktop Canvas");
            _appShortcuts = FindByPath(_computerRoot, "Screen/Desktop Canvas/App Shortcuts");

            if (_screen == null) return;

            if (_appShortcuts != null)
                EnsureDesktopShortcut();

            EnsureStatsApp();
        }
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
        [HideFromIl2Cpp]
        public static void TickRealtimeUI()
        {
            _instance?.TickRealtimeUI_Impl();
        }

        // ⚡ POPRAWKA: Cache słownika zapobiegający "ścinom" co 0.5s
        private readonly System.Collections.Generic.Dictionary<int, ProductLine> _tempProductMap = new();

        private void TickRealtimeUI_Impl()
        {
            if (_statsApp == null || !_statsApp.activeInHierarchy) return;

            // ✅ NIE odświeżaj statystyk, gdy jesteś w Terminy/Produkty
            if (_hubMode != HubMode.Stats) return;

            if (Time.realtimeSinceStartup < _nextUiRefresh) return;
            _nextUiRefresh = Time.realtimeSinceStartup + 0.5f;

            EnsureSelectedDayInitialized();
            int current = GetCurrentDaySafe();
            if (_selectedDay != current) return;

            var ds = StatsStore.GetDay(_selectedDay);
            if (ds == null || ds.Products == null) return;

            // ⚡ POPRAWKA: Czyszczenie zbuforowanego słownika zamiast alokowania nowego
            _tempProductMap.Clear();
            for (int i = 0; i < ds.Products.Count; i++)
                if (ds.Products[i] != null) _tempProductMap[ds.Products[i].ProductId] = ds.Products[i];

            bool needRebuild = false;

            foreach (var kv in _infoTmpByPid)
            {
                var infoTmp = kv.Value;
                if (infoTmp == null) continue;

                _tempProductMap.TryGetValue(kv.Key, out var p);
                p ??= new ProductLine { ProductId = kv.Key };

                UpdateTileText(infoTmp, p);
            }

            for (int i = 0; i < ds.Products.Count; i++)
            {
                var p = ds.Products[i];
                if (p.SoldUnits == 0 && p.ThrownUnits == 0 && p.SoldWeightKg <= 0.0001f && p.ThrownWeightKg <= 0.0001f) continue;
                if (!_infoTmpByPid.ContainsKey(p.ProductId))
                {
                    needRebuild = true;
                    break;
                }
            }

            if (needRebuild) BuildForHubMode();
        }

        // Dodaj to pole prywatne na początku klasy (obok innych pól prywatnych, np. pod _gameFont)
        private PriceManager _priceManager;

        // Podmień metodę GetCurrentPrice na tę:
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

        private void UpdateTileText(TextMeshProUGUI tmp, ProductLine p)
        {
            bool useKg = IsWeightProduct(p.ProductId) || p.SoldWeightKg > 0.0001f;

            float soldVal = GetSoldVisibleValue(p);
            float thrownVal = GetThrownVisibleValue(p);
            float revenue = GetRevenueVisibleValue(p);
            float loss = GetLossVisibleValue(p);

            string unitSuffix = useKg ? "kg" : Plugin.T("szt.", "pcs");
            string soldValTxt = useKg ? $"{soldVal:0.000}" : $"{Mathf.RoundToInt(soldVal)}";
            string thrownValTxt = useKg ? $"{thrownVal:0.000}" : $"{Mathf.RoundToInt(thrownVal)}";

            string newText =
                $"{Plugin.T("Sprzedane", "Sold")}: <color={StatsAppTheme.InfoHex}><b>{soldValTxt} {unitSuffix}</b></color>\n" +
                $"{Plugin.T("Przychód", "Revenue")}: <color={StatsAppTheme.PositiveHex}><b>{revenue:0.00} $</b></color>\n" +
                $"{Plugin.T("Wyrzucone", "Wasted")}: <color={StatsAppTheme.WarningHex}><b>{thrownValTxt} {unitSuffix}</b></color>\n" +
                $"{Plugin.T("Strata", "Loss")}: <color={StatsAppTheme.NegativeHex}><b>{loss:0.00} $</b></color>";

            if (tmp.text != newText)
                tmp.text = newText;
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

        // -----------------------------
        // 1) App panel
        // -----------------------------
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

        private void BuildHeaderBranding(Transform headerRoot)
        {
            if (headerRoot == null) return;

            var brandGO = new GameObject("Brand");
            brandGO.transform.SetParent(headerRoot, false);

            var brandRT = brandGO.AddComponent<RectTransform>();
            brandRT.anchorMin = new Vector2(0.02f, 0.52f);
            brandRT.anchorMax = new Vector2(0.56f, 0.94f);
            brandRT.offsetMin = Vector2.zero;
            brandRT.offsetMax = Vector2.zero;

            var brand = brandGO.AddComponent<TextMeshProUGUI>();
            brand.text = "STATS & EXPIRY";
            brand.alignment = TextAlignmentOptions.MidlineLeft;
            brand.fontStyle = FontStyles.Bold;
            brand.fontSize = 18f;
            brand.enableAutoSizing = true;
            brand.fontSizeMin = 11f;
            brand.fontSizeMax = 18f;
            brand.enableWordWrapping = false;
            brand.overflowMode = TextOverflowModes.Ellipsis;
            brand.color = StatsAppTheme.TextLight;
            brand.raycastTarget = false;
            if (_gameFont != null) brand.font = _gameFont;

            var subtitleGO = new GameObject("BrandSubtitle");
            subtitleGO.transform.SetParent(headerRoot, false);

            var subtitleRT = subtitleGO.AddComponent<RectTransform>();
            subtitleRT.anchorMin = new Vector2(0.57f, 0.56f);
            subtitleRT.anchorMax = new Vector2(0.90f, 0.90f);
            subtitleRT.offsetMin = Vector2.zero;
            subtitleRT.offsetMax = Vector2.zero;

            var subtitle = subtitleGO.AddComponent<TextMeshProUGUI>();
            subtitle.text = Plugin.T("ZARZĄDZANIE SKLEPEM I DANYMI", "STORE DATA & MANAGEMENT");
            subtitle.alignment = TextAlignmentOptions.MidlineRight;
            subtitle.fontStyle = FontStyles.Normal;
            subtitle.fontSize = 9.5f;
            subtitle.enableAutoSizing = true;
            subtitle.fontSizeMin = 7f;
            subtitle.fontSizeMax = 9.5f;
            subtitle.enableWordWrapping = false;
            subtitle.overflowMode = TextOverflowModes.Ellipsis;
            subtitle.color = new Color(StatsAppTheme.TextLight.r, StatsAppTheme.TextLight.g, StatsAppTheme.TextLight.b, 0.62f);
            subtitle.raycastTarget = false;
            if (_gameFont != null) subtitle.font = _gameFont;

            var dividerGO = new GameObject("HeaderDivider");
            dividerGO.transform.SetParent(headerRoot, false);

            var dividerRT = dividerGO.AddComponent<RectTransform>();
            dividerRT.anchorMin = new Vector2(0.02f, 0.48f);
            dividerRT.anchorMax = new Vector2(0.985f, 0.48f);
            dividerRT.sizeDelta = new Vector2(0f, 1f);
            dividerRT.anchoredPosition = Vector2.zero;

            dividerGO.AddComponent<CanvasRenderer>();
            var divider = dividerGO.AddComponent<Image>();
            divider.color = new Color(StatsAppTheme.HeaderBorder.r, StatsAppTheme.HeaderBorder.g, StatsAppTheme.HeaderBorder.b, 0.55f);
            divider.raycastTarget = false;
        }

        private void BuildHeaderWithClose(Transform appRoot)
        {
            var header = new GameObject("Header");
            header.transform.SetParent(appRoot, false);

            var hrt = header.AddComponent<RectTransform>();
            hrt.anchorMin = new Vector2(StatsAppTheme.OuterLeft, StatsAppTheme.HeaderBottom);
            hrt.anchorMax = new Vector2(StatsAppTheme.OuterRight, StatsAppTheme.HeaderTop);
            hrt.offsetMin = Vector2.zero;
            hrt.offsetMax = Vector2.zero;

            header.AddComponent<CanvasRenderer>();
            var himg = header.AddComponent<Image>();
            himg.color = StatsAppTheme.Header;
            himg.raycastTarget = true;

            var headerOutline = header.AddComponent<Outline>();
            headerOutline.effectColor = StatsAppTheme.HeaderBorder;
            headerOutline.effectDistance = new Vector2(1f, -1f);

            CacheGameTmpStyle();
            BuildHeaderBranding(header.transform);

            // =========================
            // TITLE BUTTON (mode switch)
            // =========================
            var titleBtnGO = new GameObject("TitleButton");
            titleBtnGO.transform.SetParent(header.transform, false);
            var tbrt = titleBtnGO.AddComponent<RectTransform>();
            tbrt.anchorMin = new Vector2(0.02f, 0.08f);
            tbrt.anchorMax = new Vector2(0.20f, 0.43f);
            tbrt.offsetMin = Vector2.zero;
            tbrt.offsetMax = Vector2.zero;

            titleBtnGO.AddComponent<CanvasRenderer>();
            var tbImg = titleBtnGO.AddComponent<Image>();
            tbImg.color = StatsAppTheme.Button;
            tbImg.raycastTarget = true;

            _titleModeBtn = titleBtnGO.AddComponent<Button>();
            _titleModeBtn.onClick.AddListener((UnityAction)OnTitleModeClicked);

            var titleGO = new GameObject("Title");
            titleGO.transform.SetParent(titleBtnGO.transform, false);
            var trt = titleGO.AddComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            _titleTmp = titleGO.AddComponent<TextMeshProUGUI>();
            _titleTmp.text = Plugin.T("STATYSTYKI", "STATISTICS");
            _titleTmp.alignment = TextAlignmentOptions.Center;
            _titleTmp.fontSize = 13;
            _titleTmp.fontStyle = FontStyles.Bold;
            if (_gameFont != null) _titleTmp.font = _gameFont;
            _titleTmp.color = StatsAppTheme.TextLight;
            _titleTmp.enableAutoSizing = true;
            _titleTmp.fontSizeMin = 8f;
            _titleTmp.fontSizeMax = 13f;
            _titleTmp.enableWordWrapping = false;
            _titleTmp.overflowMode = TextOverflowModes.Ellipsis;
            _titleTmp.raycastTarget = false;
            AttachHeaderDropdownArrow(_titleModeBtn, "TitleDropdownArrow");

            // =========================
            // NEW: FILTER BUTTON (Between Title and Day Selector)
            // =========================
            var filterBtnGO = new GameObject("FilterButton");
            filterBtnGO.transform.SetParent(header.transform, false);

            var fbrt = filterBtnGO.AddComponent<RectTransform>();
            // Pozycjonujemy między 0.24 a 0.33 (tytuł kończy się na 0.22)
            fbrt.anchorMin = new Vector2(0.215f, 0.08f);
            fbrt.anchorMax = new Vector2(0.325f, 0.43f);
            fbrt.offsetMin = Vector2.zero;
            fbrt.offsetMax = Vector2.zero;

            filterBtnGO.AddComponent<CanvasRenderer>();
            var fbImg = filterBtnGO.AddComponent<Image>();
            fbImg.color = StatsAppTheme.Button;
            fbImg.raycastTarget = true;

            _filterAvailableBtn = filterBtnGO.AddComponent<Button>();
            _filterAvailableBtn.onClick.AddListener((UnityAction)OnTogglePriceFilter);

            var filterTextGO = new GameObject("Text");
            filterTextGO.transform.SetParent(filterBtnGO.transform, false);
            var ftrt = filterTextGO.AddComponent<RectTransform>();
            ftrt.anchorMin = Vector2.zero;
            ftrt.anchorMax = Vector2.one;
            ftrt.offsetMin = new Vector2(4f, 0f);
            ftrt.offsetMax = new Vector2(-20f, 0f);

            _filterAvailableLabel = filterTextGO.AddComponent<TextMeshProUGUI>();
            _filterAvailableLabel.text = "DOSTĘPNE"; // Początkowy tekst
            _filterAvailableLabel.alignment = TextAlignmentOptions.Center;
            _filterAvailableLabel.fontSize = 10; // Mała czcionka by weszło
            _filterAvailableLabel.fontStyle = FontStyles.Bold;
            if (_gameFont != null) _filterAvailableLabel.font = _gameFont;
            _filterAvailableLabel.color = StatsAppTheme.TextLight;
            SafeSetOutline(_filterAvailableLabel, 0.15f);
            _filterAvailableLabel.enableAutoSizing = true;
            _filterAvailableLabel.fontSizeMin = 7f;
            _filterAvailableLabel.fontSizeMax = 11f;
            _filterAvailableLabel.enableWordWrapping = false;
            _filterAvailableLabel.overflowMode = TextOverflowModes.Ellipsis;
            _filterAvailableLabel.raycastTarget = false;
            AttachHeaderDropdownArrow(_filterAvailableBtn, "FilterDropdownArrow");

            // =========================
            // SORT MODE BUTTON (like before)
            // =========================
            var sortModeGO = new GameObject("SortMode");
            sortModeGO.transform.SetParent(header.transform, false);

            var smrt = sortModeGO.AddComponent<RectTransform>();
            // SORT MODE
            smrt.anchorMin = new Vector2(0.665f, 0.08f);
            smrt.anchorMax = new Vector2(0.875f, 0.43f);
            smrt.offsetMin = Vector2.zero;
            smrt.offsetMax = Vector2.zero;

            sortModeGO.AddComponent<CanvasRenderer>();
            var smImg = sortModeGO.AddComponent<Image>();
            smImg.color = StatsAppTheme.Button;
            smImg.raycastTarget = true;

            _sortModeBtn = sortModeGO.AddComponent<Button>();
            _sortModeBtn.onClick.AddListener((UnityAction)OnSortModeClicked);

            var smTextGO = new GameObject("Text");
            smTextGO.transform.SetParent(sortModeGO.transform, false);

            var smTextRT = smTextGO.AddComponent<RectTransform>();
            smTextRT.anchorMin = Vector2.zero;
            smTextRT.anchorMax = Vector2.one;
            smTextRT.offsetMin = new Vector2(4f, 0f);
            smTextRT.offsetMax = new Vector2(-20f, 0f);

            _sortLabelTmp = smTextGO.AddComponent<TextMeshProUGUI>();
            _sortLabelTmp.text = "SORT";
            _sortLabelTmp.raycastTarget = false;
            _sortLabelTmp.alignment = TextAlignmentOptions.Center;
            _sortLabelTmp.fontSize = 13f;
            _sortLabelTmp.fontStyle = FontStyles.Bold;
            if (_gameFont != null) _sortLabelTmp.font = _gameFont;
            _sortLabelTmp.color = StatsAppTheme.TextLight;
            _sortLabelTmp.enableAutoSizing = true;
            _sortLabelTmp.fontSizeMin = 6f;
            _sortLabelTmp.fontSizeMax = 13f;
            _sortLabelTmp.enableWordWrapping = false;
            _sortLabelTmp.overflowMode = TextOverflowModes.Ellipsis;
            _sortLabelTmp.characterSpacing = -0.5f;
            SafeSetOutline(_sortLabelTmp, 0.15f);
            _sortLabelTmp.outlineColor = new Color32(0, 0, 0, 180);
            AttachHeaderDropdownArrow(_sortModeBtn, "SortDropdownArrow");

            // =========================
            // SORT DIR BUTTON (arrow)
            // =========================
            var sortDirGO = new GameObject("SortDir");
            sortDirGO.transform.SetParent(header.transform, false);

            var sdrt = sortDirGO.AddComponent<RectTransform>();
            // SORT DIR (↑ / ↓)
            sdrt.anchorMin = new Vector2(0.885f, 0.08f);
            sdrt.anchorMax = new Vector2(0.925f, 0.43f);
            sdrt.offsetMin = Vector2.zero;
            sdrt.offsetMax = Vector2.zero;

            sortDirGO.AddComponent<CanvasRenderer>();
            var sdImg = sortDirGO.AddComponent<Image>();
            sdImg.color = StatsAppTheme.Button;
            sdImg.raycastTarget = true;

            _sortDirBtn = sortDirGO.AddComponent<Button>();
            _sortDirBtn.onClick.AddListener((UnityAction)OnSortDirClicked);

            var sdTextGO = new GameObject("Text");
            sdTextGO.transform.SetParent(sortDirGO.transform, false);

            var sdTextRT = sdTextGO.AddComponent<RectTransform>();
            sdTextRT.anchorMin = Vector2.zero;
            sdTextRT.anchorMax = Vector2.one;
            sdTextRT.offsetMin = Vector2.zero;
            sdTextRT.offsetMax = Vector2.zero;

            _sortDirTmp = sdTextGO.AddComponent<TextMeshProUGUI>();
            _sortDirTmp.text = _sortAsc ? "⬆" : "⬇";
            _sortDirTmp.raycastTarget = false;
            _sortDirTmp.alignment = TextAlignmentOptions.Center;
            _sortDirTmp.fontSize = 15;
            _sortDirTmp.fontStyle = FontStyles.Bold;
            if (_gameFont != null) _sortDirTmp.font = _gameFont;
            _sortDirTmp.color = StatsAppTheme.TextLight;
            SafeSetOutline(_sortDirTmp, 0.25f);
            _sortDirTmp.outlineColor = new Color32(0, 0, 0, 180);

            // =========================
            // NEW: SEARCH BAR (Center - only for Products)
            // =========================
            _searchBarGO = new GameObject("SearchBar");
            _searchBarGO.transform.SetParent(header.transform, false);

            var sbrt = _searchBarGO.AddComponent<RectTransform>();
            sbrt.anchorMin = new Vector2(0.34f, 0.08f);
            sbrt.anchorMax = new Vector2(0.65f, 0.43f);
            sbrt.offsetMin = Vector2.zero;
            sbrt.offsetMax = Vector2.zero;

            _searchBarGO.AddComponent<CanvasRenderer>();
            var sbImg = _searchBarGO.AddComponent<Image>();
            sbImg.color = StatsAppTheme.InputBackground;

            var searchOutline = _searchBarGO.AddComponent<Outline>();
            searchOutline.effectColor = StatsAppTheme.Border;
            searchOutline.effectDistance = new Vector2(1f, -1f);

            _searchInputField = _searchBarGO.AddComponent<TMPro.TMP_InputField>();

            // Obszar tekstu
            var textArea = new GameObject("Text Area");
            textArea.transform.SetParent(_searchBarGO.transform, false);
            var tart = textArea.AddComponent<RectTransform>();
            tart.anchorMin = Vector2.zero;
            tart.anchorMax = Vector2.one;
            tart.offsetMin = new Vector2(10f, 0f);
            tart.offsetMax = new Vector2(-10f, 0f);

            // Sam tekst wpisywany
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(textArea.transform, false);
            var txt = textGO.AddComponent<TextMeshProUGUI>();
            txt.fontSize = 12;
            txt.color = StatsAppTheme.TextDark;
            txt.alignment = TextAlignmentOptions.Left;
            txt.verticalAlignment = VerticalAlignmentOptions.Middle;
            if (_gameFont != null) txt.font = _gameFont;

            // Placeholder (tekst pomocniczy)
            var placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(textArea.transform, false);
            var phTxt = placeholderGO.AddComponent<TextMeshProUGUI>();
            phTxt.text = Plugin.T("Szukaj nazwy lub ID...", "Search by name or ID");
            phTxt.fontSize = 12;
            phTxt.fontStyle = FontStyles.Italic;
            phTxt.color = StatsAppTheme.TextMuted;
            phTxt.alignment = TextAlignmentOptions.Left;
            phTxt.verticalAlignment = VerticalAlignmentOptions.Middle;
            if (_gameFont != null) phTxt.font = _gameFont;

            // Przypisanie do komponentu
            _searchInputField.textViewport = tart;
            _searchInputField.textComponent = txt;
            _searchInputField.placeholder = phTxt;
            _searchInputField.onValueChanged.AddListener((UnityAction<string>)OnSearchValueChanged);

            // =========================
            // DAY SELECTOR (center)
            // =========================
            _daySelectorGO = new GameObject("DaySelector");
            _daySelectorGO.transform.SetParent(header.transform, false);

            var drt = _daySelectorGO.AddComponent<RectTransform>();
            drt.anchorMin = new Vector2(0.34f, 0.08f);
            drt.anchorMax = new Vector2(0.65f, 0.43f);
            drt.offsetMin = Vector2.zero;
            drt.offsetMax = Vector2.zero;

            _daySelectorGO.AddComponent<CanvasRenderer>();
            var daySelectorBg = _daySelectorGO.AddComponent<Image>();
            daySelectorBg.color = StatsAppTheme.Button;
            daySelectorBg.raycastTarget = false;

            var daySelectorOutline = _daySelectorGO.AddComponent<Outline>();
            daySelectorOutline.effectColor = StatsAppTheme.HeaderBorder;
            daySelectorOutline.effectDistance = new Vector2(1f, -1f);

            // PREV
            var prevGO = new GameObject("Prev");
            prevGO.transform.SetParent(_daySelectorGO.transform, false);

            var prt = prevGO.AddComponent<RectTransform>();
            prt.anchorMin = new Vector2(0f, 0.15f);
            prt.anchorMax = new Vector2(0.18f, 0.85f);
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;

            prevGO.AddComponent<CanvasRenderer>();
            prevGO.AddComponent<Image>().color = StatsAppTheme.Button;
            _prevDayBtn = prevGO.AddComponent<Button>();
            _prevDayBtn.onClick.AddListener((UnityAction)OnPrevDayClicked);

            var prevTxtGO = new GameObject("Text");
            prevTxtGO.transform.SetParent(prevGO.transform, false);

            var prevTxtRT = prevTxtGO.AddComponent<RectTransform>();
            prevTxtRT.anchorMin = Vector2.zero;
            prevTxtRT.anchorMax = Vector2.one;
            prevTxtRT.offsetMin = Vector2.zero;
            prevTxtRT.offsetMax = Vector2.zero;

            var prevTmp = prevTxtGO.AddComponent<TextMeshProUGUI>();
            prevTmp.text = "<";
            prevTmp.raycastTarget = false;
            prevTmp.alignment = TextAlignmentOptions.Center;
            prevTmp.fontSize = 18;
            if (_gameFont != null) prevTmp.font = _gameFont;
            prevTmp.color = StatsAppTheme.TextLight;
            SafeSetOutline(prevTmp, 0.15f);
            prevTmp.outlineColor = new Color32(0, 0, 0, 180);

            // LABEL
            var labelGO = new GameObject("Label");
            labelGO.transform.SetParent(_daySelectorGO.transform, false);

            var lrt = labelGO.AddComponent<RectTransform>();
            lrt.anchorMin = new Vector2(0.20f, 0f);
            lrt.anchorMax = new Vector2(0.80f, 1f);
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;

            _dayLabelTmp = labelGO.AddComponent<TextMeshProUGUI>();
            _dayLabelTmp.text = Plugin.T("DZIEŃ", "DAY");
            _dayLabelTmp.raycastTarget = false;
            _dayLabelTmp.alignment = TextAlignmentOptions.Center;
            _dayLabelTmp.fontSize = 15;
            _dayLabelTmp.fontStyle = FontStyles.Bold;
            if (_gameFont != null) _dayLabelTmp.font = _gameFont;
            _dayLabelTmp.color = StatsAppTheme.TextLight;
            SafeSetOutline(_dayLabelTmp, 0.15f);
            _dayLabelTmp.outlineColor = new Color32(0, 0, 0, 180);

            // NEXT
            var nextGO = new GameObject("Next");
            nextGO.transform.SetParent(_daySelectorGO.transform, false);

            var nrt = nextGO.AddComponent<RectTransform>();
            nrt.anchorMin = new Vector2(0.82f, 0.15f);
            nrt.anchorMax = new Vector2(1f, 0.85f);
            nrt.offsetMin = Vector2.zero;
            nrt.offsetMax = Vector2.zero;

            nextGO.AddComponent<CanvasRenderer>();
            nextGO.AddComponent<Image>().color = StatsAppTheme.Button;
            _nextDayBtn = nextGO.AddComponent<Button>();
            _nextDayBtn.onClick.AddListener((UnityAction)OnNextDayClicked);

            var nextTxtGO = new GameObject("Text");
            nextTxtGO.transform.SetParent(nextGO.transform, false);

            var nextTxtRT = nextTxtGO.AddComponent<RectTransform>();
            nextTxtRT.anchorMin = Vector2.zero;
            nextTxtRT.anchorMax = Vector2.one;
            nextTxtRT.offsetMin = Vector2.zero;
            nextTxtRT.offsetMax = Vector2.zero;

            var nextTmp = nextTxtGO.AddComponent<TextMeshProUGUI>();
            nextTmp.text = ">";
            nextTmp.raycastTarget = false;
            nextTmp.alignment = TextAlignmentOptions.Center;
            nextTmp.fontSize = 18;
            if (_gameFont != null) nextTmp.font = _gameFont;
            nextTmp.color = StatsAppTheme.TextLight;
            SafeSetOutline(nextTmp, 0.15f);
            nextTmp.outlineColor = new Color32(0, 0, 0, 180);

            // =========================
            // CLOSE BUTTON (right)
            // =========================
            var closeGO = new GameObject("Close");
            closeGO.transform.SetParent(header.transform, false);

            var crt = closeGO.AddComponent<RectTransform>();
            crt.anchorMin = new Vector2(0.945f, 0.56f);
            crt.anchorMax = new Vector2(0.985f, 0.90f);
            crt.offsetMin = Vector2.zero;
            crt.offsetMax = Vector2.zero;

            closeGO.AddComponent<CanvasRenderer>();
            closeGO.AddComponent<Image>().color = StatsAppTheme.Danger;

            var closeBtn = closeGO.AddComponent<Button>();
            closeBtn.onClick.AddListener((UnityAction)OnCloseClicked);

            var closeTextGO = new GameObject("Text");
            closeTextGO.transform.SetParent(closeGO.transform, false);

            var ctrt = closeTextGO.AddComponent<RectTransform>();
            ctrt.anchorMin = Vector2.zero;
            ctrt.anchorMax = Vector2.one;
            ctrt.offsetMin = Vector2.zero;
            ctrt.offsetMax = Vector2.zero;

            var ctt = closeTextGO.AddComponent<TextMeshProUGUI>();
            ctt.text = "X";
            ctt.fontSize = 12;
            ctt.alignment = TextAlignmentOptions.Center;
            if (_gameFont != null) ctt.font = _gameFont;
            ctt.raycastTarget = false;
            ctt.color = StatsAppTheme.TextLight;
            SafeSetOutline(ctt, 0.15f);
            ctt.outlineColor = new Color32(0, 0, 0, 180);

            // polish visuals like earlier
            PolishButtonVisual(_prevDayBtn, isSmall: true);
            PolishButtonVisual(_nextDayBtn, isSmall: true);
            PolishButtonVisual(_titleModeBtn, isSmall: false);

            PolishButtonVisual(_sortModeBtn, isSmall: true);
            PolishButtonVisual(_sortDirBtn, isSmall: true);

            PolishButtonVisual(closeBtn, isSmall: true);
            MakeCloseButtonRed(closeBtn);

            PolishButtonVisual(_filterAvailableBtn, isSmall: true);
                        
            // ensure close on top
            closeGO.transform.SetAsLastSibling();
        }

        // -----------------------------
        // 2) Desktop shortcut (fix: dwa razy, bo UI gry nadpisuje layout)
        // -----------------------------
        private void EnsureDesktopShortcut()
        {
            _shortcutPlaced = false;

            FixDesktopShortcut(immediate: true);

            // kilka prób, bo gra przebudowuje UI po czasie
            Invoke(nameof(FixDesktopShortcutDelayed), 0.2f);
            Invoke(nameof(FixDesktopShortcutDelayed), 0.6f);
            Invoke(nameof(FixDesktopShortcutDelayed), 1.2f);

            // twardo: próbuj co 0.25s przez chwilę, aż ustawimy pod Terminy
            CancelInvoke(nameof(FixDesktopShortcutRepeating));
            InvokeRepeating(nameof(FixDesktopShortcutRepeating), 0.25f, 0.25f);
            Invoke(nameof(StopFixShortcutRepeating), 4.0f); // po 4s kończymy na pewno
        }


        private void FixDesktopShortcutRepeating()
        {
            if (_shortcutPlaced)
            {
                CancelInvoke(nameof(FixDesktopShortcutRepeating));
                return;
            }
            FixDesktopShortcut(immediate: false);
        }

        private void StopFixShortcutRepeating()
        {
            CancelInvoke(nameof(FixDesktopShortcutRepeating));
        }


        private void FixDesktopShortcutDelayed()
        {
            FixDesktopShortcut(immediate: false);
        }

        private void FixDesktopShortcut(bool immediate)
        {
            if (_computerRoot == null) return;

            Transform shortcutsTr =
                FindByPath(_computerRoot, "Screen/Desktop Canvas/App Shortcuts") ??
                FindByPath(_computerRoot, "Computer &&/Screen/Desktop Canvas/App Shortcuts") ??
                FindByPath(_computerRoot, "Screen/DesktopCanvas/App Shortcuts");

            if (shortcutsTr == null) return;

            // znajdź istniejący skrót
            Transform statsTr = null;
            for (int i = 0; i < shortcutsTr.childCount; i++)
            {
                var c = shortcutsTr.GetChild(i);
                if (c != null && c.name == "Sales Stats.exe")
                {
                    statsTr = c;
                    break;
                }
            }

            // jeśli nie ma – klonuj pierwszy skrót z Button jako template
            if (statsTr == null)
            {
                Transform template = null;
                for (int i = 0; i < shortcutsTr.childCount; i++)
                {
                    var c = shortcutsTr.GetChild(i);
                    if (c == null) continue;
                    if (c.GetComponent<Button>() != null) { template = c; break; }
                }
                if (template == null) return;

                var go = UnityEngine.Object.Instantiate(template.gameObject, shortcutsTr, false);
                go.name = "Sales Stats.exe";
                go.SetActive(true);
                statsTr = go.transform;
            }

            // label -> STATYSTYKI
            var tmps = statsTr.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (tmps != null)
            {
                for (int i = 0; i < tmps.Length; i++)
                {
                    if (tmps[i] == null) continue;
                    tmps[i].text = Plugin.T("STATYSTYKI", "STATISTICS");
                }
            }

            // ICON: Twoja embedded ikona
            var iconTr = statsTr.Find("Icon");
            if (iconTr != null)
            {
                var img = iconTr.GetComponentInChildren<Image>(true);
                if (img != null)
                {
                    var sprite = EmbeddedIconLoader.LoadPngSprite("SalesStats.png");
                    if (sprite != null)
                    {
                        img.sprite = sprite;
                        img.preserveAspect = true;
                        img.color = Color.white;
                        img.enabled = true;
                    }
                }
            }

            // kliknięcie (tylko root)
            var rootBtn = statsTr.GetComponent<Button>();
            if (rootBtn != null)
            {
                rootBtn.onClick.RemoveAllListeners();
                rootBtn.onClick.AddListener((UnityAction)OnStatsShortcutClicked);
                _statsShortcutButton = rootBtn;
            }

            // ====== POZYCJA: Ustawiamy ikonę na samym końcu listy ======
            var le = statsTr.GetComponent<LayoutElement>();
            if (le != null) le.ignoreLayout = false;

            // ZMIANA 1: Wrzucamy ikonę na sam koniec, żeby nie gryzła się z nowymi apkami
            statsTr.SetSiblingIndex(shortcutsTr.childCount - 1);

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(shortcutsTr.GetComponent<RectTransform>());

            // ZMIANA 2: Zakomentowujemy ręczne pozycjonowanie. 
            // Unity (GridLayoutGroup) powinno samo ustawić ikonę na wolnym miejscu siatki.
            // PlaceStatsNextToMarketManual(shortcutsTr, statsTr); 

            if (immediate) Invoke(nameof(FixDesktopShortcutDelayed), 0.2f);
        }

        

        private void PlaceStatsNextToMarketManual(Transform shortcutsTr, Transform statsTr)
        {
            if (shortcutsTr == null || statsTr == null) return;

            // znajdź "Rynek"
            Transform marketTr = shortcutsTr.Find("Wholesale Market.Exe") ?? shortcutsTr.Find("Market.Exe");
            if (marketTr == null)
            {
                int idx = FindShortcutIndexByLabel(shortcutsTr, "RYNEK");
                if (idx >= 0) marketTr = shortcutsTr.GetChild(idx);
            }
            if (marketTr == null) return;

            // wyjmij Statystyki spod layoutu
            var le = statsTr.GetComponent<LayoutElement>() ?? statsTr.gameObject.AddComponent<LayoutElement>();
            le.ignoreLayout = true;

            // skopiuj RT z Rynku (rozmiar/anchor/pivot)
            var srt = statsTr.GetComponent<RectTransform>();
            var mrt = marketTr.GetComponent<RectTransform>();
            if (srt == null || mrt == null) return;

            srt.anchorMin = mrt.anchorMin;
            srt.anchorMax = mrt.anchorMax;
            srt.pivot = mrt.pivot;
            srt.sizeDelta = mrt.sizeDelta;
            srt.localScale = mrt.localScale;

            // przesunięcie "w prawo" o 1 kolumnę: cellSize.x + spacing.x
            float stepX = 0f;
            var grid = shortcutsTr.GetComponent<GridLayoutGroup>();
            if (grid != null)
                stepX = grid.cellSize.x + grid.spacing.x;

            if (stepX <= 0.01f) stepX = 75f; // fallback jeśli grid nie ma sensownych wartości

            float manualOffsetX = 30f; // + w prawo, - w lewo
            srt.anchoredPosition = mrt.anchoredPosition + new Vector2(stepX + manualOffsetX, 0f);


            Canvas.ForceUpdateCanvases();
        }

        private void OnStatsShortcutClicked() => ShowStats();
        private void OnCloseClicked() => HideStats();

        // -----------------------------
        // 3) show/hide
        // -----------------------------
        private void ShowStats()
        {
            if (_statsApp == null)
                EnsureStatsApp();
            if (_statsApp == null) return;

            HideAllAppsExceptDesktopCanvas();

            _statsApp.SetActive(true);
            _statsApp.transform.SetAsLastSibling();
            _isOpen = true;

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

        private void OnPrevDay()
        {
            _selectedDay = Mathf.Max(1, _selectedDay - 1);
            RebuildDaysUI();
            UpdateDayLabel();

            QueueBuildTiles();
        }

        private void OnNextDay()
        {
            _selectedDay = _selectedDay + 1;
            RebuildDaysUI();
            UpdateDayLabel();

            QueueBuildTiles();
        }

        private void UpdateDayLabel()
        {
            int currentDay = GetCurrentDaySafe();

            _dayLabelTmp.text = (_selectedDay == currentDay) ? Plugin.T("DZIŚ", "TODAY") : $"{Plugin.T("DZIEŃ", "DAY")} {_selectedDay}";

        }
        private void QueueBuildTiles()
        {
            _buildQueued = true;
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
            HideHeaderDropdown();
            HideChartDropdown();
            _statsApp?.SetActive(false);
            _isOpen = false;

            // 🔥 Reset dnia – przy kolejnym otwarciu będzie "dziś"
            _selectedDay = -1;
        }

        // -----------------------------
        // GRID UI
        // -----------------------------

        private void EnsureGridUI()
        {
            if (_statsApp == null) return;

            var existing = FindDirectChild(_statsApp.transform, "TilesScroll");
            if (existing != null) return;

            var scrollGO = new GameObject("TilesScroll");
            scrollGO.transform.SetParent(_statsApp.transform, false);
            scrollGO.transform.SetAsLastSibling();

            var scrollRT = scrollGO.AddComponent<RectTransform>();
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
                    if (p.SoldUnits == 0 && p.ThrownUnits == 0 && p.SoldWeightKg <= 0.0001f && p.ThrownWeightKg <= 0.0001f)
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

        // -----------------------------
        // TMP style
        // -----------------------------
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

        // -----------------------------
        // Helpers
        // -----------------------------
        private static Transform FindByPath(Transform root, string relativePath)
        {
            var parts = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            Transform current = root;

            for (int p = 0; p < parts.Length; p++)
            {
                bool found = false;
                for (int i = 0; i < current.childCount; i++)
                {
                    var c = current.GetChild(i);
                    if (c.name == parts[p])
                    {
                        current = c;
                        found = true;
                        break;
                    }
                }
                if (!found) return null;
            }

            return current;
        }

        private static Transform FindDirectChild(Transform parent, string exactName)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == exactName) return c;
            }
            return null;
        }

        private void SetTmpText(Transform root, string relativePath, string value)
        {
            var t = FindByPath(root, relativePath);
            if (t == null) return;

            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null) { tmp.text = value; return; }

            tmp = t.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) tmp.text = value;
        }

        private void DisableRaycastOnAllTMP(Transform root)
        {
            var tmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < tmps.Length; i++)
                tmps[i].raycastTarget = false;
        }

        private static int FindShortcutIndexByName(Transform shortcutsTr, string exactName)
        {
            if (shortcutsTr == null) return -1;
            for (int i = 0; i < shortcutsTr.childCount; i++)
            {
                var c = shortcutsTr.GetChild(i);
                if (c != null && string.Equals(c.name, exactName, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        private int FindShortcutIndexByLabel(Transform shortcutsTr, string labelUpper)
        {
            if (shortcutsTr == null) return -1;

            string needle = labelUpper.Trim().ToUpperInvariant();

            for (int i = 0; i < shortcutsTr.childCount; i++)
            {
                var c = shortcutsTr.GetChild(i);
                if (c == null) continue;

                var tmps = c.GetComponentsInChildren<TextMeshProUGUI>(true);
                if (tmps == null) continue;

                for (int t = 0; t < tmps.Length; t++)
                {
                    var tmp = tmps[t];
                    if (tmp == null || string.IsNullOrEmpty(tmp.text)) continue;

                    var txt = tmp.text.Trim().ToUpperInvariant();
                    if (txt == needle || txt.Contains(needle))
                        return i;
                }
            }
            return -1;
        }

        private void CloneOrCopyIconObject(Transform fromShortcut, Transform toShortcut)
        {
            if (fromShortcut == null || toShortcut == null) return;

            var fromIcon = fromShortcut.Find("Icon");
            if (fromIcon == null) return;

            // usuń nasz Icon jeśli istnieje (żeby nie było dwóch)
            var toIcon = toShortcut.Find("Icon");
            if (toIcon != null)
                UnityEngine.Object.Destroy(toIcon.gameObject);

            // sklonuj całe GO "Icon" (z wszystkimi komponentami gry)
            var clone = UnityEngine.Object.Instantiate(fromIcon.gameObject, toShortcut, false);
            clone.name = "Icon";
            clone.SetActive(true);

            // upewnij się że jest widoczny (gdyby alpha była zerowa)
            var gfx = clone.GetComponentsInChildren<UnityEngine.UI.Graphic>(true);
            if (gfx != null)
            {
                for (int i = 0; i < gfx.Length; i++)
                {
                    var g = gfx[i];
                    if (g == null) continue;
                    var c = g.color;
                    if (c.a < 0.01f) c.a = 1f;
                    g.color = c;
                    g.enabled = true;
                }
            }
        }
        private int GetCurrentDaySafe()
        {
            int day = 1;
            if (DayCycleManager.Instance != null)
                day = DayCycleManager.Instance.CurrentDay;
            return Mathf.Max(1, day);
        }

        private void EnsureSelectedDayInitialized()
        {
            if (_selectedDay >= 1) return;
            _selectedDay = GetCurrentDaySafe();
        }

        private void UpdateDayHeaderUI()
        {
            int current = GetCurrentDaySafe();
            EnsureSelectedDayInitialized();

            if (_dayLabelTmp != null)
            {
                // Tłumaczenie nagłówka dnia
                if (_selectedDay == current)
                {
                    _dayLabelTmp.text = Plugin.T("DZIŚ", "TODAY");
                }
                else
                {
                    _dayLabelTmp.text = $"{Plugin.T("DZIEŃ", "DAY")} {_selectedDay}";
                }
            }

            if (_prevDayBtn != null)
                _prevDayBtn.interactable = _selectedDay > 1;

            if (_nextDayBtn != null)
                _nextDayBtn.interactable = _selectedDay < current;

            UpdateSortHeaderUI();
        }

        private void OnPrevDayClicked()
        {
            if (_hubMode == HubMode.DailySummary)
            {
                MoveDailySummaryDay(-1);
                return;
            }

            if (_hubMode != HubMode.Stats) return;
            _selectedDay = Mathf.Max(1, _selectedDay - 1);
            RebuildDaysUI();
            UpdateDayLabel();
            QueueBuildForHubMode();
        }

        private void OnNextDayClicked()
        {
            if (_hubMode == HubMode.DailySummary)
            {
                MoveDailySummaryDay(1);
                return;
            }

            if (_hubMode != HubMode.Stats) return;
            _selectedDay = _selectedDay + 1;
            RebuildDaysUI();
            UpdateDayLabel();
            QueueBuildForHubMode();
        }

        private void OnSortModeClicked()
        {
            ToggleSortDropdown();
        }

        private void OnSortDirClicked()
        {
            // Wspólna strzałka sortowania dla wszystkich trybów
            _sortAsc = !_sortAsc;

            // aktualizujemy tylko strzałkę (label SORT zostaje od _simpleSort)
            if (_sortDirTmp != null)
                _sortDirTmp.text = _sortAsc ? "↑" : "↓";
            RefreshSortButtonText();
            BuildForHubMode();
        }
        
        private void UpdateSimpleSortLabel()
        {
            if (_sortLabelTmp == null) return;

            switch (_simpleSort)
            {
                case SimpleSortMode.Name: _sortLabelTmp.text = "SORT: NAZWA"; break;
                case SimpleSortMode.ProductId: _sortLabelTmp.text = "SORT: ID"; break;
                case SimpleSortMode.NearestExpiry: _sortLabelTmp.text = "SORT: NAJKRÓTSZY"; break;
            }
        }
        private int GetNearestDaysLeft(SortedDictionary<int, int> batches)
        {
            if (batches == null || batches.Count == 0) return int.MaxValue;

            // SortedDictionary jest posortowany po key, więc First() = najmniejszy daysLeft
            foreach (var kv in batches)
            {
                if (kv.Value > 0) return kv.Key;
            }
            return int.MaxValue;
        }

        private string GetSortLabel(StatsSortMode mode)
        {
            return mode switch
            {
                StatsSortMode.Name => Plugin.T("NAZWA", "NAME"),
                StatsSortMode.ProductId => "ID",
                StatsSortMode.SoldRevenue => Plugin.T("PRZYCHÓD", "REVENUE"),
                StatsSortMode.ThrownValue => Plugin.T("STRATA", "LOSS"),
                StatsSortMode.ThrownUnits => Plugin.T("WYRZUCONE", "WASTED"),
                StatsSortMode.SoldUnits => Plugin.T("SPRZEDANE", "SOLD"),
                _ => Plugin.T("NAZWA", "NAME"),
            };
        }

        private void UpdateSortHeaderUI()
        {
            if (_sortLabelTmp == null) return;

            bool hideSort = (_hubMode == HubMode.Charts || _hubMode == HubMode.DailySummary);
            _sortModeBtn?.gameObject.SetActive(!hideSort);
            _sortDirBtn?.gameObject.SetActive(!hideSort);
            if (hideSort) return;

            string label = "";

            if (_hubMode == HubMode.Stats)
            {
                // Wykorzystujemy GetSortLabel, ale upewnij się, że ta metoda 
                // również używa Plugin.T (poprawialiśmy to w poprzednim kroku)
                label = GetSortLabel(_statsSortMode);
            }
            else if (_hubMode == HubMode.Expiration)
            {
                label = _simpleSort switch
                {
                    SimpleSortMode.Name => Plugin.T("NAZWA", "NAME"),
                    SimpleSortMode.ProductId => "ID",
                    SimpleSortMode.NearestExpiry => Plugin.T("TERMIN", "EXPIRY"),
                    _ => Plugin.T("NAZWA", "NAME")
                };
            }
            else if (_hubMode == HubMode.Products)
            {
                label = _simpleSort switch
                {
                    SimpleSortMode.Name => Plugin.T("NAZWA", "NAME"),
                    SimpleSortMode.ProductId => "ID",
                    SimpleSortMode.PriceBuy => Plugin.T("CENA ZAK.", "BUY PRICE"),
                    SimpleSortMode.PriceSell => Plugin.T("CENA SPRZ.", "SELL PRICE"),
                    SimpleSortMode.TotalStock => Plugin.T("STAN", "STOCK"),
                    SimpleSortMode.TotalValue => Plugin.T("WARTOŚĆ", "TOTAL VALUE"),
                    _ => Plugin.T("NAZWA", "NAME")
                };
            }
            else if (_hubMode == HubMode.Analysis)
            {
                label = GetAnalysisSortLabel();
            }

            // Tłumaczymy również prefiks "SORT: "
            _sortLabelTmp.text = $"{Plugin.T("SORT", "SORT")}: {label}";

            if (_sortDirTmp != null)
            {
                _sortDirTmp.text = _sortAsc ? "⬆" : "⬇";
            }
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
                info.fontSize = 8.7f;
                info.fontStyle = FontStyles.Normal;
                if (_gameFont != null) info.font = _gameFont;
                info.color = StatsAppTheme.TileText;
                SafeSetOutline(info, 0f);
                info.enableAutoSizing = true;
                info.fontSizeMin = 6.8f;
                info.fontSizeMax = 8.7f;
                info.lineSpacing = 0f;
                info.enableWordWrapping = false;
                info.overflowMode = TextOverflowModes.Ellipsis;
                info.alignment = TextAlignmentOptions.TopLeft;
            }
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
        private static void DisableGameScriptsOnTile(Transform root)
        {
            // Wyłączamy wszystkie MonoBehaviour pochodzące z Assembly-CSharp (skrypty gry),
            // bo one potrafią po 1 klatce nadpisać TMP w prefabie.
            var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                var mb = behaviours[i];
                if (mb == null) continue;

                var t = mb.GetType();
                string asm = t.Assembly?.GetName()?.Name ?? "";

                // zostaw Unity/TMP
                if (asm == "UnityEngine" || asm.StartsWith("Unity.") || asm.StartsWith("TMPro"))
                    continue;

                // zostaw nasze
                if (!string.IsNullOrEmpty(t.Namespace) && t.Namespace.StartsWith("StatisticMod"))
                    continue;

                // wyłącz skrypty gry
                if (asm == "Assembly-CSharp")
                {
                    try { mb.enabled = false; } catch { }
                }
            }
        }
        private void OnNewDayDetected(int newDay)
        {
            // ✅ upewnij się że dzień istnieje
            StatsStore.GetDay(newDay);

            RebuildDaysUI();           // <- to dodamy poniżej

            // ✅ jeśli okno otwarte:
            if (_isOpen)
            {
                // nie przeskakuj użytkownikowi na nowy dzień jeśli ogląda stary
                // ale jeśli był na "dzisiaj", to możesz go przerzucić:
                int current = GetCurrentDaySafe();
                if (_selectedDay <= 0) _selectedDay = current;

                // jeśli wybrany dzień == poprzedni current, to przełącz na nowy
                // (opcjonalne – możesz to wywalić, jeśli nie chcesz auto-przeskoku)
                if (_selectedDay == current - 1)
                    _selectedDay = current;

                QueueBuildTiles();
            }
        }
        private void RebuildDaysUI()
        {
            // ✅ zakres dni bierzemy z danych (to działa nawet gdy nie ma sprzedaży)
            int minDay = 1;
            int maxDay = 1;

            var days = StatsStore.Data?.Days;
            if (days != null && days.Count > 0)
            {
                minDay = int.MaxValue;
                maxDay = int.MinValue;

                for (int i = 0; i < days.Count; i++)
                {
                    int d = days[i].Day;
                    if (d < minDay) minDay = d;
                    if (d > maxDay) maxDay = d;
                }

                if (minDay == int.MaxValue) minDay = 1;
                if (maxDay == int.MinValue) maxDay = 1;
            }
            // ... wylicz minDay/maxDay z danych ...

            int currentDay = GetCurrentDaySafe();
            if (maxDay < currentDay) maxDay = currentDay;

            // ✅ ogranicz selectedDay do zakresu
            if (_selectedDay < minDay) _selectedDay = minDay;
            if (_selectedDay > maxDay) _selectedDay = maxDay;

            // ✅ strzałki
            if (_prevDayBtn != null) _prevDayBtn.interactable = (_selectedDay > minDay);
            if (_nextDayBtn != null) _nextDayBtn.interactable = (_selectedDay < maxDay);

        }
        public static void NotifyNewDay(int newDay)
        {
            var inst = _instance;
            if (inst == null) return;

            inst.OnNewDayDetected(newDay);
        }
        private void OnTitleModeClicked()
        {
            ToggleHubModeDropdown();
        }

        public void HideChartDropdown()
        {
            if (_productDropPanel != null && _productDropPanel.activeSelf)
            {
                Plugin.DebugLog("[Charts] Wymuszono zamknięcie panelu wyszukiwania produktu.");
                _productDropPanel.SetActive(false);
            }

            if (_dropdownBlocker != null)
                _dropdownBlocker.SetActive(false);
        }

        private void RefreshTitleModeText()
        {
            if (_titleTmp == null) return;

            _titleTmp.text = _hubMode switch
            {
                HubMode.Stats => Plugin.T("STATYSTYKI", "STATISTICS"),
                HubMode.Expiration => Plugin.T("TERMINY", "EXPIRATION"),
                HubMode.Products => Plugin.T("PRODUKTY", "PRODUCTS"),
                HubMode.DailySummary => Plugin.T("PODSUMOWANIE", "SUMMARY"),
                HubMode.Analysis => Plugin.T("ANALIZA", "ANALYSIS"),
                HubMode.Charts => Plugin.T("WYKRESY", "CHARTS"),
                _ => "STATS"
            };
        }

        private void BuildForHubMode()
        {
            UpdateSortHeaderUI();

            // 1. Filtr dostępności w Produktach oraz zakres dni w Analizie
            if (_filterAvailableBtn != null)
            {
                bool showFilter = (_hubMode == HubMode.Products || _hubMode == HubMode.Analysis);
                _filterAvailableBtn.gameObject.SetActive(showFilter);
                if (showFilter) UpdateFilterButtonUI();
            }

            // 2. POPRAWKA SELEKTORA DNI:
            // Zmieniamy z != Products na == Stats
            // Teraz selektor dni pokaże się TYLKO w zakładce STATYSTYKI
            _daySelectorGO?.SetActive(_hubMode == HubMode.Stats || _hubMode == HubMode.DailySummary);

            // 3. Zarządzanie wyszukiwarką (Tylko w Produktach)
            _searchBarGO?.SetActive(_hubMode == HubMode.Products || _hubMode == HubMode.Analysis);

            // 4. Budowanie zawartości
            switch (_hubMode)
            {
                case HubMode.Stats:
                    if (_titleTmp != null)
                        _titleTmp.text = Plugin.T("STATYSTYKI", "STATISTICS");
                    ExitChartsLayout();
                    BuildStatsTiles();
                    break;

                case HubMode.Expiration:
                    if (_titleTmp != null)
                        _titleTmp.text = Plugin.T("TERMINY", "EXPIRATION");
                    ExitChartsLayout();
                    BuildExpirationTilesNow();
                    break;

                case HubMode.Products:
                    if (_titleTmp != null)
                        _titleTmp.text = Plugin.T("PRODUKTY", "PRODUCTS");
                    ExitChartsLayout();
                    BuildAllProductsTilesNow();
                    break;

                case HubMode.DailySummary:
                    if (_titleTmp != null)
                        _titleTmp.text = Plugin.T("PODSUMOWANIE", "SUMMARY");
                    ExitChartsLayout();
                    BuildDailySummaryTiles();
                    break;

                case HubMode.Analysis:
                    // Zakres jest pokazywany wyłącznie w osobnym przycisku filtra.
                    // Nie dopisujemy go do tytułu, bo powodowało to nachodzenie tekstu.
                    if (_titleTmp != null)
                        _titleTmp.text = Plugin.T("ANALIZA", "ANALYSIS");
                    BuildAnalysisTilesNow();
                    break;

                case HubMode.Charts:
                    if (_titleTmp != null)
                        _titleTmp.text = Plugin.T("ANALIZA PRODUKTU", "PRODUCT ANALYSIS");

                    EnterChartsLayout();
                    BuildChartsWindow();
                    ScheduleChartsPass3();
                    break;
            }
        }

        private void BuildExpirationTilesNow()
        {
            ClearTilesOnly(); //

            var visual = Plugin.ProductCache;
            if (visual == null) return;
            if (visual.Count == 0)
            {
                var idm = UnityEngine.Object.FindFirstObjectByType<global::IDManager>();
                if (idm != null) visual.Build(idm);
            }

            var global = BuildGlobalExpirationMap();
            if (global.Count == 0) { ForceTilesLayout(0); return; }

            var ids = new List<int>(global.Keys);
            var polishCulture = new System.Globalization.CultureInfo("pl-PL");

            ids.Sort((a, b) =>
            {
                int dir = _sortAsc ? 1 : -1;
                int cmp = 0;
                if (_simpleSort == SimpleSortMode.ProductId) cmp = a.CompareTo(b);
                else if (_simpleSort == SimpleSortMode.NearestExpiry) cmp = GetNearestDaysLeft(global[a]).CompareTo(GetNearestDaysLeft(global[b]));
                else cmp = string.Compare(GetProductNameSafe(a), GetProductNameSafe(b), polishCulture, System.Globalization.CompareOptions.IgnoreCase);
                if (cmp == 0) cmp = a.CompareTo(b);
                return cmp * dir;
            });

            int built = 0;
            foreach (var pid in ids)
            {
                visual.TryGet(pid, out var name, out var icon); //
                var tile = Instantiate(_tileTemplate, _tilesContent, false);
                tile.SetActive(true);
                DisableGameScriptsOnTile(tile.transform);

                // NAZWA: Sama nazwa (bez ID) + Auto-Sizing
                var nameTmp = GetTmpComponent(tile.transform, "Product Name");
                if (nameTmp != null)
                {
                    nameTmp.text = FormatTileProductName(name, pid, false); //
                    nameTmp.enableAutoSizing = true;
                    nameTmp.fontSizeMin = 6f;
                    nameTmp.fontSizeMax = 14f;
                    nameTmp.enableWordWrapping = false;
                    nameTmp.alignment = TextAlignmentOptions.Left;
                }

                var iconTr = tile.transform.Find("Product Icon");
                if (iconTr != null)
                {
                    var img = iconTr.GetComponent<UnityEngine.UI.Image>();
                    if (img != null) { img.sprite = icon; img.enabled = (icon != null); img.preserveAspect = true; }
                }

                var infoTr = tile.transform.Find("Product Brand");
                if (infoTr != null)
                {
                    var tmp = infoTr.GetComponent<TMPro.TextMeshProUGUI>();
                    if (tmp != null) tmp.text = BuildExpirationText(pid, global[pid], 4);
                }

                AdjustProductTileContent(tile.transform);
                ApplyExpirationAccent(tile.transform, global[pid]);
                ForceProfessionalTileVisuals(tile.transform);
                built++;
            }
            ForceTilesLayout(built);
        }

        private void RetryBuildExpiration()
        {
            if (!_isOpen) return;
            if (_hubMode != HubMode.Expiration) return;
            BuildExpirationTilesNow();
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

                    infoTmp.text =
                        $"{Plugin.T("Cena", "Price")}: <color={StatsAppTheme.WarningHex}><b>{Plugin.T("Z", "B")}: {buyP:N2} $</b></color> | <color={StatsAppTheme.PositiveHex}><b>{Plugin.T("S", "S")}: {sellP:N2} $</b></color>\n" +
                        $"{Plugin.T("Stan", "Stock")}: <color={StatsAppTheme.InfoHex}><b>{Plugin.T("S", "S")}: {sStockStr}</b></color> | <color={StatsAppTheme.WarningHex}><b>{Plugin.T("M", "W")}: {wStockStr}</b></color>\n" +
                        $"{Plugin.T("Wartość", "Value")} {Plugin.T("Z", "B")}: <color={StatsAppTheme.PurpleHex}><b>{totalCostValue:N2} $</b></color> | {Plugin.T("S", "S")}: <color={StatsAppTheme.PositiveHex}><b>{totalSalesValue:N2} $</b></color>";

                    infoTmp.fontSize = 8.7f;
                    infoTmp.lineSpacing = 0f;
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

        private Dictionary<int, SortedDictionary<int, int>> BuildGlobalExpirationMap()
        {
            var result = new Dictionary<int, SortedDictionary<int, int>>();

            // C5 FIX: Pancerna bramka natywna przed odczytem instancji dnia
            var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
            int currentDay = dcm != null ? dcm.CurrentDay : 1;

            // --- 1. ZLICZANIE TOWARU NA PÓŁKACH SKLEPOWYCH ---
            var allSlots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
            if (allSlots != null)
            {
                for (int i = 0; i < allSlots.Count; i++)
                {
                    var slot = allSlots[i];
                    if (slot != null && slot.HasProduct)
                    {
                        ExpirationManager.SyncShelf(slot);

                        var products = slot.GetComponentsInChildren<global::Product>(true);
                        if (products != null)
                        {
                            for (int pIdx = 0; pIdx < products.Count; pIdx++)
                            {
                                var p = products[pIdx];
                                if (p == null) continue;

                                var comp = p.GetComponent<ProductExpirationComponent>();
                                if (comp != null)
                                {
                                    int pid = comp.ProductID;
                                    int daysLeft = comp.ExpirationDay - currentDay;

                                    if (!result.TryGetValue(pid, out var agg))
                                    {
                                        agg = new SortedDictionary<int, int>();
                                        result[pid] = agg;
                                    }

                                    // OPTYMALIZACJA DRZEWA BINARNEGO: Jedno przejście zamiast ContainsKey + []
                                    agg.TryGetValue(daysLeft, out int count);
                                    agg[daysLeft] = count + 1;
                                }
                            }
                        }
                    }
                }
            }

            // --- 2. ZLICZANIE TOWARU W KARTONACH ---
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
                        if (data != null)
                        {
                            int uid = data.UID;
                            int pid = data.ProductID;

                            if (ExpirationSaveManager.boxDates.TryGetValue(uid, out var datesList) && datesList != null)
                            {
                                if (!result.TryGetValue(pid, out var agg))
                                {
                                    agg = new SortedDictionary<int, int>();
                                    result[pid] = agg;
                                }

                                for (int dIdx = 0; dIdx < datesList.Count; dIdx++)
                                {
                                    int daysLeft = datesList[dIdx] - currentDay;

                                    agg.TryGetValue(daysLeft, out int count);
                                    agg[daysLeft] = count + 1;
                                }
                            }
                        }
                    }
                    catch { }
                }
            }

            return result;
        }

        private string BuildExpirationText(int productId, SortedDictionary<int, int> batches, int maxLines = 4)
        {
            if (batches == null || batches.Count == 0) return "";

            int lines = 0;
            var sb = new System.Text.StringBuilder();
            bool isWeight = IsWeightProduct(productId);
            string unit = isWeight ? "kg" : Plugin.T("szt.", "pcs");

            foreach (var kv in batches)
            {
                if (lines >= maxLines) break;
                int d = kv.Key;
                int c = kv.Value;
                if (c <= 0) continue;

                if (sb.Length > 0) sb.Append('\n');

                string valStr;
                if (isWeight)
                {
                    float kgPerUnit = SalesUnifiedFinal.WeightPerUnit.TryGetValue(productId, out float w) ? w : 1.0f;
                    valStr = (c * kgPerUnit).ToString("N2") + " kg";
                }
                else
                {
                    valStr = c.ToString("N0") + " " + unit;
                }

                string label;
                string colorHex;

                if (d < 0)
                {
                    label = Plugin.T("PO TERMINIE", "EXPIRED");
                    colorHex = StatsAppTheme.NegativeHex;
                }
                else if (d == 0)
                {
                    label = Plugin.T("DZIŚ", "TODAY");
                    colorHex = StatsAppTheme.NegativeHex;
                }
                else if (d == 1)
                {
                    label = Plugin.T("JUTRO", "TOMORROW");
                    colorHex = StatsAppTheme.WarningHex;
                }
                else
                {
                    label = Plugin.T($"za {d} dni", $"in {d} days");
                    colorHex = StatsAppTheme.PositiveHex;
                }

                sb.Append($"<color={colorHex}><b>{label}</b></color>: <color={colorHex}><b>{valStr}</b></color>");
                lines++;
            }
            return sb.ToString();
        }

        private void ApplyExpirationAccent(Transform tile, SortedDictionary<int, int> batches)
        {
            if (tile == null || batches == null || batches.Count == 0) return;

            int minDay = int.MaxValue;
            foreach (var d in batches.Keys)
                if (d < minDay) minDay = d;

            Color accent;
            if (minDay <= 0) accent = StatsAppTheme.Negative;
            else if (minDay == 1) accent = StatsAppTheme.Warning;
            else accent = StatsAppTheme.Positive;

            var existing = tile.Find("ExpiryAccent");
            if (existing == null)
            {
                var bar = new GameObject("ExpiryAccent");
                bar.transform.SetParent(tile, false);

                var rt = bar.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0.018f, 1f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                bar.AddComponent<CanvasRenderer>();
                var img = bar.AddComponent<Image>();
                img.raycastTarget = false;
                img.color = accent;
            }
            else
            {
                var img = existing.GetComponent<Image>();
                if (img != null) img.color = accent;
            }
        }

        private bool _queuedBuildHub;

        private void QueueBuildForHubMode()
        {
            if (_queuedBuildHub) return;
            _queuedBuildHub = true;
            Invoke(nameof(DoBuildForHubMode), 0.01f);
        }

        private void DoBuildForHubMode()
        {
            _queuedBuildHub = false;

            try
            {
                Plugin.Log.LogWarning($"[UI] DoBuildForHubMode hub={_hubMode} sortMode={_statsSortMode} asc={_sortAsc}");
                BuildForHubMode();
                Plugin.Log.LogWarning("[UI] DoBuildForHubMode done");
            }
            catch (Exception e)
            {
                Plugin.Log.LogError("[UI] BuildForHubMode crashed: " + e);
            }
        }


        private void ClearTilesOnly()
        {
            if (_tilesContent == null) return;

            for (int i = _tilesContent.childCount - 1; i >= 0; i--)
            {
                Transform ch = _tilesContent.GetChild(i);
                if (ch != null)
                    Destroy(ch.gameObject);
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
                    tileRt.sizeDelta = new Vector2(StatsAppTheme.TileWidth, StatsAppTheme.TileHeight);

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
                    var rt = infoTr.GetComponent<RectTransform>();
                    if (rt != null)
                    {
                        rt.anchorMin = new Vector2(0.325f, 0.10f);
                        rt.anchorMax = new Vector2(0.965f, 0.675f);
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
                        tmp.fontSize = 8.7f;
                        tmp.fontSizeMin = 6.7f;
                        tmp.fontSizeMax = 8.7f;
                        tmp.lineSpacing = 0f;
                        tmp.paragraphSpacing = 0f;
                        tmp.enableWordWrapping = false;
                        tmp.overflowMode = TextOverflowModes.Ellipsis;
                        tmp.margin = new Vector4(1f, 1f, 2f, 0f);
                        SafeSetOutline(tmp, 0f);
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

        private void ForceTilesLayout(int built)
        {
            if (_tilesContent == null) return;
            var grid = _tilesContent.GetComponent<GridLayoutGroup>();
            if (grid != null) RecalcGridContentHeight(_tilesContent, grid, built);

            LayoutRebuilder.ForceRebuildLayoutImmediate(_tilesContent);
            Canvas.ForceUpdateCanvases();

            // Pass 3: tylko tryby oparte o kafelki produktów dostają wymuszony Product Tile skin.
            // PODSUMOWANIE i WYKRESY mają własny layout i są stylowane niezależnie.
            if (UsesProfessionalProductTileLayout())
            {
                ReapplyProfessionalTileStyles();
                ScheduleProfessionalTileReapply();
            }
        }
        private void RefreshHeaderForMode()
        {
            bool showDay = (_hubMode == HubMode.Stats || _hubMode == HubMode.DailySummary);
            _daySelectorGO?.SetActive(showDay);

            // Wywołujemy zunifikowaną metodę, która wie, co wypisać w każdym trybie
            UpdateSortHeaderUI();
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
       
        private void RefreshSortButtonText()
        {
            if (_sortLabelTmp == null || _sortDirTmp == null)
                return;

            string modeText = "";

            // Sprawdzamy w jakim trybie okna jesteśmy
            if (_hubMode == HubMode.Stats)
            {
                // Tryb Statystyki - używamy _statsSortMode
                modeText = _statsSortMode switch
                {
                    StatsSortMode.SoldRevenue => Plugin.T("PRZYCHÓD", "REVENUE"),
                    StatsSortMode.SoldUnits => Plugin.T("SPRZEDANE", "SOLD"),
                    StatsSortMode.ThrownValue => Plugin.T("STRATA", "LOSS"),
                    StatsSortMode.ThrownUnits => Plugin.T("WYRZUCONE", "WASTED"),
                    StatsSortMode.Name => Plugin.T("NAZWA", "NAME"),
                    StatsSortMode.ProductId => "ID",
                    _ => _statsSortMode.ToString()
                };
            }
            else if (_hubMode == HubMode.Analysis)
            {
                modeText = GetAnalysisSortLabel();
            }
            else
            {
                modeText = _simpleSort switch
                {
                    SimpleSortMode.Name => Plugin.T("NAZWA", "NAME"),
                    SimpleSortMode.ProductId => "ID",
                    SimpleSortMode.NearestExpiry => Plugin.T("TERMIN", "EXPIRY"),
                    _ => _simpleSort.ToString()
                };
            }

            _sortLabelTmp.text = $"SORT: {modeText}";
            _sortDirTmp.text = _sortAsc ? "⬆" : "⬇";
        }

        private string GetProductNameSafe(int productId)
        {
            try
            {
                // ID 9999 to specjalny, syntetyczny produkt używany przez stoisko z lodami.
                // Nie posiada ProductSO, dlatego nie może być rozwiązywany wyłącznie przez TryGetSO().
                if (Plugin.ProductCache != null &&
                    Plugin.ProductCache.TryGet(productId, out var cachedName, out _) &&
                    !string.IsNullOrWhiteSpace(cachedName))
                {
                    if (productId == 9999)
                        return cachedName.Trim();
                }

                if (Plugin.ProductCache != null && Plugin.ProductCache.TryGetSO(productId, out var so) && so != null)
                {
                    if (!string.IsNullOrEmpty(so.TempProductName)) return so.TempProductName.Trim();
                    if (!string.IsNullOrEmpty(so.ProductName)) return so.ProductName.Trim();
                }

                // Fallback również dla ewentualnych przyszłych produktów syntetycznych bez ProductSO.
                if (Plugin.ProductCache != null &&
                    Plugin.ProductCache.TryGet(productId, out var fallbackName, out _) &&
                    !string.IsNullOrWhiteSpace(fallbackName))
                {
                    string suffix = $" ID: {productId}";
                    string value = fallbackName.Trim();
                    if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        value = value.Substring(0, value.Length - suffix.Length).TrimEnd();
                    return value;
                }
            }
            catch { }

            return $"Produkt #{productId}";
        }

        // Pass 3e: ID produktu pokazujemy wyłącznie w widoku PRODUKTY.
        // Część nazw z cache gry już zawiera sufiks "ID: xxx", więc najpierw
        // normalizujemy nazwę, aby ID nie dublowało się i nie pojawiało w innych widokach.
        private string FormatTileProductName(string rawName, int productId, bool showId)
        {
            string id = productId.ToString();
            string name = string.IsNullOrWhiteSpace(rawName)
                ? Plugin.T("Produkt", "Product")
                : rawName.Trim();

            string[] suffixes =
            {
                $" ID: {id}",
                $" ID:{id}",
                $" ID : {id}",
                $" (ID: {id})",
                $" [ID: {id}]"
            };

            for (int i = 0; i < suffixes.Length; i++)
            {
                string suffix = suffixes[i];
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffix.Length).TrimEnd();
                    break;
                }
            }

            if (string.Equals(name, $"Produkt #{id}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, $"Product #{id}", StringComparison.OrdinalIgnoreCase))
            {
                name = Plugin.T("Produkt", "Product");
            }

            return showId ? $"{name} ID: {id}" : name;
        }
        
        private void CycleSortMode()
        {
            if (_hubMode == HubMode.Stats)
            {
                // Cykl: Name(0) -> ProductId(1) -> SoldRevenue(2) -> ThrownValue(3) -> ThrownUnits(4)
                _statsSortMode = (StatsSortMode)(((int)_statsSortMode + 1) % 5);
            }
            else if (_hubMode == HubMode.Expiration)
            {
                // Cykl: Name(0) -> ProductId(1) -> NearestExpiry(2)
                _simpleSort = (SimpleSortMode)(((int)_simpleSort + 1) % 3);
            }
            else if (_hubMode == HubMode.Products)
            {
                if (_simpleSort == SimpleSortMode.Name) _simpleSort = SimpleSortMode.ProductId;
                else _simpleSort = SimpleSortMode.Name;
            }
            else if (_hubMode == HubMode.Analysis)
            {
                CycleAnalysisView();
            }

            UpdateSortLabel();
            BuildForHubMode(); // Rebuild okna
        }
        private void UpdateSortLabel()
        {
            if (_sortLabelTmp == null) return;

            string modeText = "";
            if (_hubMode == HubMode.Stats)
            {
                modeText = _statsSortMode switch
                {
                    StatsSortMode.SoldRevenue => Plugin.T("PRZYCHÓD", "REVENUE"),
                    StatsSortMode.SoldUnits => Plugin.T("SPRZEDANE", "SOLD"),
                    StatsSortMode.ThrownValue => Plugin.T("STRATA", "LOSS"),
                    StatsSortMode.ThrownUnits => Plugin.T("WYRZUCONE", "WASTED"),
                    StatsSortMode.Name => Plugin.T("NAZWA", "NAME"),
                    StatsSortMode.ProductId => "ID",
                    _ => _statsSortMode.ToString()
                };
            }
            else if (_hubMode == HubMode.Analysis)
            {
                modeText = GetAnalysisSortLabel();
            }
            else
            {
                modeText = _simpleSort switch
                {
                    SimpleSortMode.Name => Plugin.T("NAZWA", "NAME"),
                    SimpleSortMode.ProductId => "ID",
                    SimpleSortMode.NearestExpiry => Plugin.T("TERMIN", "EXPIRY"),
                    _ => _simpleSort.ToString()
                };
            }

            _sortLabelTmp.text = $"SORT: {modeText}";
            _sortDirTmp.text = _sortAsc ? "↑" : "↓";
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

        private TMPro.TextMeshProUGUI GetTmpComponent(Transform root, string path)
        {
            var tr = root.Find(path);
            return tr?.GetComponent<TMPro.TextMeshProUGUI>();
        }

        private void OnTogglePriceFilter()
        {
            ToggleFilterDropdown();
        }

        private void UpdateFilterButtonUI()
        {
            if (_filterAvailableLabel == null) return;

            var img = _filterAvailableBtn.GetComponent<Image>();
            if (_hubMode == HubMode.Analysis)
            {
                _filterAvailableLabel.text = $"{_analysisRangeDays} {Plugin.T("DNI", "DAYS")}";
                if (img != null) img.color = StatsAppTheme.Accent;
                return;
            }

            _filterAvailableLabel.text = _onlyWithPrice
                ? Plugin.T("DOSTĘPNE", "AVAILABLE")
                : Plugin.T("WSZYSTKO", "ALL");

            if (img != null)
            {
                img.color = _onlyWithPrice
                    ? StatsAppTheme.Accent
                    : StatsAppTheme.Button;
            }
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
        private void OnSearchValueChanged(string value)
        {
            _searchFilter = value ?? string.Empty;
            if (_hubMode == HubMode.Products || _hubMode == HubMode.Analysis)
                BuildForHubMode();
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

    public class FakeMonoBehaviour
    {
        public GameObject gameObject;
        public Transform transform => gameObject != null ? gameObject.transform : null;
        public T GetComponent<T>() => gameObject.GetComponent<T>();
        public T GetComponentInChildren<T>(bool includeInactive = false) => gameObject.GetComponentInChildren<T>(includeInactive);
        public T[] GetComponentsInChildren<T>(bool includeInactive = false) => gameObject.GetComponentsInChildren<T>(includeInactive);

        public static GameObject Instantiate(GameObject original, Transform parent, bool worldPositionStays) => UnityEngine.Object.Instantiate(original, parent, worldPositionStays);
        public static GameObject Instantiate(GameObject original, Transform parent) => UnityEngine.Object.Instantiate(original, parent);
        public static GameObject Instantiate(GameObject original) => UnityEngine.Object.Instantiate(original);
        public static void Destroy(UnityEngine.Object obj) => UnityEngine.Object.Destroy(obj);

        private readonly System.Collections.Generic.Dictionary<string, float> _invokes = new();
        private readonly System.Collections.Generic.Dictionary<string, float[]> _repeats = new();

        // ⚡ OCHRONA PAMIĘCI RAM: Nie będziemy tworzyć nowej listy setki razy na sekundę!
        private readonly System.Collections.Generic.List<string> _tempKeys = new();

        public void Invoke(string methodName, float time) => _invokes[methodName] = time;
        public void InvokeRepeating(string methodName, float time, float repeatRate) => _repeats[methodName] = new float[] { time, repeatRate };
        public void CancelInvoke(string methodName) { _invokes.Remove(methodName); _repeats.Remove(methodName); }
        public void CancelInvoke() { _invokes.Clear(); _repeats.Clear(); }

        public void TickTimers(float dt)
        {
            long __pf = SmartExpiration.SEProfiler.Begin();
            try {
            if (_invokes.Count > 0)
            {
                _tempKeys.Clear();
                _tempKeys.AddRange(_invokes.Keys);
                foreach (var k in _tempKeys)
                {
                    _invokes[k] -= dt;
                    if (_invokes[k] <= 0) { _invokes.Remove(k); ExecuteMethod(k); }
                }
            }

            if (_repeats.Count > 0)
            {
                _tempKeys.Clear();
                _tempKeys.AddRange(_repeats.Keys);
                foreach (var k in _tempKeys)
                {
                    _repeats[k][0] -= dt;
                    if (_repeats[k][0] <= 0) { _repeats[k][0] = _repeats[k][1]; ExecuteMethod(k); }
                }
            }
            } finally { SmartExpiration.SEProfiler.End("TickTimers", __pf); }
        }

        // PERF: cache MethodInfo - wczesniej GetMethod+Invoke lecial co klatke przez refleksje (drogie w IL2CPP).
        private static readonly System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo> _methodCache
            = new System.Collections.Generic.Dictionary<string, System.Reflection.MethodInfo>();

        private void ExecuteMethod(string methodName)
        {
            try
            {
                System.Reflection.MethodInfo method;
                if (!_methodCache.TryGetValue(methodName, out method))
                {
                    method = this.GetType().GetMethod(methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
                    _methodCache[methodName] = method; // cache'ujemy tez null, by nie szukac w kolko nieistniejacej metody
                }
                if (method != null) method.Invoke(this, null);
            }
            catch { }
        }
    }
}
