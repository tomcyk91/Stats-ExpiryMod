using System;
using System.Collections.Generic;
using UnityEngine;

namespace StatisticMod
{
    public sealed class ProductVisualCache
    {
        public readonly Dictionary<int, ProductSO> ById = new();
        public readonly Dictionary<int, Sprite> IconById = new();
        public readonly Dictionary<int, string> NameById = new();

        public int Count => ById.Count;
        public bool IsBuilt { get; private set; } = false;

        public void Invalidate()
        {
            IsBuilt = false;
            ById.Clear();
            IconById.Clear();
            NameById.Clear();
        }

        public void Build(global::IDManager idManager)
        {
            if (IsBuilt) return;

            ById.Clear();
            IconById.Clear();
            NameById.Clear();

            try
            {
                HashSet<int> seen = new HashSet<int>();

                // 1. Bezpośredni natywny odczyt z publicznej listy m_Products
                var products = idManager != null ? idManager.m_Products : null;
                if (products != null)
                {
                    for (int i = 0; i < products.Count; i++)
                    {
                        var so = products[i];
                        if (so == null) continue;

                        int id = 0;
                        try { id = so.ID; } catch { }

                        if (id <= 0 || seen.Contains(id)) continue;

                        seen.Add(id);
                        AddProductSafe(so);
                    }
                }

                // 2. C4 FIX: Bezpośrednie odwołanie do m_ProductSODictionary bez uzywania powolnej refleksji
                if (idManager != null && idManager.m_ProductSODictionary != null)
                {
                    var dict = idManager.m_ProductSODictionary;
                    foreach (var entry in dict)
                    {
                        ProductSO so = entry.Value;
                        if (so == null) continue;

                        int realId = 0;
                        try { realId = so.ID; } catch { }

                        if (realId <= 0 || seen.Contains(realId)) continue;

                        seen.Add(realId);
                        AddProductSafe(so);
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.Log.LogError($"[ProductVisualCache] Build failed: {ex}");
            }

            ById[9999] = null;
            NameById[9999] = Plugin.T("Lody (Stoisko)", "Ice Cream (Stand)");
            IconById[9999] = EmbeddedIconLoader.LoadPngSprite("icecream");

            IsBuilt = true;
        }

        private void AddProductSafe(ProductSO p)
        {
            if (p == null) return;

            int id;
            try { id = p.ID; }
            catch { return; }

            if (id <= 0 || ById.ContainsKey(id)) return;

            ById[id] = p;

            string name = null;
            try { name = p.TempProductName; } catch { }

            if (string.IsNullOrEmpty(name))
            {
                try { name = p.ProductName; } catch { }
            }

            NameById[id] = !string.IsNullOrEmpty(name)
                ? $"{name} ID: {id}"
                : $"Unknown ID: {id}";

            Sprite icon = null;
            try { icon = p.ProductIcon; } catch { }

            IconById[id] = icon;
        }

        public bool TryGetSO(int id, out ProductSO so) => ById.TryGetValue(id, out so);

        public bool TryGet(int id, out string name, out Sprite icon)
        {
            if (id == 9999)
            {
                name = Plugin.T("Lody", "Ice Cream");
                icon = EmbeddedIconLoader.LoadPngSprite("icecream");
                return true;
            }

            NameById.TryGetValue(id, out name);
            IconById.TryGetValue(id, out icon);
            return name != null || icon != null;
        }
    }
}