using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace StatisticMod
{
    // ========================================================================
    // 1. GŁÓWNA KLASA STATYSTYK I BUFORY
    // ========================================================================
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

        public static void RecordSale(int day, int pid, float totalUnits)
        {
            float price = (PriceManager.Instance != null) ? PriceManager.Instance.SellingPrice(pid) : 0f;

            if (WeightPerUnit.TryGetValue(pid, out float kgPerUnit))
            {
                float kg = totalUnits * kgPerUnit;
                float revenue = price * kg;
                StatsStore.AddSaleF(day, pid, kg, revenue, true);
            }
            else
            {
                float revenue = price * totalUnits;
                StatsStore.AddSaleF(day, pid, totalUnits, revenue, false);
            }
        }

        public static void Payment_Trigger(Checkout checkoutInstance, string triggerName)
        {
            int id = checkoutInstance.GetInstanceID();
            if (!_multiCheckoutBuffer.ContainsKey(id) || _multiCheckoutBuffer[id].Count == 0) return;

            int day = (DayCycleManager.Instance != null) ? DayCycleManager.Instance.CurrentDay : 1;
            var buffer = _multiCheckoutBuffer[id];

            foreach (var item in buffer)
            {
                RecordSale(day, item.Key, item.Value);
            }

            buffer.Clear();
            // POPRAWKA: Usunięto StatsStore.SaveNow(); Dane zostają w RAM.
        }
    }

    // ========================================================================
    // 2. SKANOWANIE KASY
    // ========================================================================
    public static class CheckoutScreen_AddProduct_Patch
    {
        public static void Postfix(CheckoutScreen __instance, object __0, int __1)
        {
            if (__0 == null) return;
            var p = __0 as global::Product;
            if (p == null || p.m_ProductSO == null) return;

            Checkout checkout = __instance.m_Checkout;
            if (checkout == null) return;

            int cid = checkout.GetInstanceID();
            int pid = p.m_ProductSO.ID;

            if (!SalesUnifiedFinal._multiCheckoutBuffer.ContainsKey(cid))
                SalesUnifiedFinal._multiCheckoutBuffer[cid] = new Dictionary<int, float>();

            float amount = __1 > 0 ? (float)__1 : 1f;

            if (SalesUnifiedFinal._multiCheckoutBuffer[cid].ContainsKey(pid))
                SalesUnifiedFinal._multiCheckoutBuffer[cid][pid] += amount;
            else
                SalesUnifiedFinal._multiCheckoutBuffer[cid][pid] = amount;
        }
    }

    // ========================================================================
    // 3. TRIGGERY PŁATNOŚCI
    // ========================================================================
    public static class Checkout_StartCheckout_Patch
    {
        public static void Postfix(Checkout __instance)
        {
            int id = __instance.GetInstanceID();
            if (SalesUnifiedFinal._multiCheckoutBuffer.ContainsKey(id))
                SalesUnifiedFinal._multiCheckoutBuffer[id].Clear();
        }
    }

    public static class CheckoutScreen_Clear_Patch
    {
        public static void Prefix(CheckoutScreen __instance)
        {
            if (__instance.m_Checkout != null)
            {
                SalesUnifiedFinal.Payment_Trigger(__instance.m_Checkout, "CheckoutScreen.Clear()");
            }
        }
    }

    public static class DynamicPaymentHooks
    {
        public static void Prefix(Checkout __instance, MethodBase __originalMethod)
        {
            SalesUnifiedFinal.Payment_Trigger(__instance, $"Radar: {__originalMethod.Name}");
        }
    }

    // ========================================================================
    // 4. ONLINE ORDERS (Paczki)
    // ========================================================================
    public static class OnlineOrder_AddProduct_Patch
    {
        public static void Postfix(int productId) => SalesUnifiedFinal._onlineBuffer.Add(productId);
    }

    public static class OnlineOrder_Deliver_Patch
    {
        public static void Prefix()
        {
            int day = (DayCycleManager.Instance != null) ? DayCycleManager.Instance.CurrentDay : 1;
            foreach (int pid in SalesUnifiedFinal._onlineBuffer) SalesUnifiedFinal.RecordSale(day, pid, 1f);
            SalesUnifiedFinal._onlineBuffer.Clear();
            // POPRAWKA: Usunięto StatsStore.SaveNow(); Dane zostają w RAM.
        }
    }

    // ========================================================================
    // 5. INNE PATCHE (Nakładka UI)
    // ========================================================================
    public static class DayCycleOverlayPatch
    {
        public static void Postfix()
        {
            StatisticMod.GameDayOverlay.Create();
        }
    }
}