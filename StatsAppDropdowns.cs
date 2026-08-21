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
        private GameObject _headerDropdownBlocker;
        private GameObject _headerDropdownPanel;
        private HeaderDropdownKind _openHeaderDropdown = HeaderDropdownKind.None;

        private static readonly StatsSortMode[] StatsSortDropdownValues =
        {
            StatsSortMode.Name,
            StatsSortMode.ProductId,
            StatsSortMode.SoldRevenue,
            StatsSortMode.SoldUnits,
            StatsSortMode.ThrownValue,
            StatsSortMode.ThrownUnits
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
            AnalysisViewMode.Pricing
        };

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

            OpenHeaderDropdown(_titleModeBtn, HeaderDropdownKind.HubMode, 6, 210f);
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
            _headerDropdownBlocker = null;
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
                HubMode mode = (HubMode)optionIndex;
                return mode switch
                {
                    HubMode.Stats => Plugin.T("STATYSTYKI", "STATISTICS"),
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
                return (int)_hubMode == optionIndex;

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
            if (optionIndex < 0 || optionIndex > 5) return;

            HubMode selectedMode = (HubMode)optionIndex;
            if (_hubMode == selectedMode)
                return;

            HideChartDropdown();
            _hubMode = selectedMode;

            if (_hubMode == HubMode.Stats)
            {
                _selectedDay = GetCurrentDaySafe();
                RebuildDaysUI();
                UpdateDayLabel();
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
                HubMode.Stats => StatsSortDropdownValues.Length,
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
                if (optionIndex < 0 || optionIndex >= StatsSortDropdownValues.Length) return "?";
                return StatsSortDropdownValues[optionIndex] switch
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
                    _ => "?"
                };
            }

            return "?";
        }

        private bool IsSortDropdownOptionSelected(int optionIndex)
        {
            if (_hubMode == HubMode.Stats)
                return optionIndex >= 0 && optionIndex < StatsSortDropdownValues.Length &&
                       _statsSortMode == StatsSortDropdownValues[optionIndex];

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
                if (optionIndex < 0 || optionIndex >= StatsSortDropdownValues.Length) return;
                _statsSortMode = StatsSortDropdownValues[optionIndex];
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
            _headerDropdownBlocker = null;
        }
    }
}
