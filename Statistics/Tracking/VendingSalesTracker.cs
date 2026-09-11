using System;
using System.Collections.Generic;
using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;

namespace StatisticMod
{
    /// <summary>
    /// Vending analytics uses two independent native signals:
    /// 1) MoneyManager.MoneyTransition(VENDING_MACHINE) is authoritative for revenue.
    ///    This is the same source used by the game's DailyStatisticsManager.
    /// 2) Vending stock deltas are used only to count dispensed items/transactions.
    ///
    /// CollectedMoney is deliberately NOT used for revenue because it can contain
    /// money accumulated before the current save/session and can therefore create
    /// false income after loading a game.
    /// </summary>
    public sealed class VendingSalesTracker : MonoBehaviour
    {
        private sealed class Snapshot
        {
            public int Stock;
        }

        private static VendingSalesTracker _instance;
        private static readonly Dictionary<int, Snapshot> _snapshots =
            new Dictionary<int, Snapshot>();

        private float _timer;
        private int _managerInstanceId;

        public VendingSalesTracker(IntPtr ptr) : base(ptr) { }

        [HideFromIl2Cpp]
        public static void Create()
        {
            try
            {
                var existing = GameObject.Find("StatisticMod.VendingSalesTracker");
                if (existing != null)
                {
                    _instance = existing.GetComponent<VendingSalesTracker>();
                    return;
                }

                ClassInjector.RegisterTypeInIl2Cpp<VendingSalesTracker>();

                var go = new GameObject("StatisticMod.VendingSalesTracker");
                UnityEngine.Object.DontDestroyOnLoad(go);
                _instance = go.AddComponent<VendingSalesTracker>();
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning("[VendingStats] Tracker create failed: " + ex.Message);
            }
        }

        private void Awake()
        {
            _instance = this;
        }

        private void Update()
        {
            _timer += Time.unscaledDeltaTime;
            if (_timer < 0.20f) return;
            _timer = 0f;

            SampleInternal();
        }

        [HideFromIl2Cpp]
        public static void SampleNow()
        {
            try
            {
                if (_instance == null)
                {
                    var go = GameObject.Find("StatisticMod.VendingSalesTracker");
                    if (go != null)
                        _instance = go.GetComponent<VendingSalesTracker>();
                }

                _instance?.SampleInternal();
            }
            catch { }
        }

        [HideFromIl2Cpp]
        public static void ResetBaselines()
        {
            _snapshots.Clear();

            if (_instance != null)
            {
                _instance._managerInstanceId = 0;
                _instance._timer = 0f;
            }
        }

        /// <summary>
        /// Records only the native vending revenue transition. Transaction/item
        /// counts are handled by stock sampling, so collecting machine cash cannot
        /// fabricate extra vending transactions.
        /// </summary>
        [HideFromIl2Cpp]
        public static void RecordNativeRevenue(float amount)
        {
            try
            {
                if (amount <= 0.005f) return;
                if (!SalesUnifiedFinal.IsVendingDlcActive()) return;

                int day = 1;
                try
                {
                    var dcm = DayCycleManager.HasInstance
                        ? DayCycleManager.Instance
                        : null;
                    if (dcm != null)
                        day = Math.Max(1, dcm.CurrentDay);
                }
                catch { }

                // Revenue only. Count and item quantity are captured from the
                // vending stock change to remain correct even if the game batches
                // the money transition differently.
                CustomerBasketStore.RecordVendingBatch(day, 0, amount, 0f);

                Plugin.DebugLog(
                    $"[VendingStats] Native revenue transition day={day} amount={amount:0.00}.");
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning("[VendingStats] Native revenue record failed: " + ex.Message);
            }
        }

        [HideFromIl2Cpp]
        private void SampleInternal()
        {
            try
            {
                if (!SalesUnifiedFinal.IsVendingDlcActive())
                {
                    _snapshots.Clear();
                    _managerInstanceId = 0;
                    return;
                }

                if (!VendingMachineManager.HasInstance ||
                    VendingMachineManager.Instance == null)
                    return;

                var manager = VendingMachineManager.Instance;

                int managerId = 0;
                try { managerId = manager.GetInstanceID(); } catch { }

                if (_managerInstanceId != 0 &&
                    managerId != 0 &&
                    managerId != _managerInstanceId)
                {
                    _snapshots.Clear();
                }

                if (managerId != 0)
                    _managerInstanceId = managerId;

                var vendings = manager.Vendings;
                if (vendings == null) return;

                var seen = new HashSet<int>();

                for (int i = 0; i < vendings.Count; i++)
                {
                    VendingMachine vending = null;
                    try { vending = vendings[i]; } catch { }
                    if (vending == null) continue;

                    int uid = 0;
                    try { uid = vending.VendingUniqueID; } catch { }
                    if (uid <= 0)
                    {
                        try { uid = vending.GetInstanceID(); } catch { }
                    }
                    if (uid == 0) continue;

                    seen.Add(uid);

                    int currentStock = ReadStock(vending);

                    if (!_snapshots.TryGetValue(uid, out Snapshot previous))
                    {
                        // First observation is always just a stock baseline.
                        _snapshots[uid] = new Snapshot { Stock = currentStock };
                        continue;
                    }

                    int stockDrop = previous.Stock - currentStock;
                    if (stockDrop > 0)
                    {
                        int day = 1;
                        try
                        {
                            var dcm = DayCycleManager.HasInstance
                                ? DayCycleManager.Instance
                                : null;
                            if (dcm != null)
                                day = Math.Max(1, dcm.CurrentDay);
                        }
                        catch { }

                        // One vending purchase dispenses one product. Revenue is
                        // intentionally zero here; MoneyManager is authoritative.
                        CustomerBasketStore.RecordVendingBatch(
                            day,
                            stockDrop,
                            0f,
                            stockDrop);

                        Plugin.DebugLog(
                            $"[VendingStats] Stock sale detected uid={uid} " +
                            $"tx={stockDrop} stock={previous.Stock}->{currentStock}.");
                    }

                    previous.Stock = currentStock;
                }

                if (_snapshots.Count > seen.Count)
                {
                    var remove = new List<int>();
                    foreach (var pair in _snapshots)
                    {
                        if (!seen.Contains(pair.Key))
                            remove.Add(pair.Key);
                    }

                    for (int i = 0; i < remove.Count; i++)
                        _snapshots.Remove(remove[i]);
                }
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning("[VendingStats] Sample failed: " + ex.Message);
            }
        }

        [HideFromIl2Cpp]
        private static int ReadStock(VendingMachine vending)
        {
            int total = 0;

            try
            {
                var slots = vending.VendingSlots;
                if (slots == null) return 0;

                for (int i = 0; i < slots.Count; i++)
                {
                    VendingSlot slot = null;
                    try { slot = slots[i]; } catch { }
                    if (slot == null) continue;

                    try
                    {
                        total += Math.Max(0, slot.ProductCount);
                    }
                    catch { }
                }
            }
            catch { }

            return total;
        }
    }

    /// <summary>
    /// Exact vending revenue hook. DailyStatisticsManager listens to the same
    /// MoneyManager transition, so Stats & Expiry and the game's day summary
    /// now use the same source of truth.
    /// </summary>
    public static class MoneyManager_VendingRevenue_Patch
    {
        public static void Postfix(float __0, MoneyManager.TransitionType __1)
        {
            try
            {
                if (__1 != MoneyManager.TransitionType.VENDING_MACHINE)
                    return;

                VendingSalesTracker.RecordNativeRevenue(__0);
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning("[VendingStats] MoneyTransition patch failed: " + ex.Message);
            }
        }
    }

    // Compatibility signal patches. They now force only a stock sample;
    // CollectedMoney is no longer read or used as revenue.
    public static class VendingMoneyChanged_Patch
    {
        public static void Postfix()
        {
            VendingSalesTracker.SampleNow();
        }
    }

    public static class VendingCollectMoney_Patch
    {
        public static void Prefix()
        {
            VendingSalesTracker.SampleNow();
        }
    }

    public static class VendingNpcSignal_Patch
    {
        public static void Postfix()
        {
            VendingSalesTracker.SampleNow();
        }
    }
}
