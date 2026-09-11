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
        private static Transform FindByPath(Transform root, string relativePath)
        {
            var parts = relativePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            Transform current = root;

            for (int p = 0; p < parts.Length; p++)
            {
                bool found = false;
                for (int i = 0; i < current.childCount; i++)
                {
                    var c = current.GetChild(i);
                    if (c.name == parts[p])
                    {
                        current = c;
                        found = true;
                        break;
                    }
                }
                if (!found) return null;
            }

            return current;
        }

        private static Transform FindDirectChild(Transform parent, string exactName)
        {
            if (parent == null) return null;
            for (int i = 0; i < parent.childCount; i++)
            {
                var c = parent.GetChild(i);
                if (c.name == exactName) return c;
            }
            return null;
        }

        private void SetTmpText(Transform root, string relativePath, string value)
        {
            var t = FindByPath(root, relativePath);
            if (t == null) return;

            var tmp = t.GetComponent<TextMeshProUGUI>();
            if (tmp != null) { tmp.text = value; return; }

            tmp = t.GetComponentInChildren<TextMeshProUGUI>(true);
            if (tmp != null) tmp.text = value;
        }

        private void DisableRaycastOnAllTMP(Transform root)
        {
            var tmps = root.GetComponentsInChildren<TextMeshProUGUI>(true);
            for (int i = 0; i < tmps.Length; i++)
                tmps[i].raycastTarget = false;
        }

        private int GetCurrentDaySafe()
        {
            int day = 1;
            if (DayCycleManager.Instance != null)
                day = DayCycleManager.Instance.CurrentDay;
            return Mathf.Max(1, day);
        }

        private void EnsureSelectedDayInitialized()
        {
            if (_selectedDay >= 1) return;
            _selectedDay = GetCurrentDaySafe();
        }

        private static void DisableGameScriptsOnTile(Transform root)
        {
            // Wyłączamy wszystkie MonoBehaviour pochodzące z Assembly-CSharp (skrypty gry),
            // bo one potrafią po 1 klatce nadpisać TMP w prefabie.
            var behaviours = root.GetComponentsInChildren<MonoBehaviour>(true);

            for (int i = 0; i < behaviours.Length; i++)
            {
                var mb = behaviours[i];
                if (mb == null) continue;

                var t = mb.GetType();
                string asm = t.Assembly?.GetName()?.Name ?? "";

                // zostaw Unity/TMP
                if (asm == "UnityEngine" || asm.StartsWith("Unity.") || asm.StartsWith("TMPro"))
                    continue;

                // zostaw nasze
                if (!string.IsNullOrEmpty(t.Namespace) && t.Namespace.StartsWith("StatisticMod"))
                    continue;

                // wyłącz skrypty gry
                if (asm == "Assembly-CSharp")
                {
                    try { mb.enabled = false; } catch { }
                }
            }
        }

        private string GetProductNameSafe(int productId)
        {
            try
            {
                // ID 9999 to specjalny, syntetyczny produkt używany przez stoisko z lodami.
                // Nie posiada ProductSO, dlatego nie może być rozwiązywany wyłącznie przez TryGetSO().
                if (Plugin.ProductCache != null &&
                    Plugin.ProductCache.TryGet(productId, out var cachedName, out _) &&
                    !string.IsNullOrWhiteSpace(cachedName))
                {
                    if (productId == 9999)
                        return cachedName.Trim();
                }

                if (Plugin.ProductCache != null && Plugin.ProductCache.TryGetSO(productId, out var so) && so != null)
                {
                    if (!string.IsNullOrEmpty(so.TempProductName)) return so.TempProductName.Trim();
                    if (!string.IsNullOrEmpty(so.ProductName)) return so.ProductName.Trim();
                }

                // Fallback również dla ewentualnych przyszłych produktów syntetycznych bez ProductSO.
                if (Plugin.ProductCache != null &&
                    Plugin.ProductCache.TryGet(productId, out var fallbackName, out _) &&
                    !string.IsNullOrWhiteSpace(fallbackName))
                {
                    string suffix = $" ID: {productId}";
                    string value = fallbackName.Trim();
                    if (value.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        value = value.Substring(0, value.Length - suffix.Length).TrimEnd();
                    return value;
                }
            }
            catch { }

            return $"Produkt #{productId}";
        }

        private string FormatTileProductName(string rawName, int productId, bool showId)
        {
            string id = productId.ToString();
            string name = string.IsNullOrWhiteSpace(rawName)
                ? Plugin.T("Produkt", "Product")
                : rawName.Trim();

            string[] suffixes =
            {
                $" ID: {id}",
                $" ID:{id}",
                $" ID : {id}",
                $" (ID: {id})",
                $" [ID: {id}]"
            };

            for (int i = 0; i < suffixes.Length; i++)
            {
                string suffix = suffixes[i];
                if (name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffix.Length).TrimEnd();
                    break;
                }
            }

            if (string.Equals(name, $"Produkt #{id}", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(name, $"Product #{id}", StringComparison.OrdinalIgnoreCase))
            {
                name = Plugin.T("Produkt", "Product");
            }

            return showId ? $"{name} ID: {id}" : name;
        }

        private TMPro.TextMeshProUGUI GetTmpComponent(Transform root, string path)
        {
            var tr = root.Find(path);
            return tr?.GetComponent<TMPro.TextMeshProUGUI>();
        }

    }
}
