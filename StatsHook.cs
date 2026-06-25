using System;
using System.Reflection;
using System.Collections.Generic;

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

            // ⚡ ATOMOWA OPTYMALIZACJA: Bezpośrednie rzutowanie omija powolną Refleksję
            if (productObj is global::Product p && p.m_ProductSO != null) return p.m_ProductSO.ID;

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
            plan.Field = t.GetField("m_ID", BF) ?? t.GetField("m_ProductId", BF) ?? t.GetField("ProductID", BF);
            return plan;
        }

        private class AccessPlan
        {
            public FieldInfo Field;
            public int Execute(object obj)
            {
                if (Field != null)
                {
                    var v = Field.GetValue(obj);
                    if (v is int i) return i;
                }
                return -1;
            }
        }
    }
}