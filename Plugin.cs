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
    [BepInPlugin("StatsandExpiryMod", "Stats & Expiration Mod", "2.4.3")]
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
            SmartExpiration.SEProfiler.Init();
            Log.LogInfo("[Supermarket Overhaul] Starting loading mod (Stats + Expiration) v2.4.3 PERF2...");

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

            SmartExpiration.PluginConfig.BindConfig(Config);
            CustomExpirationLoader.Load();

            ClassInjector.RegisterTypeInIl2Cpp<ProductExpirationComponent>();
            ClassInjector.RegisterTypeInIl2Cpp<SmartExpiration.Patches.BoxExpirationLabel>();
            ClassInjector.RegisterTypeInIl2Cpp<TrashBoxComponent>();
            ClassInjector.RegisterTypeInIl2Cpp<TrashBoxSpawner>();
            ClassInjector.RegisterTypeInIl2Cpp<ExpirationEngine>();
            ClassInjector.RegisterTypeInIl2Cpp<SmartExpiration.Patches.BoxLabelGlobalUpdater>();

            if (_enableDebugF9) ClassInjector.RegisterTypeInIl2Cpp<F9DaySkipper>();

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

            _harmony = new Harmony("StatsandExpiryMod");

            TryPatch(AccessTools.DeclaredMethod(typeof(CheckoutScreen), "AddProduct"), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(CheckoutScreen_AddProduct_Patch), "Postfix")));
            TryPatch(AccessTools.DeclaredMethod(typeof(Checkout), "StartCheckout"), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_StartCheckout_Patch), "Postfix")));
            TryPatch(AccessTools.DeclaredMethod(typeof(CheckoutScreen), "Clear"), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(CheckoutScreen_Clear_Patch), "Prefix")), null);

            string[] possibleNames =
            {
                "TookCustomersCash", "TookCustomersCard", "FinishCheckout",
                "CompleteCheckout", "CashierCompletedCheckout"
            };

            var paymentPrefix = new HarmonyMethod(AccessTools.DeclaredMethod(typeof(DynamicPaymentHooks), "Prefix"));
            foreach (var name in possibleNames)
            {
                var m = AccessTools.DeclaredMethod(typeof(Checkout), name);
                if (m != null) TryPatch(m, paymentPrefix, null);
            }

            TryPatch(AccessTools.DeclaredMethod(typeof(OnlineOrderInteraction), "OnPaperBagProductAdded"), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(OnlineOrder_AddProduct_Patch), "Postfix")));
            TryPatch(AccessTools.DeclaredMethod(typeof(OnlineOrderInteraction), "DeliverOrder"), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(OnlineOrder_Deliver_Patch), "Prefix")), null);
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

            TryPatch(AccessTools.Method(typeof(Box), "Start"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.BoxLabelPatch), "Postfix")));
            TryPatch(AccessTools.Method(typeof(Box), "AddProduct"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.BoxPatches), "AddProduct_Prefix")), null);
            TryPatch(AccessTools.Method(typeof(Box), "GetProductFromBox"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.BoxPatches), "GetProductFromBox_Postfix")));
            TryPatch(AccessTools.Method(typeof(DayCycleManager), "FinishTheDay"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DailySpoilagePatches), "FinishTheDay_Postfix")));
            TryPatch(AccessTools.Method(typeof(DisplaySlot), "TakeProductFromDisplay"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DisplaySlotPatches), "TakeProductFromDisplay_Prefix")), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DisplaySlotPatches), "TakeProductFromDisplay_Postfix")));
            TryPatch(AccessTools.Method(typeof(DisplaySlot), "AddProduct"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DisplaySlotPatches), "AddProduct_Prefix")), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DisplaySlotPatches), "AddProduct_Postfix")));
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

            TryPatch(AccessTools.Method(typeof(SaveManager), "Save", new Type[] { typeof(SaveInfo) }), null, new HarmonyMethod(typeof(SmartExpiration.Patches.SaveManager_Save_SaveInfo_Patch), "Postfix"));
            TryPatch(AccessTools.Method(typeof(SaveManager), "Save", new Type[] { typeof(string) }), null, new HarmonyMethod(typeof(SmartExpiration.Patches.SaveManager_Save_String_Patch), "Postfix"));
            TryPatch(AccessTools.Method(typeof(SaveManager), "Save", Type.EmptyTypes), null, new HarmonyMethod(typeof(SmartExpiration.Patches.SaveManager_Save_NoArgs_Patch), "Postfix"));
            TryPatch(AccessTools.Method(typeof(SaveManager), "ApplySaveData", Type.EmptyTypes), null, new HarmonyMethod(typeof(SmartExpiration.Patches.SaveManager_ApplySaveData_Patch), "Postfix"));
            TryPatch(AccessTools.Method(typeof(SettingPriceCanvas), "OpenMenu"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.PriceCanvasPatches), "OpenMenu_Postfix")));
            TryPatch(AccessTools.Method(typeof(SettingPriceCanvas), "CloseMenu"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.PriceCanvasPatches), "CloseMenu_Postfix")));
            TryPatch(AccessTools.Method(typeof(DayCycleManager), "FinishTheDay"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.OvernightWorkersIntegration), "Prefix_BeforeOvernight")), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.OvernightWorkersIntegration), "Postfix_AfterOvernight")));
            TryPatch(AccessTools.Method(typeof(BoxInteraction), "TryTakeProductFromSlot"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.TrashBox_Take_Patch), "Prefix")), null);
            TryPatch(AccessTools.Method(typeof(BoxInteraction), "ThrowIntoTrashBin"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.TrashBox_Final_Patch), "Prefix")), null);
            TryPatch(AccessTools.Method(typeof(IceCreamManager), "CalculatePrice"), null, new HarmonyMethod(AccessTools.Method(typeof(IceCream_Sales_Patch), "Postfix")));

            Log.LogInfo("[Supermarket Overhaul] Successfully loaded!");
        }
    }
}
