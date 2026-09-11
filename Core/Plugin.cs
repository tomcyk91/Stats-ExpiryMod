using BepInEx;
using BepInEx.IL2CPP;
using BepInEx.Logging;
using BepInEx.Unity.IL2CPP;
using HarmonyLib;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using System;
using SmartExpiration;
using SmartExpiration.Patches;

namespace StatisticMod
{
    [BepInPlugin("StatsandExpiryMod", "Stats & Expiration Mod", "2.6.0")]
    public class Plugin : BasePlugin
    {
        // Zachowane dla kompatybilności z wcześniejszym kodem moda.
        // Wartość jest aktualizowana automatycznie na podstawie języka gry.
        public static bool IsPolish = false;

        public static string T(string pl, string en) => ModLocalization.Translate(pl, en);
        public static string DayLabel(int day) => ModLocalization.DayLabel(day);
        public static string DayShortLabel(int day) => ModLocalization.DayShortLabel(day);
        public static string InDays(int days) => ModLocalization.InDays(days);
        public static string DaysCount(int days) => ModLocalization.DaysCount(days);
        public static string LocalizedProductName(int productId, ProductSO fallback = null) => ModLocalization.ProductName(productId, fallback);
        public static string ProductFallback(int productId) => ModLocalization.ProductFallback(productId);
        public static string UnknownId(int productId) => ModLocalization.UnknownId(productId);
        public static string BuyShortLabel => ModLocalization.BuyShortLabel;
        public static string SellShortLabel => ModLocalization.SellShortLabel;
        public static string ShopShortLabel => ModLocalization.ShopShortLabel;
        public static string WarehouseShortLabel => ModLocalization.WarehouseShortLabel;
        public static string Money(float value, int decimals = 2, bool useGrouping = false) => CurrencyDisplay.Format(value, decimals, useGrouping);
        public static string CurrencyUnitLabel => CurrencyDisplay.UnitLabel;

        public static bool EnableLogs = false;

        internal static new ManualLogSource Log;
        internal static ProductVisualCache ProductCache;
        private Harmony _harmony;

        private static readonly bool _enableDebugF9 = false;

        public static void DebugLog(string message)
        {
            if (EnableLogs && Log != null) Log.LogInfo(message);
        }

        public static void DebugWarning(string message)
        {
            if (EnableLogs && Log != null) Log.LogWarning(message);
        }

        public static void DebugError(string message)
        {
            if (EnableLogs && Log != null) Log.LogError(message);
        }

        public static bool TypeByNameShield(string name, ref Type __result)
        {
            if (name != null && name.Contains("BepInEx.ThreadingHelper"))
            {
                __result = null;
                return false;
            }
            return true;
        }

        private void TryPatch(System.Reflection.MethodInfo original, HarmonyMethod prefix, HarmonyMethod postfix)
        {
            if (original == null)
            {
                DebugWarning("[Plugin] Skipping patch - target method not found in this game version.");
                return;
            }
            try { _harmony.Patch(original, prefix, postfix); }
            catch (Exception ex) { DebugError($"[Plugin] Failed to patch {original.Name}: {ex.Message}"); }
        }

        public override void Load()
        {
            Log = base.Log;
            ModLocalization.Initialize();

            SmartExpiration.PluginConfig.BindConfig(Config);
            bool expiryEnabled = SmartExpiration.PluginConfig.ExpiryEnabled;

            if (expiryEnabled)
                SmartExpiration.SEProfiler.Init();

            Log.LogInfo("[Supermarket Overhaul] Starting loading mod (Stats + Expiration) v2.6.0 UI POLISH...");

            try
            {
                var shieldHarmony = new Harmony("statisticmod.shield");
                var targetMethod = AccessTools.DeclaredMethod(typeof(AccessTools), "TypeByName");
                var prefixMethod = AccessTools.DeclaredMethod(typeof(Plugin), nameof(TypeByNameShield));
                if (targetMethod != null && prefixMethod != null)
                    shieldHarmony.Patch(targetMethod, new HarmonyMethod(prefixMethod));
            }
            catch (Exception ex)
            {
                DebugWarning($"[Plugin] Shield patch failed: {ex.Message}");
            }

            ProductCache = new ProductVisualCache();
            StatsRunner.Create();
            VendingSalesTracker.Create();

            if (expiryEnabled)
            {
                CustomExpirationLoader.Load();

                ClassInjector.RegisterTypeInIl2Cpp<ProductExpirationComponent>();
                ClassInjector.RegisterTypeInIl2Cpp<SmartExpiration.Patches.BoxExpirationLabel>();
                ClassInjector.RegisterTypeInIl2Cpp<TrashBoxComponent>();
                ClassInjector.RegisterTypeInIl2Cpp<TrashBoxSpawner>();
                ClassInjector.RegisterTypeInIl2Cpp<ExpirationEngine>();
                ClassInjector.RegisterTypeInIl2Cpp<SmartExpiration.Patches.BoxLabelGlobalUpdater>();

                if (_enableDebugF9)
                    ClassInjector.RegisterTypeInIl2Cpp<F9DaySkipper>();

                var engineGo = new GameObject("SmartExpirationEngine");
                UnityEngine.Object.DontDestroyOnLoad(engineGo);
                engineGo.AddComponent<ExpirationEngine>();

                var trashObj = new GameObject("TrashBoxSpawner");
                UnityEngine.Object.DontDestroyOnLoad(trashObj);
                trashObj.AddComponent<TrashBoxSpawner>();

                if (_enableDebugF9)
                {
                    GameObject debugObj = new GameObject("SmartExpiration_DebugHelper");
                    UnityEngine.Object.DontDestroyOnLoad(debugObj);
                    debugObj.AddComponent<F9DaySkipper>();
                    Log.LogWarning("⚠️ WARNING: Developer mode (F9) with Expiration is ENABLED!");
                }
            }
            else
            {
                SmartExpiration.ExpirationSaveManager.ClearAllExpirationSaveFiles();
                Log.LogInfo("[Stats & Expiry] Expiration system is disabled. Statistics and analytics remain active.");
                Log.LogInfo("[Stats & Expiry] Expiration save data was reset. Re-enabling expiry later will generate fresh dates from the current in-game day.");
            }

            _harmony = new Harmony("StatsandExpiryMod");

            TryPatch(AccessTools.DeclaredMethod(typeof(CheckoutScreen), "AddProduct"), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(CheckoutScreen_AddProduct_Patch), "Postfix")));
            TryPatch(AccessTools.DeclaredMethod(typeof(Checkout), "StartCheckout"), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_StartCheckout_Patch), "Postfix")));
            TryPatch(AccessTools.DeclaredMethod(typeof(CheckoutScreen), "Clear"), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(CheckoutScreen_Clear_Patch), "Prefix")), null);

            // Payment method capture. Do not finalize a basket from TookCustomers*
            // because those methods can run before the game has confirmed the
            // payment. Exact completion hooks below record only successful payments.
            TryPatch(
                AccessTools.DeclaredMethod(typeof(Checkout), "TookCustomersCard"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_MarkCard_Patch), "Prefix")),
                null);
            TryPatch(
                AccessTools.DeclaredMethod(typeof(Checkout), "TookCustomersCard_Order"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_MarkCard_Patch), "Prefix")),
                null);
            TryPatch(
                AccessTools.DeclaredMethod(typeof(Checkout), "TookCustomersCash"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_MarkCash_Patch), "Prefix")),
                null);
            TryPatch(
                AccessTools.DeclaredMethod(typeof(Checkout), "TookCustomersCash_Order"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_MarkCash_Patch), "Prefix")),
                null);

            TryPatch(
                AccessTools.DeclaredMethod(typeof(Checkout), "CashierTookPayment"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_CashierPaymentMethod_Patch), "Prefix")),
                null);
            TryPatch(
                AccessTools.DeclaredMethod(typeof(Checkout), "FinishScanning_Order"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_FinishScanningOrder_PaymentMethod_Patch), "Prefix")),
                null);

            TryPatch(
                AccessTools.DeclaredMethod(typeof(Checkout), "TryFinishingCardPayment"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_CardPaymentCompleted_Patch), "Prefix")),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_CardPaymentCompleted_Patch), "Postfix")));
            TryPatch(
                AccessTools.DeclaredMethod(typeof(Checkout), "FinishingCardPayment_Order"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_CardPaymentCompleted_Patch), "Prefix")),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_CardPaymentCompleted_Patch), "Postfix")));
            TryPatch(
                AccessTools.DeclaredMethod(typeof(Checkout), "TryFinishingCashPayment"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_CashPaymentCompleted_Patch), "Prefix")),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_CashPaymentCompleted_Patch), "Postfix")));
            TryPatch(
                AccessTools.DeclaredMethod(typeof(Checkout), "TryFinishingCashPayment_Order"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_CashPaymentCompleted_Patch), "Prefix")),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_CashPaymentCompleted_Patch), "Postfix")));

            TryPatch(
                AccessTools.DeclaredMethod(typeof(Checkout), "CashierCompletedCheckout"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_Completed_Patch), "Prefix")),
                null);
            TryPatch(
                AccessTools.DeclaredMethod(typeof(Checkout), "FinishSelfCheckoutOrder"),
                null,
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_SelfCheckoutCompleted_Patch), "Postfix")));

            TryPatch(AccessTools.DeclaredMethod(typeof(OnlineOrderInteraction), "OnPaperBagProductAdded"), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(OnlineOrder_AddProduct_Patch), "Postfix")));
            TryPatch(AccessTools.DeclaredMethod(typeof(OnlineOrderInteraction), "DeliverOrder"), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(OnlineOrder_Deliver_Patch), "Prefix")), null);

            // Exact completed online-order transaction (price + item count).
            TryPatch(
                AccessTools.DeclaredMethod(typeof(OnlineOrderCustomer), "DeliverOrder"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(OnlineOrderCustomer_DeliverTransaction_Patch), "Prefix")),
                null);

            // Vending analytics. Revenue is taken from MoneyManager.MoneyTransition
            // with TransitionType.VENDING_MACHINE - the same native source used by
            // DailyStatisticsManager for the game's day summary. Stock sampling is
            // kept only for vending transaction/item counts.
            TryPatch(
                AccessTools.DeclaredMethod(
                    typeof(MoneyManager),
                    "MoneyTransition",
                    new Type[] { typeof(float), typeof(MoneyManager.TransitionType), typeof(bool) }),
                null,
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(MoneyManager_VendingRevenue_Patch), "Postfix")));

            TryPatch(
                AccessTools.DeclaredMethod(typeof(VendingSlot), "TakeProductFromVending"),
                null,
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(VendingPurchaseAnalytics_Patch), "Postfix")));

            TryPatch(
                AccessTools.DeclaredMethod(typeof(VendingSlot), "NPCBuyProductAnimated"),
                null,
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(VendingNpcSignal_Patch), "Postfix")));

            TryPatch(
                AccessTools.DeclaredMethod(typeof(VendingSlot), "NPCBuyProductIdle"),
                null,
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(VendingNpcSignal_Patch), "Postfix")));

            TryPatch(
                AccessTools.DeclaredMethod(typeof(VendingSlot), "OnButtonPushed"),
                null,
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(VendingNpcSignal_Patch), "Postfix")));

            TryPatch(
                AccessTools.DeclaredMethod(typeof(VendingMachine), "SetCollectedMoney"),
                null,
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(VendingMoneyChanged_Patch), "Postfix")));

            TryPatch(
                AccessTools.DeclaredMethod(typeof(VendingMachine), "CollectMoney"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(VendingCollectMoney_Patch), "Prefix")),
                null);
            TryPatch(AccessTools.DeclaredMethod(typeof(DayCycleManager), "Awake"), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(DayCycleOverlayPatch), "Postfix")));

            TryPatch(AccessTools.DeclaredMethod(typeof(Customer), "StartShopping", Type.EmptyTypes), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Customer_StartShopping_DemandPatch), "Postfix")));
            TryPatch(AccessTools.DeclaredMethod(typeof(Customer), "TakeProduct", new Type[] { typeof(DisplaySlot), typeof(int) }), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Customer_TakeProduct_DemandPatch), "Postfix")));
            TryPatch(AccessTools.DeclaredMethod(typeof(Customer), "CheckForProductsMissing", new Type[] { typeof(bool) }), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Customer_CheckMissing_DemandPatch), "Prefix")), null);
            TryPatch(AccessTools.DeclaredMethod(typeof(Customer), "FinishShopping", new Type[] { typeof(bool) }), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Customer_FinishShopping_DemandPatch), "Prefix")), null);
            TryPatch(AccessTools.DeclaredMethod(typeof(Customer), "ResetCustomer", Type.EmptyTypes), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Customer_Reset_DemandPatch), "Postfix")));

            // Finalne dane, które gra przekazuje do ekranu podsumowania dnia.
            TryPatch(
                AccessTools.DeclaredMethod(typeof(DailyStatisticsScreen), "ApplyStatistics"),
                new HarmonyMethod(AccessTools.DeclaredMethod(typeof(DailyStatisticsScreen_ApplyStatistics_Patch), "Prefix")),
                null);

            if (expiryEnabled)
            {
                TryPatch(AccessTools.Method(typeof(Box), "Start"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.BoxLabelPatch), "Postfix")));
                TryPatch(AccessTools.Method(typeof(Box), "AddProduct"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.BoxPatches), "AddProduct_Prefix")), null);
                TryPatch(AccessTools.Method(typeof(Box), "GetProductFromBox"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.BoxPatches), "GetProductFromBox_Postfix")));
                var spoilFinishTarget = AccessTools.Method(typeof(DayCycleManager), "FinishTheDay");
                var spoilNextDayTarget = AccessTools.Method(typeof(DayCycleManager), "StartNextDay");

                Log.LogInfo(
                    "[SpoilageHook] targets: FinishTheDay=" + (spoilFinishTarget != null) +
                    ", StartNextDay=" + (spoilNextDayTarget != null));

                TryPatch(
                    spoilFinishTarget,
                    new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DailySpoilagePatches), "FinishTheDay_Prefix")),
                    new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DailySpoilagePatches), "FinishTheDay_Postfix")));

                TryPatch(
                    spoilNextDayTarget,
                    null,
                    new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DailySpoilagePatches), "StartNextDay_Postfix")));
                TryPatch(AccessTools.Method(typeof(DisplaySlot), "TakeProductFromDisplay"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DisplaySlotPatches), "TakeProductFromDisplay_Prefix")), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DisplaySlotPatches), "TakeProductFromDisplay_Postfix")));
                TryPatch(AccessTools.Method(typeof(DisplaySlot), "AddProduct"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DisplaySlotPatches), "AddProduct_Prefix")), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DisplaySlotPatches), "AddProduct_Postfix")));
            }
            // Reset wszystkich danych statystycznych przy tworzeniu NOWEJ gry.
            // SaveInfo daje pewny SlotIndex, więc inne sloty pozostają nietknięte.
            TryPatch(
                AccessTools.Method(typeof(SaveManager), "CreateLoadNewSave", new Type[] { typeof(SaveInfo) }),
                new HarmonyMethod(AccessTools.Method(typeof(SaveManager_CreateLoadNewSave_StatisticsReset_Patch), "Prefix")),
                null);
            TryPatch(
                AccessTools.Method(typeof(SaveManager), "CreateLoadNewSave_MP", new Type[] { typeof(SaveInfo) }),
                new HarmonyMethod(AccessTools.Method(typeof(SaveManager_CreateLoadNewSaveMP_StatisticsReset_Patch), "Prefix")),
                null);
            TryPatch(
                AccessTools.Method(typeof(DailyStatisticsScreen), "StartNewGame", Type.EmptyTypes),
                new HarmonyMethod(AccessTools.Method(typeof(DailyStatisticsScreen_StartNewGame_StatisticsReset_Patch), "Prefix")),
                null);
            TryPatch(
                AccessTools.Method(typeof(BankruptcyCanvas), "StartNewGame", Type.EmptyTypes),
                new HarmonyMethod(AccessTools.Method(typeof(BankruptcyCanvas_StartNewGame_StatisticsReset_Patch), "Prefix")),
                null);

            // These save hooks stay active even in Stats-only mode because they
            // are also responsible for persisting statistics/analytics. The patch
            // methods themselves skip only the expiration sidecar when expiry is off.
            TryPatch(AccessTools.Method(typeof(SaveManager), "Save", new Type[] { typeof(SaveInfo) }), null, new HarmonyMethod(typeof(SmartExpiration.Patches.SaveManager_Save_SaveInfo_Patch), "Postfix"));
            TryPatch(AccessTools.Method(typeof(SaveManager), "Save", new Type[] { typeof(string) }), null, new HarmonyMethod(typeof(SmartExpiration.Patches.SaveManager_Save_String_Patch), "Postfix"));
            TryPatch(AccessTools.Method(typeof(SaveManager), "Save", Type.EmptyTypes), null, new HarmonyMethod(typeof(SmartExpiration.Patches.SaveManager_Save_NoArgs_Patch), "Postfix"));
            TryPatch(AccessTools.Method(typeof(SaveManager), "ApplySaveData", Type.EmptyTypes), null, new HarmonyMethod(typeof(SmartExpiration.Patches.SaveManager_ApplySaveData_Patch), "Postfix"));

            if (expiryEnabled)
            {
                TryPatch(AccessTools.Method(typeof(SettingPriceCanvas), "OpenMenu"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.PriceCanvasPatches), "OpenMenu_Postfix")));
                TryPatch(AccessTools.Method(typeof(SettingPriceCanvas), "CloseMenu"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.PriceCanvasPatches), "CloseMenu_Postfix")));
                TryPatch(AccessTools.Method(typeof(DayCycleManager), "FinishTheDay"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.OvernightWorkersIntegration), "Prefix_BeforeOvernight")), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.OvernightWorkersIntegration), "Postfix_AfterOvernight")));
                TryPatch(AccessTools.Method(typeof(BoxInteraction), "TryTakeProductFromSlot"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.TrashBox_Take_Patch), "Prefix")), null);
                TryPatch(AccessTools.Method(typeof(BoxInteraction), "ThrowIntoTrashBin"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.TrashBox_Final_Patch), "Prefix")), null);
            }
            // Ice cream: record a sale only when a customer receives a fully correct
            // IceCreamStatus.  The patch also captures the real ingredient COGS
            // (cone + every flavour scoop) for the Profitability tab.
            TryPatch(
                AccessTools.Method(typeof(Customer), "DeliverIceCream", new Type[] { typeof(IceCreamStatus) }),
                new HarmonyMethod(AccessTools.Method(typeof(IceCream_Sales_Patch), "Prefix")),
                new HarmonyMethod(AccessTools.Method(typeof(IceCream_Sales_Patch), "Postfix")));

            Log.LogInfo("[Supermarket Overhaul] Successfully loaded!");
        }
    }
}
