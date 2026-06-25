using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
                try { if (SaveManager.Instance != null && !string.IsNullOrEmpty(SaveManager.Instance.m_CurrentSaveFilePath)) slotName = Path.GetFileNameWithoutExtension(SaveManager.Instance.m_CurrentSaveFilePath); } catch { }
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

        // NEW: flag to mark runtimeBoxDates that came from save file (do not auto-overwrite)
        public static Dictionary<int, bool> runtimeBoxDatesFromSave = new Dictionary<int, bool>();

        // NEW: store config version under which runtimeBoxDates were generated (diagnostic / future use)
        public static Dictionary<int, int> runtimeBoxConfigVersion = new Dictionary<int, int>();

        public static Dictionary<int, Queue<SavedBoxData>> pendingLoadedBoxes = new Dictionary<int, Queue<SavedBoxData>>();

        public static bool SaveDataInitialized = false;
        public static bool SaveLoaded = false;

        public static List<global::Product> GetSortedProducts(Transform parent)
        {
            var products = parent.GetComponentsInChildren<global::Product>(true).ToList();
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

        // Helper: bezpieczne pobranie ProductID z global::Product
        public static int GetProductIdFromProduct(global::Product p)
        {
            if (p == null) return 0;

            try
            {
                Type t = p.GetType();

                var prop = t.GetProperty("Data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (prop != null)
                {
                    var dataObj = prop.GetValue(p);
                    if (dataObj != null)
                    {
                        var idProp = dataObj.GetType().GetProperty("ProductID") ?? dataObj.GetType().GetProperty("ID") ?? dataObj.GetType().GetProperty("Uid") ?? dataObj.GetType().GetProperty("UID");
                        if (idProp != null)
                        {
                            var val = idProp.GetValue(dataObj);
                            if (val is int) return (int)val;
                        }
                    }
                }

                var field = t.GetField("Data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (field != null)
                {
                    var dataObj = field.GetValue(p);
                    if (dataObj != null)
                    {
                        var idProp = dataObj.GetType().GetProperty("ProductID") ?? dataObj.GetType().GetProperty("ID") ?? dataObj.GetType().GetProperty("Uid") ?? dataObj.GetType().GetProperty("UID");
                        if (idProp != null)
                        {
                            var val = idProp.GetValue(dataObj);
                            if (val is int) return (int)val;
                        }
                    }
                }

                string[] altNames = { "ProductSO", "ProductScriptable", "SO", "ProductData" };
                foreach (var name in altNames)
                {
                    var pprop = t.GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pprop != null)
                    {
                        var so = pprop.GetValue(p);
                        if (so != null)
                        {
                            var idProp = so.GetType().GetProperty("ID") ?? so.GetType().GetProperty("ProductID");
                            if (idProp != null)
                            {
                                var val = idProp.GetValue(so);
                                if (val is int) return (int)val;
                            }
                        }
                    }

                    var pfield = t.GetField(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                    if (pfield != null)
                    {
                        var so = pfield.GetValue(p);
                        if (so != null)
                        {
                            var idProp = so.GetType().GetProperty("ID") ?? so.GetType().GetProperty("ProductID");
                            if (idProp != null)
                            {
                                var val = idProp.GetValue(so);
                                if (val is int) return (int)val;
                            }
                        }
                    }
                }

                var m = t.GetMethod("GetProductID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                if (m != null)
                {
                    var r = m.Invoke(p, null);
                    if (r is int) return (int)r;
                }
            }
            catch { }

            return 0;
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
                    if (products == null || products.Length == 0) continue;

                    List<int> datesToSave = new List<int>();

                    foreach (var p in products)
                    {
                        if (p == null) continue;
                        var comp = p.GetComponent<ProductExpirationComponent>();
                        if (comp != null)
                        {
                            datesToSave.Add(comp.ExpirationDay);
                        }
                        else
                        {
                            int boxKey = box.GetInstanceID();
                            if (runtimeBoxDates.ContainsKey(boxKey) && runtimeBoxDates[boxKey].Count > 0)
                            {
                                var list = runtimeBoxDates[boxKey];
                                int index = Math.Min(datesToSave.Count, list.Count - 1);
                                if (index >= 0 && index < list.Count)
                                    datesToSave.Add(list[index]);
                                else
                                {
                                    int prodId = GetProductIdFromProduct(p);
                                    int day = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;
                                    int shelfLife = ExpirationCalculator.GetDaysForProduct(null, prodId);
                                    datesToSave.Add(day + shelfLife);
                                }
                            }
                            else
                            {
                                int prodId = GetProductIdFromProduct(p);
                                int day = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;
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
                            try
                            {
                                int oldUid = 0;
                                var dataProp = box.GetType().GetProperty("Data", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
                                if (dataProp != null)
                                {
                                    var dataObj = dataProp.GetValue(box);
                                    if (dataObj != null)
                                    {
                                        var uidProp = dataObj.GetType().GetProperty("UID") ?? dataObj.GetType().GetProperty("Uid") ?? dataObj.GetType().GetProperty("Id");
                                        if (uidProp != null)
                                        {
                                            var uidVal = uidProp.GetValue(dataObj);
                                            if (uidVal is int) oldUid = (int)uidVal;
                                        }
                                    }
                                }
                                if (oldUid > 0 && boxDeliveryDays.ContainsKey(oldUid))
                                    deliveryDay = boxDeliveryDays[oldUid];
                            }
                            catch { }
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
                StatisticMod.Plugin.DebugLog("[LoadData] CustomExpirationLoader.Load() called at start of LoadData()");
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugLog($"[LoadData] Error calling CustomExpirationLoader.Load(): {ex.Message}");
            }

            string fileToLoad = null;
            bool migratedFromLegacy = false;

            if (File.Exists(NewSaveFilePath)) fileToLoad = NewSaveFilePath;
            else if (File.Exists(LegacySaveFilePath)) { fileToLoad = LegacySaveFilePath; migratedFromLegacy = true; }
            else
            {
                StatisticMod.Plugin.DebugLog("[LoadData] No save file found.");
                return;
            }

            try
            {
                string[] lines = File.ReadAllLines(fileToLoad);

                foreach (string line in lines)
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(line) || !line.Contains("|")) continue;
                        string[] parts = line.Split('|');

                        if (parts[0] == "PBOX" && parts.Length >= 3)
                        {
                            if (int.TryParse(parts[1], out int productId))
                            {
                                List<int> dates = parts[2].Split(',').Select(int.Parse).ToList();
                                int deliveryDay = parts.Length >= 4 ? int.Parse(parts[3]) : 1;

                                if (!pendingLoadedBoxes.ContainsKey(productId))
                                    pendingLoadedBoxes[productId] = new Queue<SavedBoxData>();

                                pendingLoadedBoxes[productId].Enqueue(new SavedBoxData { Dates = dates, DeliveryDay = deliveryDay });
                                StatisticMod.Plugin.DebugLog($"[LoadData] Enqueued PBOX for productId={productId} datesCount={dates.Count} deliveryDay={deliveryDay}");
                            }
                        }
                        else if (parts[0] == "BOX" && parts.Length >= 3)
                        {
                            if (int.TryParse(parts[1], out int boxUID))
                            {
                                if (boxUID > 0)
                                {
                                    boxDates[boxUID] = parts[2].Split(',').Select(int.Parse).ToList();
                                    if (parts.Length >= 4 && int.TryParse(parts[3], out int deliveryDay))
                                    {
                                        boxDeliveryDays[boxUID] = deliveryDay;
                                    }
                                    StatisticMod.Plugin.DebugLog($"[LoadData] Loaded BOX uid={boxUID} datesCount={boxDates[boxUID].Count}");
                                }
                            }
                        }
                        else if (parts.Length == 2)
                        {
                            string path = parts[0];
                            List<int> loadedList = parts[1].Split(',').Select(int.Parse).ToList();
                            slotDates[path] = loadedList;
                            StatisticMod.Plugin.DebugLog($"[LoadData] Loaded slotDates for path={path} count={loadedList.Count}");
                        }
                    }
                    catch (Exception ex)
                    {
                        StatisticMod.Plugin.Log.LogWarning($"[LoadData] Błąd przetwarzania linii {line}: {ex.Message}");
                    }
                }

                if (migratedFromLegacy)
                {
                    try
                    {
                        string dir = Path.GetDirectoryName(NewSaveFilePath);
                        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        File.Copy(LegacySaveFilePath, NewSaveFilePath, true);
                        File.Delete(LegacySaveFilePath);
                    }
                    catch { }
                }

                CustomExpirationLoader.Load();
                SaveDataInitialized = true;
                SaveLoaded = true;

                StatisticMod.Plugin.DebugLog($"[LoadData] DONE. pendingLoadedBoxes={pendingLoadedBoxes.Count} boxDates={boxDates.Count} slotDates={slotDates.Count}");
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError($"[LoadData] BŁĄD ODCZYTU GŁÓWNEGO: {ex}");
            }
        }
    }
}
