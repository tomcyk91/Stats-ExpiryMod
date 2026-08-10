using System;
using System.IO;
using UnityEngine;

namespace StatisticMod
{
    /// <summary>
    /// Centralny reset danych moda wykonywany tylko przy tworzeniu nowej gry.
    /// Usuwa dane wyłącznie z wybranego slotu, bez dotykania innych zapisów.
    /// </summary>
    public static class NewGameStatisticsReset
    {
        private static bool _isResetting;
        private static float _lastResetAt = -1000f;
        private static string _lastResetSlot = string.Empty;

        public static void ResetForSlot(int slotIndex, string source)
        {
            if (slotIndex < 0) slotIndex = 0;
            ResetForSlotName("slot_" + slotIndex, source);
        }

        public static void ResetCurrentSlot(string source)
        {
            ResetForSlotName(ResolveCurrentSlot(), source);
        }

        private static void ResetForSlotName(string slotName, string source)
        {
            slotName = NormalizeSlotName(slotName);
            float now = Time.realtimeSinceStartup;

            // Te same przejście może przejść przez dwa hooki (np. ekran
            // bankructwa, a potem CreateLoadNewSave). Drugi reset jest zbędny.
            if (_isResetting) return;
            if (string.Equals(slotName, _lastResetSlot, StringComparison.OrdinalIgnoreCase) &&
                now - _lastResetAt < 2f) return;

            _isResetting = true;
            try
            {
                StatsStore.ResetForNewGame(slotName);
                DailySummaryStore.ResetForNewGame();
                BusinessAnalysisStore.ResetForNewGame();

                DemandTrackingManager.ClearAllSessions();
                SalesUnifiedFinal.ClearRuntimeBuffers();
                StockSnapshotService.Invalidate();
                StatsSearchCache.ForceInvalidate();

                StatsRunner.NotifyNewGameReset();
                StatsAppManager.NotifyStatisticsReset();

                _lastResetSlot = slotName;
                _lastResetAt = now;

                Plugin.Log?.LogInfo(
                    $"[NewGameReset] Wyczyszczono statystyki slotu {slotName}. " +
                    $"Źródło={source}; stats={StatsStore.AbsoluteFilePath}; " +
                    $"daily={DailySummaryStore.AbsoluteFilePath}; " +
                    $"analysis={BusinessAnalysisStore.AbsoluteFilePath}");
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogError("[NewGameReset] Reset failed: " + ex);
            }
            finally
            {
                _isResetting = false;
            }
        }

        private static string ResolveCurrentSlot()
        {
            try
            {
                var saveManager = SaveManager.Instance;
                if (saveManager != null && !string.IsNullOrEmpty(saveManager.m_CurrentSaveFilePath))
                {
                    string name = Path.GetFileNameWithoutExtension(saveManager.m_CurrentSaveFilePath);
                    if (!string.IsNullOrEmpty(name) &&
                        name.StartsWith("slot_", StringComparison.OrdinalIgnoreCase))
                    {
                        return name;
                    }
                }
            }
            catch { }

            return StatsStore.CurrentSlot;
        }

        private static string NormalizeSlotName(string slotName)
        {
            if (string.IsNullOrWhiteSpace(slotName)) return "slot_0";
            string name = Path.GetFileNameWithoutExtension(slotName.Trim()).ToLowerInvariant();
            return name.StartsWith("slot_", StringComparison.OrdinalIgnoreCase)
                ? name
                : "slot_0";
        }
    }

    // Najdokładniejszy hook: SaveInfo zawiera SlotIndex wybranego przez gracza slotu.
    public static class SaveManager_CreateLoadNewSave_StatisticsReset_Patch
    {
        public static void Prefix(SaveInfo __0)
            => NewGameStatisticsReset.ResetForSlot(__0.SlotIndex, "SaveManager.CreateLoadNewSave");
    }

    public static class SaveManager_CreateLoadNewSaveMP_StatisticsReset_Patch
    {
        public static void Prefix(SaveInfo __0)
            => NewGameStatisticsReset.ResetForSlot(__0.SlotIndex, "SaveManager.CreateLoadNewSave_MP");
    }

    // Fallbacki dla rozpoczęcia nowej gry po bankructwie/podsumowaniu.
    public static class DailyStatisticsScreen_StartNewGame_StatisticsReset_Patch
    {
        public static void Prefix()
            => NewGameStatisticsReset.ResetCurrentSlot("DailyStatisticsScreen.StartNewGame");
    }

    public static class BankruptcyCanvas_StartNewGame_StatisticsReset_Patch
    {
        public static void Prefix()
            => NewGameStatisticsReset.ResetCurrentSlot("BankruptcyCanvas.StartNewGame");
    }
}
