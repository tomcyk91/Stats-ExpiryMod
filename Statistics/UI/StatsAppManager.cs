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
        private float _expirationTileHeight = StatsAppTheme.TileHeight;

        private TMP_FontAsset _gameFont;
        private Color _gameColor = Color.white;
        private bool _gameStyleCached;

        // Tooltip legend for compact one-letter labels in the Products view.
        private GameObject _productInfoTooltipGO;
        private RectTransform _productInfoTooltipRT;
        private TextMeshProUGUI _productInfoTooltipTitle;
        private TextMeshProUGUI _productInfoTooltipBody;

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
        private enum HubMode { Stats, Profitability, Categories, IceCream, Customers, Checkouts, Expiration, Products, DailySummary, Analysis, Charts }
        private HubMode _hubMode = HubMode.Stats;

        private GameObject _daySelectorGO;
        private StatsSortMode _statsSortMode = StatsSortMode.SoldRevenue;
        public enum ProductSortMode { Name, ID, StockTotal, StockShop, StockWarehouse, PriceBuy, PriceSell }

        private GameObject _searchBarGO;
        private TMPro.TMP_InputField _searchInputField;
        private string _searchFilter = "";


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
            if (!_isOpen || (_hubMode != HubMode.Stats && _hubMode != HubMode.Profitability && _hubMode != HubMode.Categories && _hubMode != HubMode.IceCream && _hubMode != HubMode.Customers && _hubMode != HubMode.Checkouts)) return;

            _refreshTimer += Time.deltaTime;
            if (_refreshTimer >= REFRESH_RATE)
            {
                _refreshTimer = 0f;
                if (_hubMode == HubMode.Profitability) RefreshProfitabilityLive();
                else if (_hubMode == HubMode.Categories) RefreshCategoryStatsLive();
                else if (_hubMode == HubMode.IceCream) RefreshIceCreamLive();
                else if (_hubMode == HubMode.Customers) RefreshCustomerBasketLive();
                else if (_hubMode == HubMode.Checkouts) RefreshCheckoutAnalyticsLive();
                else RefreshStatsLive();
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

            // Realtime refresh is needed for both sales statistics and profitability.
            if (_hubMode != HubMode.Stats && _hubMode != HubMode.Profitability && _hubMode != HubMode.Categories && _hubMode != HubMode.IceCream && _hubMode != HubMode.Customers && _hubMode != HubMode.Checkouts) return;

            if (Time.realtimeSinceStartup < _nextUiRefresh) return;
            _nextUiRefresh = Time.realtimeSinceStartup + 0.5f;

            EnsureSelectedDayInitialized();
            int current = GetCurrentDaySafe();
            if (_selectedDay != current) return;

            if (_hubMode == HubMode.Categories)
            {
                RefreshCategoryStatsLive();
                return;
            }

            if (_hubMode == HubMode.IceCream)
            {
                RefreshIceCreamLive();
                return;
            }

            if (_hubMode == HubMode.Customers)
            {
                RefreshCustomerBasketLive();
                return;
            }

            if (_hubMode == HubMode.Checkouts)
            {
                RefreshCheckoutAnalyticsLive();
                return;
            }

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

                if (_hubMode == HubMode.Profitability) UpdateProfitabilityTileText(infoTmp, p);
                else UpdateTileText(infoTmp, p);
            }

            for (int i = 0; i < ds.Products.Count; i++)
            {
                var p = ds.Products[i];
                bool visible = _hubMode == HubMode.Profitability
                    ? HasProfitabilityActivity(p)
                    : HasVisibleStatsActivity(p);
                if (!visible) continue;
                if (!_infoTmpByPid.ContainsKey(p.ProductId))
                {
                    needRebuild = true;
                    break;
                }
            }

            if (needRebuild) BuildForHubMode();
        }

        private PriceManager _priceManager;

        // UI implementation is split into focused StatsApp*.cs partial files.


        


        private void QueueBuildTiles()
        {
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
        public static void NotifyNewDay(int newDay)
        {
            var inst = _instance;
            if (inst == null) return;

            inst.OnNewDayDetected(newDay);
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


        private void BuildForHubMode()
        {
            HideProductInfoTooltip();

            // Expiry cards may grow to fit all batches; other views keep their normal height.
            if (_tilesContent != null && _expirationTileHeight != StatsAppTheme.TileHeight)
            {
                var grid = _tilesContent.GetComponent<GridLayoutGroup>();
                if (grid != null)
                    grid.cellSize = new Vector2(grid.cellSize.x, StatsAppTheme.TileHeight);
            }
            _expirationTileHeight = StatsAppTheme.TileHeight;

            if (!SmartExpiration.PluginConfig.ExpiryEnabled &&
                _hubMode == HubMode.Expiration)
            {
                _hubMode = HubMode.Stats;
            }

            if (!SmartExpiration.PluginConfig.ExpiryEnabled &&
                (_statsSortMode == StatsSortMode.ThrownValue || _statsSortMode == StatsSortMode.ThrownUnits))
            {
                _statsSortMode = StatsSortMode.SoldRevenue;
            }

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
            _daySelectorGO?.SetActive(_hubMode == HubMode.Stats || _hubMode == HubMode.Profitability || _hubMode == HubMode.Categories || _hubMode == HubMode.IceCream || _hubMode == HubMode.Customers || _hubMode == HubMode.Checkouts || _hubMode == HubMode.DailySummary);

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

                case HubMode.Profitability:
                    if (_titleTmp != null)
                        _titleTmp.text = Plugin.T("RENTOWNOŚĆ", "PROFITABILITY");
                    ExitChartsLayout();
                    BuildProfitabilityTiles();
                    break;

                case HubMode.Categories:
                    if (_titleTmp != null)
                        _titleTmp.text = Plugin.T("KATEGORIE", "CATEGORIES");
                    ExitChartsLayout();
                    BuildCategoryTiles();
                    break;

                case HubMode.IceCream:
                    if (_titleTmp != null)
                        _titleTmp.text = Plugin.T("LODY", "ICE CREAM");
                    ExitChartsLayout();
                    BuildIceCreamTiles();
                    break;

                case HubMode.Customers:
                    if (_titleTmp != null)
                        _titleTmp.text = Plugin.T("KLIENCI", "CUSTOMERS");
                    ExitChartsLayout();
                    BuildCustomerBasketTiles();
                    break;

                case HubMode.Checkouts:
                    if (_titleTmp != null)
                        _titleTmp.text = Plugin.T("KASY", "CHECKOUTS");
                    ExitChartsLayout();
                    BuildCheckoutAnalyticsTiles();
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
