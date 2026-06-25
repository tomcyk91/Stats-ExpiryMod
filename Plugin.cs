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
    [BepInPlugin("StatsandExpiryMod", "Stats & Expiration Mod", "2.3.6")]
    public class Plugin : BasePlugin
    {
        public static bool IsPolish = true;
        public static string T(string pl, string en) => IsPolish ? pl : en;

        public static bool EnableLogs = false;

        internal static new ManualLogSource Log;
        internal static ProductVisualCache ProductCache;
        private Harmony _harmony;

        // ==========================================
        // DEWELOPERKA SMART EXPIRATION
        // ==========================================
        private static readonly bool _enableDebugF9 = true;

        public static void DebugLog(string message)
        {
            if (EnableLogs && Log != null)
                Log.LogInfo(message);
        }

        public static void DebugWarning(string message)
        {
            if (EnableLogs && Log != null)
                Log.LogWarning(message);
        }

        public static void DebugError(string message)
        {
            if (EnableLogs && Log != null)
                Log.LogError(message);
        }

        // ⚡ TARCZA ABSOLUTNA - Naprawia laga Warehouse Refill
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
            try
            {
                _harmony.Patch(original, prefix, postfix);
            }
            catch (Exception ex)
            {
                DebugError($"[Plugin] Failed to patch {original.Name}: {ex.Message}");
            }
        }

        public override void Load()
        {
            Log = base.Log;
            SmartExpiration.SEProfiler.Init(); // PROFILER: log klatek+sekcji co 2s (SEprof). Wylacz: SEProfiler.Enabled=false
            Log.LogInfo("[Supermarket Overhaul] Starting loading mod (Stats + Expiration) v2.3.6-prof...");

            // 1. ENABLING ABSOLUTE SHIELD
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

            // 2. INITIALIZING STATISTICS
            ProductCache = new ProductVisualCache();
            StatsRunner.Create();

            // 3. LOADING SMART EXPIRATION CONFIGURATION    
            SmartExpiration.PluginConfig.BindConfig(Config);
            CustomExpirationLoader.Load();

            // 4. REGISTERING IL2CPP COMPONENTS FOR BOTH MODS
            ClassInjector.RegisterTypeInIl2Cpp<ProductExpirationComponent>();
            ClassInjector.RegisterTypeInIl2Cpp<SmartExpiration.Patches.BoxExpirationLabel>();
            ClassInjector.RegisterTypeInIl2Cpp<TrashBoxComponent>();
            ClassInjector.RegisterTypeInIl2Cpp<TrashBoxSpawner>();
            ClassInjector.RegisterTypeInIl2Cpp<ExpirationEngine>();
            ClassInjector.RegisterTypeInIl2Cpp<SmartExpiration.Patches.BoxLabelGlobalUpdater>();

            if (_enableDebugF9)
                ClassInjector.RegisterTypeInIl2Cpp<F9DaySkipper>();

            // 5. CREATING CONTROLLERS (GAME OBJECTS) FOR EXPIRATION
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

            // 6. MANUAL HARMONY PATCHING (BOTH MODS)
            _harmony = new Harmony("StatsandExpiryMod");

            // --- A) PATCHES STATISTIC MOD ---
            TryPatch(AccessTools.DeclaredMethod(typeof(CheckoutScreen), "AddProduct"), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(CheckoutScreen_AddProduct_Patch), "Postfix")));

            TryPatch(AccessTools.DeclaredMethod(typeof(Checkout), "StartCheckout"), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Checkout_StartCheckout_Patch), "Postfix")));

            TryPatch(AccessTools.DeclaredMethod(typeof(CheckoutScreen), "Clear"), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(CheckoutScreen_Clear_Patch), "Prefix")), null);

            string[] possibleNames =
            {
                "TookCustomersCash",
                "TookCustomersCard",
                "FinishCheckout",
                "CompleteCheckout",
                "CashierCompletedCheckout"
            };

            var paymentPrefix = new HarmonyMethod(AccessTools.DeclaredMethod(typeof(DynamicPaymentHooks), "Prefix"));
            foreach (var name in possibleNames)
            {
                var m = AccessTools.DeclaredMethod(typeof(Checkout), name);
                if (m != null)
                    TryPatch(m, paymentPrefix, null);
            }

            TryPatch(AccessTools.DeclaredMethod(typeof(OnlineOrderInteraction), "OnPaperBagProductAdded"), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(OnlineOrder_AddProduct_Patch), "Postfix")));

            TryPatch(AccessTools.DeclaredMethod(typeof(OnlineOrderInteraction), "DeliverOrder"), new HarmonyMethod(AccessTools.DeclaredMethod(typeof(OnlineOrder_Deliver_Patch), "Prefix")), null);

            TryPatch(AccessTools.DeclaredMethod(typeof(DayCycleManager), "Awake"), null, new HarmonyMethod(AccessTools.DeclaredMethod(typeof(DayCycleOverlayPatch), "Postfix")));

            // --- B) PATCHES SMART EXPIRATION ---
            TryPatch(AccessTools.Method(typeof(Box), "Start"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.BoxLabelPatch), "Postfix")));

            TryPatch(AccessTools.Method(typeof(Box), "AddProduct"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.BoxPatches), "AddProduct_Prefix")), null);

            TryPatch(AccessTools.Method(typeof(Box), "GetProductFromBox"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.BoxPatches), "GetProductFromBox_Postfix")));

            TryPatch(AccessTools.Method(typeof(DayCycleManager), "FinishTheDay"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DailySpoilagePatches), "FinishTheDay_Postfix")));

            TryPatch(AccessTools.Method(typeof(DisplaySlot), "TakeProductFromDisplay"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DisplaySlotPatches), "TakeProductFromDisplay_Prefix")), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DisplaySlotPatches), "TakeProductFromDisplay_Postfix")));

            TryPatch(AccessTools.Method(typeof(DisplaySlot), "AddProduct"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DisplaySlotPatches), "AddProduct_Prefix")), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.DisplaySlotPatches), "AddProduct_Postfix")));

            TryPatch(AccessTools.Method(typeof(SaveManager), "Save", new Type[] { typeof(SaveInfo) }), null, new HarmonyMethod(typeof(SmartExpiration.Patches.SaveManager_Save_SaveInfo_Patch), "Postfix"));

            TryPatch(AccessTools.Method(typeof(SaveManager), "Save", new Type[] { typeof(string) }), null, new HarmonyMethod(typeof(SmartExpiration.Patches.SaveManager_Save_String_Patch), "Postfix"));

            TryPatch(AccessTools.Method(typeof(SaveManager), "Save", Type.EmptyTypes), null, new HarmonyMethod(typeof(SmartExpiration.Patches.SaveManager_Save_NoArgs_Patch), "Postfix"));

            TryPatch(AccessTools.Method(typeof(SaveManager), "ApplySaveData", Type.EmptyTypes), null, new HarmonyMethod(typeof(SmartExpiration.Patches.SaveManager_ApplySaveData_Patch), "Postfix"));

            TryPatch(AccessTools.Method(typeof(SettingPriceCanvas), "OpenMenu"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.PriceCanvasPatches), "OpenMenu_Postfix")));

            TryPatch(AccessTools.Method(typeof(SettingPriceCanvas), "CloseMenu"), null, new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.PriceCanvasPatches), "CloseMenu_Postfix")));

            TryPatch(AccessTools.Method(typeof(DayCycleManager), "FinishTheDay"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.OvernightWorkersIntegration), "Prefix_BeforeOvernight")), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.OvernightWorkersIntegration), "Postfix_AfterOvernight")));

            TryPatch(AccessTools.Method(typeof(BoxInteraction), "TryTakeProductFromSlot"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.TrashBox_Take_Patch), "Prefix")), null);

            TryPatch(AccessTools.Method(typeof(BoxInteraction), "ThrowIntoTrashBin"), new HarmonyMethod(AccessTools.Method(typeof(SmartExpiration.Patches.TrashBox_Final_Patch), "Prefix")), null);

            Log.LogInfo("[Supermarket Overhaul] Successfully loaded!");
        }
    }
}