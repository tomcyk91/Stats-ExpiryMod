using System.Collections.Generic;

namespace StatisticMod
{
    internal sealed class CustomerDemandSession
    {
        public int CustomerInstanceId;
        public int Day;
        public readonly Dictionary<int, int> Requested = new();
        public readonly Dictionary<int, float> RequestedPrices = new();
        public readonly Dictionary<int, int> PickedFallback = new();
    }

    public static class DemandTrackingManager
    {
        private static readonly Dictionary<int, CustomerDemandSession> Sessions = new();
        private static readonly HashSet<int> FinalizedCustomers = new();

        public static void BeginNewShopping(Customer customer)
        {
            if (customer == null) return;
            int customerId = customer.GetInstanceID();
            FinalizedCustomers.Remove(customerId);
            Sessions.Remove(customerId);
            CaptureSession(customer);
        }

        private static void CaptureSession(Customer customer)
        {
            if (customer == null) return;

            int customerId = customer.GetInstanceID();
            if (Sessions.ContainsKey(customerId) || FinalizedCustomers.Contains(customerId)) return;

            ItemQuantity shoppingList = null;
            try { shoppingList = customer.ShoppingList; } catch { }
            if (shoppingList == null || shoppingList.Products == null) return;

            int day = 1;
            try
            {
                var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                if (dcm != null) day = dcm.CurrentDay;
            }
            catch { }

            var session = new CustomerDemandSession
            {
                CustomerInstanceId = customerId,
                Day = day
            };

            try
            {
                foreach (var pair in shoppingList.Products)
                {
                    if (pair.Key > 0 && pair.Value > 0)
                        session.Requested[pair.Key] = pair.Value;
                }
            }
            catch { }

            try
            {
                if (shoppingList.ProductPrice != null)
                {
                    foreach (var pair in shoppingList.ProductPrice)
                    {
                        if (pair.Key > 0 && pair.Value >= 0f)
                            session.RequestedPrices[pair.Key] = pair.Value;
                    }
                }
            }
            catch { }

            if (session.Requested.Count > 0)
                Sessions[customerId] = session;
        }

        public static void ProductPicked(Customer customer, int productId, bool succeeded)
        {
            if (!succeeded || customer == null || productId <= 0) return;

            int customerId = customer.GetInstanceID();
            if (FinalizedCustomers.Contains(customerId)) return;

            if (!Sessions.TryGetValue(customerId, out CustomerDemandSession session))
            {
                CaptureSession(customer);
                Sessions.TryGetValue(customerId, out session);
            }
            if (session == null) return;

            session.PickedFallback.TryGetValue(productId, out int current);
            session.PickedFallback[productId] = current + 1;
        }

        public static void FinalizeCustomer(Customer customer, bool shortchange, string source)
        {
            if (customer == null) return;

            int customerId = customer.GetInstanceID();
            if (FinalizedCustomers.Contains(customerId)) return;

            if (!Sessions.TryGetValue(customerId, out CustomerDemandSession session))
            {
                CaptureSession(customer);
                Sessions.TryGetValue(customerId, out session);
            }
            if (session == null) return;

            Sessions.Remove(customerId);
            FinalizedCustomers.Add(customerId);

            var picked = new Dictionary<int, int>();
            try
            {
                ItemQuantity cart = customer.ShoppingCart;
                if (cart != null && cart.Products != null)
                {
                    foreach (var pair in cart.Products)
                    {
                        if (pair.Key > 0 && pair.Value > 0)
                            picked[pair.Key] = pair.Value;
                    }
                }
            }
            catch { }

            foreach (var pair in session.PickedFallback)
            {
                picked.TryGetValue(pair.Key, out int cartCount);
                if (pair.Value > cartCount) picked[pair.Key] = pair.Value;
            }

            var results = new List<DemandResultItem>(session.Requested.Count);
            bool hasMissingProducts = false;

            foreach (var pair in session.Requested)
            {
                int productId = pair.Key;
                int requested = pair.Value;
                picked.TryGetValue(productId, out int got);
                if (got < 0) got = 0;
                if (got > requested) got = requested;

                int missed = requested - got;
                if (missed > 0) hasMissingProducts = true;

                float price = GetRequestPrice(session, productId);
                bool isWeight = SalesUnifiedFinal.WeightPerUnit.TryGetValue(productId, out float kgPerUnit);

                results.Add(new DemandResultItem
                {
                    ProductId = productId,
                    RequestedUnits = requested,
                    PickedUnits = got,
                    MissedUnits = missed,
                    PriceAtRequest = price,
                    IsWeight = isWeight,
                    KgPerUnit = isWeight ? kgPerUnit : 0f,
                    // Produkt zebrany przez klienta z definicji był wystawiony.
                    WasDisplayed = got > 0,
                    MissReason = missed > 0 ? MissReason.Other : MissReason.None
                });
            }

            // Pełny snapshot jest potrzebny wyłącznie dla brakujących pozycji.
            if (hasMissingProducts)
            {
                StockSnapshot snapshot = StockSnapshotService.Capture();
                for (int i = 0; i < results.Count; i++)
                {
                    DemandResultItem item = results[i];
                    if (item == null || item.MissedUnits <= 0) continue;

                    snapshot.TryGet(item.ProductId, out ProductStockState stockState);
                    item.WasDisplayed |= stockState != null && stockState.IsDisplayed;
                    item.MissReason = StockSnapshotService.ClassifyMissing(stockState);
                }
            }

            BusinessAnalysisStore.RecordCustomerResult(session.Day, results);
            Plugin.DebugLog($"[Demand] Finalized customer={customerId}, products={results.Count}, source={source}, shortchange={shortchange}");
        }

        public static void Abort(Customer customer)
        {
            if (customer == null) return;
            int customerId = customer.GetInstanceID();
            Sessions.Remove(customerId);
            FinalizedCustomers.Remove(customerId);
        }

        public static void ClearAllSessions()
        {
            Sessions.Clear();
            FinalizedCustomers.Clear();
        }

        private static float GetRequestPrice(CustomerDemandSession session, int productId)
        {
            if (session.RequestedPrices.TryGetValue(productId, out float captured) && captured > 0f)
                return captured;

            try
            {
                var pm = PriceManager.HasInstance ? PriceManager.Instance : null;
                if (pm != null) return pm.SellingPrice(productId);
            }
            catch { }
            return 0f;
        }
    }

    public static class Customer_StartShopping_DemandPatch
    {
        public static void Postfix(Customer __instance) => DemandTrackingManager.BeginNewShopping(__instance);
    }

    public static class Customer_TakeProduct_DemandPatch
    {
        public static void Postfix(Customer __instance, int __1, bool __result)
            => DemandTrackingManager.ProductPicked(__instance, __1, __result);
    }

    public static class Customer_CheckMissing_DemandPatch
    {
        public static void Prefix(Customer __instance, bool __0)
            => DemandTrackingManager.FinalizeCustomer(__instance, __0, "CheckForProductsMissing");
    }

    public static class Customer_FinishShopping_DemandPatch
    {
        public static void Prefix(Customer __instance, bool __0)
            => DemandTrackingManager.FinalizeCustomer(__instance, __0, "FinishShopping");
    }

    public static class Customer_Reset_DemandPatch
    {
        public static void Postfix(Customer __instance) => DemandTrackingManager.Abort(__instance);
    }
}
