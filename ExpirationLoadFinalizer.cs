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
        private const int MaxEmptySceneRetries = 180; // 90 s przy 0.5 s; ciężkie zestawy modów potrafią ładować długo
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
                yield break;
            }

            _syncInProgress = true;
            InitialSyncComplete = false;
            bool completedSuccessfully = false;

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

                // ApplySaveData kończy się jeszcze w Main Menu. Nie wolno
                // uznać synchronizacji za zakończoną tylko dlatego, że
                // sidecar został już wczytany. Czekamy na fizyczne obiekty
                // właściwej sceny gry.
                while ((allSlots == null || allSlots.Length == 0) &&
                       retry < MaxEmptySceneRetries)
                {
                    retry++;
                    yield return new WaitForSecondsRealtime(EmptySceneRetryDelay);
                    allSlots = GetCurrentSlots(true);
                }

                if (allSlots == null || allSlots.Length == 0)
                {
                    StatisticMod.Plugin.Log.LogWarning(
                        "[DelayedSync] Gameplay DisplaySlots were not found before timeout. " +
                        "Expiration sidecar writes remain BLOCKED to protect existing PBOX3 data.");

                    yield break;
                }

                // PBOX3 is restored by the saved physical fingerprint
                // (box type + transform + product/count), never by game UID.
                int restoredBoxes =
                    ExpirationSaveManager.RestoreLoadedBoxesFromPbox3();

                try
                {
                    var labels =
                        SmartExpiration.Patches.BoxLabelPatch.AllLabels;

                    if (labels != null)
                    {
                        for (int i = 0; i < labels.Count; i++)
                        {
                            var label = labels[i];
                            if (label != null)
                                label.ForceRefreshAfterPbox3Restore();
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatisticMod.Plugin.DebugWarning(
                        $"[DelayedSync] Box label refresh error: {ex.Message}");
                }


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

                // Jednorazowa migracja bezpieczeństwa po pełnym odtworzeniu
                // terminów półek. Używa tego samego snapshotu slotów i nie
                // wykonuje dodatkowego globalnego skanu półek.
                yield return ExpirationSafetyMigration.RunOnceCoroutine(allSlots);

                // ExpiryRescueV3 reloads SmartExpiration.txt after a successful
                // file rewrite. LoadData() intentionally clears runtime caches,
                // so on the rare first migration run we must apply PBOX3/SDEL
                // once more before writes are enabled.
                if (ExpirationSafetyMigration.ReloadedSidecarThisRun)
                {
                    int restoredAfterMigration =
                        ExpirationSaveManager.RestoreLoadedBoxesFromPbox3();

                    for (int i = 0; i < allSlots.Length; i++)
                    {
                        DisplaySlot slot = allSlots[i];
                        if (slot == null)
                            continue;

                        bool hasProductAfterMigration = false;
                        try { hasProductAfterMigration = slot.HasProduct; } catch { }

                        if (!hasProductAfterMigration)
                            continue;

                        try
                        {
                            ExpirationManager.SyncShelf(slot);
                            LabelExclamationOverlay.QueueSlot(slot);
                        }
                        catch (Exception ex)
                        {
                            StatisticMod.Plugin.DebugWarning(
                                $"[DelayedSync] Post-migration SyncShelf error: {ex.Message}");
                        }
                    }

                    try
                    {
                        var labels =
                            SmartExpiration.Patches.BoxLabelPatch.AllLabels;

                        if (labels != null)
                        {
                            for (int i = 0; i < labels.Count; i++)
                            {
                                var label = labels[i];
                                if (label != null)
                                    label.ForceRefreshAfterPbox3Restore();
                            }
                        }
                    }
                    catch { }

                }

                // Dopiero po rzeczywistym odtworzeniu sceny zezwalamy
                // SaveData() na zapis SmartExpiration.txt.
                completedSuccessfully = true;
                InitialSyncComplete = true;
            }
            finally
            {
                _syncInProgress = false;

                // Fail-CLOSED, nie fail-open. Jeżeli właściwa scena nie została
                // zsynchronizowana, pozostawiamy zapis sidecara zablokowany.
                // Lepszy brak aktualizacji sidecara w tej sesji niż utrata
                // poprawnego PBOX3 przez zapis danych z Main Menu/load runtime.
                if (!completedSuccessfully)
                    InitialSyncComplete = false;
            }
        }

        private static DisplaySlot[] GetCurrentSlots(bool invalidate)
        {
            if (invalidate) SceneSlotCache.InvalidateSlots();
            return SceneSlotCache.GetSlots();
        }
    }
}
