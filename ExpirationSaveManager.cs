using StatisticMod;
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SmartExpiration
{
    public class SavedBoxData
    {
        public List<int> Dates;
        public int DeliveryDay;
    }

    public static class ExpirationSaveManager
    {
        private static string CurrentSlotName
        {
            get
            {
                string slotName = "slot_0";
                try
                {
                    var sm = SaveManager.HasInstance ? SaveManager.Instance : null;
                    if (sm != null && !string.IsNullOrEmpty(sm.m_CurrentSaveFilePath))
                        slotName = Path.GetFileNameWithoutExtension(sm.m_CurrentSaveFilePath);
                }
                catch { }
                return slotName;
            }
        }

        private static string NewSaveFilePath
        {
            get
            {
                string slotFolder = Path.Combine(Application.persistentDataPath, CurrentSlotName);
                return Path.Combine(slotFolder, "SmartExpiration.txt");
            }
        }

        private static string LegacySaveFilePath => Path.Combine(Application.persistentDataPath, $"SmartExpiration_{CurrentSlotName}.txt");

        public static Dictionary<string, List<int>> slotDates = new Dictionary<string, List<int>>();

        public static Dictionary<int, List<int>> boxDates = new Dictionary<int, List<int>>();
        public static Dictionary<int, int> boxDeliveryDays = new Dictionary<int, int>();

        public static Dictionary<int, List<int>> runtimeBoxDates = new Dictionary<int, List<int>>();
        public static Dictionary<int, int> runtimeBoxDeliveryDays = new Dictionary<int, int>();

        public static Dictionary<int, bool> runtimeBoxDatesFromSave = new Dictionary<int, bool>();
        public static Dictionary<int, int> runtimeBoxConfigVersion = new Dictionary<int, int>();

        public static Dictionary<int, Queue<SavedBoxData>> pendingLoadedBoxes = new Dictionary<int, Queue<SavedBoxData>>();

        public static bool SaveDataInitialized = false;
        public static bool SaveLoaded = false;

        // A2 FIX: Ręczna iteracja natywnej tablicy IL2CPP całkowicie omija systemowe LINQ (.ToList)
        public static List<global::Product> GetSortedProducts(Transform parent)
        {
            var il2cppArray = parent.GetComponentsInChildren<global::Product>(true);
            var products = new List<global::Product>(il2cppArray != null ? il2cppArray.Count : 0);

            if (il2cppArray != null)
            {
                for (int i = 0; i < il2cppArray.Count; i++)
                {
                    if (il2cppArray[i] != null) products.Add(il2cppArray[i]);
                }
            }

            products.Sort((a, b) => a.transform.GetSiblingIndex().CompareTo(b.transform.GetSiblingIndex()));
            return products;
        }

        public static string GetSlotPath(DisplaySlot slot)
        {
            if (slot == null) return "UnknownSlot";
            Transform current = slot.transform;
            string path = current.name;

            while (current.parent != null && current.parent.parent != null)
            {
                current = current.parent;
                path = $"{current.name}_{current.GetSiblingIndex()}/{path}";
            }
            return path;
        }

        // A1 FIX: Chirurgiczna delegacja do zweryfikowanego czystego skryptu ProductKey
        public static int GetProductIdFromProduct(global::Product p)
        {
            if (p == null) return 0;
            try
            {
                int id = ProductKey.GetId(p);
                return id > 0 ? id : 0;
            }
            catch { return 0; }
        }

        public static void SaveData()
        {
            StatisticMod.Plugin.DebugLog($"[SaveData] START -> {NewSaveFilePath}");
            List<string> linesToSave = new List<string>();
            var allSlots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();

            int savedSlotsCount = 0;
            foreach (var slot in allSlots)
            {
                try
                {
                    if (slot != null && slot.HasProduct)
                    {
                        ExpirationManager.SyncShelf(slot);
                        var products = GetSortedProducts(slot.transform);
                        List<int> datesList = new List<int>();

                        foreach (var p in products)
                        {
                            if (p != null)
                            {
                                var comp = p.GetComponent<ProductExpirationComponent>();
                                if (comp == null)
                                {
                                    ExpirationManager.EnsureExpiration(p, slot);
                                    comp = p.GetComponent<ProductExpirationComponent>();
                                }
                                if (comp != null) datesList.Add(comp.ExpirationDay);
                            }
                        }

                        if (datesList.Count > 0)
                        {
                            string path = GetSlotPath(slot);
                            string joinedDates = string.Join(",", datesList);
                            linesToSave.Add($"{path}|{joinedDates}");
                            savedSlotsCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatisticMod.Plugin.Log.LogError($"[SaveData] Błąd zapisu na slocie półki: {ex.Message}");
                }
            }

            int savedBoxesCount = 0;
            var allBoxes = UnityEngine.Object.FindObjectsOfType<Box>();

            foreach (var box in allBoxes)
            {
                try
                {
                    if (box == null) continue;

                    var products = box.GetComponentsInChildren<global::Product>(true);
                    if (products == null || products.Count == 0) continue;

                    List<int> datesToSave = new List<int>();

                    for (int i = 0; i < products.Count; i++)
                    {
                        var p = products[i];
                        if (p == null) continue;

                        var comp = p.GetComponent<ProductExpirationComponent>();
                        if (comp != null)
                        {
                            datesToSave.Add(comp.ExpirationDay);
                        }
                        else
                        {
                            int bKey = box.GetInstanceID();
                            if (runtimeBoxDates.ContainsKey(bKey) && runtimeBoxDates[bKey].Count > 0)
                            {
                                var list = runtimeBoxDates[bKey];
                                int idx = Math.Min(datesToSave.Count, list.Count - 1);
                                if (idx >= 0 && idx < list.Count)
                                    datesToSave.Add(list[idx]);
                                else
                                {
                                    int prodId = GetProductIdFromProduct(p);
                                    var dayCycle = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                                    int day = dayCycle != null ? dayCycle.CurrentDay : 1;
                                    int shelfLife = ExpirationCalculator.GetDaysForProduct(null, prodId);
                                    datesToSave.Add(day + shelfLife);
                                }
                            }
                            else
                            {
                                int prodId = GetProductIdFromProduct(p);
                                var dayCycle = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                                int day = dayCycle != null ? dayCycle.CurrentDay : 1;
                                int shelfLife = ExpirationCalculator.GetDaysForProduct(null, prodId);
                                datesToSave.Add(day + shelfLife);
                            }
                        }
                    }

                    if (datesToSave.Count > 0)
                    {
                        int productId = -1;
                        try { if (products[0] != null) productId = GetProductIdFromProduct(products[0]); } catch { productId = -1; }

                        int deliveryDay = 1;
                        int boxKey = box.GetInstanceID();

                        if (runtimeBoxDeliveryDays.ContainsKey(boxKey))
                            deliveryDay = runtimeBoxDeliveryDays[boxKey];
                        else
                        {
                            int oldUid = TryGetLegacyBoxUid(box);
                            if (oldUid > 0 && boxDeliveryDays.ContainsKey(oldUid))
                                deliveryDay = boxDeliveryDays[oldUid];
                        }

                        if (productId > 0)
                        {
                            linesToSave.Add($"PBOX|{productId}|{string.Join(",", datesToSave)}|{deliveryDay}");
                            boxDates[boxKey] = new List<int>(datesToSave);
                            boxDeliveryDays[boxKey] = deliveryDay;
                            runtimeBoxDatesFromSave[boxKey] = true;
                            savedBoxesCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatisticMod.Plugin.Log.LogError($"[SaveData] Błąd zapisu kartonu: {ex.Message}");
                }
            }

            try
            {
                string dir = Path.GetDirectoryName(NewSaveFilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllLines(NewSaveFilePath, linesToSave);
                StatisticMod.Plugin.DebugLog($"[SaveData] DONE. Shelves={savedSlotsCount}, Boxes={savedBoxesCount}, Lines={linesToSave.Count}");

                try { if (File.Exists(LegacySaveFilePath)) File.Delete(LegacySaveFilePath); } catch { }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError($"[SaveData] BŁĄD ZAPISU: {ex}");
            }
        }

        public static void LoadData()
        {
            // Reset jest ważny przy zmianie slotu oraz przy ponownym ładowaniu zapisu.
            SaveDataInitialized = false;
            SaveLoaded = false;

            slotDates.Clear();
            boxDates.Clear();
            boxDeliveryDays.Clear();
            pendingLoadedBoxes.Clear();
            runtimeBoxDates.Clear();
            runtimeBoxDeliveryDays.Clear();
            runtimeBoxDatesFromSave.Clear();
            runtimeBoxConfigVersion.Clear();

            try
            {
                CustomExpirationLoader.Load();
                StatisticMod.Plugin.DebugLog("[LoadData] Custom expiration configuration loaded.");
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    $"[LoadData] Custom expiration configuration error: {ex.Message}");
            }

            string fileToLoad = null;
            bool migratedFromLegacy = false;

            if (File.Exists(NewSaveFilePath))
            {
                fileToLoad = NewSaveFilePath;
            }
            else if (File.Exists(LegacySaveFilePath))
            {
                fileToLoad = LegacySaveFilePath;
                migratedFromLegacy = true;
            }
            else
            {
                // Brak pliku jest prawidłowym stanem nowego zapisu.
                // Finalizer nie powinien w takim przypadku czekać pełnego timeoutu.
                SaveDataInitialized = true;
                SaveLoaded = true;
                StatisticMod.Plugin.DebugLog(
                    "[LoadData] No expiration save file found. New empty state initialized.");
                return;
            }

            bool detailedLogs = false;
            try
            {
                detailedLogs = PluginConfig.DetailedLoadLogs != null &&
                               PluginConfig.DetailedLoadLogs.Value;
            }
            catch
            {
                detailedLogs = false;
            }

            int loadedPboxRecords = 0;
            int loadedLegacyBoxes = 0;
            int loadedSlots = 0;
            int skippedLines = 0;
            int malformedLines = 0;

            try
            {
                // ReadLines nie tworzy od razu dużej tablicy wszystkich wpisów.
                foreach (string line in File.ReadLines(fileToLoad))
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(line) || !line.Contains("|"))
                        {
                            skippedLines++;
                            continue;
                        }

                        string[] parts = line.Split('|');

                        if (parts[0] == "PBOX" && parts.Length >= 3)
                        {
                            if (!int.TryParse(parts[1], out int productId) || productId <= 0)
                            {
                                malformedLines++;
                                continue;
                            }

                            List<int> dates = ParseCsvInts(parts[2]);
                            int deliveryDay = 1;
                            if (parts.Length >= 4)
                                int.TryParse(parts[3], out deliveryDay);
                            if (deliveryDay < 1) deliveryDay = 1;

                            if (!pendingLoadedBoxes.ContainsKey(productId))
                                pendingLoadedBoxes[productId] = new Queue<SavedBoxData>();

                            pendingLoadedBoxes[productId].Enqueue(
                                new SavedBoxData
                                {
                                    Dates = dates,
                                    DeliveryDay = deliveryDay
                                });

                            loadedPboxRecords++;

                            if (detailedLogs)
                            {
                                StatisticMod.Plugin.DebugLog(
                                    $"[LoadData] PBOX productId={productId} " +
                                    $"dates={dates.Count} deliveryDay={deliveryDay}");
                            }
                        }
                        else if (parts[0] == "BOX" && parts.Length >= 3)
                        {
                            if (!int.TryParse(parts[1], out int boxUID) || boxUID <= 0)
                            {
                                malformedLines++;
                                continue;
                            }

                            boxDates[boxUID] = ParseCsvInts(parts[2]);

                            if (parts.Length >= 4 &&
                                int.TryParse(parts[3], out int deliveryDay) &&
                                deliveryDay > 0)
                            {
                                boxDeliveryDays[boxUID] = deliveryDay;
                            }

                            loadedLegacyBoxes++;

                            if (detailedLogs)
                            {
                                StatisticMod.Plugin.DebugLog(
                                    $"[LoadData] BOX uid={boxUID} dates={boxDates[boxUID].Count}");
                            }
                        }
                        else if (parts.Length == 2)
                        {
                            string path = parts[0];
                            if (string.IsNullOrEmpty(path))
                            {
                                malformedLines++;
                                continue;
                            }

                            List<int> loadedList = ParseCsvInts(parts[1]);
                            slotDates[path] = loadedList;
                            loadedSlots++;

                            if (detailedLogs)
                            {
                                StatisticMod.Plugin.DebugLog(
                                    $"[LoadData] SLOT path={path} dates={loadedList.Count}");
                            }
                        }
                        else
                        {
                            malformedLines++;
                        }
                    }
                    catch (Exception ex)
                    {
                        malformedLines++;
                        StatisticMod.Plugin.DebugWarning(
                            $"[LoadData] Invalid record skipped: {ex.Message}");
                    }
                }

                if (migratedFromLegacy)
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(NewSaveFilePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                            Directory.CreateDirectory(dir);

                        File.Copy(LegacySaveFilePath, NewSaveFilePath, true);
                        File.Delete(LegacySaveFilePath);
                        StatisticMod.Plugin.DebugLog("[LoadData] Legacy save migrated.");
                    }
                    catch (Exception ex)
                    {
                        StatisticMod.Plugin.DebugWarning(
                            $"[LoadData] Legacy migration warning: {ex.Message}");
                    }
                }

                // Ponowne odczytanie po migracji zachowuje dotychczasowe zachowanie moda.
                CustomExpirationLoader.Load();

                SaveDataInitialized = true;
                SaveLoaded = true;

                StatisticMod.Plugin.DebugLog(
                    $"[LoadData] DONE. slots={loadedSlots}, pboxRecords={loadedPboxRecords}, " +
                    $"legacyBoxes={loadedLegacyBoxes}, productQueues={pendingLoadedBoxes.Count}, " +
                    $"skipped={skippedLines}, malformed={malformedLines}");
            }
            catch (Exception ex)
            {
                SaveDataInitialized = false;
                SaveLoaded = false;
                StatisticMod.Plugin.Log.LogError(
                    $"[LoadData] BŁĄD ODCZYTU GŁÓWNEGO: {ex}");
            }
        }

        // --- MIKRO-PARSERY ARCHITEKTONICZNE ---

        private static List<int> ParseCsvInts(string csv)
        {
            var list = new List<int>();
            if (string.IsNullOrEmpty(csv)) return list;
            var tokens = csv.Split(',');
            for (int i = 0; i < tokens.Length; i++)
            {
                if (int.TryParse(tokens[i], out int val)) list.Add(val);
            }
            return list;
        }

        private static int TryGetLegacyBoxUid(Box box)
        {
            if (box == null) return 0;
            try
            {
                var prop = box.GetType().GetProperty("Data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    var dataObj = prop.GetValue(box, null);
                    if (dataObj != null)
                    {
                        var uidProp = dataObj.GetType().GetProperty("UID") ?? dataObj.GetType().GetProperty("Uid") ?? dataObj.GetType().GetProperty("Id");
                        if (uidProp != null)
                        {
                            var val = uidProp.GetValue(dataObj, null);
                            if (val is int i && i != 807810400) return i;
                        }
                    }
                }
            }
            catch { }
            return 0;
        }
    }
}