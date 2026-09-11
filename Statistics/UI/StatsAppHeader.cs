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
            tart.offsetMin = new Vector2(10f, 2f);
            tart.offsetMax = new Vector2(-10f, -2f);

            // TMP_InputField sam nie przycina dzieci do recta viewportu.
            // RectMask2D gwarantuje, że wpisywany tekst i placeholder nigdy
            // nie wyjdą poza prostokąt pola wyszukiwania.
            textArea.AddComponent<RectMask2D>();

            // Sam tekst wpisywany
            var textGO = new GameObject("Text");
            textGO.transform.SetParent(textArea.transform, false);
            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = Vector2.zero;
            textRT.offsetMax = Vector2.zero;

            var txt = textGO.AddComponent<TextMeshProUGUI>();
            txt.fontSize = 9.5f;
            txt.color = StatsAppTheme.TextDark;
            txt.alignment = TextAlignmentOptions.Left;
            txt.verticalAlignment = VerticalAlignmentOptions.Middle;
            txt.enableWordWrapping = false;
            txt.overflowMode = TextOverflowModes.Truncate;
            txt.raycastTarget = false;
            if (_gameFont != null) txt.font = _gameFont;

            // Placeholder (tekst pomocniczy)
            var placeholderGO = new GameObject("Placeholder");
            placeholderGO.transform.SetParent(textArea.transform, false);
            var placeholderRT = placeholderGO.AddComponent<RectTransform>();
            placeholderRT.anchorMin = Vector2.zero;
            placeholderRT.anchorMax = Vector2.one;
            placeholderRT.offsetMin = Vector2.zero;
            placeholderRT.offsetMax = Vector2.zero;

            var phTxt = placeholderGO.AddComponent<TextMeshProUGUI>();
            phTxt.text = Plugin.T("Szukaj nazwy lub ID...", "Search by name or ID");
            phTxt.fontSize = 8.5f;
            phTxt.fontStyle = FontStyles.Italic;
            phTxt.color = StatsAppTheme.TextMuted;
            phTxt.alignment = TextAlignmentOptions.Left;
            phTxt.verticalAlignment = VerticalAlignmentOptions.Middle;
            phTxt.enableWordWrapping = false;
            phTxt.overflowMode = TextOverflowModes.Ellipsis;
            phTxt.raycastTarget = false;
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

            // New scalable navigation: fixed tabs instead of one long mode dropdown.
            BuildNavigationTabs(header.transform);
            ApplyTabbedHeaderLayout();

            // ensure close on top
            closeGO.transform.SetAsLastSibling();
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

            if (_hubMode != HubMode.Stats && _hubMode != HubMode.Profitability && _hubMode != HubMode.Categories && _hubMode != HubMode.IceCream && _hubMode != HubMode.Customers && _hubMode != HubMode.Checkouts) return;
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

            if (_hubMode != HubMode.Stats && _hubMode != HubMode.Profitability && _hubMode != HubMode.Categories && _hubMode != HubMode.IceCream && _hubMode != HubMode.Customers && _hubMode != HubMode.Checkouts) return;
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

            bool hideSort = (_hubMode == HubMode.Charts || _hubMode == HubMode.DailySummary || _hubMode == HubMode.Customers || _hubMode == HubMode.Checkouts);
            _sortModeBtn?.gameObject.SetActive(!hideSort);
            _sortDirBtn?.gameObject.SetActive(!hideSort);
            if (hideSort) return;

            string label = "";

            if (_hubMode == HubMode.Stats)
            {
                label = GetSortLabel(_statsSortMode);
            }
            else if (_hubMode == HubMode.Profitability)
            {
                label = GetProfitSortLabel(_profitSortMode);
            }
            else if (_hubMode == HubMode.IceCream)
            {
                label = GetIceCreamViewLabel(_iceCreamView);
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
            _sortLabelTmp.text = _hubMode == HubMode.IceCream
                ? $"{Plugin.T("WIDOK", "VIEW")}: {label}"
                : $"{Plugin.T("SORT", "SORT")}: {label}";

            if (_sortDirTmp != null)
            {
                _sortDirTmp.text = _sortAsc ? "⬆" : "⬇";
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

        private void OnTitleModeClicked()
        {
            // Legacy title button is hidden by the tabbed header layout.
            // Keep this method for compatibility with older UI instances.
            RefreshNavigationTabs();
        }

        private void RefreshTitleModeText()
        {
            if (_titleTmp == null) return;

            _titleTmp.text = _hubMode switch
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
                _ => "STATS"
            };

            RefreshNavigationTabs();
        }

        private void RefreshHeaderForMode()
        {
            bool showDay = (_hubMode == HubMode.Stats || _hubMode == HubMode.Profitability || _hubMode == HubMode.Categories || _hubMode == HubMode.IceCream || _hubMode == HubMode.Customers || _hubMode == HubMode.Checkouts || _hubMode == HubMode.DailySummary);
            _daySelectorGO?.SetActive(showDay);

            // Wywołujemy zunifikowaną metodę, która wie, co wypisać w każdym trybie
            UpdateSortHeaderUI();
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
            else if (_hubMode == HubMode.Profitability)
            {
                modeText = GetProfitSortLabel(_profitSortMode);
            }
            else if (_hubMode == HubMode.Categories)
            {
                modeText = GetCategorySortLabel(_categorySortMode);
            }
            else if (_hubMode == HubMode.IceCream)
            {
                modeText = GetIceCreamViewLabel(_iceCreamView);
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

            _sortLabelTmp.text = _hubMode == HubMode.IceCream ? $"{Plugin.T("WIDOK", "VIEW")}: {modeText}" : $"SORT: {modeText}";
            _sortDirTmp.text = _sortAsc ? "⬆" : "⬇";
        }

        private void CycleSortMode()
        {
            if (_hubMode == HubMode.Stats)
            {
                StatsSortMode[] values = GetStatsSortDropdownValues();
                int currentIndex = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] == _statsSortMode)
                    {
                        currentIndex = i;
                        break;
                    }
                }

                _statsSortMode = values[(currentIndex + 1) % values.Length];
            }
            else if (_hubMode == HubMode.Profitability)
            {
                ProfitSortMode[] values = GetProfitSortDropdownValues();
                int currentIndex = 0;
                for (int i = 0; i < values.Length; i++)
                {
                    if (values[i] == _profitSortMode)
                    {
                        currentIndex = i;
                        break;
                    }
                }
                _profitSortMode = values[(currentIndex + 1) % values.Length];
            }
            else if (_hubMode == HubMode.Categories)
            {
                _categorySortMode = (CategorySortMode)(((int)_categorySortMode + 1) % 5);
            }
            else if (_hubMode == HubMode.IceCream)
            {
                _iceCreamView = (IceCreamViewMode)(((int)_iceCreamView + 1) % 4);
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
            else if (_hubMode == HubMode.Profitability)
            {
                modeText = GetProfitSortLabel(_profitSortMode);
            }
            else if (_hubMode == HubMode.Categories)
            {
                modeText = GetCategorySortLabel(_categorySortMode);
            }
            else if (_hubMode == HubMode.IceCream)
            {
                modeText = GetIceCreamViewLabel(_iceCreamView);
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

            _sortLabelTmp.text = _hubMode == HubMode.IceCream ? $"{Plugin.T("WIDOK", "VIEW")}: {modeText}" : $"SORT: {modeText}";
            _sortDirTmp.text = _sortAsc ? "↑" : "↓";
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

        private void OnSearchValueChanged(string value)
        {
            _searchFilter = value ?? string.Empty;
            if (_hubMode == HubMode.Products || _hubMode == HubMode.Analysis)
                BuildForHubMode();
        }

    }
}
