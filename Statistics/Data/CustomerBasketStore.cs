using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;

namespace StatisticMod
{
    /// <summary>
    /// Slot-aware daily checkout/customer metrics used by the Customers view.
    /// The file is intentionally separate from StatisticMod.stats.tsv so older
    /// statistics files stay fully compatible.
    /// </summary>
    public static class CustomerBasketStore
    {
        public sealed class DayData
        {
            public int Day;
            public int Transactions;
            public float Items;
            public float CheckoutRevenue;

            // Captured now as well, so the next Checkout Analytics step can use
            // historical data collected from this version onward.
            public int CardTransactions;
            public int CashTransactions;
            public int SelfCheckoutTransactions;
            public int CashierCheckoutTransactions;
            public int PlayerCheckoutTransactions;

            // Revenue split by payment / checkout operator. These fields are
            // captured from Checkout.TotalPrice at the same moment as the
            // completed transaction, so they use the exact basket total.
            public float CardRevenue;
            public float CashRevenue;
            public float SelfCheckoutRevenue;
            public float CashierCheckoutRevenue;
            public float PlayerCheckoutRevenue;

            // Stage 2 already stored transaction counts, but not per-channel
            // revenue. Old rows are therefore marked incomplete instead of
            // pretending that missing historical revenue equals zero.
            public bool ChannelRevenueComplete;

            // Version 3.1+: exact payment-method tracking. Previous versions
            // classified too many card payments as cash, so old rows are marked
            // unreliable rather than displaying incorrect historical data.
            public bool PaymentMethodReliable;
            public int PaymentTrackedTransactions;

            // Non-checkout sales channels.
            public int OnlineOrderTransactions;
            public float OnlineOrderItems;
            public float OnlineOrderRevenue;

            public int VendingTransactions;
            public float VendingItems;
            public float VendingRevenue;

            // False for rows created by older versions that did not track online
            // orders / vending purchases.
            public bool ExtendedChannelsComplete;
        }

        private static readonly Dictionary<int, DayData> _days = new Dictionary<int, DayData>();
        private static readonly HashSet<long> _runtimeOnlineOrderKeys = new HashSet<long>();
        private static string _activePath;
        private static bool _loaded;
        private static bool _dirty;

        public static string AbsoluteFilePath
        {
            get
            {
                EnsureLoadedForCurrentSlot();
                return _activePath;
            }
        }

        public static void ReloadCommitted()
        {
            // Force a disk reload even when the game reloads the SAME save slot.
            // This drops runtime-only checkout/customer changes made after the
            // player's last actual game save.
            _activePath = ResolvePath();
            _runtimeOnlineOrderKeys.Clear();
            LoadInternal(_activePath);
            _loaded = true;
        }

        public static DayData TryGetDay(int day)
        {
            EnsureLoadedForCurrentSlot();
            if (day < 1) return null;
            _days.TryGetValue(day, out DayData data);
            return data;
        }

        public static void RecordTransaction(
            int day,
            float revenue,
            float items,
            bool paidByCard,
            bool selfCheckout,
            bool hasCashier)
        {
            if (day < 1) day = 1;
            EnsureLoadedForCurrentSlot();

            DayData data = GetOrCreateDay(day);

            data.Transactions++;
            data.CheckoutRevenue += Mathf.Max(0f, revenue);
            data.Items += Mathf.Max(0f, items);

            // If this row came from the previous build, its historical card/cash
            // split is known to be wrong. Start a fresh exact split from the first
            // new transaction while keeping the day's total checkout counters.
            if (!data.PaymentMethodReliable)
            {
                data.CardTransactions = 0;
                data.CashTransactions = 0;
                data.CardRevenue = 0f;
                data.CashRevenue = 0f;
                data.PaymentTrackedTransactions = 0;
                data.PaymentMethodReliable = true;
            }

            data.PaymentTrackedTransactions++;

            if (paidByCard)
            {
                data.CardTransactions++;
                data.CardRevenue += Mathf.Max(0f, revenue);
            }
            else
            {
                data.CashTransactions++;
                data.CashRevenue += Mathf.Max(0f, revenue);
            }

            if (selfCheckout)
            {
                data.SelfCheckoutTransactions++;
                data.SelfCheckoutRevenue += Mathf.Max(0f, revenue);
            }
            else if (hasCashier)
            {
                data.CashierCheckoutTransactions++;
                data.CashierCheckoutRevenue += Mathf.Max(0f, revenue);
            }
            else
            {
                data.PlayerCheckoutTransactions++;
                data.PlayerCheckoutRevenue += Mathf.Max(0f, revenue);
            }

            // Keep runtime data in memory. It is committed only by a real game
            // save/QuickSave or when the in-game day is completed.
            _dirty = true;
        }


        public static void RecordOnlineOrder(
            int day,
            int orderId,
            float revenue,
            float items)
        {
            if (day < 1) day = 1;
            EnsureLoadedForCurrentSlot();

            if (orderId > 0)
            {
                long key = ((long)day << 32) ^ (uint)orderId;
                if (!_runtimeOnlineOrderKeys.Add(key))
                    return;
            }

            DayData data = GetOrCreateDay(day);
            data.OnlineOrderTransactions++;
            data.OnlineOrderRevenue += Mathf.Max(0f, revenue);
            data.OnlineOrderItems += Mathf.Max(0f, items);

            _dirty = true;
        }

        public static void RecordVendingPurchase(
            int day,
            float revenue,
            float items)
        {
            RecordVendingBatch(day, 1, revenue, items);
        }

        public static void RecordVendingBatch(
            int day,
            int transactions,
            float revenue,
            float items)
        {
            if (day < 1) day = 1;
            if (transactions <= 0 && revenue <= 0f && items <= 0f) return;

            EnsureLoadedForCurrentSlot();

            DayData data = GetOrCreateDay(day);
            data.VendingTransactions += Math.Max(0, transactions);
            data.VendingRevenue += Mathf.Max(0f, revenue);
            data.VendingItems += Mathf.Max(0f, items);

            _dirty = true;
        }

        public static int TotalTransactions(DayData data, bool includeVending)
        {
            if (data == null) return 0;
            int result = Math.Max(0, data.Transactions) + Math.Max(0, data.OnlineOrderTransactions);
            if (includeVending) result += Math.Max(0, data.VendingTransactions);
            return result;
        }

        public static float TotalRevenue(DayData data, bool includeVending)
        {
            if (data == null) return 0f;
            float result = Mathf.Max(0f, data.CheckoutRevenue) + Mathf.Max(0f, data.OnlineOrderRevenue);
            if (includeVending) result += Mathf.Max(0f, data.VendingRevenue);
            return result;
        }

        private static DayData GetOrCreateDay(int day)
        {
            if (_days.TryGetValue(day, out DayData existing) && existing != null)
                return existing;

            var data = new DayData
            {
                Day = day,
                ChannelRevenueComplete = true,
                PaymentMethodReliable = true,
                ExtendedChannelsComplete = true
            };
            _days[day] = data;
            return data;
        }

        public static void SaveNow()
        {
            if (!_loaded || !_dirty || string.IsNullOrEmpty(_activePath)) return;

            try
            {
                string dir = Path.GetDirectoryName(_activePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                string temp = _activePath + ".tmp";
                var lines = new List<string>
                {
                    "Day\tTransactions\tItems\tCheckoutRevenue\tCardTransactions\tCashTransactions\tSelfCheckoutTransactions\tCashierCheckoutTransactions\tPlayerCheckoutTransactions\tCardRevenue\tCashRevenue\tSelfCheckoutRevenue\tCashierCheckoutRevenue\tPlayerCheckoutRevenue\tChannelRevenueComplete\tPaymentMethodReliable\tPaymentTrackedTransactions\tOnlineOrderTransactions\tOnlineOrderItems\tOnlineOrderRevenue\tVendingTransactions\tVendingItems\tVendingRevenue\tExtendedChannelsComplete"
                };

                var sorted = new List<DayData>(_days.Values);
                sorted.Sort((a, b) => a.Day.CompareTo(b.Day));

                for (int i = 0; i < sorted.Count; i++)
                {
                    DayData d = sorted[i];
                    if (d == null || d.Day < 1) continue;

                    lines.Add(string.Join("\t", new[]
                    {
                        d.Day.ToString(CultureInfo.InvariantCulture),
                        d.Transactions.ToString(CultureInfo.InvariantCulture),
                        d.Items.ToString("R", CultureInfo.InvariantCulture),
                        d.CheckoutRevenue.ToString("R", CultureInfo.InvariantCulture),
                        d.CardTransactions.ToString(CultureInfo.InvariantCulture),
                        d.CashTransactions.ToString(CultureInfo.InvariantCulture),
                        d.SelfCheckoutTransactions.ToString(CultureInfo.InvariantCulture),
                        d.CashierCheckoutTransactions.ToString(CultureInfo.InvariantCulture),
                        d.PlayerCheckoutTransactions.ToString(CultureInfo.InvariantCulture),
                        d.CardRevenue.ToString("R", CultureInfo.InvariantCulture),
                        d.CashRevenue.ToString("R", CultureInfo.InvariantCulture),
                        d.SelfCheckoutRevenue.ToString("R", CultureInfo.InvariantCulture),
                        d.CashierCheckoutRevenue.ToString("R", CultureInfo.InvariantCulture),
                        d.PlayerCheckoutRevenue.ToString("R", CultureInfo.InvariantCulture),
                        d.ChannelRevenueComplete ? "1" : "0",
                        d.PaymentMethodReliable ? "1" : "0",
                        d.PaymentTrackedTransactions.ToString(CultureInfo.InvariantCulture),
                        d.OnlineOrderTransactions.ToString(CultureInfo.InvariantCulture),
                        d.OnlineOrderItems.ToString("R", CultureInfo.InvariantCulture),
                        d.OnlineOrderRevenue.ToString("R", CultureInfo.InvariantCulture),
                        d.VendingTransactions.ToString(CultureInfo.InvariantCulture),
                        d.VendingItems.ToString("R", CultureInfo.InvariantCulture),
                        d.VendingRevenue.ToString("R", CultureInfo.InvariantCulture),
                        d.ExtendedChannelsComplete ? "1" : "0"
                    }));
                }

                File.WriteAllLines(temp, lines);
                if (File.Exists(_activePath)) File.Delete(_activePath);
                File.Move(temp, _activePath);
                _dirty = false;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("[CustomerBasketStore] Save failed: " + ex.Message);
            }
        }

        public static void ResetForNewGame(string slotName)
        {
            string target = ResolvePathForSlot(slotName);

            if (_loaded && string.Equals(target, _activePath, StringComparison.OrdinalIgnoreCase))
            {
                _days.Clear();
                _runtimeOnlineOrderKeys.Clear();
                _dirty = false;
            }

            DeleteFileSafe(target);
            DeleteFileSafe(target + ".tmp");
        }

        private static void EnsureLoadedForCurrentSlot()
        {
            string path = ResolvePath();
            if (_loaded && string.Equals(path, _activePath, StringComparison.OrdinalIgnoreCase))
                return;

            // Switching/loading a slot must discard runtime-only changes unless
            // the game was actually saved or the day had already been committed.
            _activePath = path;
            _runtimeOnlineOrderKeys.Clear();
            LoadInternal(path);
            _loaded = true;
        }

        private static void LoadInternal(string path)
        {
            _days.Clear();
            _dirty = false;

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                foreach (string line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("Day\t", StringComparison.OrdinalIgnoreCase)) continue;

                    string[] p = line.Split('\t');
                    if (p.Length < 4) continue;
                    if (!int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day) || day < 1)
                        continue;

                    var data = new DayData
                    {
                        Day = day,
                        Transactions = PI(p, 1),
                        Items = PF(p, 2),
                        CheckoutRevenue = PF(p, 3),
                        CardTransactions = PI(p, 4),
                        CashTransactions = PI(p, 5),
                        SelfCheckoutTransactions = PI(p, 6),
                        CashierCheckoutTransactions = PI(p, 7),
                        PlayerCheckoutTransactions = PI(p, 8),
                        CardRevenue = PF(p, 9),
                        CashRevenue = PF(p, 10),
                        SelfCheckoutRevenue = PF(p, 11),
                        CashierCheckoutRevenue = PF(p, 12),
                        PlayerCheckoutRevenue = PF(p, 13),
                        ChannelRevenueComplete = p.Length >= 15 && PI(p, 14) != 0,
                        PaymentMethodReliable = p.Length >= 16 && PI(p, 15) != 0,
                        PaymentTrackedTransactions = PI(p, 16),
                        OnlineOrderTransactions = PI(p, 17),
                        OnlineOrderItems = PF(p, 18),
                        OnlineOrderRevenue = PF(p, 19),
                        VendingTransactions = PI(p, 20),
                        VendingItems = PF(p, 21),
                        VendingRevenue = PF(p, 22),
                        ExtendedChannelsComplete = p.Length >= 24 && PI(p, 23) != 0
                    };

                    _days[day] = data;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("[CustomerBasketStore] Load failed: " + ex.Message);
                _days.Clear();
            }
        }

        private static string ResolvePath()
        {
            try
            {
                string statsPath = StatsStore.AbsoluteFilePath;
                if (!string.IsNullOrEmpty(statsPath))
                    return statsPath + ".customers.tsv";
            }
            catch { }

            return Path.Combine(Application.persistentDataPath, "StatisticMod.stats.customers.tsv");
        }

        private static string ResolvePathForSlot(string slotName)
        {
            string normalized = string.IsNullOrWhiteSpace(slotName)
                ? "slot_0"
                : Path.GetFileNameWithoutExtension(slotName.Trim()).ToLowerInvariant();

            if (!normalized.StartsWith("slot_", StringComparison.OrdinalIgnoreCase))
                normalized = "slot_0";

            return Path.Combine(
                Application.persistentDataPath,
                normalized,
                "StatisticMod.stats.tsv.customers.tsv");
        }

        private static int PI(string[] p, int index)
        {
            if (p == null || index < 0 || index >= p.Length) return 0;
            int.TryParse(p[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value);
            return value;
        }

        private static float PF(string[] p, int index)
        {
            if (p == null || index < 0 || index >= p.Length) return 0f;
            float.TryParse(p[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
            return value;
        }

        private static void DeleteFileSafe(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    File.Delete(path);
            }
            catch { }
        }
    }
}
