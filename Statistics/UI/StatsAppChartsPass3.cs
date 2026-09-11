using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace StatisticMod
{
    /// <summary>
    /// Pass 3 dla widoku ANALIZA PRODUKTU / WYKRESY.
    /// Nie zastępuje StatsAppCharts.cs i nie rusza logiki danych.
    /// Po zbudowaniu istniejącego widoku tylko porządkuje jego layout i styl.
    /// </summary>
    public partial class StatsAppManager
    {
        private void ScheduleChartsPass3()
        {
            try
            {
                CancelInvoke(nameof(ApplyChartsPass3Delayed));
                // BuildChartsWindow odświeża słupki z lekkim opóźnieniem, więc skin nakładamy
                // chwilę później, kiedy hierarchy jest już kompletne.
                Invoke(nameof(ApplyChartsPass3Delayed), 0.10f);
            }
            catch
            {
                ApplyChartsPass3Delayed();
            }
        }

        private void ApplyChartsPass3Delayed()
        {
            if (_hubMode != HubMode.Charts || _tilesContent == null)
                return;

            try
            {
                Transform root = _tilesContent.Find("Charts_Root");
                if (root == null) return;

                StyleChartsRoot(root);
                StyleChartsTopBar(FindDeepChildPass3(root, "TopBar"));
                StyleChartsArea(FindDeepChildPass3(root, "ChartArea"));
                StyleChartsKnownControls();

                var rt = root.GetComponent<RectTransform>();
                if (rt != null)
                    LayoutRebuilder.ForceRebuildLayoutImmediate(rt);

                Canvas.ForceUpdateCanvases();
            }
            catch (Exception ex)
            {
                Plugin.DebugLog("[StatsUI Pass3] Charts style: " + ex.Message);
            }
        }

        private void StyleChartsRoot(Transform root)
        {
            var v = root.GetComponent<VerticalLayoutGroup>();
            if (v != null)
            {
                v.padding = new RectOffset(8, 8, 8, 12);
                v.spacing = 7f;
                v.childControlWidth = true;
                v.childControlHeight = true;
                v.childForceExpandWidth = true;
                v.childForceExpandHeight = false;
            }
        }

        private void StyleChartsTopBar(Transform topBar)
        {
            if (topBar == null) return;

            var le = topBar.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredHeight = 126f;
                le.minHeight = 116f;
                le.flexibleHeight = 0f;
            }

            var bg = topBar.GetComponent<Image>();
            if (bg != null)
            {
                bg.sprite = null;
                bg.material = null;
                bg.color = StatsAppTheme.ChartHeaderSurface;
            }

            var outline = topBar.GetComponent<Outline>();
            if (outline == null) outline = topBar.gameObject.AddComponent<Outline>();
            outline.effectColor = StatsAppTheme.Border;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;

            var v = topBar.GetComponent<VerticalLayoutGroup>();
            if (v != null)
            {
                v.padding = new RectOffset(8, 8, 6, 6);
                v.spacing = 5f;
            }

            Transform row1 = FindDeepChildPass3(topBar, "Row1");
            if (row1 != null)
            {
                var rowLe = row1.GetComponent<LayoutElement>();
                if (rowLe != null) rowLe.preferredHeight = 36f;

                var h = row1.GetComponent<HorizontalLayoutGroup>();
                if (h != null)
                {
                    h.spacing = 6f;
                    h.childControlWidth = true;
                    h.childControlHeight = true;
                    // WAŻNE: przy forceExpandWidth Unity potrafiło ścisnąć pierwszy blok
                    // "Widok: PRODUKT / SKLEP" praktycznie do zera. Szerokości są teraz
                    // kontrolowane przez LayoutElement poniżej.
                    h.childForceExpandWidth = false;
                    h.childForceExpandHeight = true;
                    h.childAlignment = TextAnchor.MiddleCenter;
                }

                // W Row1 są trzy kontrolki: WIDOK | PRODUKT | KATEGORIA.
                // Stylujemy wszystkie bez zakładania kolejności dzieci.
                for (int i = 0; i < row1.childCount; i++)
                    StyleChartsSelectorContainer(row1.GetChild(i));
            }

            Transform scope = FindDeepChildPass3(topBar, "Left_Scope");
            Transform left = FindDeepChildPass3(topBar, "Left_Product");
            Transform right = FindDeepChildPass3(topBar, "Right_Category");

            if (scope != null) scope.gameObject.SetActive(true);

            // STORE ma tylko dwa widoczne selektory: WIDOK + KATEGORIA.
            // Rozciągamy je na CAŁĄ szerokość i dzielimy dokładnie 50/50.
            bool productPickerVisible = left != null && left.gameObject.activeInHierarchy;

            if (!productPickerVisible)
            {
                SetChartSelectorWidth(scope, 0f, 0f, 1f);
                SetChartSelectorWidth(right, 0f, 0f, 1f);
            }
            else
            {
                // PRODUCT ma trzy kontrolki. Zachowujemy czytelny podział:
                // WIDOK kompaktowy, PRODUKT bierze środek, KATEGORIA ma stałe minimum.
                SetChartSelectorWidth(scope, 105f, 118f, 0f);
                SetChartSelectorWidth(left, 220f, 300f, 1f);
                SetChartSelectorWidth(right, 165f, 225f, 0f);
            }

            Transform row2 = FindDeepChildPass3(topBar, "Row2");
            if (row2 != null)
            {
                var rowLe = row2.GetComponent<LayoutElement>();
                if (rowLe != null) rowLe.preferredHeight = 27f;

                var h = row2.GetComponent<HorizontalLayoutGroup>();
                if (h != null) h.spacing = 6f;
            }

            Transform title = FindDeepChildPass3(topBar, "ChartTitle");
            if (title != null)
            {
                var titleLe = title.GetComponent<LayoutElement>();
                if (titleLe != null) titleLe.preferredHeight = 23f;
            }

            Transform legend = FindDeepChildPass3(topBar, "Legend");
            if (legend != null)
            {
                var legLe = legend.GetComponent<LayoutElement>();
                if (legLe != null) legLe.preferredHeight = 16f;

                var h = legend.GetComponent<HorizontalLayoutGroup>();
                if (h != null) h.spacing = 6f;
            }
        }

        private static void SetChartSelectorWidth(Transform container, float min, float preferred, float flexible)
        {
            if (container == null) return;

            var le = container.GetComponent<LayoutElement>();
            if (le == null) le = container.gameObject.AddComponent<LayoutElement>();

            le.minWidth = min;
            le.preferredWidth = preferred;
            le.flexibleWidth = flexible;
        }

        private void StyleChartsSelectorContainer(Transform container)
        {
            if (container == null) return;

            var img = container.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = null;
                img.material = null;
                img.color = StatsAppTheme.Button;
            }

            // Zmniejszamy wysokość przycisków z dawnego 55 px.
            var les = container.GetComponentsInChildren<LayoutElement>(true);
            for (int i = 0; i < les.Length; i++)
            {
                var le = les[i];
                if (le == null) continue;
                if (le.preferredHeight > 40f)
                    le.preferredHeight = 38f;
            }

            var tmps = container.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < tmps.Length; i++)
            {
                var tmp = tmps[i];
                if (tmp == null) continue;
                if (_gameFont != null) tmp.font = _gameFont;
                tmp.color = StatsAppTheme.TextLight;
                tmp.enableAutoSizing = true;
                tmp.fontSize = 10.5f;
                tmp.fontSizeMin = 7.5f;
                tmp.fontSizeMax = 10.5f;
                tmp.enableWordWrapping = false;
                tmp.overflowMode = TextOverflowModes.Ellipsis;
                SafeSetOutline(tmp, 0f);
            }
        }

        private void StyleChartsKnownControls()
        {
            if (_productPickLabel != null)
            {
                if (_gameFont != null) _productPickLabel.font = _gameFont;
                _productPickLabel.fontSize = 10.5f;
                _productPickLabel.enableAutoSizing = true;
                _productPickLabel.fontSizeMin = 7.5f;
                _productPickLabel.fontSizeMax = 10.5f;
                _productPickLabel.enableWordWrapping = false;
                _productPickLabel.overflowMode = TextOverflowModes.Ellipsis;
                _productPickLabel.color = StatsAppTheme.TextLight;
            }

            if (_categoryCycleLabel != null)
            {
                if (_gameFont != null) _categoryCycleLabel.font = _gameFont;
                _categoryCycleLabel.fontSize = 9.8f;
                _categoryCycleLabel.enableAutoSizing = true;
                _categoryCycleLabel.fontSizeMin = 7.2f;
                _categoryCycleLabel.fontSizeMax = 9.8f;
                _categoryCycleLabel.enableWordWrapping = false;
                _categoryCycleLabel.overflowMode = TextOverflowModes.Ellipsis;
                _categoryCycleLabel.color = StatsAppTheme.TextLight;
            }

            if (_chartTitleTmp != null)
            {
                if (_gameFont != null) _chartTitleTmp.font = _gameFont;
                _chartTitleTmp.fontSize = 15f;
                _chartTitleTmp.enableAutoSizing = true;
                _chartTitleTmp.fontSizeMin = 11.5f;
                _chartTitleTmp.fontSizeMax = 15f;
                _chartTitleTmp.fontStyle = FontStyles.Bold;
                _chartTitleTmp.enableWordWrapping = false;
                _chartTitleTmp.overflowMode = TextOverflowModes.Ellipsis;
                _chartTitleTmp.color = StatsAppTheme.TextDark;
                SafeSetOutline(_chartTitleTmp as TMP_Text, 0f);
            }

            if (_legendTextTmp != null)
            {
                if (_gameFont != null) _legendTextTmp.font = _gameFont;
                _legendTextTmp.fontSize = 10.5f;
                _legendTextTmp.enableWordWrapping = false;
                _legendTextTmp.color = StatsAppTheme.TextMuted;
                SafeSetOutline(_legendTextTmp, 0f);
            }

            if (_legendSwatchImg != null)
            {
                var rt = _legendSwatchImg.rectTransform;
                if (rt != null) rt.sizeDelta = new Vector2(9f, 9f);
            }

            // Przyciski 7/14/21 dni: neutralne nieaktywne + wyraźny aktywny.
            foreach (var kv in _rangeBtnImages)
            {
                int days = kv.Key;
                Image img = kv.Value;
                if (img == null) continue;

                bool active = days == _chartDaysRange;
                img.sprite = null;
                img.material = null;
                img.color = active ? StatsAppTheme.Accent : StatsAppTheme.ChartRangeInactive;

                Button btn = img.GetComponent<Button>();
                if (btn != null)
                {
                    var colors = btn.colors;
                    colors.normalColor = img.color;
                    colors.highlightedColor = active ? StatsAppTheme.AccentHover : StatsAppTheme.SurfaceAlt;
                    colors.pressedColor = StatsAppTheme.ButtonPressed;
                    colors.selectedColor = colors.highlightedColor;
                    colors.disabledColor = new Color(0.65f, 0.69f, 0.72f, 0.65f);
                    colors.colorMultiplier = 1f;
                    btn.colors = colors;
                }

                if (_rangeBtnTexts.TryGetValue(days, out TextMeshProUGUI tmp) && tmp != null)
                {
                    if (_gameFont != null) tmp.font = _gameFont;
                    tmp.fontSize = 9.5f;
                    tmp.fontStyle = active ? FontStyles.Bold : FontStyles.Normal;
                    tmp.color = active ? StatsAppTheme.TextLight : StatsAppTheme.TextDark;
                    tmp.enableWordWrapping = false;
                }
            }
        }

        private void StyleChartsArea(Transform chartArea)
        {
            if (chartArea == null) return;

            var le = chartArea.GetComponent<LayoutElement>();
            if (le != null)
            {
                le.preferredHeight = 235f;
                le.minHeight = 165f;
                le.flexibleHeight = 1f;
            }

            var img = chartArea.GetComponent<Image>();
            if (img != null)
            {
                img.sprite = null;
                img.material = null;
                img.color = StatsAppTheme.ChartBackground;
            }

            var outline = chartArea.GetComponent<Outline>();
            if (outline == null) outline = chartArea.gameObject.AddComponent<Outline>();
            outline.effectColor = StatsAppTheme.TileBorder;
            outline.effectDistance = new Vector2(1f, -1f);
            outline.useGraphicAlpha = false;

            if (_chartBarsContainer != null)
            {
                var rt = _chartBarsContainer.GetComponent<RectTransform>();
                if (rt != null)
                {
                    rt.anchorMin = new Vector2(0f, 0f);
                    rt.anchorMax = new Vector2(1f, 0.91f);
                    // Więcej miejsca pod osią X, żeby etykiety D1/D2/... nie były
                    // przycinane przez dolną krawędź viewportu.
                    rt.offsetMin = new Vector2(14f, 18f);
                    rt.offsetMax = new Vector2(-14f, -8f);
                }
            }
        }

        private static Transform FindDeepChildPass3(Transform parent, string exactName)
        {
            if (parent == null || string.IsNullOrEmpty(exactName)) return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child == null) continue;
                if (child.name == exactName) return child;

                Transform nested = FindDeepChildPass3(child, exactName);
                if (nested != null) return nested;
            }

            return null;
        }
    }
}
