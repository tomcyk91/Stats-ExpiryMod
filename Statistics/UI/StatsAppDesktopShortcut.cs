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

    }
}
