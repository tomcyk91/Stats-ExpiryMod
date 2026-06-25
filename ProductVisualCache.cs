using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
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

                // 1. Główna lista produktów - używamy tylko m_Products
                var products = idManager != null ? idManager.m_Products : null;
                if (products != null)
                {
                    foreach (var so in products)
                    {
                        if (so == null) continue;

                        int id = 0;
                        try { id = so.ID; } catch { }

                        if (id <= 0 || seen.Contains(id))
                            continue;

                        seen.Add(id);
                        AddProductSafe(so);
                    }
                }

                // 2. BEZPIECZNY FALLBACK - Skanujemy ukryty słownik C++ zamiast pętli do 5000
                if (idManager != null)
                {
                    try
                    {
                        var dictField = AccessTools.Field(typeof(global::IDManager), "m_ProductDictionary");
                        if (dictField != null)
                        {
                            var dictObj = dictField.GetValue(idManager);
                            if (dictObj != null)
                            {
                                var il2cppObj = dictObj as Il2CppInterop.Runtime.InteropTypes.Il2CppObjectBase;
                                var dict = il2cppObj.Cast<Il2CppSystem.Collections.Generic.Dictionary<int, ProductSO>>();

                                foreach (var entry in dict)
                                {
                                    ProductSO so = entry.Value;
                                    if (so == null) continue;

                                    int realId = 0;
                                    try { realId = so.ID; } catch { }

                                    if (realId <= 0 || seen.Contains(realId))
                                        continue;

                                    seen.Add(realId);
                                    AddProductSafe(so);
                                }
                            }
                        }
                    }
                    catch
                    {
                        // Ciche zignorowanie, bez spamu w logach
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

            if (id <= 0 || ById.ContainsKey(id))
                return;

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

        private IEnumerable<ProductSO> EnumerateFromReflection(global::IDManager idManager)
        {
            if (idManager == null)
                yield break;

            object value = null;
            Type t = idManager.GetType();

            // property: Products
            try
            {
                var prop = AccessTools.Property(t, "Products");
                if (prop != null)
                    value = prop.GetValue(idManager, null);
            }
            catch { }

            // field: Products
            if (value == null)
            {
                try
                {
                    var field = AccessTools.Field(t, "Products");
                    if (field != null)
                        value = field.GetValue(idManager);
                }
                catch { }
            }

            // field: m_Products
            if (value == null)
            {
                try
                {
                    var field = AccessTools.Field(t, "m_Products");
                    if (field != null)
                        value = field.GetValue(idManager);
                }
                catch { }
            }

            if (value is IEnumerable enumerable)
            {
                foreach (var item in enumerable)
                {
                    if (item is ProductSO so)
                        yield return so;
                }
            }
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