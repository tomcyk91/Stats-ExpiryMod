using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using System.Linq;

namespace SmartExpiration.Patches
{
    [HarmonyPatch(typeof(SettingPriceCanvas))]
    internal static class PriceCanvasPatches
    {
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(SettingPriceCanvas), "OpenMenu") != null &&
                   AccessTools.Method(typeof(SettingPriceCanvas), "CloseMenu") != null;
        }

        public static GameObject ExpiryPanel;
        public static TMP_Text ExpiryTitleElement;
        public static TMP_Text ExpiryTextElement;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(SettingPriceCanvas.OpenMenu))]
        private static void OpenMenu_Postfix(SettingPriceCanvas __instance, PriceTag priceTag)
        {
            if (__instance == null || priceTag == null)
                return;

            // Panel tworzymy ZAWSZE.
            // Nie uzależniamy już jego istnienia od priceTag.DisplaySlot,
            // ponieważ po aktualizacji gry część PriceTagów może nie mieć
            // tego pola ustawionego w chwili OpenMenu.
            EnsureTextElement(__instance);

            if (ExpiryPanel == null || ExpiryTextElement == null)
                return;

            if (ExpiryTitleElement != null)
                ExpiryTitleElement.text =
                    StatisticMod.Plugin.T("TERMINY PRZYDATNOŚCI", "EXPIRATION DATES");

            // Najpierw natywna referencja, a jeśli jej nie ma,
            // próbujemy znaleźć DisplaySlot w hierarchii PriceTag.
            DisplaySlot slot = null;

            try
            {
                slot = priceTag.DisplaySlot;
            }
            catch
            {
                slot = null;
            }

            if (slot == null)
            {
                try
                {
                    slot = priceTag.GetComponentInParent<DisplaySlot>();
                }
                catch
                {
                    slot = null;
                }
            }

            if (slot == null)
            {
                // Najważniejsze: panel NIE ZNIKA.
                ExpiryTextElement.text =
                    $"<color=#BFD1D7>{StatisticMod.Plugin.T("Brak danych o terminie", "No expiration data")}</color>";

                UpdateExpiryFontSize(1);
                LayoutExpiryPanel(__instance);
                ExpiryPanel.SetActive(true);
                ExpiryPanel.transform.SetAsLastSibling();
                return;
            }

            int currentDay = DayCycleManager.Instance != null
                ? DayCycleManager.Instance.CurrentDay
                : 1;

            Dictionary<int, int> daysLeftCounts = new Dictionary<int, int>();

            try
            {
                var products = ExpirationSaveManager.GetSortedProducts(slot.transform);

                if (products != null)
                {
                    foreach (var p in products)
                    {
                        if (p == null)
                            continue;

                        var comp = p.GetComponent<ProductExpirationComponent>();
                        if (comp == null)
                            continue;

                        int dLeft = comp.ExpirationDay - currentDay;

                        if (!daysLeftCounts.ContainsKey(dLeft))
                            daysLeftCounts[dLeft] = 0;

                        daysLeftCounts[dLeft]++;
                    }
                }
            }
            catch
            {
                // UI ma zostać widoczne nawet jeśli odczyt danych terminu
                // nie powiedzie się dla nietypowego rodzaju półki.
            }

            if (daysLeftCounts.Count == 0)
            {
                ExpiryTextElement.text =
                    $"<color=#BFD1D7>{StatisticMod.Plugin.T("Brak danych o terminie", "No expiration data")}</color>";

                UpdateExpiryFontSize(1);
            }
            else
            {
                List<string> rows = new List<string>();

                foreach (var kvp in daysLeftCounts.OrderBy(k => k.Key))
                {
                    int dLeft = kvp.Key;
                    int count = kvp.Value;

                    string color =
                        dLeft <= 0 ? "#FF6B6B" :
                        dLeft == 1 ? "#FFB347" :
                        dLeft <= 3 ? "#FFD85A" :
                        "#68E875";

                    string dayString = dLeft == 1
                        ? StatisticMod.Plugin.T("dzień", "day")
                        : StatisticMod.Plugin.T("dni", "days");

                    string inString = StatisticMod.Plugin.T("za", "in");
                    string pcsString = StatisticMod.Plugin.T("szt.", "pcs.");

                    rows.Add(
                        $"<color={color}><b>{count} {pcsString}</b>  {inString} {dLeft} {dayString}</color>");
                }

                ExpiryTextElement.text = string.Join("\n", rows);
                UpdateExpiryFontSize(rows.Count);
            }

            LayoutExpiryPanel(__instance);

            ExpiryPanel.SetActive(true);
            ExpiryPanel.transform.SetAsLastSibling();
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(SettingPriceCanvas.CloseMenu))]
        private static void CloseMenu_Postfix()
        {
            if (ExpiryPanel != null)
                ExpiryPanel.SetActive(false);
        }

        private static void EnsureTextElement(SettingPriceCanvas canvas)
        {
            if (canvas == null)
                return;

            // To jest dokładnie host używany przez starą, działającą wersję.
            Transform host = canvas.transform;

            if (ExpiryPanel != null)
            {
                if (ExpiryPanel.transform.parent != host)
                    ExpiryPanel.transform.SetParent(host, false);

                ExpiryPanel.transform.SetAsLastSibling();
                LayoutExpiryPanel(canvas);
                return;
            }

            TMP_Text refText = null;

            try
            {
                refText = canvas.m_ProductName;
            }
            catch
            {
                refText = null;
            }

            if (refText == null)
                refText = canvas.GetComponentInChildren<TMP_Text>(true);

            if (refText == null)
                return;

            ExpiryPanel = new GameObject("ExpirationBackground");
            ExpiryPanel.transform.SetParent(host, false);
            ExpiryPanel.transform.SetAsLastSibling();

            Image bgImage = ExpiryPanel.AddComponent<Image>();

            // Kolor oparty o nowe ciemnoturkusowe UI okna ceny.
            bgImage.color = new Color32(2, 46, 57, 245);
            bgImage.raycastTarget = false;

            Outline outline = ExpiryPanel.AddComponent<Outline>();
            outline.effectColor = new Color32(8, 103, 125, 220);
            outline.effectDistance = new Vector2(2f, -2f);
            outline.useGraphicAlpha = true;

            // Górna linia łącząca wizualnie panel z oknem ceny.
            GameObject accentObj = new GameObject("TopAccent");
            accentObj.transform.SetParent(ExpiryPanel.transform, false);

            RectTransform accentRt = accentObj.AddComponent<RectTransform>();
            accentRt.anchorMin = new Vector2(0f, 1f);
            accentRt.anchorMax = new Vector2(1f, 1f);
            accentRt.pivot = new Vector2(0.5f, 1f);
            accentRt.anchoredPosition = Vector2.zero;
            accentRt.sizeDelta = new Vector2(0f, 4f);

            Image accent = accentObj.AddComponent<Image>();
            accent.color = new Color32(12, 105, 128, 255);
            accent.raycastTarget = false;

            GameObject titleObj = new GameObject("ExpirationTitle");
            titleObj.transform.SetParent(ExpiryPanel.transform, false);

            ExpiryTitleElement = titleObj.AddComponent<TextMeshProUGUI>();
            ApplyReferenceFont(ExpiryTitleElement, refText);
            ExpiryTitleElement.text =
                StatisticMod.Plugin.T("TERMINY PRZYDATNOŚCI", "EXPIRATION DATES");
            ExpiryTitleElement.fontSize = 25f;
            ExpiryTitleElement.fontStyle = FontStyles.Bold;
            ExpiryTitleElement.alignment = TextAlignmentOptions.Center;
            ExpiryTitleElement.color = new Color32(242, 250, 252, 255);
            ExpiryTitleElement.enableAutoSizing = true;
            ExpiryTitleElement.fontSizeMin = 18f;
            ExpiryTitleElement.fontSizeMax = 25f;
            ExpiryTitleElement.enableWordWrapping = false;
            ExpiryTitleElement.overflowMode = TextOverflowModes.Ellipsis;
            ExpiryTitleElement.raycastTarget = false;

            RectTransform titleRt = ExpiryTitleElement.rectTransform;
            titleRt.anchorMin = new Vector2(0.04f, 0.60f);
            titleRt.anchorMax = new Vector2(0.96f, 0.94f);
            titleRt.offsetMin = Vector2.zero;
            titleRt.offsetMax = Vector2.zero;

            GameObject textObj = new GameObject("ExpirationText");
            textObj.transform.SetParent(ExpiryPanel.transform, false);

            ExpiryTextElement = textObj.AddComponent<TextMeshProUGUI>();
            ApplyReferenceFont(ExpiryTextElement, refText);
            ExpiryTextElement.fontSize = 30f;
            ExpiryTextElement.fontStyle = FontStyles.Bold;
            ExpiryTextElement.alignment = TextAlignmentOptions.Center;
            ExpiryTextElement.richText = true;
            ExpiryTextElement.enableAutoSizing = true;
            ExpiryTextElement.fontSizeMin = 13f;
            ExpiryTextElement.fontSizeMax = 30f;
            ExpiryTextElement.enableWordWrapping = false;
            ExpiryTextElement.overflowMode = TextOverflowModes.Ellipsis;
            ExpiryTextElement.color = Color.white;
            ExpiryTextElement.raycastTarget = false;

            RectTransform textRt = ExpiryTextElement.rectTransform;
            textRt.anchorMin = new Vector2(0.04f, 0.08f);
            textRt.anchorMax = new Vector2(0.96f, 0.61f);
            textRt.offsetMin = Vector2.zero;
            textRt.offsetMax = Vector2.zero;

            LayoutExpiryPanel(canvas);
        }

        private static void UpdateExpiryFontSize(int rowCount)
        {
            if (ExpiryTextElement == null)
                return;

            // Im więcej różnych terminów, tym mniejszy tekst.
            // AutoSizing pozostaje włączony jako dodatkowe zabezpieczenie,
            // ale maksymalny rozmiar dobieramy już świadomie do liczby linii.
            float size;

            if (rowCount <= 1)
                size = 30f;
            else if (rowCount == 2)
                size = 27f;
            else if (rowCount == 3)
                size = 24f;
            else if (rowCount == 4)
                size = 21f;
            else if (rowCount == 5)
                size = 18f;
            else if (rowCount == 6)
                size = 16f;
            else
                size = 14f;

            ExpiryTextElement.fontSize = size;
            ExpiryTextElement.fontSizeMax = size;
            ExpiryTextElement.fontSizeMin = Mathf.Min(13f, size);
        }

        private static void ApplyReferenceFont(TMP_Text target, TMP_Text reference)
        {
            if (target == null || reference == null)
                return;

            target.font = reference.font;
            target.fontSharedMaterial = reference.fontSharedMaterial;
        }

        private static void LayoutExpiryPanel(SettingPriceCanvas canvas)
        {
            if (canvas == null || ExpiryPanel == null)
                return;

            RectTransform rt = ExpiryPanel.GetComponent<RectTransform>();
            if (rt == null)
                return;

            // CELOWO bez m_Menu, world corners, SetParent(true), itp.
            // To jest sprawdzony układ ze starej wersji, która faktycznie
            // pokazywała panel po aktualizacji gry.
            rt.localScale = Vector3.one;
            rt.anchorMin = new Vector2(0.5f, 0.5f);
            rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);

            // Panel pod głównym oknem, lekko niżej aby nie nachodził na przycisk OK.
            rt.sizeDelta = new Vector2(600f, 155f);
            rt.anchoredPosition = new Vector2(0f, -435f);

            Vector3 local = rt.localPosition;
            local.z = 0f;
            rt.localPosition = local;

            ExpiryPanel.transform.SetAsLastSibling();
        }
    }
}