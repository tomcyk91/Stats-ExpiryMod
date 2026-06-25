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
        public static TMP_Text ExpiryTextElement;

        [HarmonyPostfix]
        [HarmonyPatch(nameof(SettingPriceCanvas.OpenMenu))]
        private static void OpenMenu_Postfix(SettingPriceCanvas __instance, PriceTag priceTag)
        {
            if (priceTag == null || priceTag.DisplaySlot == null) return;

            EnsureTextElement(__instance);

            if (ExpiryTextElement != null && ExpiryPanel != null)
            {
                int currentDay = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;

                var products = ExpirationSaveManager.GetSortedProducts(priceTag.DisplaySlot.transform);
                Dictionary<int, int> daysLeftCounts = new Dictionary<int, int>();

                foreach (var p in products)
                {
                    if (p != null)
                    {
                        var comp = p.GetComponent<ProductExpirationComponent>();
                        if (comp != null)
                        {
                            int dLeft = comp.ExpirationDay - currentDay;

                            if (!daysLeftCounts.ContainsKey(dLeft)) daysLeftCounts[dLeft] = 0;
                            daysLeftCounts[dLeft]++;
                        }
                    }
                }

                string finalText = $"<color=#FFFFFF>{StatisticMod.Plugin.T("Terminy przydatności:", "Expiration dates:")}</color>\n";
                if (daysLeftCounts.Count == 0)
                {
                    finalText += $"<color=#C0C0C0>{StatisticMod.Plugin.T("Brak towaru", "Empty")}</color>";
                }
                else
                {
                    foreach (var kvp in daysLeftCounts.OrderBy(k => k.Key))
                    {
                        int dLeft = kvp.Key;
                        int count = kvp.Value;
                        string color = dLeft <= 0 ? "#FF0000" : (dLeft == 1 ? "#FFA500" : "#00FF00");

                        string dayString = dLeft == 1 ? StatisticMod.Plugin.T("dzień", "day") : StatisticMod.Plugin.T("dni", "days");
                        string inString = StatisticMod.Plugin.T("za", "in");
                        string pcsString = StatisticMod.Plugin.T("szt.", "pcs.");

                        finalText += $"<color={color}>{count} {pcsString} {inString} {dLeft} {dayString}</color>\n";
                    }
                }

                ExpiryTextElement.text = finalText;
                ExpiryPanel.SetActive(true);
            }
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(SettingPriceCanvas.CloseMenu))]
        private static void CloseMenu_Postfix()
        {
            if (ExpiryPanel != null)
            {
                ExpiryPanel.SetActive(false);
            }
        }

        private static void EnsureTextElement(SettingPriceCanvas canvas)
        {
            if (ExpiryPanel != null)
            {
                if (ExpiryPanel.transform.parent != canvas.transform)
                {
                    ExpiryPanel.transform.SetParent(canvas.transform, false);
                    ExpiryPanel.transform.SetAsLastSibling();
                }
                return;
            }

            var refText = canvas.GetComponentInChildren<TMP_Text>(true);
            if (refText == null) return;

            ExpiryPanel = new GameObject("ExpirationBackground");
            ExpiryPanel.transform.SetParent(canvas.transform, false);
            ExpiryPanel.transform.SetAsLastSibling();

            Image bgImage = ExpiryPanel.AddComponent<Image>();
            bgImage.color = new Color(0f, 0f, 0f, 0.85f);

            RectTransform bgRt = ExpiryPanel.GetComponent<RectTransform>();
            bgRt.localScale = Vector3.one;
            bgRt.sizeDelta = new Vector2(300, 150);
            bgRt.anchorMin = new Vector2(0.5f, 0.5f);
            bgRt.anchorMax = new Vector2(0.5f, 0.5f);
            bgRt.pivot = new Vector2(0.5f, 0.5f);
            bgRt.anchoredPosition = new Vector2(0, -400);

            GameObject textObj = new GameObject("ExpirationText");
            textObj.transform.SetParent(ExpiryPanel.transform, false);

            ExpiryTextElement = textObj.AddComponent<TextMeshProUGUI>();
            ExpiryTextElement.font = refText.font;
            ExpiryTextElement.fontSharedMaterial = refText.fontSharedMaterial;
            ExpiryTextElement.fontSize = 20;
            ExpiryTextElement.alignment = TextAlignmentOptions.Center;
            ExpiryTextElement.richText = true;
            ExpiryTextElement.overflowMode = TextOverflowModes.Overflow;
            ExpiryTextElement.color = Color.white;

            RectTransform txtRt = ExpiryTextElement.rectTransform;
            txtRt.localScale = Vector3.one;
            txtRt.anchorMin = new Vector2(0, 0);
            txtRt.anchorMax = new Vector2(1, 1);
            txtRt.pivot = new Vector2(0.5f, 0.5f);
            txtRt.sizeDelta = new Vector2(-20, -20);
            txtRt.anchoredPosition = Vector2.zero;
        }
    }
}