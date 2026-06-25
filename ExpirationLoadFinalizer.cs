using System.Collections;
using UnityEngine;
using System;

namespace SmartExpiration
{
    public class ExpirationLoadFinalizer : MonoBehaviour
    {
        private const float MaxWaitSeconds = 10f;
        private const float PollInterval = 0.1f;

        public static IEnumerator DelayedSyncCoroutine()
        {
            float startTime = Time.realtimeSinceStartup;
            // Poczekaj aż SaveLoaded lub timeout
            while (!ExpirationSaveManager.SaveLoaded && Time.realtimeSinceStartup - startTime < MaxWaitSeconds)
                yield return null;

            if (!ExpirationSaveManager.SaveLoaded)
                StatisticMod.Plugin.DebugLog("[DelayedSync] Warning: SaveLoaded not set after timeout.");
            else
                StatisticMod.Plugin.DebugLog("[DelayedSync] SaveLoaded detected.");

            // Dodatkowe krótkie opóźnienie, by obiekty sceny zdążyły się zainicjalizować
            yield return new WaitForSeconds(0.5f);

            // Opcjonalnie czekaj aż pojawi się przynajmniej jedna zajęta półka
            startTime = Time.realtimeSinceStartup;
            bool foundAny = false;
            while (Time.realtimeSinceStartup - startTime < MaxWaitSeconds)
            {
                var slots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
                foreach (var s in slots)
                {
                    try { if (s != null && s.HasProduct) { foundAny = true; break; } }
                    catch { }
                }
                if (foundAny) break;
                yield return new WaitForSeconds(PollInterval);
            }

            // Wykonaj synchronizację wszystkich półek
            var allSlots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
            int synced = 0;
            foreach (var slot in allSlots)
            {
                try
                {
                    if (slot == null) continue;
                    ExpirationManager.SyncShelf(slot);
                    if (slot.HasProduct) synced++;
                }
                catch (Exception ex)
                {
                    StatisticMod.Plugin.DebugLog($"[DelayedSync] SyncShelf error: {ex.Message}");
                }
            }

            StatisticMod.Plugin.DebugLog($"[DelayedSync] Synced shelves after load: {synced}");
        }
    }
}
