using System;
using System.Reflection;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes;

namespace StatisticMod
{
    public static class StatsHook
    {
        private const BindingFlags BF = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly Dictionary<Type, AccessPlan> _cache = new(64);
        private static readonly HashSet<Type> _failedTypes = new();

        public static int TryGetProductId(object productObj)
        {
            if (productObj == null) return -1;

            // ⚡ SZYBKA ŚCIEŻKA IL2CPP: Bezpieczny downcast rzutuje pamięć natywną
            if (productObj is Il2CppObjectBase baseObj)
            {
                var p = baseObj.TryCast<global::Product>();
                if (p != null && p.m_ProductSO != null) return p.m_ProductSO.ID;
            }
            else if (productObj is global::Product pManaged && pManaged.m_ProductSO != null)
            {
                return pManaged.m_ProductSO.ID;
            }

            var t = productObj.GetType();

            if (_failedTypes.Contains(t)) return -1;

            if (!_cache.TryGetValue(t, out var plan))
            {
                plan = BuildPlan(t);
                _cache[t] = plan;
            }

            int id = plan.Execute(productObj);

            if (id == -1) _failedTypes.Add(t);
            return id;
        }

        private static AccessPlan BuildPlan(Type t)
        {
            var plan = new AccessPlan();

            // 1. Priorytet IL2CPP: Szukamy Właściwości (Properties). 
            // Natywne pola C++ są wystawiane w C# właśnie jako Properties!
            plan.Prop = t.GetProperty("m_ID", BF)
                     ?? t.GetProperty("m_ProductId", BF)
                     ?? t.GetProperty("ProductID", BF);

            // 2. Fallback: Szukamy tradycyjnych Pól (Fields) dla czystego .NET
            plan.Field = t.GetField("m_ID", BF)
                      ?? t.GetField("m_ProductId", BF)
                      ?? t.GetField("ProductID", BF);

            return plan;
        }

        private class AccessPlan
        {
            public PropertyInfo Prop;
            public FieldInfo Field;

            public int Execute(object obj)
            {
                // Ścieżka A: Odczyt przez Właściwość (Omija bug 807810400 w IL2CPP)
                if (Prop != null && Prop.CanRead)
                {
                    var v = Prop.GetValue(obj, null);
                    if (v is int i) return i;
                }

                // Ścieżka B: Odczyt przez Pole (Tylko dla obiektów zarządzanych C#)
                if (Field != null)
                {
                    // ŻELAZNA ZASADA IL2CPP: Odczyt pola wartościowego z Il2CppObjectBase 
                    // zawsze zwraca śmieci 807810400. Przerywamy natychmiast!
                    if (obj is Il2CppObjectBase) return -1;

                    var v = Field.GetValue(obj);
                    if (v is int i) return i;
                }

                return -1;
            }
        }
    }
}