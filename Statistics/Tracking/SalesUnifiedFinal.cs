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

        // Exact payment-method hints captured from the game's own payment methods.
        // Key = Checkout instance ID, value = true for card / false for cash.
        private static readonly Dictionary<int, bool> _paymentMethodHints = new Dictionary<int, bool>();


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
            _paymentMethodHints.Clear();
        }

        public static void MarkPaymentMethod(Checkout checkout, bool paidByCard)
        {
            if (checkout == null) return;
            _paymentMethodHints[checkout.GetInstanceID()] = paidByCard;
        }

        public static void ClearPaymentMethod(Checkout checkout)
        {
            if (checkout == null) return;
            _paymentMethodHints.Remove(checkout.GetInstanceID());
        }

        public static bool IsVendingDlcActive()
        {
            bool active = false;

            try
            {
                if (VendingMachineManager.HasInstance && VendingMachineManager.Instance != null)
                {
                    active |= VendingMachineManager.Instance.IsDLCActivated;

                    // Runtime fallback: existing vending machines prove that the
                    // vending DLC content is available in the loaded store.
                    var vendings = VendingMachineManager.Instance.Vendings;
                    active |= vendings != null && vendings.Count > 0;
                }
            }
            catch { }

            try
            {
                active |= DLCTestController.VendingMachineDLC ||
                          DLCTestController.AllDlcBundleBought;
            }
            catch { }

            return active;
        }

        public static void RecordSale(int day, int pid, float totalUnits)
        {
            var pm = PriceManager.HasInstance ? PriceManager.Instance : null;
            float price = 0f;
            float unitCost = -1f;

            if (pm != null)
            {
                try { price = pm.SellingPrice(pid); } catch { }
                try
                {
                    float currentCost = pm.CurrentCost(pid);
                    if (currentCost > 0.0001f) unitCost = currentCost;
                }
                catch { }
            }

            if (WeightPerUnit.TryGetValue(pid, out float kgPerUnit))
            {
                float kg = totalUnits * kgPerUnit;
                float revenue = price * kg;
                StatsStore.AddSaleF(day, pid, kg, revenue, true, unitCost);

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
                StatsStore.AddSaleF(day, pid, totalUnits, revenue, false, unitCost);

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

            // Capture basket-level KPIs before the buffer is cleared. TotalPrice is
            // preferred because it reflects the checkout's real total. If a game
            // update clears it before one of our fallback hooks fires, calculate the
            // same value from the buffered products instead.
            float basketItems = 0f;
            foreach (var item in buffer)
                basketItems += Mathf.Max(0f, item.Value);

            float basketRevenue = 0f;
            try { basketRevenue = Mathf.Max(0f, checkoutInstance.TotalPrice); } catch { }
            if (basketRevenue <= 0.0001f)
                basketRevenue = EstimateBufferedRevenue(buffer);

            bool selfCheckout = false;
            bool hasCashier = false;
            try { selfCheckout = checkoutInstance.IsSelfCheckout; } catch { }
            try { hasCashier = checkoutInstance.HasCashier; } catch { }

            bool paidByCard = false;

            // Self-checkouts in the current game only accept card payments.
            if (selfCheckout)
            {
                paidByCard = true;
            }
            else if (_paymentMethodHints.TryGetValue(id, out bool exactPaymentMethod))
            {
                paidByCard = exactPaymentMethod;
            }
            else
            {
                try { paidByCard = checkoutInstance.IsLastPaymentViaCard; } catch { }

                // Final fallback for older/alternate game paths.
                if (!string.IsNullOrEmpty(triggerName))
                {
                    if (triggerName.IndexOf("Card", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        paidByCard = true;
                    else if (triggerName.IndexOf("Cash", System.StringComparison.OrdinalIgnoreCase) >= 0)
                        paidByCard = false;
                }
            }

            CustomerBasketStore.RecordTransaction(
                day,
                basketRevenue,
                basketItems,
                paidByCard,
                selfCheckout,
                hasCashier);

            foreach (var item in buffer)
                RecordSale(day, item.Key, item.Value);

            buffer.Clear();
            _paymentMethodHints.Remove(id);
        }

        private static float EstimateBufferedRevenue(Dictionary<int, float> buffer)
        {
            if (buffer == null || buffer.Count == 0) return 0f;

            var pm = PriceManager.HasInstance ? PriceManager.Instance : null;
            if (pm == null) return 0f;

            float total = 0f;
            foreach (var item in buffer)
            {
                if (item.Value <= 0f) continue;

                float price = 0f;
                try { price = pm.SellingPrice(item.Key); } catch { }
                if (price <= 0f) continue;

                if (WeightPerUnit.TryGetValue(item.Key, out float kgPerUnit))
                    total += price * item.Value * kgPerUnit;
                else
                    total += price * item.Value;
            }

            return Mathf.Max(0f, total);
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

            SalesUnifiedFinal.ClearPaymentMethod(__instance);
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

    public static class Checkout_MarkCard_Patch
    {
        public static void Prefix(Checkout __instance)
        {
            SalesUnifiedFinal.MarkPaymentMethod(__instance, true);
        }
    }

    public static class Checkout_MarkCash_Patch
    {
        public static void Prefix(Checkout __instance)
        {
            SalesUnifiedFinal.MarkPaymentMethod(__instance, false);
        }
    }

    public static class Checkout_CashierPaymentMethod_Patch
    {
        public static void Prefix(Checkout __instance, bool __0)
        {
            SalesUnifiedFinal.MarkPaymentMethod(__instance, __0);
        }
    }

    public static class Checkout_FinishScanningOrder_PaymentMethod_Patch
    {
        public static void Prefix(Checkout __instance, bool __0)
        {
            SalesUnifiedFinal.MarkPaymentMethod(__instance, __0);
        }
    }

    public static class Checkout_CardPaymentCompleted_Patch
    {
        public static void Prefix(Checkout __instance)
        {
            // Set the hint before the game body runs because some versions clear
            // the checkout UI from inside the payment method.
            SalesUnifiedFinal.MarkPaymentMethod(__instance, true);
        }

        public static void Postfix(Checkout __instance, bool __result)
        {
            if (!__result || __instance == null) return;
            SalesUnifiedFinal.MarkPaymentMethod(__instance, true);
            SalesUnifiedFinal.Payment_Trigger(__instance, "TryFinishingCardPayment");
        }
    }

    public static class Checkout_CashPaymentCompleted_Patch
    {
        public static void Prefix(Checkout __instance)
        {
            SalesUnifiedFinal.MarkPaymentMethod(__instance, false);
        }

        public static void Postfix(Checkout __instance, bool __result)
        {
            if (!__result || __instance == null) return;
            SalesUnifiedFinal.MarkPaymentMethod(__instance, false);
            SalesUnifiedFinal.Payment_Trigger(__instance, "TryFinishingCashPayment");
        }
    }

    public static class Checkout_Completed_Patch
    {
        public static void Prefix(Checkout __instance)
        {
            if (__instance != null)
                SalesUnifiedFinal.Payment_Trigger(__instance, "CheckoutCompleted");
        }
    }

    public static class Checkout_SelfCheckoutCompleted_Patch
    {
        public static void Postfix(Checkout __instance)
        {
            if (__instance == null) return;
            SalesUnifiedFinal.MarkPaymentMethod(__instance, true);
            SalesUnifiedFinal.Payment_Trigger(__instance, "SelfCheckoutCard");
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


    public static class OnlineOrderCustomer_DeliverTransaction_Patch
    {
        public static void Prefix(OrderListData __0)
        {
            try
            {
                OrderListData order = __0;
                if (order == null) return;

                var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                int day = dcm != null ? dcm.CurrentDay : 1;

                float items = 0f;
                try
                {
                    if (order.OrderItems != null)
                    {
                        for (int i = 0; i < order.OrderItems.Count; i++)
                        {
                            OrderData item = order.OrderItems[i];
                            if (item != null && item.ProductCount > 0)
                                items += item.ProductCount;
                        }
                    }
                }
                catch { }

                float revenue = 0f;
                try { revenue = Mathf.Max(0f, order.TotalPrice); } catch { }

                CustomerBasketStore.RecordOnlineOrder(
                    day,
                    order.ID,
                    revenue,
                    items);
            }
            catch { }
        }
    }

    public static class VendingPurchaseAnalytics_Patch
    {
        public static void Postfix(VendingSlot __instance, Product __result)
        {
            // IL2CPP does not reliably expose this return value as the sale
            // completion signal. The native vending money counter is authoritative.
            try { VendingSalesTracker.SampleNow(); } catch { }
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
        // The old implementation patched IceCreamManager.CalculatePrice(), which is
        // only a price calculator and does not prove that an ice cream was actually
        // delivered/sold.  We now patch Customer.DeliverIceCream(IceCreamStatus).
        // Prefix captures the complete recipe before the game can release/change the
        // status object; Postfix commits the sale only after DeliverIceCream returns.
        public sealed class IceCreamSaleState
        {
            public bool ShouldRecord;
            public float Revenue;
            public float TotalCost;
            public bool CostKnown;
            public int ConeProductId;
            public int ScoopCount;
            public int ToppingIndex;
            public string Recipe;
        }

        public static void Prefix(Customer __instance, IceCreamStatus __0, out IceCreamSaleState __state)
        {
            __state = null;

            try
            {
                if (__instance == null) return;

                // DeliverIceCream only pays for a fully correct order.  Mirror the
                // game's own comparison so a wrong/unfinished cone is never counted.
                IceCreamRequest request = null;
                try { request = __instance.IceCreamRequest; } catch { }
                if (request == null) return;

                float match = 0f;
                try { match = request.Compare(__0); } catch { return; }
                if (match < 0.9999f) return;

                var manager = IceCreamManager.HasInstance ? IceCreamManager.Instance : null;
                if (manager == null) return;

                float revenue = 0f;
                try { revenue = manager.CalculatePrice(__0); } catch { }
                if (revenue <= 0.0001f) return;

                var pm = PriceManager.HasInstance ? PriceManager.Instance : null;
                bool costKnown = pm != null;
                float totalCost = 0f;

                int conePid = 0;
                int coneIndex = -1;
                try { coneIndex = __0.ConeIndex; } catch { }

                try
                {
                    var cones = manager.ConeProductSO;
                    if (cones == null || coneIndex < 0 || coneIndex >= cones.Length || cones[coneIndex] == null)
                    {
                        costKnown = false;
                    }
                    else
                    {
                        conePid = cones[coneIndex].ID;
                        if (!TryGetIngredientCost(pm, conePid, out float coneCost))
                            costKnown = false;
                        else
                            totalCost += coneCost;
                    }
                }
                catch
                {
                    costKnown = false;
                }

                int scoopCount = 0;
                var recipeParts = new List<KeyValuePair<int, int>>();

                try
                {
                    if (__0.Flavours == null)
                    {
                        costKnown = false;
                    }
                    else
                    {
                        foreach (var pair in __0.Flavours)
                        {
                            IceCreamFlavour flavour = pair.Key;
                            int count = pair.Value;
                            if (flavour == null || count <= 0) continue;

                            scoopCount += count;

                            ProductSO flavourProduct = null;
                            try { flavourProduct = flavour.Product; } catch { }
                            if (flavourProduct == null)
                            {
                                costKnown = false;
                                continue;
                            }

                            int flavourPid = flavourProduct.ID;
                            recipeParts.Add(new KeyValuePair<int, int>(flavourPid, count));

                            if (!TryGetIngredientCost(pm, flavourPid, out float flavourCost))
                            {
                                costKnown = false;
                                continue;
                            }

                            totalCost += flavourCost * count;
                        }
                    }
                }
                catch
                {
                    costKnown = false;
                }

                recipeParts.Sort((a, b) => a.Key.CompareTo(b.Key));
                string recipe = string.Empty;
                for (int i = 0; i < recipeParts.Count; i++)
                {
                    if (i > 0) recipe += ",";
                    recipe += recipeParts[i].Key + "x" + recipeParts[i].Value;
                }

                int toppingIndex = -1;
                try
                {
                    if (__0.Topping != null) toppingIndex = __0.Topping.Index;
                }
                catch { }

                __state = new IceCreamSaleState
                {
                    ShouldRecord = true,
                    Revenue = revenue,
                    TotalCost = totalCost,
                    CostKnown = costKnown,
                    ConeProductId = conePid,
                    ScoopCount = scoopCount,
                    ToppingIndex = toppingIndex,
                    Recipe = recipe
                };
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Stats&Expiry][IceCream] Capture failed: {e.Message}");
                __state = null;
            }
        }

        public static void Postfix(IceCreamSaleState __state)
        {
            if (__state == null || !__state.ShouldRecord) return;

            try
            {
                var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                int day = dcm != null ? dcm.CurrentDay : 1;

                // qty=1, so the complete ingredient cost of this cone can be passed
                // as unitCost. StatsStore will then fill SoldCost + CostedUnits and
                // the existing Profitability UI immediately becomes complete.
                float unitCost = __state.CostKnown ? __state.TotalCost : -1f;
                StatsStore.AddSale(day, 9999, 1, __state.Revenue, unitCost);
                StatsStore.RecordIceCreamSale(
                    day,
                    __state.ConeProductId,
                    __state.ToppingIndex,
                    __state.ScoopCount,
                    __state.Recipe,
                    __state.Revenue,
                    __state.TotalCost,
                    __state.CostKnown);

                BusinessAnalysisStore.RecordConfirmedSale(
                    day, 9999, 1f, 0f, __state.Revenue, false);

                // Per-sale logging was useful while validating ice-cream COGS,
                // but on a busy store it creates a large amount of synchronous
                // BepInEx file I/O. Keep it available only in explicit debug mode.
                if (Plugin.EnableLogs)
                {
                    string costText = __state.CostKnown
                        ? __state.TotalCost.ToString("0.00")
                        : "UNKNOWN";

                    Plugin.DebugLog(
                        $"[Stats&Expiry][IceCream] SALE day={day} " +
                        $"conePid={__state.ConeProductId} scoops={__state.ScoopCount} " +
                        $"flavours=[{__state.Recipe}] topping={__state.ToppingIndex} " +
                        $"revenue={__state.Revenue:0.00} cost={costText}");
                }
            }
            catch (System.Exception e)
            {
                Plugin.Log.LogWarning($"[Stats&Expiry][IceCream] Commit failed: {e.Message}");
            }
        }

        private static bool TryGetIngredientCost(PriceManager pm, int productId, out float cost)
        {
            cost = -1f;
            if (pm == null || productId <= 0) return false;

            try
            {
                float currentCost = pm.CurrentCost(productId);
                if (currentCost <= 0.0001f) return false;

                cost = currentCost;
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}
