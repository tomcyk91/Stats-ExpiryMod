using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace SmartExpiration
{
    /// <summary>
    /// ExpiryRescueV3
    ///
    /// Jednorazowa, bezpieczna migracja danych zapisanych przez starszą
    /// wersję moda z błędem losowych / już wygasłych terminów.
    ///
    /// WAŻNE:
    /// V3 NIE skanuje sceny i NIE wywołuje SaveData().
    /// Modyfikuje wyłącznie istniejące rekordy SmartExpiration.txt,
    /// zachowując wszystkie linie i strukturę pliku.
    /// </summary>
    public static class ExpirationSafetyMigration
    {
        private const string MigrationId = "ExpiryRescueV3";

        private const string MarkerFileName =
            "StatsExpiry_Migration_ExpiryRescueV3.done";

        private const string BackupFileName =
            "SmartExpiration.pre_ExpiryRescueV3.bak";

        private const string TempFileName =
            "SmartExpiration.ExpiryRescueV3.tmp";

        public static IEnumerator RunOnceCoroutine(
            DisplaySlot[] ignoredSceneSnapshot)
        {
            string slotName =
                GetCurrentSlotName();

            if (string.IsNullOrEmpty(slotName))
            {
                StatisticMod.Plugin.Log.LogWarning(
                    "[ExpiryRescueV3] Could not resolve active save slot. " +
                    "Migration was NOT executed and marker was NOT created.");

                yield break;
            }

            string slotDirectory =
                Path.Combine(
                    Application.persistentDataPath,
                    slotName);

            string savePath =
                Path.Combine(
                    slotDirectory,
                    "SmartExpiration.txt");

            string markerPath =
                Path.Combine(
                    slotDirectory,
                    MarkerFileName);

            string backupPath =
                Path.Combine(
                    slotDirectory,
                    BackupFileName);

            string tempPath =
                Path.Combine(
                    slotDirectory,
                    TempFileName);

            if (File.Exists(markerPath))
            {
                StatisticMod.Plugin.DebugLog(
                    $"[ExpiryRescueV3] Already completed for slot {slotName}. Skipping.");

                yield break;
            }

            int currentDay =
                GetSavedCurrentDay();

            if (currentDay <= 0)
            {
                StatisticMod.Plugin.Log.LogWarning(
                    "[ExpiryRescueV3] Could not resolve authoritative saved game day. " +
                    "Migration was NOT executed and marker was NOT created.");

                yield break;
            }

            int rescueExpirationDay =
                currentDay + 1;

            // Jeżeli nie ma jeszcze sidecara, nie ma starych terminów do naprawy.
            if (!File.Exists(savePath))
            {
                try
                {
                    Directory.CreateDirectory(slotDirectory);

                    WriteMarker(
                        markerPath,
                        slotName,
                        currentDay,
                        rescueExpirationDay,
                        0,
                        0,
                        0,
                        0,
                        "No SmartExpiration.txt existed.");

                    StatisticMod.Plugin.Log.LogInfo(
                        $"[ExpiryRescueV3] No SmartExpiration.txt for slot={slotName}. " +
                        "Nothing to rescue. Migration marked as completed.");
                }
                catch (Exception ex)
                {
                    StatisticMod.Plugin.Log.LogWarning(
                        $"[ExpiryRescueV3] Could not write empty-state marker: {ex.Message}");
                }

                yield break;
            }

            StatisticMod.Plugin.Log.LogInfo(
                $"[ExpiryRescueV3] Safe file migration started. " +
                $"Slot={slotName}, CurrentDay={currentDay}, " +
                $"RescueDay={rescueExpirationDay}");

            string[] originalLines;

            try
            {
                originalLines =
                    File.ReadAllLines(savePath);
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError(
                    $"[ExpiryRescueV3] Could not read SmartExpiration.txt: {ex}");

                yield break;
            }

            string[] migratedLines =
                new string[originalLines.Length];

            int correctedDates = 0;
            int correctedShelfDates = 0;
            int correctedBoxDates = 0;
            int recognizedRecords = 0;
            int malformedRecords = 0;

            for (int i = 0; i < originalLines.Length; i++)
            {
                string line =
                    originalLines[i];

                migratedLines[i] =
                    line;

                if (string.IsNullOrWhiteSpace(line) ||
                    !line.Contains("|"))
                {
                    continue;
                }

                try
                {
                    string[] parts =
                        line.Split('|');

                    int datesPartIndex = -1;
                    bool isShelf = false;
                    bool isBox = false;

                    if (parts.Length == 2)
                    {
                        // DisplaySlotPath|date,date,date
                        datesPartIndex = 1;
                        isShelf = true;
                    }
                    else if (parts[0] == "PBOX2" &&
                             parts.Length >= 5)
                    {
                        // PBOX2|uid|productId|dates|deliveryDay
                        datesPartIndex = 3;
                        isBox = true;
                    }
                    else if (parts[0] == "PBOX" &&
                             parts.Length >= 3)
                    {
                        // PBOX|productId|dates|deliveryDay
                        datesPartIndex = 2;
                        isBox = true;
                    }
                    else if (parts[0] == "BOX" &&
                             parts.Length >= 3)
                    {
                        // BOX|uid|dates|deliveryDay
                        datesPartIndex = 2;
                        isBox = true;
                    }
                    else
                    {
                        // Nieznane rekordy zostawiamy dokładnie bez zmian.
                        continue;
                    }

                    recognizedRecords++;

                    string csv =
                        parts[datesPartIndex];

                    if (string.IsNullOrWhiteSpace(csv))
                    {
                        malformedRecords++;
                        continue;
                    }

                    string[] tokens =
                        csv.Split(',');

                    bool lineChanged = false;

                    for (int t = 0; t < tokens.Length; t++)
                    {
                        if (!int.TryParse(
                                tokens[t],
                                out int expirationDay))
                        {
                            // Nie niszczymy nieznanych tokenów.
                            continue;
                        }

                        // daysLeft = ExpirationDay - CurrentDay
                        // Ratowanie dotyczy daysLeft <= 0.
                        if (expirationDay <= currentDay)
                        {
                            tokens[t] =
                                rescueExpirationDay.ToString();

                            correctedDates++;

                            if (isShelf)
                                correctedShelfDates++;
                            else if (isBox)
                                correctedBoxDates++;

                            lineChanged = true;
                        }
                    }

                    if (lineChanged)
                    {
                        parts[datesPartIndex] =
                            string.Join(",", tokens);

                        migratedLines[i] =
                            string.Join("|", parts);
                    }
                }
                catch (Exception ex)
                {
                    malformedRecords++;

                    StatisticMod.Plugin.DebugWarning(
                        $"[ExpiryRescueV3] Record {i + 1} left unchanged: {ex.Message}");

                    migratedLines[i] =
                        line;
                }
            }

            // =========================================================
            // ZAPIS BEZPIECZNY
            //
            // 1. Backup oryginału - nigdy go nie nadpisujemy.
            // 2. Zapis do pliku tymczasowego.
            // 3. Kopia temp -> SmartExpiration.txt.
            //
            // Nie używamy ExpirationSaveManager.SaveData(), ponieważ w czasie
            // ApplySaveData gra może być jeszcze w Main Menu bez półek.
            // =========================================================

            try
            {
                Directory.CreateDirectory(slotDirectory);

                if (!File.Exists(backupPath))
                {
                    File.Copy(
                        savePath,
                        backupPath,
                        false);
                }

                File.WriteAllLines(
                    tempPath,
                    migratedLines);

                File.Copy(
                    tempPath,
                    savePath,
                    true);

                try
                {
                    File.Delete(tempPath);
                }
                catch { }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError(
                    $"[ExpiryRescueV3] Could not safely rewrite SmartExpiration.txt: {ex}");

                // Bez udanego zapisu NIE tworzymy markera.
                yield break;
            }

            // Przeładuj dokładnie ten sam poprawiony plik do pamięci.
            // Nie tworzymy żadnych nowych terminów i nie zapisujemy sceny.
            try
            {
                ExpirationSaveManager.LoadData();

                if (!ExpirationSaveManager.SaveLoaded)
                {
                    StatisticMod.Plugin.Log.LogWarning(
                        "[ExpiryRescueV3] Corrected file was written, but reload did not " +
                        "finish successfully. Marker was NOT created.");

                    yield break;
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogWarning(
                    $"[ExpiryRescueV3] Corrected file was written, but reload failed: " +
                    $"{ex.Message}. Marker was NOT created.");

                yield break;
            }

            try
            {
                WriteMarker(
                    markerPath,
                    slotName,
                    currentDay,
                    rescueExpirationDay,
                    correctedDates,
                    correctedShelfDates,
                    correctedBoxDates,
                    malformedRecords,
                    $"RecognizedRecords={recognizedRecords}");

                StatisticMod.Plugin.Log.LogInfo(
                    $"[ExpiryRescueV3] Safe migration completed. " +
                    $"CorrectedDates={correctedDates}, " +
                    $"ShelfDates={correctedShelfDates}, " +
                    $"BoxDates={correctedBoxDates}, " +
                    $"RecognizedRecords={recognizedRecords}, " +
                    $"MalformedRecords={malformedRecords}. " +
                    $"Backup={backupPath}");
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogWarning(
                    $"[ExpiryRescueV3] Data was corrected and reloaded, but marker " +
                    $"could not be written: {ex.Message}. " +
                    $"Backup remains at {backupPath}");
            }

            yield break;
        }

        private static int GetSavedCurrentDay()
        {
            try
            {
                var saveManager =
                    SaveManager.HasInstance
                        ? SaveManager.Instance
                        : null;

                if (saveManager != null &&
                    saveManager.Progression != null)
                {
                    int savedDay =
                        saveManager.Progression.CurrentDay;

                    if (savedDay > 0)
                        return savedDay;
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    $"[ExpiryRescueV3] Saved day read failed: {ex.Message}");
            }

            return -1;
        }

        private static string GetCurrentSlotName()
        {
            try
            {
                var saveManager =
                    SaveManager.HasInstance
                        ? SaveManager.Instance
                        : null;

                if (saveManager != null &&
                    !string.IsNullOrEmpty(
                        saveManager.m_CurrentSaveFilePath))
                {
                    return Path.GetFileNameWithoutExtension(
                        saveManager.m_CurrentSaveFilePath);
                }
            }
            catch { }

            return null;
        }

        private static void WriteMarker(
            string markerPath,
            string slotName,
            int currentDay,
            int rescueExpirationDay,
            int correctedDates,
            int correctedShelfDates,
            int correctedBoxDates,
            int malformedRecords,
            string extra)
        {
            File.WriteAllText(
                markerPath,
                $"Migration={MigrationId}{Environment.NewLine}" +
                $"Slot={slotName}{Environment.NewLine}" +
                $"GameDay={currentDay}{Environment.NewLine}" +
                $"RescueExpirationDay={rescueExpirationDay}{Environment.NewLine}" +
                $"CorrectedDates={correctedDates}{Environment.NewLine}" +
                $"ShelfDates={correctedShelfDates}{Environment.NewLine}" +
                $"BoxDates={correctedBoxDates}{Environment.NewLine}" +
                $"MalformedRecords={malformedRecords}{Environment.NewLine}" +
                $"{extra}{Environment.NewLine}" +
                $"CompletedUtc={DateTime.UtcNow:O}{Environment.NewLine}");
        }
    }
}