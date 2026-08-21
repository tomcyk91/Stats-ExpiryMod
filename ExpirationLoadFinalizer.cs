using System;
using System.Collections;
using System.Diagnostics;
using UnityEngine;

namespace SmartExpiration
{
    public class ExpirationLoadFinalizer : MonoBehaviour
    {
        private const float MaxWaitSeconds = 10f;
        private const float InitialSceneDelay = 0.50f;
        private const float EmptySceneRetryDelay = 0.50f;
        private const int MaxEmptySceneRetries = 6;
        private const double MaxSyncBudgetMsPerFrame = 0.30;

        private static bool _syncInProgress;
        public static bool InitialSyncComplete { get; private set; }
        public static bool SyncInProgress => _syncInProgress;

        public static void BeginNewLoad()
        {
            InitialSyncComplete = false;
            SceneSlotCache.InvalidateSlots();
        }

        public static IEnumerator DelayedSyncCoroutine()
        {
            if (_syncInProgress)
            {
                StatisticMod.Plugin.DebugLog("[DelayedSync] Synchronization already running - duplicate request skipped.");
                yield break;
            }

            _syncInProgress = true;
            InitialSyncComplete = false;

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
                    StatisticMod.Plugin.DebugWarning("[DelayedSync] Save data timeout; startup sync skipped.");
                    yield break;
                }

                yield return new WaitForSecondsRealtime(InitialSceneDelay);

                DisplaySlot[] allSlots = GetCurrentSlots(true);
                int retry = 0;
                while ((allSlots == null || allSlots.Length == 0) && retry < MaxEmptySceneRetries)
                {
                    retry++;
                    yield return new WaitForSecondsRealtime(EmptySceneRetryDelay);
                    allSlots = GetCurrentSlots(true);
                }

                if (allSlots == null) allSlots = new DisplaySlot[0];

                int slotsPerFrame = 2;
                try
                {
                    if (PluginConfig.LoadSyncSlotsPerFrame != null)
                        slotsPerFrame = Mathf.Clamp(PluginConfig.LoadSyncSlotsPerFrame.Value, 1, 8);
                }
                catch { slotsPerFrame = 2; }

                int scanned = 0;
                int occupied = 0;
                int errors = 0;
                int processedThisFrame = 0;
                long frameStart = Stopwatch.GetTimestamp();
                double tickToMs = 1000.0 / Stopwatch.Frequency;

                for (int i = 0; i < allSlots.Length; i++)
                {
                    DisplaySlot slot = allSlots[i];
                    scanned++;
                    if (slot == null) continue;

                    bool hasProduct = false;
                    try { hasProduct = slot.HasProduct; } catch { }
                    if (!hasProduct) continue; // empty shelves need no expiration initialization

                    try
                    {
                        ExpirationManager.SyncShelf(slot);
                        occupied++;
                        LabelExclamationOverlay.QueueSlot(slot);
                    }
                    catch (Exception ex)
                    {
                        errors++;
                        StatisticMod.Plugin.DebugWarning($"[DelayedSync] SyncShelf error: {ex.Message}");
                    }

                    processedThisFrame++;
                    double elapsedMs = (Stopwatch.GetTimestamp() - frameStart) * tickToMs;
                    if (processedThisFrame >= slotsPerFrame || elapsedMs >= MaxSyncBudgetMsPerFrame)
                    {
                        processedThisFrame = 0;
                        yield return null;
                        frameStart = Stopwatch.GetTimestamp();
                    }
                }

                // Reuse exactly this native-cache snapshot for marker initialization.
                // No second scene search after startup synchronization.
                InitialSyncComplete = true;
                StatisticMod.Plugin.DebugLog(
                    $"[DelayedSync] DONE. scanned={scanned}, occupied={occupied}, errors={errors}, " +
                    $"batch={slotsPerFrame}, budget={MaxSyncBudgetMsPerFrame:F2}ms");
            }
            finally
            {
                _syncInProgress = false;
                if (ExpirationSaveManager.SaveLoaded)
                    InitialSyncComplete = true; // fail-soft: never block event-driven runtime forever
            }
        }

        private static DisplaySlot[] GetCurrentSlots(bool invalidate)
        {
            if (invalidate) SceneSlotCache.InvalidateSlots();
            return SceneSlotCache.GetSlots();
        }
    }
}
