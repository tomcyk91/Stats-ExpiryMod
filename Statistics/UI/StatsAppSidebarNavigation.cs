using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;

namespace StatisticMod
{
    public partial class StatsAppManager
    {
        private GameObject _sidebarNavigationRoot;
        private GameObject _sidebarDrawerPanel;
        private GameObject _sidebarOutsideClickBlocker;
        private RectTransform _sidebarNavigationContent;
        private ScrollRect _sidebarNavigationScroll;
        private Button _sidebarToggleButton;
        private TextMeshProUGUI _sidebarToggleLabel;
        private bool _sidebarExpanded;

        private void EnsureSidebarNavigation()
        {
            if (_statsApp == null)
                return;

            if (_sidebarNavigationRoot != null &&
                _sidebarNavigationContent != null &&
                _sidebarToggleButton != null)
            {
                RefreshSidebarNavigation();
                return;
            }

            Transform existing = FindDirectChild(_statsApp.transform, "SidebarNavigation");
            if (existing != null)
            {
                existing.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(existing.gameObject);
            }

            var root = new GameObject("SidebarNavigation");
            root.transform.SetParent(_statsApp.transform, false);
            root.transform.SetAsLastSibling();
            _sidebarNavigationRoot = root;

            var rootRT = root.AddComponent<RectTransform>();
            rootRT.anchorMin = new Vector2(0.035f, StatsAppTheme.ContentBottom);
            rootRT.anchorMax = new Vector2(0.180f, StatsAppTheme.ContentTop);
            rootRT.offsetMin = Vector2.zero;
            rootRT.offsetMax = Vector2.zero;

            // Full-app transparent blocker. It sits behind the drawer and toggle,
            // but above the app content while the drawer is open. Clicking anywhere
            // outside the drawer closes it.
            var blockerGO = new GameObject("DrawerOutsideClickBlocker");
            blockerGO.transform.SetParent(_statsApp.transform, false);
            _sidebarOutsideClickBlocker = blockerGO;

            var blockerRT = blockerGO.AddComponent<RectTransform>();
            blockerRT.anchorMin = Vector2.zero;
            blockerRT.anchorMax = Vector2.one;
            blockerRT.offsetMin = Vector2.zero;
            blockerRT.offsetMax = Vector2.zero;

            blockerGO.AddComponent<CanvasRenderer>();
            var blockerImage = blockerGO.AddComponent<Image>();
            blockerImage.color = new Color(0f, 0f, 0f, 0.001f);
            blockerImage.raycastTarget = true;

            var blockerButton = blockerGO.AddComponent<Button>();
            blockerButton.transition = Selectable.Transition.None;
            blockerButton.onClick.AddListener((UnityAction)CloseSidebarNavigation);

            blockerGO.SetActive(false);

            // Drawer body (overlays tiles when expanded).
            var panel = new GameObject("DrawerPanel");
            panel.transform.SetParent(root.transform, false);
            panel.transform.SetAsFirstSibling();
            _sidebarDrawerPanel = panel;

            var panelRT = panel.AddComponent<RectTransform>();
            panelRT.anchorMin = Vector2.zero;
            panelRT.anchorMax = Vector2.one;
            panelRT.offsetMin = Vector2.zero;
            panelRT.offsetMax = Vector2.zero;

            panel.AddComponent<CanvasRenderer>();
            var panelImage = panel.AddComponent<Image>();
            panelImage.color = new Color32(27, 55, 73, 245);
            panelImage.raycastTarget = true;

            var panelOutline = panel.AddComponent<Outline>();
            panelOutline.effectColor = StatsAppTheme.Border;
            panelOutline.effectDistance = new Vector2(1f, -1f);

            var accent = new GameObject("TopAccent");
            accent.transform.SetParent(panel.transform, false);

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

            var scroll = panel.AddComponent<ScrollRect>();
            _sidebarNavigationScroll = scroll;
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Clamped;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 260f;

            var viewportGO = new GameObject("Viewport");
            viewportGO.transform.SetParent(panel.transform, false);

            var viewportRT = viewportGO.AddComponent<RectTransform>();
            viewportRT.anchorMin = Vector2.zero;
            viewportRT.anchorMax = Vector2.one;
            viewportRT.offsetMin = new Vector2(5f, 6f);
            viewportRT.offsetMax = new Vector2(-5f, -6f);

            viewportGO.AddComponent<CanvasRenderer>();
            var viewportImage = viewportGO.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportImage.raycastTarget = true;
            viewportGO.AddComponent<RectMask2D>();

            var contentGO = new GameObject("Content");
            contentGO.transform.SetParent(viewportGO.transform, false);

            var contentRT = contentGO.AddComponent<RectTransform>();
            _sidebarNavigationContent = contentRT;
            contentRT.anchorMin = new Vector2(0f, 1f);
            contentRT.anchorMax = new Vector2(1f, 1f);
            contentRT.pivot = new Vector2(0.5f, 1f);
            contentRT.anchoredPosition = Vector2.zero;
            contentRT.sizeDelta = Vector2.zero;

            scroll.viewport = viewportRT;
            scroll.content = contentRT;

            var sbGO = new GameObject("Scrollbar Vertical");
            sbGO.transform.SetParent(panel.transform, false);
            sbGO.transform.SetAsLastSibling();

            var sbRT = sbGO.AddComponent<RectTransform>();
            sbRT.anchorMin = new Vector2(1f, 0f);
            sbRT.anchorMax = new Vector2(1f, 1f);
            sbRT.pivot = new Vector2(1f, 1f);
            sbRT.sizeDelta = new Vector2(7f, 0f);
            sbRT.anchoredPosition = Vector2.zero;

            sbGO.AddComponent<CanvasRenderer>();
            var sbBg = sbGO.AddComponent<Image>();
            sbBg.color = new Color32(18, 39, 53, 220);

            var sb = sbGO.AddComponent<Scrollbar>();
            sb.direction = Scrollbar.Direction.BottomToTop;
            sb.numberOfSteps = 0;

            var slidingGO = new GameObject("Sliding Area");
            slidingGO.transform.SetParent(sbGO.transform, false);

            var slidingRT = slidingGO.AddComponent<RectTransform>();
            slidingRT.anchorMin = Vector2.zero;
            slidingRT.anchorMax = Vector2.one;
            slidingRT.offsetMin = new Vector2(1f, 2f);
            slidingRT.offsetMax = new Vector2(-1f, -2f);

            var handleGO = new GameObject("Handle");
            handleGO.transform.SetParent(slidingGO.transform, false);

            var handleRT = handleGO.AddComponent<RectTransform>();
            handleRT.anchorMin = Vector2.zero;
            handleRT.anchorMax = Vector2.one;
            handleRT.offsetMin = Vector2.zero;
            handleRT.offsetMax = Vector2.zero;

            handleGO.AddComponent<CanvasRenderer>();
            var handleImage = handleGO.AddComponent<Image>();
            handleImage.color = StatsAppTheme.ScrollThumb;

            sb.handleRect = handleRT;
            sb.targetGraphic = handleImage;

            scroll.verticalScrollbar = sb;
            scroll.verticalScrollbarVisibility =
                ScrollRect.ScrollbarVisibility.AutoHideAndExpandViewport;
            scroll.verticalScrollbarSpacing = -7f;

            // Toggle handle — always visible, even when drawer is collapsed.
            var toggleGO = new GameObject("DrawerToggle");
            // The toggle lives directly on the app root, not inside the drawer,
            // so it sits on the slim white strip at the far left and remains
            // visible whether the drawer is open or closed.
            toggleGO.transform.SetParent(_statsApp.transform, false);
            toggleGO.transform.SetAsLastSibling();

            var toggleRT = toggleGO.AddComponent<RectTransform>();
            float contentMidY = (StatsAppTheme.ContentBottom + StatsAppTheme.ContentTop) * 0.5f;
            toggleRT.anchorMin = new Vector2(0f, contentMidY);
            toggleRT.anchorMax = new Vector2(0f, contentMidY);
            toggleRT.pivot = new Vector2(1f, 0.5f);
            toggleRT.sizeDelta = new Vector2(11f, 88f);
            toggleRT.anchoredPosition = new Vector2(23f, 0f);

            toggleGO.AddComponent<CanvasRenderer>();
            var toggleImage = toggleGO.AddComponent<Image>();
            toggleImage.color = new Color32(38, 129, 183, 255);
            toggleImage.raycastTarget = true;

            var toggleOutline = toggleGO.AddComponent<Outline>();
            toggleOutline.effectColor = new Color32(130, 220, 245, 255);
            toggleOutline.effectDistance = new Vector2(1f, -1f);

            var toggleButton = toggleGO.AddComponent<Button>();
            _sidebarToggleButton = toggleButton;
            var toggleColors = toggleButton.colors;
            toggleColors.normalColor = toggleImage.color;
            toggleColors.highlightedColor = new Color32(52, 149, 201, 255);
            toggleColors.pressedColor = new Color32(26, 99, 144, 255);
            toggleColors.selectedColor = toggleImage.color;
            toggleColors.fadeDuration = 0.08f;
            toggleButton.colors = toggleColors;
            toggleButton.onClick.AddListener((UnityAction)ToggleSidebarNavigation);

            var toggleTextGO = new GameObject("Text");
            toggleTextGO.transform.SetParent(toggleGO.transform, false);

            var toggleTextRT = toggleTextGO.AddComponent<RectTransform>();
            toggleTextRT.anchorMin = Vector2.zero;
            toggleTextRT.anchorMax = Vector2.one;
            toggleTextRT.offsetMin = Vector2.zero;
            toggleTextRT.offsetMax = Vector2.zero;

            var toggleText = toggleTextGO.AddComponent<TextMeshProUGUI>();
            _sidebarToggleLabel = toggleText;
            toggleText.text = "▶";
            toggleText.alignment = TextAlignmentOptions.Center;
            toggleText.fontStyle = FontStyles.Bold;
            toggleText.fontSize = 10.5f;
            toggleText.enableWordWrapping = false;
            toggleText.overflowMode = TextOverflowModes.Overflow;
            toggleText.color = Color.white;
            toggleText.raycastTarget = false;
            if (_gameFont != null)
                toggleText.font = _gameFont;

            SafeSetOutline(toggleText, 0.15f);

            // Start collapsed so the tile area always keeps full width.
            _sidebarExpanded = false;
            ApplySidebarExpandedState();
            RefreshSidebarNavigation();
        }

        private void ToggleSidebarNavigation()
        {
            _sidebarExpanded = !_sidebarExpanded;
            ApplySidebarExpandedState();
        }

        private void CloseSidebarNavigation()
        {
            if (!_sidebarExpanded)
                return;

            _sidebarExpanded = false;
            ApplySidebarExpandedState();
        }

        private void ApplySidebarExpandedState()
        {
            if (_sidebarOutsideClickBlocker != null)
                _sidebarOutsideClickBlocker.SetActive(_sidebarExpanded);

            if (_sidebarDrawerPanel != null)
                _sidebarDrawerPanel.SetActive(_sidebarExpanded);

            if (_sidebarToggleLabel != null)
                _sidebarToggleLabel.text = _sidebarExpanded ? "◀" : "▶";

            // Keep the blocker behind the drawer, and the toggle above both.
            if (_sidebarOutsideClickBlocker != null && _sidebarExpanded)
                _sidebarOutsideClickBlocker.transform.SetAsLastSibling();

            if (_sidebarNavigationRoot != null)
                _sidebarNavigationRoot.transform.SetAsLastSibling();

            if (_sidebarToggleButton != null)
                _sidebarToggleButton.transform.SetAsLastSibling();
        }

        private void RefreshSidebarNavigation()
        {
            if (_sidebarNavigationRoot == null ||
                _sidebarNavigationContent == null)
            {
                return;
            }

            NavigationSection section = GetNavigationSection(_hubMode);
            HubMode[] modes = GetSubModesForSection(section);

            for (int i = _sidebarNavigationContent.childCount - 1; i >= 0; i--)
            {
                Transform child = _sidebarNavigationContent.GetChild(i);
                if (child == null) continue;

                child.gameObject.SetActive(false);
                UnityEngine.Object.Destroy(child.gameObject);
            }

            const float topPadding = 8f;
            const float buttonHeight = 39f;
            const float gap = 6f;

            for (int i = 0; i < modes.Length; i++)
            {
                HubMode captured = modes[i];
                CreateSidebarNavigationButton(
                    _sidebarNavigationContent,
                    GetHubModeTabLabel(captured),
                    captured == _hubMode,
                    i,
                    topPadding,
                    buttonHeight,
                    gap,
                    (UnityAction)(() => SwitchToHubMode(captured)));
            }

            float contentHeight =
                topPadding * 2f +
                modes.Length * buttonHeight +
                Mathf.Max(0, modes.Length - 1) * gap;

            Vector2 size = _sidebarNavigationContent.sizeDelta;
            size.y = Mathf.Max(1f, contentHeight);
            _sidebarNavigationContent.sizeDelta = size;

            if (_sidebarNavigationScroll != null &&
                _sidebarNavigationScroll.verticalNormalizedPosition < 0f)
            {
                _sidebarNavigationScroll.verticalNormalizedPosition = 1f;
            }

            ApplySidebarExpandedState();
        }

        private void CreateSidebarNavigationButton(
            RectTransform parent,
            string label,
            bool selected,
            int index,
            float topPadding,
            float height,
            float gap,
            UnityAction action)
        {
            if (parent == null)
                return;

            var go = new GameObject("SidebarModule_" + index);
            go.transform.SetParent(parent, false);

            var rt = go.AddComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(1f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(
                0f,
                -(topPadding + index * (height + gap)));

            go.AddComponent<CanvasRenderer>();

            Color normal = new Color32(34, 74, 95, 255);
            Color selectedColor = new Color32(33, 119, 157, 255);
            Color hover = new Color32(43, 98, 122, 255);
            Color pressed = new Color32(24, 61, 80, 255);

            var image = go.AddComponent<Image>();
            image.color = selected ? selectedColor : normal;
            image.raycastTarget = true;

            var outline = go.AddComponent<Outline>();
            outline.effectColor = selected
                ? new Color32(112, 211, 225, 255)
                : new Color32(49, 92, 112, 220);
            outline.effectDistance = new Vector2(0f, -1f);

            var button = go.AddComponent<Button>();
            var colors = button.colors;
            colors.normalColor = image.color;
            colors.highlightedColor = selected ? selectedColor : hover;
            colors.pressedColor = pressed;
            colors.selectedColor = image.color;
            colors.fadeDuration = 0.08f;
            button.colors = colors;
            button.onClick.AddListener(action);

            if (selected)
            {
                var activeBar = new GameObject("ActiveBar");
                activeBar.transform.SetParent(go.transform, false);

                var barRT = activeBar.AddComponent<RectTransform>();
                barRT.anchorMin = new Vector2(0f, 0f);
                barRT.anchorMax = new Vector2(0f, 1f);
                barRT.pivot = new Vector2(0f, 0.5f);
                barRT.sizeDelta = new Vector2(4f, 0f);
                barRT.anchoredPosition = Vector2.zero;

                activeBar.AddComponent<CanvasRenderer>();
                var barImage = activeBar.AddComponent<Image>();
                barImage.color = StatsAppTheme.AccentHover;
                barImage.raycastTarget = false;
            }

            var textGO = new GameObject("Text");
            textGO.transform.SetParent(go.transform, false);

            var textRT = textGO.AddComponent<RectTransform>();
            textRT.anchorMin = Vector2.zero;
            textRT.anchorMax = Vector2.one;
            textRT.offsetMin = new Vector2(selected ? 10f : 7f, 1f);
            textRT.offsetMax = new Vector2(-6f, -1f);

            var tmp = textGO.AddComponent<TextMeshProUGUI>();
            tmp.text = label;
            tmp.alignment = TextAlignmentOptions.MidlineLeft;
            tmp.fontStyle = FontStyles.Bold;
            tmp.fontSize = 9.5f;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMin = 5.5f;
            tmp.fontSizeMax = 9.5f;
            tmp.enableWordWrapping = false;
            tmp.overflowMode = TextOverflowModes.Ellipsis;
            tmp.color = StatsAppTheme.TextLight;
            tmp.raycastTarget = false;

            if (_gameFont != null)
                tmp.font = _gameFont;

            SafeSetOutline(tmp, 0.10f);
        }
    }
}
