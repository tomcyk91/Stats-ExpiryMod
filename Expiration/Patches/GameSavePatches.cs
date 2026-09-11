using BepInEx.Unity.IL2CPP.Utils;
using HarmonyLib;
using System;
using UnityEngine;

namespace SmartExpiration.Patches
{
    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), new Type[] { typeof(SaveInfo) })]
    internal static class SaveManager_Save_SaveInfo_Patch
    {
        // B3 FIX: Tarcza ochronna przed usunięciem przeciążenia Save(SaveInfo)
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(SaveManager), "Save", new[] { typeof(SaveInfo) }) != null;
        }

        public static void Postfix()
        {
            try
            {
                StatisticMod.StatsStore.SaveNow();
                StatisticMod.BusinessAnalysisStore.SaveNow();
                StatisticMod.DailySummaryStore.SaveNow();
                StatisticMod.VendingSalesTracker.SampleNow();
                StatisticMod.CustomerBasketStore.SaveNow();
                StatisticMod.Plugin.DebugLog("[GameSavePatches] Zapisano statystyki (SaveInfo).");

                if (PluginConfig.ExpiryEnabled)
                {
                    StatisticMod.Plugin.DebugLog("[GameSavePatches] Save(SaveInfo) -> ExpirationSaveManager.SaveData()");
                    ExpirationSaveManager.SaveData();
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError($"[GameSavePatches] Błąd po Save(SaveInfo): {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), new Type[] { typeof(string) })]
    internal static class SaveManager_Save_String_Patch
    {
        // B3 FIX: Tarcza ochronna przed usunięciem przeciążenia Save(string)
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(SaveManager), "Save", new[] { typeof(string) }) != null;
        }

        public static void Postfix()
        {
            try
            {
                StatisticMod.StatsStore.SaveNow();
                StatisticMod.BusinessAnalysisStore.SaveNow();
                StatisticMod.DailySummaryStore.SaveNow();
                StatisticMod.VendingSalesTracker.SampleNow();
                StatisticMod.CustomerBasketStore.SaveNow();
                StatisticMod.Plugin.DebugLog("[GameSavePatches] Zapisano statystyki (String).");

                if (PluginConfig.ExpiryEnabled)
                {
                    StatisticMod.Plugin.DebugLog("[GameSavePatches] Save(string) -> ExpirationSaveManager.SaveData()");
                    ExpirationSaveManager.SaveData();
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError($"[GameSavePatches] Błąd po Save(string): {ex}");
            }
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.Save), new Type[] { })]
    internal static class SaveManager_Save_NoArgs_Patch
    {
        // B3 FIX: Tarcza ochronna przed usunięciem przeciążenia bezparametrowego Save()
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(SaveManager), "Save", Type.EmptyTypes) != null;
        }

        public static void Postfix()
        {
            try
            {
                StatisticMod.StatsStore.SaveNow();
                StatisticMod.BusinessAnalysisStore.SaveNow();
                StatisticMod.DailySummaryStore.SaveNow();
                StatisticMod.VendingSalesTracker.SampleNow();
                StatisticMod.CustomerBasketStore.SaveNow();
                StatisticMod.Plugin.DebugLog("[GameSavePatches] Zapisano statystyki (NoArgs).");

                if (PluginConfig.ExpiryEnabled)
                {
                    StatisticMod.Plugin.DebugLog("[GameSavePatches] Save() -> ExpirationSaveManager.SaveData()");
                    ExpirationSaveManager.SaveData();
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError($"[GameSavePatches] Błąd po Save(): {ex}");
            }
        }
    }

    // ========================================================================
    // ASEKURACJA DLA KLAWISZA F5
    // ========================================================================
    [HarmonyPatch(typeof(SaveManager), "QuickSave")]
    internal static class SaveManager_QuickSave_Patch
    {
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(SaveManager), "QuickSave") != null;
        }

        public static void Postfix()
        {
            try
            {
                StatisticMod.StatsStore.SaveNow();
                StatisticMod.BusinessAnalysisStore.SaveNow();
                StatisticMod.DailySummaryStore.SaveNow();
                StatisticMod.VendingSalesTracker.SampleNow();
                StatisticMod.CustomerBasketStore.SaveNow();
                StatisticMod.Plugin.DebugLog("[GameSavePatches] Zapisano statystyki (QuickSave / F5).");

                if (PluginConfig.ExpiryEnabled)
                    ExpirationSaveManager.SaveData();
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError($"[GameSavePatches] Błąd po QuickSave(): {ex}");
            }
        }
    }

    // ========================================================================
    // WCZYTYWANIE GRY
    // ========================================================================
    [HarmonyPatch(typeof(SaveManager), "ApplySaveData", new Type[] { })]
    internal static class SaveManager_ApplySaveData_Patch
    {
        // B3 FIX: Tarcza ochronna przed zmianami w wewnętrznym skrypcie wczytywania gry
        public static bool Prepare()
        {
            return AccessTools.Method(typeof(SaveManager), "ApplySaveData", Type.EmptyTypes) != null;
        }

        public static void Postfix()
        {
            try
            {
                StatisticMod.Plugin.DebugLog("========================================");
                StatisticMod.Plugin.DebugLog("[GameSavePatches] ApplySaveData() zakończone. Wczytuję dane expiration...");

                try
                {
                    StatisticMod.StatsStore.RedetectSlotForce();

                    // ApplySaveData means the game has returned to the state stored
                    // in the save file. Reload every statistics sidecar as well,
                    // including same-slot reloads, so unsaved runtime stats vanish.
                    StatisticMod.StatsStore.Load();
                    StatisticMod.BusinessAnalysisStore.Load();
                    StatisticMod.DailySummaryStore.Load();
                    StatisticMod.CustomerBasketStore.ReloadCommitted();
                    StatisticMod.VendingSalesTracker.ResetBaselines();
                    StatisticMod.StatsRunner.NotifyGameLoaded();

                    StatisticMod.Plugin.Log.LogInfo($"[GameSavePatches] Po ApplySaveData -> AKTYWNY SLOT={StatisticMod.StatsStore.CurrentSlot}; statystyki przywrocone do ostatniego zatwierdzonego zapisu.");
                }
                catch (Exception exSlot) { StatisticMod.Plugin.Log.LogWarning("[GameSavePatches] Stats reload after ApplySaveData failed: " + exSlot.Message); }

                if (!PluginConfig.ExpiryEnabled)
                {
                    StatisticMod.Plugin.DebugLog(
                        "[GameSavePatches] Expiration is disabled - skipping expiration load/sync.");
                    return;
                }

                SmartExpiration.ExpirationLoadFinalizer.BeginNewLoad();
                ExpirationSaveManager.LoadData();

                try
                {
                    var engineGo = GameObject.Find("SmartExpirationEngine");
                    if (engineGo != null)
                    {
                        var engineComp = engineGo.GetComponent<ExpirationEngine>();
                        if (engineComp != null)
                        {
                            engineComp.StartCoroutine(SmartExpiration.ExpirationLoadFinalizer.DelayedSyncCoroutine());
                            StatisticMod.Plugin.DebugLog("[GameSavePatches] Started DelayedSyncCoroutine on SmartExpirationEngine.");
                        }
                        else
                        {
                            StatisticMod.Plugin.DebugLog("[GameSavePatches] SmartExpirationEngine found but ExpirationEngine component missing.");
                        }
                    }
                    else
                    {
                        StatisticMod.Plugin.DebugLog("[GameSavePatches] SmartExpirationEngine not found; skipping delayed sync.");
                    }
                }
                catch (Exception ex)
                {
                    StatisticMod.Plugin.DebugLog($"[GameSavePatches] Error starting delayed sync: {ex.Message}");
                }

                StatisticMod.Plugin.DebugLog("[GameSavePatches] LoadData() wykonane. Synchronizacja półek zostanie przeprowadzona przez DelayedSyncCoroutine.");
                StatisticMod.Plugin.DebugLog("========================================");
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError($"[GameSavePatches] Błąd po ApplySaveData(): {ex}");
            }
        }
    }
}