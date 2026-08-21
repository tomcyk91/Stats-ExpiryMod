using PG;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace StatisticMod
{
    // StatsAppCharts.cs
    // Wymaga, żeby StatsAppManager był partial i miał pola/metody:
    // _tilesContent, _gameFont, ClearTilesOnly(), PolishButtonVisual(), GetProductNameSafe(int), StatsStore.TryGetDay/CurrentDay
    public partial class StatsAppManager
    {
        public enum Metric { SoldAmount, SoldRevenue, WasteAmount, WasteLoss }
        public enum AmountUnit { Units, Kg }

        // --- state ---
        private int _chartSelectedProductId = -1;
        private int _chartDaysRange = 14; // 7/14/30

        private Transform _chartProductListContent;
        private Transform _chartBarsContainer;

        private TMP_Text _chartTitleTmp;
        private TMP_Text _chartLegendTmp;
        // --- disable outer ScrollRect in chart mode (no scrolling) ---
        private bool _chartsOuterScrollSaved = false;
        private ScrollRect _chartsOuterScrollRect;
        private bool _chartsPrevVertical;
        private bool _chartsPrevHorizontal;
        private Scrollbar _chartsPrevVScrollbar;
        private bool _chartsPrevVScrollbarActive;

        private Transform _floatingRoot;

        private GameObject _productDropPanel;
        private GameObject _dropdownBlocker;
        private TextMeshProUGUI _productPickLabel;

        private Button _categoryCycleBtn;
        private TextMeshProUGUI _categoryCycleLabel;

        private readonly Dictionary<int, Image> _rangeBtnImages = new Dictionary<int, Image>();
        private readonly Dictionary<int, TextMeshProUGUI> _rangeBtnTexts = new Dictionary<int, TextMeshProUGUI>();

        private TMP_InputField _productSearchInput;
        private Image _legendSwatchImg;
        private TextMeshProUGUI _legendTextTmp;
        // aktualnie wybrana “kategoria” (metryka)
        private Metric _chartMetric = Metric.WasteLoss;

        // cykliczne przełączanie po klikaniu w przycisk “Kategoria”
        private readonly Metric[] _metricCycle = new[]
        {
            Metric.SoldAmount,   // Sprzedane (kg/szt)
            Metric.SoldRevenue,  // Wartość sprzedaży (zł)
            Metric.WasteAmount,  // Wyrzucone (kg/szt)
            Metric.WasteLoss     // Wartość straty (zł)
        };
        private int _metricCycleIndex = 0;



        // ==== helpers: layout / scroll ====
        private static void StretchToParent(GameObject go)
        {
            if (go == null) return;
            var rt = go.GetComponent<RectTransform>() ?? go.AddComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            rt.pivot = new Vector2(0.5f, 0.5f);
        }

        private void DisableTilesScrollForCharts()
        {
            try
            {
                // _tilesContent siedzi zwykle w ScrollRect (TilesScroll). Dla wykresów nie chcemy przewijania.
                var sr = _tilesContent?.GetComponentInParent<ScrollRect>();
                if (sr == null) return;

                sr.horizontal = false;
                sr.vertical = false;
                sr.movementType = ScrollRect.MovementType.Clamped;
                sr.inertia = false;

                sr.verticalScrollbar?.gameObject.SetActive(false);
                sr.horizontalScrollbar?.gameObject.SetActive(false);
            }
            catch { }
        }

        // ===== ENTRY =====
        private void DisableOuterScrollForCharts()
        {
            try
            {
                // _tilesContent is the content under a ScrollRect (TilesScroll) in your app window.
                var sr = _tilesContent?.GetComponentInParent<ScrollRect>();
                if (sr == null) return;

                if (!_chartsOuterScrollSaved)
                {
                    _chartsOuterScrollSaved = true;
                    _chartsOuterScrollRect = sr;
                    _chartsPrevVertical = sr.vertical;
                    _chartsPrevHorizontal = sr.horizontal;
                    _chartsPrevVScrollbar = sr.verticalScrollbar;
                    _chartsPrevVScrollbarActive = (sr.verticalScrollbar != null && sr.verticalScrollbar.gameObject.activeSelf);
                }

                sr.velocity = Vector2.zero;
                sr.vertical = false;
                sr.horizontal = false;
                sr.scrollSensitivity = 0f;

                sr.verticalScrollbar?.gameObject.SetActive(false);
            }
            catch { }
        }

        // Call this from other modes if you want the scroll back (stats/expiry/etc.).
        private void RestoreOuterScrollAfterCharts()
        {
            try
            {
                if (!_chartsOuterScrollSaved || _chartsOuterScrollRect == null) return;

                var sr = _chartsOuterScrollRect;
                sr.vertical = _chartsPrevVertical;
                sr.horizontal = _chartsPrevHorizontal;
                sr.scrollSensitivity = 30f;

                sr.verticalScrollbar?.gameObject.SetActive(_chartsPrevVScrollbarActive);
            }
            catch { }
        }

        private void BuildChartsWindow()
        {
            if (_tilesContent == null)
            {
                Plugin.Log.LogError("[CHARTS] _tilesContent is NULL — cannot build charts");
                return;
            }

            // =========================================================
            // WYMUSZENIE KATEGORII "SPRZEDANE" PRZY OTWARCIU WYKRESÓW
            // =========================================================
            _chartMetric = Metric.SoldAmount; // Ustawienie trybu na "Sprzedane"
            _metricCycleIndex = 0;            // Wyzerowanie licznika kliknięć
            _chartScope = ChartScope.Product;
            _summaryMetric = SummaryMetric.TotalCustomers;
            _summaryMetricCycleIndex = 0;
            // =========================================================

            // charts should NOT scroll
            DisableOuterScrollForCharts();
            try
            {
                var sr = _tilesContent.GetComponentInParent<ScrollRect>();
                if (sr != null)
                {
                    sr.vertical = false;
                    sr.horizontal = false;
                    sr.inertia = false;
                    sr.scrollSensitivity = 0f;
                }
            }
            catch { }

            ClearTilesOnly();

            _rangeBtnImages.Clear();
            _rangeBtnTexts.Clear();           

            // ROOT (pion)
            var rootGO = new GameObject("Charts_Root");
            rootGO.transform.SetParent(_tilesContent, false);

            var rtRoot = rootGO.AddComponent<RectTransform>();
            rtRoot.anchorMin = Vector2.zero;
            rtRoot.anchorMax = Vector2.one;
            rtRoot.offsetMin = Vector2.zero;
            rtRoot.offsetMax = Vector2.zero;

            var rootV = rootGO.AddComponent<VerticalLayoutGroup>();
            rootV.padding = new RectOffset(10, 10, 10, 18);
            rootV.spacing = 12; // Większy odstęp między wyszukiwarką a resztą
            rootV.childControlWidth = true;
            rootV.childControlHeight = true;
            rootV.childForceExpandWidth = true;
            rootV.childForceExpandHeight = false;

            // ===== TOP BAR (Rozszerzony) =====
            var topBar = new GameObject("TopBar");
            topBar.transform.SetParent(rootGO.transform, false);

            var leTop = topBar.AddComponent<LayoutElement>();
            leTop.preferredHeight = 180;   // Zwiększono z 100 na 180, aby pomieścić większe elementy
            leTop.flexibleHeight = 0;

            topBar.AddComponent<Image>().color = new Color(0, 0, 0, 0.15f);

            var topV = topBar.AddComponent<VerticalLayoutGroup>();
            topV.padding = new RectOffset(10, 10, 10, 10);
            topV.spacing = 10; // Większy odstęp między rzędami
            topV.childControlWidth = true;
            topV.childControlHeight = true;
            topV.childForceExpandWidth = true;
            topV.childForceExpandHeight = false;

            // Row1: [Produkt picker] | [Kategoria]
            var row1 = new GameObject("Row1");
            row1.transform.SetParent(topBar.transform, false);

            // ZWIĘKSZONO WYSOKOŚĆ RZĘDU 1 (Produkt i Kategoria)
            row1.AddComponent<LayoutElement>().preferredHeight = 55;

            var h1 = row1.AddComponent<HorizontalLayoutGroup>();
            h1.spacing = 12;
            h1.childControlWidth = true;
            h1.childControlHeight = true;
            h1.childForceExpandWidth = true;
            h1.childForceExpandHeight = true;

            // Left 0: przełącznik zakresu PRODUKT / SKLEP
            var scopeRoot = new GameObject("Left_Scope");
            scopeRoot.transform.SetParent(row1.transform, false);
            var leScope = scopeRoot.AddComponent<LayoutElement>();
            leScope.preferredWidth = 175;
            leScope.flexibleWidth = 0;
            scopeRoot.AddComponent<Image>().color = new Color(0f, 0f, 0.3774f, 1f);

            var scopeV = scopeRoot.AddComponent<VerticalLayoutGroup>();
            scopeV.childControlWidth = true;
            scopeV.childControlHeight = true;
            scopeV.childForceExpandWidth = true;
            scopeV.childForceExpandHeight = true;

            BuildChartScopeButton(scopeRoot.transform, height: 55);

            // Left: product picker
            var left = new GameObject("Left_Product");
            left.transform.SetParent(row1.transform, false);
            var leLeft = left.AddComponent<LayoutElement>();
            leLeft.flexibleWidth = 1;
            leLeft.minWidth = 360;
            left.AddComponent<Image>().color = new Color(0f, 0f, 0.3774f, 1f);
            _chartProductPickerRoot = left;

            var leftV = left.AddComponent<VerticalLayoutGroup>();
            leftV.childControlWidth = true;
            leftV.childControlHeight = true;
            leftV.childForceExpandWidth = true;
            leftV.childForceExpandHeight = true;

            // Right: category cycler
            var right = new GameObject("Right_Category");
            right.transform.SetParent(row1.transform, false);
            var leRight = right.AddComponent<LayoutElement>();
            leRight.preferredWidth = 240; // Nieco szerszy
            leRight.flexibleWidth = 0;
            right.AddComponent<Image>().color = new Color(0f, 0f, 0.3774f, 1f);

            var rightV = right.AddComponent<VerticalLayoutGroup>();
            rightV.childControlWidth = true;
            rightV.childControlHeight = true;
            rightV.childForceExpandWidth = true;
            rightV.childForceExpandHeight = true;

            // Budujemy elementy z nową wysokością 55
            BuildProductPicker(left.transform, pickerHeight: 55);
            BuildCategoryCycleButton(right.transform, height: 55);
            UpdateChartScopeVisuals();

            // Row2: range buttons (7 / 14 / 30 dni)
            var row2 = new GameObject("Row2");
            row2.transform.SetParent(topBar.transform, false);

            // ZWIĘKSZONO WYSOKOŚĆ RZĘDU 2 (Dni)
            row2.AddComponent<LayoutElement>().preferredHeight = 42;

            var rangeH = row2.AddComponent<HorizontalLayoutGroup>();
            rangeH.spacing = 8;
            rangeH.childForceExpandWidth = true;
            rangeH.childControlWidth = true;
            rangeH.childControlHeight = true;

            // W metodzie BuildChartsWindow()
            CreateRangeBtn(row2.transform, Plugin.T("7 dni", "7 days"), 7);
            CreateRangeBtn(row2.transform, Plugin.T("14 dni", "14 days"), 14);
            CreateRangeBtn(row2.transform, Plugin.T("21 dni", "21 days"), 21);

            // Title
            var titleGO = new GameObject("ChartTitle");
            titleGO.transform.SetParent(topBar.transform, false);
            var titleLE = titleGO.AddComponent<LayoutElement>();
            titleLE.preferredHeight = 35;
            titleLE.flexibleHeight = 0;

            _chartTitleTmp = titleGO.AddComponent<TextMeshProUGUI>();
            _chartTitleTmp.fontSize = 20; // Większa czcionka tytułu
            _chartTitleTmp.fontStyle = FontStyles.Bold;
            _chartTitleTmp.alignment = TextAlignmentOptions.Center;
            _chartTitleTmp.color = new Color(0f, 0f, 0.3774f, 1f);
            if (_gameFont != null) _chartTitleTmp.font = _gameFont;

            // Legend
            var legendGO = new GameObject("Legend");
            legendGO.transform.SetParent(topBar.transform, false);

            var leLeg = legendGO.AddComponent<LayoutElement>();
            leLeg.preferredHeight = 25; // Zwiększono wysokość legendy
            leLeg.flexibleHeight = 0;

            var legH = legendGO.AddComponent<HorizontalLayoutGroup>();
            legH.spacing = 10;
            legH.childAlignment = TextAnchor.MiddleCenter;
            legH.childControlWidth = false;
            legH.childControlHeight = true;
            legH.childForceExpandWidth = false;
            legH.childForceExpandHeight = false;
            legH.padding = new RectOffset(6, 6, 0, 0);

            var swatchGO = new GameObject("Swatch");
            swatchGO.transform.SetParent(legendGO.transform, false);
            var swRT = swatchGO.AddComponent<RectTransform>();
            swRT.sizeDelta = new Vector2(14, 14); // Nieco większy kwadrat

            _legendSwatchImg = swatchGO.AddComponent<Image>();
            _legendSwatchImg.color = Color.white;

            var txtGO = new GameObject("Text");
            txtGO.transform.SetParent(legendGO.transform, false);

            _legendTextTmp = txtGO.AddComponent<TextMeshProUGUI>();
            _legendTextTmp.fontSize = 14;
            _legendTextTmp.alignment = TextAlignmentOptions.Center;
            _legendTextTmp.color = new Color(0f, 0f, 0.3774f, 1f);
            _legendTextTmp.enableWordWrapping = false;
            if (_gameFont != null) _legendTextTmp.font = _gameFont;

            _chartLegendTmp = _legendTextTmp;

            // ===== CHART AREA =====
            var chartArea = new GameObject("ChartArea");
            chartArea.transform.SetParent(rootGO.transform, false);

            var leChart = chartArea.AddComponent<LayoutElement>();
            leChart.preferredHeight = 260f; // Skalujemy obszar wykresu, żeby zmieścić większą górę
            leChart.minHeight = 200f;
            leChart.flexibleHeight = 1f; // Pozwalamy wykresowi zająć resztę miejsca

            chartArea.AddComponent<Image>().color = new Color(0, 0, 0, 0.18f);

            var barsRoot = new GameObject("BarsRoot");
            barsRoot.transform.SetParent(chartArea.transform, false);

            var rtBars = barsRoot.AddComponent<RectTransform>();
            rtBars.anchorMin = new Vector2(0f, 0f);
            rtBars.anchorMax = new Vector2(1f, 0.90f);

            // Pass 3d: stały margines pod osią X. To jest w rdzeniu wykresu,
            // więc działa również po przełączeniu PRODUKT <-> SKLEP.
            rtBars.offsetMin = new Vector2(10, 18);
            rtBars.offsetMax = new Vector2(-10, -10);

            _chartBarsContainer = barsRoot.transform;

            UpdateChartHeader();
            UpdateLegendCategoryMode();

            // Odświeżenie layoutu
            LayoutRebuilder.ForceRebuildLayoutImmediate(rtRoot);
            Invoke(nameof(RefreshChart), 0.05f);
        }

        private void BuildProductPicker(Transform parent, int pickerHeight)
        {
            if (parent == null)
            {
                Plugin.Log.LogError("[PICKER] parent == null");
                return;
            }

            //
            // =============================
            // PRZYCISK WYBORU PRODUKTU
            // =============================
            //
            var btnGO = new GameObject("ProductPick");
            btnGO.transform.SetParent(parent, false);

            var le = btnGO.AddComponent<LayoutElement>();
            le.preferredHeight = pickerHeight;

            btnGO.AddComponent<Image>().color = new Color(0f, 0f, 0.3774f, 1f);
            StretchToParent(btnGO);

            var btn = btnGO.AddComponent<Button>();

            // Tekst
            var tGO = new GameObject("Text");
            tGO.transform.SetParent(btnGO.transform, false);

            _productPickLabel = tGO.AddComponent<TextMeshProUGUI>();
            _productPickLabel.fontSize = 12;
            _productPickLabel.color = Color.white;
            _productPickLabel.alignment = TextAlignmentOptions.Left;

            if (_gameFont != null)
                _productPickLabel.font = _gameFont;

            var tRT = _productPickLabel.GetComponent<RectTransform>();
            tRT.anchorMin = Vector2.zero;
            tRT.anchorMax = Vector2.one;
            tRT.offsetMin = new Vector2(10, 0);
            tRT.offsetMax = new Vector2(-10, 0);

            PolishButtonVisual(btn, true);

            //
            // =============================
            // FLOATING ROOT — nakładany UI dropdownu
            // =============================
            //
            if (_floatingRoot == null)
            {
                var fr = new GameObject("FloatingUIRoot");

                // KLUCZOWE — ten sam canvas co okno statystyk
                fr.transform.SetParent(_statsApp.transform, false);

                var rtFR = fr.AddComponent<RectTransform>();
                rtFR.anchorMin = Vector2.zero;
                rtFR.anchorMax = Vector2.one;
                rtFR.sizeDelta = Vector2.zero;
                rtFR.anchoredPosition = Vector2.zero;

                _floatingRoot = fr.transform;
                _floatingRoot.SetAsLastSibling();
            }


            //
            // =============================
            // BLOCKER
            // =============================
            //
            _dropdownBlocker = new GameObject("DropdownBlocker");
            _dropdownBlocker.transform.SetParent(_floatingRoot, false);
            var blockerRT = _dropdownBlocker.AddComponent<RectTransform>();
            blockerRT.anchorMin = Vector2.zero;
            blockerRT.anchorMax = Vector2.one;
            blockerRT.sizeDelta = Vector2.zero;
            
            var blockerImg = _dropdownBlocker.AddComponent<Image>();
            blockerImg.color = new Color(0, 0, 0, 0); // Niewidzialny
            
            var blockerBtn = _dropdownBlocker.AddComponent<Button>();
            blockerBtn.onClick.AddListener((UnityEngine.Events.UnityAction)(() =>
            {
                if (_productDropPanel != null) _productDropPanel.SetActive(false);
                if (_dropdownBlocker != null) _dropdownBlocker.SetActive(false);
            }));
            _dropdownBlocker.SetActive(false);

            //
            // =============================
            // PANEL DROPDOWN
            // =============================
            //
            _productDropPanel = new GameObject("ProductDropPanel");
            _productDropPanel.transform.SetParent(_floatingRoot, false);

            var pRT = _productDropPanel.AddComponent<RectTransform>();
            pRT.anchorMin = new Vector2(0, 1);
            pRT.anchorMax = new Vector2(0, 1);
            pRT.pivot = new Vector2(0, 1);

            // nowa, węższa szerokość
            pRT.sizeDelta = new Vector2(390, 260);

            // jasne tło jak UI gry (górne nagłówki, sort, itp.)
            var bg = _productDropPanel.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0.3774f, 1f);

            // elegancka ramka jak w UI gry
            var outline = _productDropPanel.AddComponent<Outline>();
            outline.effectColor = new Color(1f, 1f, 1f, 0.25f);
            outline.effectDistance = new Vector2(1, -1);

            // budujemy listę produktów
            BuildChartProductSelector(_productDropPanel.transform);

            _productDropPanel.SetActive(false);

            //
            // =============================
            // ZDARZENIE KLIK – otwarcie dropdownu
            // =============================
            //

            btn.onClick.AddListener((UnityAction)(() =>
            {
                bool show = !_productDropPanel.activeSelf;
                _productDropPanel.SetActive(show);
                if (_dropdownBlocker != null) _dropdownBlocker.SetActive(show);

                if (show)
                {
                    if (_floatingRoot != null) _floatingRoot.SetAsLastSibling();
                    // 1. Najpierw zbuduj/odśwież zawartość (wyszukiwarka + lista produktów)
                    BuildProductDropdownContent();

                    // 2. Niezawodne ustawienie pozycji (SetParent trick)
                    var pRT = _productDropPanel.GetComponent<RectTransform>();
                    var btnRT = btn.GetComponent<RectTransform>();
                    
                    // a) Tymczasowo przypinamy do przycisku
                    pRT.SetParent(btnRT, false);
                    
                    // b) Ustawiamy idealnie pod spodem
                    pRT.anchorMin = new Vector2(0, 0); // lewy-dolny róg przycisku
                    pRT.anchorMax = new Vector2(0, 0);
                    pRT.pivot = new Vector2(0, 1);     // lewy-górny róg dropdownu
                    pRT.anchoredPosition = Vector2.zero; // przyklejone
                    
                    // c) Wracamy do _floatingRoot zachowując pozycję
                    pRT.SetParent(_floatingRoot, true);
                    
                    // d) Zabezpieczenie Z-axis i skali
                    var pos = pRT.localPosition;
                    pos.z = 0;
                    pRT.localPosition = pos;
                    pRT.localScale = Vector3.one;

                    // 3. Opcjonalnie: Automatycznie ustaw kursor w polu wyszukiwania
                    if (_productSearchInput != null)
                    {
                        _productSearchInput.ActivateInputField();
                        _productSearchInput.text = ""; // Czyść poprzednie wyszukiwanie przy otwarciu
                    }
                }
            }));

            UpdateProductPickLabel();
        }

        private void UpdateProductPickLabel()
        {
            if (_productPickLabel == null) return;

            if (_chartSelectedProductId <= 0)
            {
                _productPickLabel.text = Plugin.T("Produkt: (kliknij aby wybrać)", "Product: (click to select)");
                return;
            }

            string name = GetProductNameSafe(_chartSelectedProductId);
            _productPickLabel.text = $"Product: <color=#AAAAAA>[{_chartSelectedProductId}]</color> {name}";
        }

        private void BuildCategoryCycleButton(Transform parent, int height)
        {
            var btnGO = new GameObject("CategoryCycle");
            btnGO.transform.SetParent(parent, false);

            var le = btnGO.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            le.flexibleHeight = 0;

            btnGO.AddComponent<Image>().color = new Color(0f, 0f, 0.3774f, 1f);
            StretchToParent(btnGO);

            _categoryCycleBtn = btnGO.AddComponent<Button>();
            PolishButtonVisual(_categoryCycleBtn, true);

            var tGO = new GameObject("Text");
            tGO.transform.SetParent(btnGO.transform, false);
            _categoryCycleLabel = tGO.AddComponent<TextMeshProUGUI>();
            _categoryCycleLabel.fontSize = 12;
            _categoryCycleLabel.alignment = TextAlignmentOptions.Center;
            _categoryCycleLabel.color = Color.white;
            if (_gameFont != null) _categoryCycleLabel.font = _gameFont;

            var rt = tGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            _categoryCycleBtn.onClick.AddListener((Action)(() =>
            {
                OnChartCategoryClicked();
            }));

            UpdateCategoryCycleLabel();
        }

        private string GetMetricLabel(Metric m)
        {
            return m switch
            {
                Metric.SoldAmount => Plugin.T("Sprzedane", "Sold"),
                Metric.SoldRevenue => Plugin.T("Wartość sprzedaży", "Sales Revenue"),
                Metric.WasteAmount => Plugin.T("Wyrzucone", "Wasted"),
                Metric.WasteLoss => Plugin.T("Wartość strat", "Wastage Loss"),
                _ => "—"
            };
        }

        private void UpdateCategoryCycleLabel()
        {
            if (_categoryCycleLabel == null) return;

            // Tłumaczenie etykiety "Kategoria" / "Category"
            string prefix = Plugin.T("Kategoria", "Category");

            _categoryCycleLabel.text = $"{prefix}: {GetActiveChartMetricLabel()}";
        }

        private void BuildChartProductSelector(Transform parent)
        {
            BuildChartLeftPanel(parent);
            RefreshChartProductList(_chartProductListContent);
        }

        private void BuildChartLeftPanel(Transform parent)
        {
            var vlg = parent.gameObject.GetComponent<VerticalLayoutGroup>() ?? parent.gameObject.AddComponent<VerticalLayoutGroup>();
            vlg.spacing = 6;
            vlg.padding = new RectOffset(6, 6, 6, 6);
            vlg.childControlWidth = true;
            vlg.childControlHeight = true;
            vlg.childForceExpandWidth = true;
            vlg.childForceExpandHeight = false;

            var scrollGO = new GameObject("ListScroll");
            scrollGO.transform.SetParent(parent, false);

            var leScroll = scrollGO.AddComponent<LayoutElement>();
            leScroll.flexibleHeight = 1;
            leScroll.minHeight = 80;

            scrollGO.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.28f);

            var scrollRect = scrollGO.AddComponent<ScrollRect>();
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.scrollSensitivity = 28f;

            var viewport = new GameObject("Viewport");
            viewport.transform.SetParent(scrollGO.transform, false);
            var rtViewport = viewport.AddComponent<RectTransform>();
            rtViewport.anchorMin = Vector2.zero;
            rtViewport.anchorMax = Vector2.one;
            rtViewport.offsetMin = Vector2.zero;
            rtViewport.offsetMax = Vector2.zero;

            viewport.AddComponent<Image>().color = new Color(1, 1, 1, 0.01f);
            var mask = viewport.AddComponent<Mask>();
            mask.showMaskGraphic = false;

            var content = new GameObject("Content");
            content.transform.SetParent(viewport.transform, false);
            var rtContent = content.AddComponent<RectTransform>();
            rtContent.anchorMin = new Vector2(0, 1);
            rtContent.anchorMax = new Vector2(1, 1);
            rtContent.pivot = new Vector2(0.5f, 1);
            rtContent.anchoredPosition = Vector2.zero;
            rtContent.sizeDelta = new Vector2(0, 0);

            var cVlg = content.AddComponent<VerticalLayoutGroup>();
            cVlg.padding = new RectOffset(4, 4, 4, 4);
            cVlg.spacing = 4;
            cVlg.childControlWidth = true;
            cVlg.childControlHeight = true;
            cVlg.childForceExpandWidth = true;
            cVlg.childForceExpandHeight = false;

            var fitter = content.AddComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
            fitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;

            scrollRect.viewport = rtViewport;
            scrollRect.content = rtContent;

            _chartProductListContent = content.transform;
        }

        private void RefreshChartProductList(Transform content)
        {
            if (content == null) return;

            for (int i = content.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(content.GetChild(i).gameObject);

            if (Plugin.ProductCache == null)
                return;

            if (Plugin.ProductCache.Count == 0)
            {
                var idm = UnityEngine.Object.FindFirstObjectByType<global::IDManager>();
                if (idm != null)
                    Plugin.ProductCache.Build(idm);
            }

            var allProducts = IDManager.Instance?.m_Products;
            if (allProducts == null || allProducts.Count == 0) return;

            foreach (var productSO in allProducts)
            {
                if (productSO == null) continue;

                int pid = 0;
                try { pid = productSO.ID; } catch { continue; }
                if (pid <= 0) continue;

                string pName = null;
                Sprite icon = null;

                if (!Plugin.ProductCache.TryGet(pid, out pName, out icon))
                {
                    try { pName = productSO.ComplexName(1f); } catch { }
                    if (string.IsNullOrWhiteSpace(pName))
                        pName = $"Unknown ID: {pid}";
                }

                CreateProductListButton(content, pName, pid, icon);
            }

            // Lody (ID 9999) są produktem syntetycznym i nie występują w IDManager.m_Products.
            // Dodajemy je ręcznie, aby były dostępne również na liście wyboru wykresu.
            if (Plugin.ProductCache.TryGet(9999, out var iceCreamName, out var iceCreamIcon))
                CreateProductListButton(content, iceCreamName, 9999, iceCreamIcon);
        }

        private void CreateProductListButton(Transform parent, string name, int id, Sprite icon)
        {
            // Sprawdzanie, czy produkt jest odblokowany.
            // ID 9999 = Lody ze stoiska; nie posiada natywnej licencji produktu.
            if (id != 9999 && !IsProductUnlocked(id))
            {
                return;  // Jeśli produkt nie jest odblokowany, pomijamy tworzenie przycisku
            }

            var btnGO = new GameObject("ListBtn_" + id);
            btnGO.transform.SetParent(parent, false);

            // Tło jak w PRODUKTY (ciemny granat)
            var img = btnGO.AddComponent<Image>();
            img.color = (id == _chartSelectedProductId)
                ? new Color(0f, 0f, 0.3774f, 1f)   // zaznaczony (jasnoniebieski)
                : new Color(0f, 0f, 0f, 0.28f);    // normalny

            var le = btnGO.AddComponent<LayoutElement>();
            le.minHeight = 26;

            // Przyciski
            var btn = btnGO.AddComponent<Button>();

            var colors = btn.colors;
            colors.normalColor = img.color;
            colors.highlightedColor = new Color(0f, 0f, 0.3774f, 1f);   // hover
            colors.pressedColor = new Color(0.18f, 0.28f, 0.45f, 1f);       // pressed
            colors.selectedColor = colors.highlightedColor;
            btn.colors = colors;

            btn.onClick.AddListener((UnityAction)(() =>
            {
                _chartSelectedProductId = id;

                UpdateProductPickLabel();
                _productDropPanel?.SetActive(false);
                if (_dropdownBlocker != null) _dropdownBlocker.SetActive(false);

                UpdateChartHeader();
                RefreshChart();
            }));

            // Ikona
            var iconGO = new GameObject("Icon");
            iconGO.transform.SetParent(btnGO.transform, false);
            var iconImg = iconGO.AddComponent<Image>();
            iconImg.sprite = icon; // Ustaw ikonę produktu
            iconImg.color = Color.white;  // Można dodać custom kolor jeśli chcesz

            // Ustawiamy rozmiar ikony
            var iconRT = iconGO.GetComponent<RectTransform>();
            iconRT.sizeDelta = new Vector2(20, 20); // Rozmiar ikony
            iconRT.anchorMin = new Vector2(0f, 0.5f);
            iconRT.anchorMax = new Vector2(0f, 0.5f);
            iconRT.anchoredPosition = new Vector2(10, 0); // Trochę odsunięte

            //
            // ===== LABEL (ID i NAME) =====
            //
            var textGO = new GameObject("Label");
            textGO.transform.SetParent(btnGO.transform, false);

            // Pobieramy nazwę z GetProductNameSafe w sposób identyczny jak w UpdateProductPickLabel
            string productName = GetProductNameSafe(id);

            var txt = textGO.AddComponent<TextMeshProUGUI>();
            txt.text = $"<color=#BBBBBB>[{id}]</color>  {productName}";
            txt.fontSize = 13;
            txt.fontStyle = FontStyles.Bold;
            txt.alignment = TextAlignmentOptions.Left;
            txt.color = Color.white;

            if (_gameFont != null)
                txt.font = _gameFont;

            var rt = txt.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0.5f);
            rt.anchorMax = new Vector2(1f, 0.5f);
            rt.offsetMin = new Vector2(30, 0); // Trochę większy margines od lewej, żeby ikona była widoczna
            rt.offsetMax = new Vector2(-8, 0); // Odsunięcie od prawej krawędzi

            PolishButtonVisual(btn, false);
        }
        private void BuildProductDropdownContent()
        {
            if (_chartProductListContent == null) return;

            for (int i = _chartProductListContent.childCount - 1; i >= 0; i--)
            {
                var child = _chartProductListContent.GetChild(i);
                if (child != null) UnityEngine.Object.Destroy(child.gameObject);
            }

            // Wyszukiwarka na górze
            CreateSearchInput(_chartProductListContent);

            if (Plugin.ProductCache == null)
                return;

            if (Plugin.ProductCache.Count == 0)
            {
                var idm = UnityEngine.Object.FindFirstObjectByType<global::IDManager>();
                if (idm != null)
                    Plugin.ProductCache.Build(idm);
            }

            if (Plugin.ProductCache.NameById == null || Plugin.ProductCache.NameById.Count == 0)
                return;

            foreach (var kvp in Plugin.ProductCache.NameById)
            {
                int productId = kvp.Key;

                if (!Plugin.ProductCache.TryGet(productId, out var productName, out var icon))
                {
                    productName = kvp.Value;
                    icon = null;
                }

                CreateProductListButton(_chartProductListContent, productName, productId, icon);
            }

            Canvas.ForceUpdateCanvases();
            if (_chartProductListContent.TryGetComponent<RectTransform>(out var rt))
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rt);
            }
        }

        private void CreateSearchInput(Transform parent)
        {
            var searchGO = new GameObject("SearchInput_Container");
            searchGO.transform.SetParent(parent, false);

            // Kluczowe dla VerticalLayoutGroup: LayoutElement
            var le = searchGO.AddComponent<LayoutElement>();
            le.minHeight = 45f;
            le.preferredHeight = 45f;
            le.flexibleWidth = 1f;

            var rt = searchGO.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(180, 45);

            // Tło wyszukiwarki
            var img = searchGO.AddComponent<Image>();
            img.color = new Color(0.1f, 0.1f, 0.1f, 0.95f);

            // Obiekt InputField
            _productSearchInput = searchGO.AddComponent<TMP_InputField>();

            // TextArea - obszar w którym wpisujemy tekst
            var textArea = new GameObject("TextArea");
            textArea.transform.SetParent(searchGO.transform, false);
            var textRT = textArea.AddComponent<RectTransform>();
            // Ustawiamy marginesy wewnątrz tła
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.sizeDelta = new Vector2(-20, -10); // 10px marginesu z każdej strony

            // Tekst wyświetlany
            var textDisplay = textArea.AddComponent<TextMeshProUGUI>();
            textDisplay.font = _gameFont;
            textDisplay.fontSize = 16;
            textDisplay.color = Color.yellow;
            textDisplay.alignment = TextAlignmentOptions.Left;
            textDisplay.verticalAlignment = VerticalAlignmentOptions.Middle;

            _productSearchInput.textViewport = textRT;
            _productSearchInput.textComponent = textDisplay;

            // Placeholder
            var placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(textArea.transform, false);
            var pText = placeholderGO.AddComponent<TextMeshProUGUI>();
            pText.text = Plugin.T("Szukaj produktu...", "Search product...");
            pText.font = _gameFont;
            pText.fontSize = 16;
            pText.color = new Color(0.6f, 0.6f, 0.6f, 0.5f);
            pText.alignment = TextAlignmentOptions.Left;
            pText.verticalAlignment = VerticalAlignmentOptions.Middle;

            _productSearchInput.placeholder = pText;

            // Podpięcie eventu
            _productSearchInput.onValueChanged.AddListener((Action<string>)FilterProductList);
                        
            // Wymuś odświeżenie po dodaniu
            LayoutRebuilder.ForceRebuildLayoutImmediate(parent.GetComponent<RectTransform>());
        }
                
        private void FilterProductList(string query)
        {
            if (_chartProductListContent == null) return;

            string term = query.ToLower().Trim();
            bool isQueryEmpty = string.IsNullOrEmpty(term);

            for (int i = 0; i < _chartProductListContent.childCount; i++)
            {
                Transform child = _chartProductListContent.GetChild(i);

                // i == 0 to nasza wyszukiwarka - jej nigdy nie ukrywamy!
                if (i == 0) continue;

                var textComp = child.GetComponentInChildren<TMP_Text>();
                if (textComp != null)
                {
                    // Sprawdzamy czy nazwa produktu zawiera szukaną frazę
                    bool matches = isQueryEmpty || textComp.text.ToLower().Contains(term);
                    child.gameObject.SetActive(matches);
                }
            }

            // Wymuszamy na Unity przeliczenie układu VerticalLayoutGroup
            LayoutRebuilder.ForceRebuildLayoutImmediate(_chartProductListContent.GetComponent<RectTransform>());
        }

        private void CreateRangeBtn(Transform parent, string label, int days)
        {
            var btnGO = new GameObject("Range_" + days);
            btnGO.transform.SetParent(parent, false);

            var le = btnGO.AddComponent<LayoutElement>();
            le.preferredHeight = 26;
            le.flexibleWidth = 1;

            var img = btnGO.AddComponent<Image>();

            var btn = btnGO.AddComponent<Button>();
            // Konfiguracja kolorów przejść (Transition), aby przycisk reagował na naciśnięcie
            btn.transition = Selectable.Transition.ColorTint;
            var colors = btn.colors;
            colors.pressedColor = new Color(0.5f, 0.5f, 0.5f, 1f); // Ciemniejszy przy naciśnięciu
            btn.colors = colors;

            btn.onClick.AddListener((Action)(() =>
            {
                _chartDaysRange = days;
                UpdateRangeButtonsVisual();
                RefreshChart();
            }));

            var tGO = new GameObject("Text");
            tGO.transform.SetParent(btnGO.transform, false);

            var tmp = tGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.fontSize = 10;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.color = Color.white;
            if (_gameFont != null) tmp.font = _gameFont;

            var rt = tGO.GetComponent<RectTransform>();
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            // Stylizacja wizualna
            PolishButtonVisual(btn, true);

            _rangeBtnImages[days] = img;
            _rangeBtnTexts[days] = tmp;

            UpdateRangeButtonsVisual();
        }

        private void UpdateRangeButtonsVisual()
        {
            foreach (var kv in _rangeBtnImages)
            {
                int days = kv.Key;
                var img = kv.Value;
                _rangeBtnTexts.TryGetValue(days, out var tmp);

                // Ujednolicony kolor bazowy dla wszystkich zdefiniowanych zakresów
                Color baseColor = new Color(0f, 0f, 0.3447f, 1f);

                // Dodano obsługę wartości 21, aby nie wpadała w kolor szary (default)
                bool isValidRange = (days == 7 || days == 14 || days == 21 || days == 30 || days == 28);
                if (!isValidRange)
                {
                    baseColor = new Color(0.65f, 0.65f, 0.65f, 1f);
                }

                bool active = (_chartDaysRange == days);

                // Ustawienie koloru obrazka z uwzględnieniem przezroczystości dla stanu aktywnego/nieaktywnego
                img.color = active
                    ? new Color(baseColor.r, baseColor.g, baseColor.b, 0.85f)
                    : new Color(baseColor.r, baseColor.g, baseColor.b, 0.35f);

                if (tmp != null)
                {
                    tmp.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
                }
            }
        }

        private void UpdateChartHeader()
        {
            if (_chartTitleTmp == null) return;
            _chartTitleTmp.text = GetChartHeaderText();
        }

        private void RefreshChart()
        {
            if (_chartBarsContainer == null) return;

            // 1. Czyszczenie starych słupków
            for (int i = _chartBarsContainer.childCount - 1; i >= 0; i--)
                UnityEngine.Object.Destroy(_chartBarsContainer.GetChild(i).gameObject);

            if (_chartScope == ChartScope.Store)
            {
                RefreshStoreSummaryChart();
                return;
            }

            // ZGODNIE Z ŻYCZENIEM: Pusty wykres na start
            if (_chartSelectedProductId <= 0) return;

            // 2. Pobranie jednostki
            AmountUnit unit = SafeGetUnit(_chartSelectedProductId);
            float weightFactor = 1.0f;
            if (SalesUnifiedFinal.WeightPerUnit != null && SalesUnifiedFinal.WeightPerUnit.TryGetValue(_chartSelectedProductId, out float w))
                weightFactor = w;

            // ✅ KLUCZOWE: Używamy TYLKO dnia z gry. Żadnych pętli szukających dnia w historii!
            int currentDay = StatsStore.CurrentDay;
            int fromDay = Math.Max(1, currentDay - (_chartDaysRange - 1));

            var days = new List<int>();
            var values = new List<float>();
            float maxAbs = 0.1f;

            var allDays = StatsStore.Data?.Days;

            // 4. Pętla pobierania danych
            for (int d = fromDay; d <= currentDay; d++)
            {
                float val = 0f;
                // Szukamy dokładnie tego numeru dnia w pamięci
                var dayData = allDays?.Find(x => x != null && x.Day == d);

                if (dayData != null && dayData.Products != null)
                {
                    var line = dayData.Products.Find(x => x != null && x.ProductId == _chartSelectedProductId);
                    if (line != null)
                    {
                        val = _chartMetric switch
                        {
                            Metric.SoldAmount => (unit == AmountUnit.Kg) ? (line.SoldWeightKg > 0.001f ? line.SoldWeightKg : (line.SoldUnits * weightFactor)) : (float)line.SoldUnits,
                            Metric.SoldRevenue => line.SoldRevenue,
                            Metric.WasteAmount => (unit == AmountUnit.Kg) ? (line.ThrownWeightKg > 0.001f ? line.ThrownWeightKg : (line.ThrownUnits * weightFactor)) : (float)line.ThrownUnits,
                            Metric.WasteLoss => line.ThrownValue,
                            _ => 0f
                        };
                    }
                }

                days.Add(d);
                values.Add(val);
                if (Mathf.Abs(val) > maxAbs) maxAbs = Mathf.Abs(val);
            }

            // 5. Rysowanie
            var rtC = _chartBarsContainer.GetComponent<RectTransform>();
            float groupW = rtC.rect.width / days.Count;
            float barW = Mathf.Clamp(groupW * 0.45f, 10f, 30f);

            for (int i = 0; i < days.Count; i++)
            {
                float xCenter = (i + 0.5f) * groupW;
                string lab = FormatMetricValue(_chartMetric, values[i], unit);
                DrawBar(xCenter, 28f, barW, (rtC.rect.height - 52f) * 0.7f, values[i], maxAbs, GetMetricColor(_chartMetric), lab);
                DrawDayLabel(xCenter, days[i], groupW);
            }

            UpdateLegendSingleMetric(unit);
        }


        private Color GetMetricColor(Metric m)
        {
            return m switch
            {
                Metric.SoldAmount => new Color(0.2f, 0.6f, 1f, 0.85f),
                Metric.SoldRevenue => new Color(0.25f, 0.85f, 0.25f, 0.85f),
                Metric.WasteAmount => new Color(1f, 0.35f, 0.35f, 0.85f),
                Metric.WasteLoss => new Color(1f, 0.55f, 0.15f, 0.85f),
                _ => Color.white
            };
        }

        private string FormatMetricValue(Metric m, float val, AmountUnit unit)
        {
            if (val <= 0.0001f) return "0";

            if (m == Metric.SoldRevenue || m == Metric.WasteLoss)
                return val.ToString("N2") + "$";

            if (unit == AmountUnit.Kg)
                return val.ToString("N3") + "kg";

            return val.ToString("N0") + Plugin.T("szt", "pcs");
        }

        private void DrawBar(float x, float y0, float w, float usableH, float value, float maxValue, Color col, string label)
        {
            float h = (Mathf.Abs(value) / Mathf.Max(0.0001f, maxValue)) * usableH;
            h = Mathf.Max(2f, h);

            var barGO = new GameObject("Bar");
            barGO.transform.SetParent(_chartBarsContainer, false);

            var img = barGO.AddComponent<Image>();
            img.color = col;

            var rt = barGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0.5f, 0);
            rt.anchoredPosition = new Vector2(x, y0);
            rt.sizeDelta = new Vector2(w, h);

            AddChartLabel(barGO.transform, label, new Vector2(0, h + 2f), Mathf.Max(70f, w * 6f), 8);
        }

        private void DrawDayLabel(float xCenter, int day, float groupW)
        {
            var dayGO = new GameObject("Day_" + day);
            dayGO.transform.SetParent(_chartBarsContainer, false);

            var t = dayGO.AddComponent<TextMeshProUGUI>();
            t.text = "D" + day;
            t.fontSize = 9;
            t.alignment = TextAlignmentOptions.Center;
            t.color = new Color(0f, 0f, 0.3774f, 1f);
            if (_gameFont != null) t.font = _gameFont;

            var rt = dayGO.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0, 0);
            rt.anchorMax = new Vector2(0, 0);
            rt.pivot = new Vector2(0.5f, 0);
            // Pass 3d: etykiety dni nie siedzą na dolnej krawędzi / nie są przycinane.
            rt.anchoredPosition = new Vector2(xCenter, 8f);
            rt.sizeDelta = new Vector2(groupW, 18f);
        }

        private void AddChartLabel(Transform parent, string text, Vector2 pos, float width, int fontSize)
        {
            var go = new GameObject("Label");
            go.transform.SetParent(parent, false);

            var txt = go.AddComponent<TextMeshProUGUI>();
            txt.text = text;

            // === AUTOMATYCZNY ROZMIAR TEKSTU ===
            txt.enableAutoSizing = true;
            txt.fontSizeMin = 4f;       // Minimalny czytelny rozmiar
            txt.fontSizeMax = fontSize; // Maksymalny rozmiar (z parametru, np. 8)
            txt.enableWordWrapping = false;

            txt.alignment = TextAlignmentOptions.Center;
            txt.color = new Color(0f, 0f, 0.3774f, 1f);
            if (_gameFont != null) txt.font = _gameFont;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0.5f, 0f);
            rt.anchorMax = new Vector2(0.5f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.anchoredPosition = pos;
            // Zwiększamy obszar dla Auto-Sizingu, żeby tekst miał gdzie "pracować"
            rt.sizeDelta = new Vector2(width, 25f);
        }

        private void UpdateLegendCategoryMode()
        {
            if (_legendTextTmp == null && _chartLegendTmp == null) return;

            if (_chartScope == ChartScope.Store)
            {
                UpdateStoreSummaryLegend();
                return;
            }

            Color col = GetMetricColor(_chartMetric);
            if (_legendSwatchImg != null) _legendSwatchImg.color = new Color(col.r, col.g, col.b, 0.95f);

            // bez kg/szt, bo w trybie kategorii jednostka zależy od produktów w kategorii
            string txt = $"{GetMetricLabel(_chartMetric)}";

            if (_legendTextTmp != null) _legendTextTmp.text = txt;
            if (_chartLegendTmp != null) _chartLegendTmp.text = txt;
        }

        private void UpdateLegendSingleMetric(AmountUnit unit)
        {
            if (_legendTextTmp == null && _chartLegendTmp == null) return;

            if (_chartSelectedProductId <= 0)
            {
                string emptyTxt = Plugin.T("Wybierz produkt z listy...", "Select a product from the list...");
                if (_legendTextTmp != null) _legendTextTmp.text = emptyTxt;
                if (_chartLegendTmp != null) _chartLegendTmp.text = emptyTxt;
                if (_legendSwatchImg != null) _legendSwatchImg.color = Color.clear;
                return;
            }

            float totalValue = 0f;
            int currentDay = StatsStore.CurrentDay;
            
            int fromDay = Math.Max(1, currentDay - (_chartDaysRange - 1));

            float weightFactor = 1.0f;
            if (SalesUnifiedFinal.WeightPerUnit != null && SalesUnifiedFinal.WeightPerUnit.TryGetValue(_chartSelectedProductId, out float w))
                weightFactor = w;

            // ZMIANA: Tu też szukamy po konkretnym dniu w głównej liście!
            var allDays = StatsStore.Data?.Days;

            for (int d = fromDay; d <= currentDay; d++)
            {
                var dayData = allDays?.Find(x => x != null && x.Day == d);
                if (dayData == null || dayData.Products == null) continue;

                var line = dayData.Products.Find(x => x != null && x.ProductId == _chartSelectedProductId);
                if (line == null) continue;

                totalValue += _chartMetric switch
                {
                    Metric.SoldAmount => (unit == AmountUnit.Kg) ? (line.SoldWeightKg > 0 ? line.SoldWeightKg : line.SoldUnits * weightFactor) : line.SoldUnits,
                    Metric.SoldRevenue => line.SoldRevenue,
                    Metric.WasteAmount => (unit == AmountUnit.Kg) ? (line.ThrownWeightKg > 0 ? line.ThrownWeightKg : line.ThrownUnits * weightFactor) : line.ThrownUnits,
                    Metric.WasteLoss => line.ThrownValue,
                    _ => 0f
                };
            }

            Color col = GetMetricColor(_chartMetric);
            if (_legendSwatchImg != null) _legendSwatchImg.color = new Color(col.r, col.g, col.b, 0.95f);

            string hexCol = _chartMetric switch
            {
                Metric.SoldAmount => "#3399FF",
                Metric.SoldRevenue => "#40D940",
                Metric.WasteAmount => "#FF5959",
                Metric.WasteLoss => "#FF8C26",
                _ => "#FFFFFF"
            };

            string metricName = _chartMetric switch
            {
                Metric.SoldAmount => Plugin.T("Sprzedane", "Sold"),
                Metric.SoldRevenue => Plugin.T("Wartość sprzedaży", "Sales Revenue"),
                Metric.WasteAmount => Plugin.T("Wyrzucone", "Wasted"),
                Metric.WasteLoss => Plugin.T("Wartość strat", "Wastage Loss"),
                _ => Plugin.T("Wynik", "Result")
            };

            string pcsLabel = Plugin.T("szt", "pcs");
            string unitStr = _chartMetric switch
            {
                Metric.SoldRevenue => "$",
                Metric.WasteLoss => "$",
                Metric.SoldAmount => (unit == AmountUnit.Kg ? "kg" : pcsLabel),
                Metric.WasteAmount => (unit == AmountUnit.Kg ? "kg" : pcsLabel),
                _ => ""
            };

            string formattedTotal = (_chartMetric == Metric.SoldRevenue || _chartMetric == Metric.WasteLoss || unit == AmountUnit.Kg)
                ? totalValue.ToString("N2")
                : totalValue.ToString("N0");

            string txt = $"{metricName} ({unitStr}): <color={hexCol}>{formattedTotal}</color>";

            if (_legendTextTmp != null) _legendTextTmp.text = txt;
            if (_chartLegendTmp != null) _chartLegendTmp.text = txt;
        }

        private string FormatAmount(float v, AmountUnit u)
        {
            return u == AmountUnit.Kg ? $"{v:0.000} kg" : $"{v:0} szt";
        }

        private string FormatMoney(float v)
        {
            return $"{v:0.00} zł";
        }

        private float SafeGetFloat(object line, string name, float fallbackA)
        {
            if (line == null) return fallbackA;

            // PANCERNE MAPOWANIE: Omija całkowicie błędy Reflection i niezgodności nazw!
            if (line is DayStats ds)
            {
                if (name.Contains("SoldAmount") || name.Contains("SoldUnits"))
                    return ds.SoldUnits + ds.SoldWeightKg;
                if (name.Contains("SoldRevenue"))
                    return ds.SoldRevenue;
                if (name.Contains("WasteAmount") || name.Contains("ThrownUnits"))
                    return ds.ThrownUnits + ds.ThrownWeightKg;
                if (name.Contains("WasteLoss") || name.Contains("ThrownValue") || name.Contains("ThrownRevenue"))
                    return ds.ThrownValue;
            }
            else if (line is ProductLine pl)
            {
                if (name.Contains("SoldAmount") || name.Contains("SoldUnits"))
                    return pl.SoldUnits + pl.SoldWeightKg;
                if (name.Contains("SoldRevenue"))
                    return pl.SoldRevenue;
                if (name.Contains("WasteAmount") || name.Contains("ThrownUnits"))
                    return pl.ThrownUnits + pl.ThrownWeightKg;
                if (name.Contains("WasteLoss") || name.Contains("ThrownValue") || name.Contains("ThrownRevenue"))
                    return pl.ThrownValue;
            }

            // Fallback do starego Reflection (w razie nietypowych zapytań)
            try
            {
                var t = line.GetType();
                var flags = System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

                var p = t.GetProperty(name, flags);
                if (p != null) return (float)Convert.ToDouble(p.GetValue(line));

                var f = t.GetField(name, flags);
                if (f != null) return (float)Convert.ToDouble(f.GetValue(line));
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[SafeGetFloat] Error for {name}: {e.Message}");
            }
            return fallbackA;
        }

        private AmountUnit SafeGetUnit(int productId)
        {
            // MEGA PANCERNE POBIERANIE JEDNOSTKI BEZ RYZYKA BŁĘDU (Silent Error)
            if (SalesUnifiedFinal.WeightPerUnit != null && SalesUnifiedFinal.WeightPerUnit.ContainsKey(productId))
            {
                return AmountUnit.Kg;
            }
            return AmountUnit.Units;
        }
    }
}
