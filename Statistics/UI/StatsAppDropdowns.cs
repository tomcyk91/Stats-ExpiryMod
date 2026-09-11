using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace StatisticMod
{
    public partial class StatsAppManager
    {
        private enum HeaderDropdownKind
        {
            None = 0,
            HubMode = 1,
            Filter = 2,
            Sort = 3
        }

        private GameObject _headerDropdownRoot;
        private GameObject _headerDropdownPanel;
        private HeaderDropdownKind _openHeaderDropdown = HeaderDropdownKind.None;

        private static readonly StatsSortMode[] StatsSortDropdownValuesWithExpiry =
        {
            StatsSortMode.Name,
            StatsSortMode.ProductId,
            StatsSortMode.SoldRevenue,
            StatsSortMode.SoldUnits,
            StatsSortMode.ThrownValue,
            StatsSortMode.ThrownUnits
        };

        private static readonly StatsSortMode[] StatsSortDropdownValuesWithoutExpiry =
        {
            StatsSortMode.Name,
            StatsSortMode.ProductId,
            StatsSortMode.SoldRevenue,
            StatsSortMode.SoldUnits
        };

        private static StatsSortMode[] GetStatsSortDropdownValues()
        {
            return SmartExpiration.PluginConfig.ExpiryEnabled
                ? StatsSortDropdownValuesWithExpiry
                : StatsSortDropdownValuesWithoutExpiry;
        }


        private static readonly ProfitSortMode[] ProfitSortDropdownValues =
        {
            ProfitSortMode.GrossProfit,
            ProfitSortMode.Margin,
            ProfitSortMode.SoldRevenue,
            ProfitSortMode.SoldCost,
            ProfitSortMode.Name,
            ProfitSortMode.ProductId
        };

        private static ProfitSortMode[] GetProfitSortDropdownValues() => ProfitSortDropdownValues;

        private static readonly CategorySortMode[] CategorySortDropdownValues =
        {
            CategorySortMode.GrossProfit,
            CategorySortMode.SoldRevenue,
            CategorySortMode.Margin,
            CategorySortMode.MissedRevenue,
            CategorySortMode.Name
        };

        private static readonly IceCreamViewMode[] IceCreamViewDropdownValues =
        {
            IceCreamViewMode.Summary,
            IceCreamViewMode.Cones,
            IceCreamViewMode.Flavours,
            IceCreamViewMode.Combinations
        };

        private static readonly SimpleSortMode[] ExpirationSortDropdownValues =
        {
            SimpleSortMode.Name,
            SimpleSortMode.ProductId,
            SimpleSortMode.NearestExpiry
        };

        private static readonly SimpleSortMode[] ProductsSortDropdownValues =
        {
            SimpleSortMode.Name,
            SimpleSortMode.ProductId,
            SimpleSortMode.PriceBuy,
            SimpleSortMode.PriceSell,
            SimpleSortMode.TotalStock,
            SimpleSortMode.TotalValue
        };

        private static readonly AnalysisViewMode[] AnalysisSortDropdownValues =
        {
            AnalysisViewMode.Demand,
            AnalysisViewMode.MissedSales,
            AnalysisViewMode.Restock,
            AnalysisViewMode.Pricing,
            AnalysisViewMode.ExpiryRisk
        };

        private static readonly HubMode[] HubModeDropdownValuesWithExpiry =
        {
            HubMode.Stats,
            HubMode.Profitability,
            HubMode.Categories,
            HubMode.IceCream,
            HubMode.Customers,
            HubMode.Checkouts,
            HubMode.Expiration,
            HubMode.Products,
            HubMode.DailySummary,
            HubMode.Analysis,
            HubMode.Charts
        };

        private static readonly HubMode[] HubModeDropdownValuesWithoutExpiry =
        {
            HubMode.Stats,
            HubMode.Profitability,
            HubMode.Categories,
            HubMode.IceCream,
            HubMode.Customers,
            HubMode.Checkouts,
            HubMode.Products,
            HubMode.DailySummary,
            HubMode.Analysis,
            HubMode.Charts
        };

        private static HubMode[] GetHubModeDropdownValues()
        {
            return SmartExpiration.PluginConfig.ExpiryEnabled
                ? HubModeDropdownValuesWithExpiry
                : HubModeDropdownValuesWithoutExpiry;
        }

        /// <summary>
        /// Dodaje strzałkę i jednocześnie naprawia listener przycisku.
        /// Rebind jest wykonywany także wtedy, gdy strzałka już istnieje,
        /// dzięki czemu UI utworzone przez wcześniejszą wersję DLL nie zachowuje
        /// starego lub uszkodzonego onClick.
        /// </summary>
        private void AttachHeaderDropdownArrow(Button button, string objectName)
        {
            if (button == null) return;

            RebindHeaderDropdownButton(button);

            Transform existing = button.transform.Find(objectName);
            if (existing != null) return;

            var arrowGO = new GameObject(objectName);
            arrowGO.transform.SetParent(button.transform, false);

            var arrowRT = arrowGO.AddComponent<RectTransform>();
            arrowRT.anchorMin = new Vector2(1f, 0f);
            arrowRT.anchorMax = new Vector2(1f, 1f);
            arrowRT.pivot = new Vector2(1f, 0.5f);
            arrowRT.sizeDelta = new Vector2(18f, 0f);
            arrowRT.anchoredPosition = new Vector2(-2f, 0f);

            var arrow = arrowGO.AddComponent<TextMeshProUGUI>();
            arrow.text = "▼";
            arrow.alignment = TextAlignmentOptions.Center;
            arrow.fontSize = 9f;
            arrow.fontStyle = FontStyles.Bold;
            arrow.color = StatsAppTheme.TextLight;
            arrow.raycastTarget = false;
            if (_gameFont != null) arrow.font = _gameFont;
            SafeSetOutline(arrow, 0.12f);

            var labels = button.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < labels.Length; i++)
            {
                TextMeshProUGUI label = labels[i];
                if (label == null || label == arrow) continue;

                RectTransform rt = label.rectTransform;
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(4f, 0f);
                rt.offsetMax = new Vector2(-20f, 0f);
                label.enableWordWrapping = false;
                label.overflowMode = TextOverflowModes.Ellipsis;
                break;
            }
        }

        private void RebindHeaderDropdownButton(Button button)
        {
            if (button == null) return;

            // Każdy z tych przycisków ma tylko jedną funkcję. Usunięcie starych
            // listenerów zapobiega podwójnemu wywołaniu oraz zachowaniu callbacku
            // z poprzedniej wersji UI.
            button.onClick.RemoveAllListeners();
            button.interactable = true;

            if (button == _titleModeBtn)
            {
                button.onClick.AddListener((UnityAction)(() => ToggleHubModeDropdown()));
            }
            else if (button == _filterAvailableBtn)
            {
                button.onClick.AddListener((UnityAction)(() => ToggleFilterDropdown()));
            }
            else if (button == _sortModeBtn)
            {
                button.onClick.AddListener((UnityAction)(() => ToggleSortDropdown()));
            }
        }

        private void ToggleHubModeDropdown()
        {
            HideChartDropdown();

            if (IsHeaderDropdownOpen(HeaderDropdownKind.HubMode))
            {
                HideHeaderDropdown();
                return;
            }

            HubMode[] values = GetHubModeDropdownValues();
            OpenHeaderDropdown(_titleModeBtn, HeaderDropdownKind.HubMode, values.Length, 210f);
        }

        private void ToggleFilterDropdown()
        {
            if (_hubMode != HubMode.Products && _hubMode != HubMode.Analysis)
                return;

            HideChartDropdown();

            if (IsHeaderDropdownOpen(HeaderDropdownKind.Filter))
            {
                HideHeaderDropdown();
                return;
            }

            int count = _hubMode == HubMode.Analysis ? 3 : 2;
            OpenHeaderDropdown(_filterAvailableBtn, HeaderDropdownKind.Filter, count, 150f);
        }

        private void ToggleSortDropdown()
        {
            if (_hubMode == HubMode.Charts)
                return;

            HideChartDropdown();

            if (IsHeaderDropdownOpen(HeaderDropdownKind.Sort))
            {
                HideHeaderDropdown();
                return;
            }

            int count = GetSortDropdownOptionCount();
            if (count <= 0) return;

            OpenHeaderDropdown(_sortModeBtn, HeaderDropdownKind.Sort, count, 185f);
        }

        private bool IsHeaderDropdownOpen(HeaderDropdownKind kind)
        {
            return _openHeaderDropdown == kind &&
                   _headerDropdownRoot != null &&
                   _headerDropdownRoot.activeSelf;
        }

        /// <summary>
        /// Stabilny wariant listy: panel jest dzieckiem klikniętego przycisku,
        /// więc nie wymaga przeliczania współrzędnych między różnymi pivotami.
        /// Własny Canvas z overrideSorting umieszcza go ponad pozostałym UI,
        /// a GraphicRaycaster gwarantuje działanie przycisków opcji.
        /// </summary>
        private void OpenHeaderDropdown(Button anchorButton, HeaderDropdownKind kind, int optionCount, float minimumWidth)
        {
            if (anchorButton == null || optionCount <= 0)
                return;

            HideHeaderDropdown();
            Canvas.ForceUpdateCanvases();

            RectTransform anchorRT = anchorButton.GetComponent<RectTransform>();
            if (anchorRT == null) return;

            bool alignRight = kind == HeaderDropdownKind.Sort;
            float anchorWidth = Mathf.Max(1f, anchorRT.rect.width);
            float width = Mathf.Max(minimumWidth, anchorWidth);
            float height = 12f + optionCount * 36f;

            _headerDropdownRoot = new GameObject("HeaderDropdownRoot_" + kind);
            _headerDropdownRoot.transform.SetParent(anchorButton.transform, false);

            RectTransform rootRT = _headerDropdownRoot.AddComponent<RectTransform>();
            if (alignRight)
            {
                rootRT.anchorMin = new Vector2(1f, 0f);
                rootRT.anchorMax = new Vector2(1f, 0f);
                rootRT.pivot = new Vector2(1f, 1f);
            }
            else
            {
                rootRT.anchorMin = new Vector2(0f, 0f);
                rootRT.anchorMax = new Vector2(0f, 0f);
                rootRT.pivot = new Vector2(0f, 1f);
            }

            rootRT.anchoredPosition = new Vector2(0f, -2f);
            rootRT.sizeDelta = new Vector2(width, height);
            rootRT.localScale = Vector3.one;

            Canvas popupCanvas = _headerDropdownRoot.AddComponent<Canvas>();
            popupCanvas.overrideSorting = true;
            popupCanvas.sortingOrder = 32000;

            _headerDropdownRoot.AddComponent<GraphicRaycaster>();

            CanvasGroup group = _headerDropdownRoot.AddComponent<CanvasGroup>();
            group.alpha = 1f;
            group.interactable = true;
            group.blocksRaycasts = true;

            Image panelImage = _headerDropdownRoot.AddComponent<Image>();
            panelImage.color = StatsAppTheme.DropdownBackground;
            panelImage.raycastTarget = true;

            Outline outline = _headerDropdownRoot.AddComponent<Outline>();
            outline.effectColor = StatsAppTheme.HeaderBorder;
            outline.effectDistance = new Vector2(1f, -1f);

            VerticalLayoutGroup layout = _headerDropdownRoot.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(6, 6, 6, 6);
            layout.spacing = 3f;
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _headerDropdownPanel = _headerDropdownRoot;
            _openHeaderDropdown = kind;

            BuildHeaderDropdownOptions(kind, optionCount);

            _headerDropdownRoot.transform.SetAsLastSibling();
            _headerDropdownRoot.SetActive(true);
            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(rootRT);

            Plugin.DebugLog($"[StatsUI] Opened dropdown {kind}, options={optionCount}.");
        }
        private void BuildHeaderDropdownOptions(HeaderDropdownKind kind, int optionCount)
        {
            for (int i = 0; i < optionCount; i++)
            {
                string label = GetHeaderDropdownOptionLabel(kind, i);
                bool selected = IsHeaderDropdownOptionSelected(kind, i);
                CreateHeaderDropdownOption(kind, i, label, selected);
            }
        }

        private void CreateHeaderDropdownOption(HeaderDropdownKind kind, int optionIndex, string label, bool selected)
        {
            if (_headerDropdownPanel == null) return;

            var optionGO = new GameObject("Option_" + optionIndex);
            optionGO.transform.SetParent(_headerDropdownPanel.transform, false);

            var optionRT = optionGO.AddComponent<RectTransform>();
            optionRT.sizeDelta = new Vector2(0f, 33f);

            var layoutElement = optionGO.AddComponent<LayoutElement>();
            layoutElement.minHeight = 33f;
            layoutElement.preferredHeight = 33f;
            layoutElement.flexibleWidth = 1f;

            var image = optionGO.AddComponent<Image>();
            image.color = selected
                ? StatsAppTheme.DropdownSelected
                : StatsAppTheme.DropdownItem;

            var button = optionGO.AddComponent<Button>();
            button.transition = Selectable.Transition.ColorTint;
            ColorBlock colors = button.colors;
            colors.normalColor = image.color;
            colors.highlightedColor = selected
                ? StatsAppTheme.AccentHover
                : StatsAppTheme.ButtonHover;
            colors.pressedColor = StatsAppTheme.ButtonPressed;
            colors.selectedColor = colors.highlightedColor;
            button.colors = colors;

            HeaderDropdownKind capturedKind = kind;
            int capturedIndex = optionIndex;
            button.onClick.AddListener((UnityAction)(() =>
            {
                ApplyHeaderDropdownOption(capturedKind, capturedIndex);
            }));

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(optionGO.transform, false);

            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(10f, 0f);
            textRT.offsetMax = new Vector2(-10f, 0f);

            var text = textGO.AddComponent<TextMeshProUGUI>();
            text.text = selected ? "✓  " + label : "    " + label;
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.fontSize = 12f;
            text.fontStyle = selected ? FontStyles.Bold : FontStyles.Normal;
            text.color = StatsAppTheme.TextLight;
            text.enableAutoSizing = true;
            text.fontSizeMin = 9f;
            text.fontSizeMax = 12f;
            text.enableWordWrapping = false;
            text.overflowMode = TextOverflowModes.Ellipsis;
            text.raycastTarget = false;
            if (_gameFont != null) text.font = _gameFont;
            SafeSetOutline(text, 0.10f);
        }

        private string GetHeaderDropdownOptionLabel(HeaderDropdownKind kind, int optionIndex)
        {
            if (kind == HeaderDropdownKind.HubMode)
            {
                HubMode[] values = GetHubModeDropdownValues();
                if (optionIndex < 0 || optionIndex >= values.Length) return "?";

                HubMode mode = values[optionIndex];
                return mode switch
                {
                    HubMode.Stats => Plugin.T("STATYSTYKI", "STATISTICS"),
                    HubMode.Profitability => Plugin.T("RENTOWNOŚĆ", "PROFITABILITY"),
                    HubMode.Categories => Plugin.T("KATEGORIE", "CATEGORIES"),
                    HubMode.IceCream => Plugin.T("LODY", "ICE CREAM"),
                    HubMode.Customers => Plugin.T("KLIENCI", "CUSTOMERS"),
                    HubMode.Checkouts => Plugin.T("KASY", "CHECKOUTS"),
                    HubMode.Expiration => Plugin.T("TERMINY", "EXPIRATION"),
                    HubMode.Products => Plugin.T("PRODUKTY", "PRODUCTS"),
                    HubMode.DailySummary => Plugin.T("PODSUMOWANIE", "SUMMARY"),
                    HubMode.Analysis => Plugin.T("ANALIZA", "ANALYSIS"),
                    HubMode.Charts => Plugin.T("WYKRESY", "CHARTS"),
                    _ => "?"
                };
            }

            if (kind == HeaderDropdownKind.Filter)
            {
                if (_hubMode == HubMode.Analysis)
                {
                    int days = optionIndex == 0 ? 7 : optionIndex == 1 ? 14 : 30;
                    return days + " " + Plugin.T("DNI", "DAYS");
                }

                return optionIndex == 0
                    ? Plugin.T("TYLKO DOSTĘPNE", "AVAILABLE ONLY")
                    : Plugin.T("WSZYSTKIE PRODUKTY", "ALL PRODUCTS");
            }

            if (kind == HeaderDropdownKind.Sort)
                return GetSortDropdownOptionLabel(optionIndex);

            return "?";
        }

        private bool IsHeaderDropdownOptionSelected(HeaderDropdownKind kind, int optionIndex)
        {
            if (kind == HeaderDropdownKind.HubMode)
            {
                HubMode[] values = GetHubModeDropdownValues();
                return optionIndex >= 0 &&
                       optionIndex < values.Length &&
                       _hubMode == values[optionIndex];
            }

            if (kind == HeaderDropdownKind.Filter)
            {
                if (_hubMode == HubMode.Analysis)
                {
                    int days = optionIndex == 0 ? 7 : optionIndex == 1 ? 14 : 30;
                    return _analysisRangeDays == days;
                }

                return optionIndex == 0 ? _onlyWithPrice : !_onlyWithPrice;
            }

            if (kind == HeaderDropdownKind.Sort)
                return IsSortDropdownOptionSelected(optionIndex);

            return false;
        }

        private void ApplyHeaderDropdownOption(HeaderDropdownKind kind, int optionIndex)
        {
            HideHeaderDropdown();

            if (kind == HeaderDropdownKind.HubMode)
            {
                ApplyHubModeDropdownOption(optionIndex);
                return;
            }

            if (kind == HeaderDropdownKind.Filter)
            {
                ApplyFilterDropdownOption(optionIndex);
                return;
            }

            if (kind == HeaderDropdownKind.Sort)
                ApplySortDropdownOption(optionIndex);
        }

        private void ApplyHubModeDropdownOption(int optionIndex)
        {
            HubMode[] values = GetHubModeDropdownValues();
            if (optionIndex < 0 || optionIndex >= values.Length) return;
            SwitchToHubMode(values[optionIndex]);
        }

        private void SwitchToHubMode(HubMode selectedMode)
        {
            if (!SmartExpiration.PluginConfig.ExpiryEnabled && selectedMode == HubMode.Expiration)
                selectedMode = HubMode.Products;

            if (_hubMode == selectedMode)
            {
                RefreshNavigationTabs();
                return;
            }

            HideHeaderDropdown();
            HideChartDropdown();
            _hubMode = selectedMode;

            if (_hubMode == HubMode.Stats || _hubMode == HubMode.Profitability || _hubMode == HubMode.Categories || _hubMode == HubMode.IceCream || _hubMode == HubMode.Customers || _hubMode == HubMode.Checkouts)
            {
                int currentDay = GetCurrentDaySafe();

                // Kategorie domyślnie pokazują ostatni zakończony dzień.
                // Np. gdy gra jest na dniu 11, po wejściu w Kategorie od razu
                // zobaczymy zapisane statystyki z dnia 10.
                _selectedDay = (_hubMode == HubMode.Categories)
                    ? Mathf.Max(1, currentDay - 1)
                    : currentDay;

                RebuildDaysUI();
                UpdateDayLabel();
                if (_hubMode == HubMode.Profitability)
                {
                    _profitSortMode = ProfitSortMode.GrossProfit;
                    _sortAsc = false;
                }
                else if (_hubMode == HubMode.Categories)
                {
                    _categorySortMode = CategorySortMode.GrossProfit;
                    _categoryLiveSignature = int.MinValue;
                    _sortAsc = false;
                }
                else if (_hubMode == HubMode.IceCream)
                {
                    _iceCreamView = IceCreamViewMode.Summary;
                    _iceCreamLiveSignature = int.MinValue;
                    _sortAsc = false;
                }
            }
            else if (_hubMode == HubMode.Expiration)
            {
                _simpleSort = SimpleSortMode.NearestExpiry;
                _sortAsc = true;
            }
            else if (_hubMode == HubMode.Products)
            {
                _simpleSort = SimpleSortMode.Name;
                _sortAsc = true;
            }
            else if (_hubMode == HubMode.DailySummary)
            {
                EnterDailySummaryMode();
            }
            else if (_hubMode == HubMode.Analysis)
            {
                _analysisView = AnalysisViewMode.Demand;
                _analysisRangeDays = 7;
                _sortAsc = false;
            }

            RefreshTitleModeText();
            BuildForHubMode();
            RefreshHeaderForMode();
            RefreshNavigationTabs();
        }

        private void ApplyFilterDropdownOption(int optionIndex)
        {
            if (_hubMode == HubMode.Analysis)
            {
                _analysisRangeDays = optionIndex == 0 ? 7 : optionIndex == 1 ? 14 : 30;
                UpdateFilterButtonUI();
                BuildAnalysisTilesNow();
                return;
            }

            if (_hubMode == HubMode.Products)
            {
                _onlyWithPrice = optionIndex == 0;
                UpdateFilterButtonUI();
                BuildAllProductsTilesNow();
            }
        }

        private int GetSortDropdownOptionCount()
        {
            return _hubMode switch
            {
                HubMode.Stats => GetStatsSortDropdownValues().Length,
                HubMode.Profitability => ProfitSortDropdownValues.Length,
                HubMode.Categories => CategorySortDropdownValues.Length,
                HubMode.IceCream => IceCreamViewDropdownValues.Length,
                HubMode.Expiration => ExpirationSortDropdownValues.Length,
                HubMode.Products => ProductsSortDropdownValues.Length,
                HubMode.Analysis => AnalysisSortDropdownValues.Length,
                _ => 0
            };
        }

        private string GetSortDropdownOptionLabel(int optionIndex)
        {
            if (_hubMode == HubMode.Stats)
            {
                StatsSortMode[] values = GetStatsSortDropdownValues();
                if (optionIndex < 0 || optionIndex >= values.Length) return "?";
                return values[optionIndex] switch
                {
                    StatsSortMode.Name => Plugin.T("NAZWA", "NAME"),
                    StatsSortMode.ProductId => "ID",
                    StatsSortMode.SoldRevenue => Plugin.T("PRZYCHÓD", "REVENUE"),
                    StatsSortMode.SoldUnits => Plugin.T("SPRZEDANE", "SOLD"),
                    StatsSortMode.ThrownValue => Plugin.T("STRATA", "LOSS"),
                    StatsSortMode.ThrownUnits => Plugin.T("WYRZUCONE", "WASTED"),
                    _ => "?"
                };
            }

            if (_hubMode == HubMode.Profitability)
            {
                if (optionIndex < 0 || optionIndex >= ProfitSortDropdownValues.Length) return "?";
                return GetProfitSortLabel(ProfitSortDropdownValues[optionIndex]);
            }

            if (_hubMode == HubMode.Categories)
            {
                if (optionIndex < 0 || optionIndex >= CategorySortDropdownValues.Length) return "?";
                return GetCategorySortLabel(CategorySortDropdownValues[optionIndex]);
            }

            if (_hubMode == HubMode.IceCream)
            {
                if (optionIndex < 0 || optionIndex >= IceCreamViewDropdownValues.Length) return "?";
                return GetIceCreamViewLabel(IceCreamViewDropdownValues[optionIndex]);
            }

            if (_hubMode == HubMode.Expiration)
            {
                if (optionIndex < 0 || optionIndex >= ExpirationSortDropdownValues.Length) return "?";
                return ExpirationSortDropdownValues[optionIndex] switch
                {
                    SimpleSortMode.Name => Plugin.T("NAZWA", "NAME"),
                    SimpleSortMode.ProductId => "ID",
                    SimpleSortMode.NearestExpiry => Plugin.T("NAJBLIŻSZY TERMIN", "NEAREST EXPIRY"),
                    _ => "?"
                };
            }

            if (_hubMode == HubMode.Products)
            {
                if (optionIndex < 0 || optionIndex >= ProductsSortDropdownValues.Length) return "?";
                return ProductsSortDropdownValues[optionIndex] switch
                {
                    SimpleSortMode.Name => Plugin.T("NAZWA", "NAME"),
                    SimpleSortMode.ProductId => "ID",
                    SimpleSortMode.PriceBuy => Plugin.T("CENA ZAKUPU", "BUY PRICE"),
                    SimpleSortMode.PriceSell => Plugin.T("CENA SPRZEDAŻY", "SELL PRICE"),
                    SimpleSortMode.TotalStock => Plugin.T("ŁĄCZNY STAN", "TOTAL STOCK"),
                    SimpleSortMode.TotalValue => Plugin.T("WARTOŚĆ STANU", "STOCK VALUE"),
                    _ => "?"
                };
            }

            if (_hubMode == HubMode.Analysis)
            {
                if (optionIndex < 0 || optionIndex >= AnalysisSortDropdownValues.Length) return "?";
                return AnalysisSortDropdownValues[optionIndex] switch
                {
                    AnalysisViewMode.Demand => Plugin.T("POPYT", "DEMAND"),
                    AnalysisViewMode.MissedSales => Plugin.T("UTRACONA SPRZEDAŻ", "MISSED SALES"),
                    AnalysisViewMode.Restock => Plugin.T("UZUPEŁNIANIE", "RESTOCK"),
                    AnalysisViewMode.Pricing => Plugin.T("CENY", "PRICING"),
                    AnalysisViewMode.ExpiryRisk => Plugin.T("RYZYKO TERMINU", "EXPIRY RISK"),
                    _ => "?"
                };
            }

            return "?";
        }

        private bool IsSortDropdownOptionSelected(int optionIndex)
        {
            if (_hubMode == HubMode.Stats)
            {
                StatsSortMode[] values = GetStatsSortDropdownValues();
                return optionIndex >= 0 && optionIndex < values.Length &&
                       _statsSortMode == values[optionIndex];
            }

            if (_hubMode == HubMode.Profitability)
                return optionIndex >= 0 && optionIndex < ProfitSortDropdownValues.Length &&
                       _profitSortMode == ProfitSortDropdownValues[optionIndex];

            if (_hubMode == HubMode.Categories)
                return optionIndex >= 0 && optionIndex < CategorySortDropdownValues.Length &&
                       _categorySortMode == CategorySortDropdownValues[optionIndex];

            if (_hubMode == HubMode.IceCream)
                return optionIndex >= 0 && optionIndex < IceCreamViewDropdownValues.Length &&
                       _iceCreamView == IceCreamViewDropdownValues[optionIndex];

            if (_hubMode == HubMode.Expiration)
                return optionIndex >= 0 && optionIndex < ExpirationSortDropdownValues.Length &&
                       _simpleSort == ExpirationSortDropdownValues[optionIndex];

            if (_hubMode == HubMode.Products)
                return optionIndex >= 0 && optionIndex < ProductsSortDropdownValues.Length &&
                       _simpleSort == ProductsSortDropdownValues[optionIndex];

            if (_hubMode == HubMode.Analysis)
                return optionIndex >= 0 && optionIndex < AnalysisSortDropdownValues.Length &&
                       _analysisView == AnalysisSortDropdownValues[optionIndex];

            return false;
        }

        private void ApplySortDropdownOption(int optionIndex)
        {
            if (_hubMode == HubMode.Stats)
            {
                StatsSortMode[] values = GetStatsSortDropdownValues();
                if (optionIndex < 0 || optionIndex >= values.Length) return;
                _statsSortMode = values[optionIndex];
            }
            else if (_hubMode == HubMode.Profitability)
            {
                if (optionIndex < 0 || optionIndex >= ProfitSortDropdownValues.Length) return;
                _profitSortMode = ProfitSortDropdownValues[optionIndex];
            }
            else if (_hubMode == HubMode.Categories)
            {
                if (optionIndex < 0 || optionIndex >= CategorySortDropdownValues.Length) return;
                _categorySortMode = CategorySortDropdownValues[optionIndex];
            }
            else if (_hubMode == HubMode.IceCream)
            {
                if (optionIndex < 0 || optionIndex >= IceCreamViewDropdownValues.Length) return;
                _iceCreamView = IceCreamViewDropdownValues[optionIndex];
            }
            else if (_hubMode == HubMode.Expiration)
            {
                if (optionIndex < 0 || optionIndex >= ExpirationSortDropdownValues.Length) return;
                _simpleSort = ExpirationSortDropdownValues[optionIndex];
            }
            else if (_hubMode == HubMode.Products)
            {
                if (optionIndex < 0 || optionIndex >= ProductsSortDropdownValues.Length) return;
                _simpleSort = ProductsSortDropdownValues[optionIndex];
            }
            else if (_hubMode == HubMode.Analysis)
            {
                if (optionIndex < 0 || optionIndex >= AnalysisSortDropdownValues.Length) return;
                _analysisView = AnalysisSortDropdownValues[optionIndex];
            }
            else
            {
                return;
            }

            UpdateSortHeaderUI();
            RefreshSortButtonText();
            BuildForHubMode();
        }


        private void HideHeaderDropdown()
        {
            _openHeaderDropdown = HeaderDropdownKind.None;

            if (_headerDropdownRoot != null)
            {
                _headerDropdownRoot.SetActive(false);
                UnityEngine.Object.Destroy(_headerDropdownRoot);
            }

            _headerDropdownRoot = null;
            _headerDropdownPanel = null;
        }
        // ============================================================
        // Scalable tab navigation
        // ============================================================
        private enum NavigationSection
        {
            Sales = 0,
            Products = 1,
            Customers = 2,
            Analysis = 3
        }

        private GameObject _mainNavigationRoot;
        private TextMeshProUGUI _currentScreenTitleTmp;

        private void BuildNavigationTabs(Transform headerRoot)
        {
            if (headerRoot == null) return;

            var legacySubtitle = headerRoot.Find("BrandSubtitle");
            if (legacySubtitle != null)
                legacySubtitle.gameObject.SetActive(false);

            var brand = headerRoot.Find("Brand")?.GetComponent<RectTransform>();
            if (brand != null)
            {
                brand.anchorMin = new Vector2(0.02f, 0.52f);
                brand.anchorMax = new Vector2(0.31f, 0.94f);
            }

            // Only the four top-level sections stay in the header.
            // Individual modules are now rendered in a scalable scrollable
            // navigation panel on the left side of the app body.
            _mainNavigationRoot = CreateNavigationRoot(
                headerRoot, "MainNavigationTabs",
                new Vector2(0.33f, 0.54f), new Vector2(0.93f, 0.91f));

            EnsureCurrentScreenTitle(headerRoot);
            RefreshNavigationTabs();
        }

        private void EnsureCurrentScreenTitle(Transform headerRoot)
        {
            if (headerRoot == null) return;

            Transform existing = headerRoot.Find("CurrentScreenTitle");
            GameObject root;

            if (existing != null)
            {
                root = existing.gameObject;
                _currentScreenTitleTmp = root.GetComponentInChildren<TextMeshProUGUI>(true);
            }
            else
            {
                root = new GameObject("CurrentScreenTitle");
                root.transform.SetParent(headerRoot, false);
                root.AddComponent<CanvasRenderer>();

                var bg = root.AddComponent<Image>();
                bg.color = new Color32(34, 74, 95, 255);
                bg.raycastTarget = false;

                var outline = root.AddComponent<Outline>();
                outline.effectColor = new Color32(71, 135, 148, 230);
                outline.effectDistance = new Vector2(0f, -1f);

                var textGO = new GameObject("Text");
                textGO.transform.SetParent(root.transform, false);
                var rt = textGO.AddComponent<RectTransform>();
                rt.anchorMin = Vector2.zero;
                rt.anchorMax = Vector2.one;
                rt.offsetMin = new Vector2(8f, 0f);
                rt.offsetMax = new Vector2(-8f, 0f);

                _currentScreenTitleTmp = textGO.AddComponent<TextMeshProUGUI>();
                _currentScreenTitleTmp.alignment = TextAlignmentOptions.Center;
                _currentScreenTitleTmp.fontStyle = FontStyles.Bold;
                _currentScreenTitleTmp.fontSize = 9.5f;
                _currentScreenTitleTmp.enableAutoSizing = true;
                _currentScreenTitleTmp.fontSizeMin = 6.5f;
                _currentScreenTitleTmp.fontSizeMax = 9.5f;
                _currentScreenTitleTmp.enableWordWrapping = false;
                _currentScreenTitleTmp.overflowMode = TextOverflowModes.Ellipsis;
                _currentScreenTitleTmp.color = StatsAppTheme.TextLight;
                _currentScreenTitleTmp.raycastTarget = false;
                if (_gameFont != null) _currentScreenTitleTmp.font = _gameFont;
                SafeSetOutline(_currentScreenTitleTmp, 0.10f);
            }

            SetHeaderRect(root, 0.475f, 0.705f);
            root.transform.SetAsLastSibling();
            RefreshCurrentScreenTitle();
        }

        private void RefreshCurrentScreenTitle()
        {
            if (_currentScreenTitleTmp == null) return;
            _currentScreenTitleTmp.text =
                GetHubModeTabLabel(_hubMode);
        }

        private GameObject CreateNavigationRoot(Transform parent, string name, Vector2 min, Vector2 max)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            var rt = root.AddComponent<RectTransform>();
            rt.anchorMin = min;
            rt.anchorMax = max;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
            return root;
        }

        private void ApplyTabbedHeaderLayout()
        {
            if (_titleModeBtn != null)
                _titleModeBtn.gameObject.SetActive(false);

            // The second header row is now reserved entirely for context controls.
            // The old horizontal sub-tab row has been removed.
            SetHeaderRect(_filterAvailableBtn != null ? _filterAvailableBtn.gameObject : null, 0.020f, 0.130f);
            SetHeaderRect(_searchBarGO, 0.140f, 0.400f);

            // Day selector is deliberately left of center so it does not fight
            // with sorting controls on narrower resolutions.
            SetHeaderRect(_daySelectorGO, 0.210f, 0.460f);

            if (_currentScreenTitleTmp != null)
                SetHeaderRect(_currentScreenTitleTmp.transform.parent.gameObject, 0.475f, 0.705f);

            SetHeaderRect(_sortModeBtn != null ? _sortModeBtn.gameObject : null, 0.720f, 0.895f);
            SetHeaderRect(_sortDirBtn != null ? _sortDirBtn.gameObject : null, 0.902f, 0.940f);
        }

        private static void SetHeaderRect(GameObject go, float minX, float maxX)
        {
            if (go == null) return;
            var rt = go.GetComponent<RectTransform>();
            if (rt == null) return;
            rt.anchorMin = new Vector2(minX, 0.08f);
            rt.anchorMax = new Vector2(maxX, 0.43f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        private NavigationSection GetNavigationSection(HubMode mode)
        {
            return mode switch
            {
                HubMode.Stats => NavigationSection.Sales,
                HubMode.Profitability => NavigationSection.Sales,
                HubMode.Checkouts => NavigationSection.Sales,

                HubMode.Products => NavigationSection.Products,
                HubMode.Categories => NavigationSection.Products,
                HubMode.IceCream => NavigationSection.Products,
                HubMode.Expiration => NavigationSection.Products,

                HubMode.Customers => NavigationSection.Customers,

                HubMode.DailySummary => NavigationSection.Analysis,
                HubMode.Analysis => NavigationSection.Analysis,
                HubMode.Charts => NavigationSection.Analysis,
                _ => NavigationSection.Sales
            };
        }

        private HubMode GetDefaultHubModeForSection(NavigationSection section)
        {
            return section switch
            {
                NavigationSection.Sales => HubMode.Stats,
                NavigationSection.Products => HubMode.Products,
                NavigationSection.Customers => HubMode.Customers,
                NavigationSection.Analysis => HubMode.DailySummary,
                _ => HubMode.Stats
            };
        }

        private void RefreshNavigationTabs()
        {
            if (_mainNavigationRoot == null) return;
            RefreshCurrentScreenTitle();

            ClearNavigationChildren(_mainNavigationRoot.transform);

            NavigationSection selectedSection = GetNavigationSection(_hubMode);
            NavigationSection[] sections =
            {
                NavigationSection.Sales,
                NavigationSection.Products,
                NavigationSection.Customers,
                NavigationSection.Analysis
            };

            for (int i = 0; i < sections.Length; i++)
            {
                NavigationSection captured = sections[i];
                CreateNavigationButton(
                    _mainNavigationRoot.transform,
                    GetSectionLabel(captured),
                    captured == selectedSection,
                    i, sections.Length,
                    (UnityAction)(() => SwitchToHubMode(GetDefaultHubModeForSection(captured))),
                    true);
            }

            // The left sidebar can contain an arbitrary number of modules and
            // therefore no longer consumes horizontal header space.
            RefreshSidebarNavigation();
        }

        private HubMode[] GetSubModesForSection(NavigationSection section)
        {
            if (section == NavigationSection.Sales)
                return new[] { HubMode.Stats, HubMode.Profitability, HubMode.Checkouts };

            if (section == NavigationSection.Products)
            {
                if (SmartExpiration.PluginConfig.ExpiryEnabled)
                    return new[] { HubMode.Products, HubMode.Categories, HubMode.IceCream, HubMode.Expiration };
                return new[] { HubMode.Products, HubMode.Categories, HubMode.IceCream };
            }

            if (section == NavigationSection.Customers)
                return new[] { HubMode.Customers };

            return new[] { HubMode.DailySummary, HubMode.Analysis, HubMode.Charts };
        }

        private string GetSectionLabel(NavigationSection section)
        {
            return section switch
            {
                NavigationSection.Sales => Plugin.T("SPRZEDAŻ", "SALES"),
                NavigationSection.Products => Plugin.T("PRODUKTY", "PRODUCTS"),
                NavigationSection.Customers => Plugin.T("KLIENCI", "CUSTOMERS"),
                NavigationSection.Analysis => Plugin.T("ANALIZA", "ANALYSIS"),
                _ => string.Empty
            };
        }

        private string GetHubModeTabLabel(HubMode mode)
        {
            return mode switch
            {
                HubMode.Stats => Plugin.T("STATYSTYKI", "STATISTICS"),
                HubMode.Profitability => Plugin.T("RENTOWNOŚĆ", "PROFITABILITY"),
                HubMode.Categories => Plugin.T("KATEGORIE", "CATEGORIES"),
                HubMode.IceCream => Plugin.T("LODY", "ICE CREAM"),
                HubMode.Customers => Plugin.T("KLIENCI", "CUSTOMERS"),
                HubMode.Checkouts => Plugin.T("KASY", "CHECKOUTS"),
                HubMode.Expiration => Plugin.T("TERMINY", "EXPIRATION"),
                HubMode.Products => Plugin.T("PRODUKTY", "PRODUCTS"),
                HubMode.DailySummary => Plugin.T("PODSUMOWANIE", "SUMMARY"),
                HubMode.Analysis => Plugin.T("ANALIZA", "ANALYSIS"),
                HubMode.Charts => Plugin.T("WYKRESY", "CHARTS"),
                _ => string.Empty
            };
        }

        private void ClearNavigationChildren(Transform root)
        {
            if (root == null) return;
            for (int i = root.childCount - 1; i >= 0; i--)
            {
                var child = root.GetChild(i);
                if (child == null) continue;
                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }
        }

        private void CreateNavigationButton(
            Transform parent, string label, bool selected, int index, int count,
            UnityAction action, bool mainTab)
        {
            if (parent == null || count <= 0) return;

            const float gap = 0.008f;
            float slot = 1f / count;
            float left = index * slot + gap * 0.5f;
            float right = (index + 1) * slot - gap * 0.5f;

            var go = new GameObject((mainTab ? "MainTab_" : "SubTab_") + index);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(left, 0f);
            rt.anchorMax = new Vector2(right, 1f);
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;

            go.AddComponent<CanvasRenderer>();
            var image = go.AddComponent<Image>();

            // Main tabs and sub-tabs deliberately use different color families so
            // the hierarchy is obvious at a glance.
            Color normalColor;
            Color selectedColor;
            Color hoverColor;
            Color pressedColor;
            Color outlineColor;

            if (mainTab)
            {
                // Main section tabs: darker navy.
                normalColor = new Color32(28, 63, 92, 255);
                selectedColor = new Color32(34, 111, 157, 255);
                hoverColor = new Color32(39, 86, 122, 255);
                pressedColor = new Color32(22, 52, 77, 255);
                outlineColor = selected ? StatsAppTheme.AccentHover : StatsAppTheme.HeaderBorder;
            }
            else
            {
                // Sub-tabs: lighter teal/blue, visibly separate from the main row.
                normalColor = new Color32(42, 104, 118, 255);
                selectedColor = new Color32(50, 151, 171, 255);
                hoverColor = new Color32(52, 125, 142, 255);
                pressedColor = new Color32(32, 82, 95, 255);
                outlineColor = selected
                    ? new Color32(112, 211, 225, 255)
                    : new Color32(71, 135, 148, 230);
            }

            image.color = selected ? selectedColor : normalColor;
            image.raycastTarget = true;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = outlineColor;
            outline.effectDistance = selected ? new Vector2(0f, -2f) : new Vector2(0f, -1f);

            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = image.color;
            colors.highlightedColor = selected ? selectedColor : hoverColor;
            colors.pressedColor = pressedColor;
            colors.selectedColor = image.color;
            button.colors = colors;
            button.onClick.AddListener(action);

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);
            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(4f, 0f);
            textRT.offsetMax = new Vector2(-4f, 0f);

            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.fontStyle = FontStyles.Bold;
            tmp.fontSize = mainTab ? 10.5f : 9.5f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = mainTab ? 6.5f : 6f;
            tmp.fontSizeMax = mainTab ? 10.5f : 9.5f;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.color = StatsAppTheme.TextLight;
            tmp.raycastTarget = false;
            if (_gameFont != null) tmp.font = _gameFont;
            SafeSetOutline(tmp, 0.12f);
        }

    }
}
