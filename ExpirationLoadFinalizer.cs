using System;
using System.Collections;
using UnityEngine;

namespace SmartExpiration
{
    public class ExpirationLoadFinalizer : MonoBehaviour
    {
        private const float MaxWaitSeconds = 10f;
        private const float InitialSceneDelay = 0.75f;
        private const float EmptySceneRetryDelay = 0.5f;
        private const int MaxEmptySceneRetries = 3;

        private static bool _syncInProgress;

        public static IEnumerator DelayedSyncCoroutine()
        {
            // ApplySaveData może zostać wywołane więcej niż raz podczas jednego ładowania.
            // Nie uruchamiamy równolegle kilku pełnych synchronizacji półek.
            if (_syncInProgress)
            {
                StatisticMod.Plugin.DebugLog("[DelayedSync] Synchronization already running - duplicate request skipped.");
                yield break;
            }

            _syncInProgress = true;

            try
            {
                float waitStartedAt = Time.realtimeSinceStartup;

                while (!ExpirationSaveManager.SaveLoaded &&
                       Time.realtimeSinceStartup - waitStartedAt < MaxWaitSeconds)
                {
                    yield return null;
                }

                if (!ExpirationSaveManager.SaveLoaded)
                {
                    StatisticMod.Plugin.DebugWarning(
                        "[DelayedSync] Save data was not loaded before timeout. Shelf synchronization skipped.");
                    yield break;
                }

                StatisticMod.Plugin.DebugLog("[DelayedSync] SaveLoaded detected.");

                // Czekamy w czasie rzeczywistym, aby pauza lub zerowy timeScale nie blokowały inicjalizacji.
                yield return new WaitForSecondsRealtime(InitialSceneDelay);

                var allSlots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
                int retry = 0;
                bool hasAnySlot = HasAnySlot(allSlots);

                // Zamiast skanowania sceny co 0,1 s wykonujemy najwyżej kilka kontrolowanych prób.
                while (!hasAnySlot && retry < MaxEmptySceneRetries)
                {
                    retry++;
                    yield return new WaitForSecondsRealtime(EmptySceneRetryDelay);
                    allSlots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
                    hasAnySlot = HasAnySlot(allSlots);
                }

                if (!hasAnySlot)
                {
                    StatisticMod.Plugin.DebugWarning(
                        "[DelayedSync] No DisplaySlot objects found. Shelf synchronization skipped.");
                    yield break;
                }

                int slotsPerFrame = 4;
                try
                {
                    if (PluginConfig.LoadSyncSlotsPerFrame != null)
                        slotsPerFrame = Mathf.Clamp(PluginConfig.LoadSyncSlotsPerFrame.Value, 1, 32);
                }
                catch
                {
                    slotsPerFrame = 4;
                }

                float syncStartedAt = Time.realtimeSinceStartup;
                int scanned = 0;
                int occupied = 0;
                int errors = 0;
                int processedThisFrame = 0;

                for (int i = 0; i < allSlots.Length; i++)
                {
                    DisplaySlot slot = allSlots[i];
                    scanned++;

                    try
                    {
                        if (slot != null)
                        {
                            ExpirationManager.SyncShelf(slot);
                            if (slot.HasProduct) occupied++;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        // Szczegółowy wyjątek jest przydatny, ale występuje tylko w razie realnego błędu.
                        StatisticMod.Plugin.DebugWarning(
                            $"[DelayedSync] SyncShelf error: {ex.Message}");
                    }

                    processedThisFrame++;
                    if (processedThisFrame >= slotsPerFrame)
                    {
                        processedThisFrame = 0;
                        yield return null;
                    }
                }

                float elapsedMs = (Time.realtimeSinceStartup - syncStartedAt) * 1000f;
                StatisticMod.Plugin.DebugLog(
                    $"[DelayedSync] DONE. scanned={scanned}, occupied={occupied}, " +
                    $"errors={errors}, batch={slotsPerFrame}, elapsed={elapsedMs:F1} ms");
            }
            finally
            {
                _syncInProgress = false;
            }
        }

        private static bool HasAnySlot(Il2CppInterop.Runtime.InteropTypes.Arrays.Il2CppArrayBase<DisplaySlot> slots)
        {
            if (slots == null) return false;

            for (int i = 0; i < slots.Length; i++)
            {
                if (slots[i] != null) return true;
            }

            return false;
        }
    }
}
