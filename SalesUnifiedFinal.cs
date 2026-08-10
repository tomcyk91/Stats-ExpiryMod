using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Il2CppInterop.Runtime.InteropTypes;

namespace StatisticMod
{
    public static class SalesUnifiedFinal
    {
        public static readonly Dictionary<int, Dictionary<int, float>> _multiCheckoutBuffer = new Dictionary<int, Dictionary<int, float>>();
        public static readonly List<int> _onlineBuffer = new List<int>();

        public static readonly Dictionary<int, float> WeightPerUnit = new Dictionary<int, float>
        {
            {165, 0.120f}, {166, 0.400f}, {167, 0.200f}, {168, 0.130f}, {169, 0.060f},
            {171, 0.050f}, {172, 0.060f}, {173, 0.200f}, {174, 0.080f}, {175, 0.065f},
            {176, 0.150f}, {177, 1.500f}, {178, 0.0178f}, {179, 0.113f}, {180, 0.452f},
            {181, 0.178f}, {182, 1.000f}, {183, 0.160f}, {184, 1.300f}, {185, 5.000f},
            {186, 0.119f}, {187, 0.119f}, {188, 10.000f}
        };

        public static void ClearRuntimeBuffers()
        {
            foreach (var entry in _multiCheckoutBuffer)
                entry.Value?.Clear();

            _multiCheckoutBuffer.Clear();
            _onlineBuffer.Clear();
        }

        public static void RecordSale(int day, int pid, float totalUnits)
        {
            var pm = PriceManager.HasInstance ? PriceManager.Instance : null;
            float price = pm != null ? pm.SellingPrice(pid) : 0f;

            if (WeightPerUnit.TryGetValue(pid, out float kgPerUnit))
            {
                float kg = totalUnits * kgPerUnit;
                float revenue = price * kg;
                StatsStore.AddSaleF(day, pid, kg, revenue, true);

                try
                {
                    BusinessAnalysisStore.RecordConfirmedSale(
                        day, pid, totalUnits, kg, revenue, true);
                }
                catch { }
            }
            else
            {
                float revenue = price * totalUnits;
                StatsStore.AddSaleF(day, pid, totalUnits, revenue, false);

                try
                {
                    BusinessAnalysisStore.RecordConfirmedSale(
                        day, pid, totalUnits, 0f, revenue, false);
                }
                catch { }
            }
        }

        public static void Payment_Trigger(Checkout checkoutInstance, string triggerName)
        {
            if (checkoutInstance == null) return;
            int id = checkoutInstance.GetInstanceID();
            if (!_multiCheckoutBuffer.ContainsKey(id) || _multiCheckoutBuffer[id].Count == 0) return;

            var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
            int day = dcm != null ? dcm.CurrentDay : 1;

            var buffer = _multiCheckoutBuffer[id];
            foreach (var item in buffer)
                RecordSale(day, item.Key, item.Value);

            buffer.Clear();
        }
    }

    public static class CheckoutScreen_AddProduct_Patch
    {
        public static void Postfix(CheckoutScreen __instance, object __0, int __1)
        {
            if (__instance == null || __0 == null) return;

            global::Product p = null;
            if (__0 is Il2CppObjectBase baseObj)
                p = baseObj.TryCast<global::Product>();
            else
                p = __0 as global::Product;

            if (p == null || p.m_ProductSO == null) return;

            Checkout checkout = __instance.m_Checkout;
            if (checkout == null) return;

            int cid = checkout.GetInstanceID();
            int pid = p.m_ProductSO.ID;

            if (!SalesUnifiedFinal._multiCheckoutBuffer.ContainsKey(cid))
                SalesUnifiedFinal._multiCheckoutBuffer[cid] = new Dictionary<int, float>();

            float amount = __1 > 0 ? __1 : 1f;

            if (SalesUnifiedFinal._multiCheckoutBuffer[cid].ContainsKey(pid))
                SalesUnifiedFinal._multiCheckoutBuffer[cid][pid] += amount;
            else
                SalesUnifiedFinal._multiCheckoutBuffer[cid][pid] = amount;
        }
    }

    public static class Checkout_StartCheckout_Patch
    {
        public static void Postfix(Checkout __instance)
        {
            if (__instance == null) return;
            int id = __instance.GetInstanceID();
            if (SalesUnifiedFinal._multiCheckoutBuffer.ContainsKey(id))
                SalesUnifiedFinal._multiCheckoutBuffer[id].Clear();
        }
    }

    public static class CheckoutScreen_Clear_Patch
    {
        public static void Prefix(CheckoutScreen __instance)
        {
            if (__instance != null && __instance.m_Checkout != null)
                SalesUnifiedFinal.Payment_Trigger(__instance.m_Checkout, "CheckoutScreen.Clear()");
        }
    }

    public static class DynamicPaymentHooks
    {
        public static void Prefix(Checkout __instance, MethodBase __originalMethod)
        {
            if (__instance != null && __originalMethod != null)
                SalesUnifiedFinal.Payment_Trigger(__instance, $"Radar: {__originalMethod.Name}");
        }
    }

    public static class OnlineOrder_AddProduct_Patch
    {
        public static void Postfix(int productId) => SalesUnifiedFinal._onlineBuffer.Add(productId);
    }

    public static class OnlineOrder_Deliver_Patch
    {
        public static void Prefix()
        {
            var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
            int day = dcm != null ? dcm.CurrentDay : 1;

            foreach (int pid in SalesUnifiedFinal._onlineBuffer)
                SalesUnifiedFinal.RecordSale(day, pid, 1f);

            SalesUnifiedFinal._onlineBuffer.Clear();
        }
    }

    public static class DayCycleOverlayPatch
    {
        public static void Postfix()
        {
            StatisticMod.GameDayOverlay.Create();
        }
    }

    public static class IceCream_Sales_Patch
    {
        public static void Postfix(float __result)
        {
            try
            {
                if (__result <= 0f) return;

                var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                int day = dcm != null ? dcm.CurrentDay : 1;

                StatsStore.AddSale(day, 9999, 1, __result);
                BusinessAnalysisStore.RecordConfirmedSale(
                    day, 9999, 1f, 0f, __result, false);
            }
            catch { }
        }
    }
}
