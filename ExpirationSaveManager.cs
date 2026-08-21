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
        public int BoxUid;
        public int ProductId;
        public List<int> Dates;
        public int DeliveryDay;
    }

    public static class ExpirationSaveManager
    {
        private const int InvalidLegacyBoxUid = 807810400;

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

        private static string LegacySaveFilePath =>
            Path.Combine(Application.persistentDataPath, $"SmartExpiration_{CurrentSlotName}.txt");

        public static Dictionary<string, List<int>> slotDates =
            new Dictionary<string, List<int>>();

        // Cache kompatybilności:
        // - klucze runtime InstanceID
        // - trwałe BoxData.UID
        public static Dictionary<int, List<int>> boxDates =
            new Dictionary<int, List<int>>();

        public static Dictionary<int, int> boxDeliveryDays =
            new Dictionary<int, int>();

        // Bieżąca sesja - klucz = Box.GetInstanceID().
        public static Dictionary<int, List<int>> runtimeBoxDates =
            new Dictionary<int, List<int>>();

        public static Dictionary<int, int> runtimeBoxDeliveryDays =
            new Dictionary<int, int>();

        public static Dictionary<int, bool> runtimeBoxDatesFromSave =
            new Dictionary<int, bool>();

        public static Dictionary<int, int> runtimeBoxConfigVersion =
            new Dictionary<int, int>();

        // Stary PBOX bez UID. Zachowany tylko dla migracji.
        public static Dictionary<int, Queue<SavedBoxData>> pendingLoadedBoxes =
            new Dictionary<int, Queue<SavedBoxData>>();

        // Nowy PBOX2 - dokładne dopasowanie po trwałym BoxData.UID.
        public static Dictionary<int, SavedBoxData> pendingLoadedBoxesByUid =
            new Dictionary<int, SavedBoxData>();

        public static bool SaveDataInitialized = false;
        public static bool SaveLoaded = false;

        // A2 FIX: Ręczna iteracja natywnej tablicy IL2CPP całkowicie omija systemowe LINQ (.ToList).
        public static List<global::Product> GetSortedProducts(Transform parent)
        {
            var il2cppArray = parent.GetComponentsInChildren<global::Product>(true);
            var products = new List<global::Product>(
                il2cppArray != null ? il2cppArray.Count : 0);

            if (il2cppArray != null)
            {
                for (int i = 0; i < il2cppArray.Count; i++)
                {
                    if (il2cppArray[i] != null)
                        products.Add(il2cppArray[i]);
                }
            }

            products.Sort(
                (a, b) => a.transform.GetSiblingIndex()
                    .CompareTo(b.transform.GetSiblingIndex()));

            return products;
        }

        public static string GetSlotPath(DisplaySlot slot)
        {
            if (slot == null)
                return "UnknownSlot";

            Transform current = slot.transform;
            string path = current.name;

            while (current.parent != null &&
                   current.parent.parent != null)
            {
                current = current.parent;
                path = $"{current.name}_{current.GetSiblingIndex()}/{path}";
            }

            return path;
        }

        // A1 FIX: delegacja do ProductKey.
        public static int GetProductIdFromProduct(global::Product p)
        {
            if (p == null)
                return 0;

            try
            {
                int id = ProductKey.GetId(p);
                return id > 0 ? id : 0;
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Trwały identyfikator konkretnego kartonu zapisany przez grę.
        /// Nie używamy GetInstanceID(), ponieważ zmienia się pomiędzy sesjami.
        /// </summary>
        public static int GetStableBoxUid(Box box)
        {
            if (box == null)
                return 0;

            try
            {
                if (box.Data != null)
                {
                    int uid = box.Data.UID;
                    if (uid > 0 && uid != InvalidLegacyBoxUid)
                        return uid;
                }
            }
            catch { }

            // Fallback kompatybilności dla nietypowych wrapperów IL2CPP.
            return TryGetLegacyBoxUid(box);
        }

        public static int GetBoxProductId(Box box)
        {
            if (box == null)
                return 0;

            try
            {
                if (box.Data != null && box.Data.ProductID > 0)
                    return box.Data.ProductID;
            }
            catch { }

            // Ostatni fallback: fizyczny Product będący dzieckiem kartonu.
            try
            {
                var products = box.GetComponentsInChildren<global::Product>(true);
                if (products != null && products.Count > 0 && products[0] != null)
                    return GetProductIdFromProduct(products[0]);
            }
            catch { }

            return 0;
        }

        private static int GetCurrentDay()
        {
            try
            {
                var dcm = DayCycleManager.HasInstance
                    ? DayCycleManager.Instance
                    : null;

                if (dcm != null && dcm.CurrentDay > 0)
                    return dcm.CurrentDay;
            }
            catch { }

            return 1;
        }

        /// <summary>
        /// Pobiera istniejący stan terminów kartonu BEZ tworzenia nowych dat.
        /// SaveData ma wyłącznie zapisywać stan, nigdy go "naprawiać".
        /// </summary>
        private static bool TryGetExactBoxDates(
            Box box,
            out List<int> result)
        {
            result = null;

            if (box == null)
                return false;

            int expectedCount;

            try
            {
                expectedCount = box.ProductCount;
            }
            catch
            {
                return false;
            }

            if (expectedCount <= 0)
                return false;

            int runtimeKey = box.GetInstanceID();
            int stableUid = GetStableBoxUid(box);

            // 1. Runtime bieżącej sesji - główne źródło prawdy.
            if (runtimeBoxDates.TryGetValue(
                    runtimeKey,
                    out List<int> runtimeDates) &&
                runtimeDates != null &&
                runtimeDates.Count == expectedCount)
            {
                result = new List<int>(runtimeDates);
                return true;
            }

            // 2. Trwały cache po UID - m.in. dane wczytane z PBOX2.
            if (stableUid > 0 &&
                boxDates.TryGetValue(
                    stableUid,
                    out List<int> uidDates) &&
                uidDates != null &&
                uidDates.Count == expectedCount)
            {
                result = new List<int>(uidDates);
                return true;
            }

            // 3. Stary cache po InstanceID.
            if (boxDates.TryGetValue(
                    runtimeKey,
                    out List<int> instanceDates) &&
                instanceDates != null &&
                instanceDates.Count == expectedCount)
            {
                result = new List<int>(instanceDates);
                return true;
            }

            // 4. Ostatnia możliwość - każdy fizyczny produkt musi istnieć
            // i każdy musi mieć ProductExpirationComponent.
            try
            {
                var products =
                    box.GetComponentsInChildren<global::Product>(true);

                if (products == null ||
                    products.Count != expectedCount)
                {
                    return false;
                }

                List<int> componentDates =
                    new List<int>(expectedCount);

                for (int i = 0; i < products.Count; i++)
                {
                    var product = products[i];

                    if (product == null)
                        return false;

                    var comp =
                        product.GetComponent<ProductExpirationComponent>();

                    if (comp == null)
                        return false;

                    componentDates.Add(comp.ExpirationDay);
                }

                if (componentDates.Count != expectedCount)
                    return false;

                result = componentDates;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void SaveData()
        {
            StatisticMod.Plugin.DebugLog(
                $"[SaveData] START -> {NewSaveFilePath}");

            List<string> linesToSave = new List<string>();

            // ============================================================
            // PÓŁKI
            // ============================================================

            var allSlots =
                UnityEngine.Object.FindObjectsOfType<DisplaySlot>();

            int savedSlotsCount = 0;

            foreach (var slot in allSlots)
            {
                try
                {
                    if (slot != null && slot.HasProduct)
                    {
                        ExpirationManager.SyncShelf(slot);

                        var products =
                            GetSortedProducts(slot.transform);

                        List<int> datesList =
                            new List<int>();

                        foreach (var p in products)
                        {
                            if (p == null)
                                continue;

                            var comp =
                                p.GetComponent<ProductExpirationComponent>();

                            if (comp == null)
                            {
                                ExpirationManager.EnsureExpiration(
                                    p,
                                    slot);

                                comp =
                                    p.GetComponent<ProductExpirationComponent>();
                            }

                            if (comp != null)
                                datesList.Add(comp.ExpirationDay);
                        }

                        if (datesList.Count > 0)
                        {
                            string path =
                                GetSlotPath(slot);

                            string joinedDates =
                                string.Join(",", datesList);

                            linesToSave.Add(
                                $"{path}|{joinedDates}");

                            savedSlotsCount++;
                        }
                    }
                }
                catch (Exception ex)
                {
                    StatisticMod.Plugin.Log.LogError(
                        $"[SaveData] Błąd zapisu na slocie półki: {ex.Message}");
                }
            }

            // ============================================================
            // KARTONY - NOWY FORMAT PBOX2
            // PBOX2|boxUID|productId|dates|deliveryDay
            // ============================================================

            int savedBoxesCount = 0;
            int legacyFallbackBoxesCount = 0;
            int skippedBoxesCount = 0;

            var allBoxes =
                UnityEngine.Object.FindObjectsOfType<Box>();

            HashSet<int> usedStableUids =
                new HashSet<int>();

            foreach (var box in allBoxes)
            {
                try
                {
                    if (box == null)
                        continue;

                    int productCount = 0;

                    try
                    {
                        productCount = box.ProductCount;
                    }
                    catch { }

                    // Pusty karton nie ma terminów produktów.
                    if (productCount <= 0)
                        continue;

                    int productId =
                        GetBoxProductId(box);

                    if (productId <= 0)
                    {
                        skippedBoxesCount++;

                        StatisticMod.Plugin.DebugWarning(
                            $"[SaveData] Box skipped - invalid ProductID. " +
                            $"instance={box.GetInstanceID()} count={productCount}");

                        continue;
                    }

                    // Kluczowa zasada: zapis NIE generuje nowej daty.
                    if (!TryGetExactBoxDates(
                            box,
                            out List<int> datesToSave))
                    {
                        skippedBoxesCount++;

                        StatisticMod.Plugin.DebugWarning(
                            $"[SaveData] Box skipped - no exact expiration state. " +
                            $"uid={GetStableBoxUid(box)} " +
                            $"productId={productId} " +
                            $"count={productCount} " +
                            $"instance={box.GetInstanceID()}");

                        continue;
                    }

                    int runtimeKey =
                        box.GetInstanceID();

                    int stableUid =
                        GetStableBoxUid(box);

                    int deliveryDay =
                        GetCurrentDay();

                    if (runtimeBoxDeliveryDays.TryGetValue(
                            runtimeKey,
                            out int runtimeDeliveryDay) &&
                        runtimeDeliveryDay > 0)
                    {
                        deliveryDay = runtimeDeliveryDay;
                    }
                    else if (stableUid > 0 &&
                             boxDeliveryDays.TryGetValue(
                                 stableUid,
                                 out int stableDeliveryDay) &&
                             stableDeliveryDay > 0)
                    {
                        deliveryDay = stableDeliveryDay;
                    }
                    else if (boxDeliveryDays.TryGetValue(
                                 runtimeKey,
                                 out int oldRuntimeDeliveryDay) &&
                             oldRuntimeDeliveryDay > 0)
                    {
                        deliveryDay = oldRuntimeDeliveryDay;
                    }

                    if (deliveryDay < 1)
                        deliveryDay = 1;

                    string joinedDates =
                        string.Join(",", datesToSave);

                    if (stableUid > 0 &&
                        usedStableUids.Add(stableUid))
                    {
                        linesToSave.Add(
                            $"PBOX2|{stableUid}|{productId}|" +
                            $"{joinedDates}|{deliveryDay}");

                        savedBoxesCount++;
                    }
                    else
                    {
                        // Jeżeli UID jest zerowy albo zduplikowany,
                        // nie udajemy, że mamy trwałe dopasowanie.
                        // Zachowujemy rekord w starym formacie PBOX.
                        linesToSave.Add(
                            $"PBOX|{productId}|{joinedDates}|{deliveryDay}");

                        legacyFallbackBoxesCount++;

                        StatisticMod.Plugin.DebugWarning(
                            $"[SaveData] Box has no unique stable UID. " +
                            $"Falling back to legacy PBOX. " +
                            $"uid={stableUid} productId={productId} " +
                            $"instance={runtimeKey}");
                    }

                    // Runtime bieżącej sesji.
                    runtimeBoxDates[runtimeKey] =
                        new List<int>(datesToSave);

                    runtimeBoxDeliveryDays[runtimeKey] =
                        deliveryDay;

                    runtimeBoxDatesFromSave[runtimeKey] =
                        true;

                    // Cache po InstanceID - zgodność z istniejącym kodem.
                    boxDates[runtimeKey] =
                        new List<int>(datesToSave);

                    boxDeliveryDays[runtimeKey] =
                        deliveryDay;

                    // Trwały cache po UID.
                    if (stableUid > 0)
                    {
                        boxDates[stableUid] =
                            new List<int>(datesToSave);

                        boxDeliveryDays[stableUid] =
                            deliveryDay;
                    }
                }
                catch (Exception ex)
                {
                    skippedBoxesCount++;

                    StatisticMod.Plugin.Log.LogError(
                        $"[SaveData] Błąd zapisu kartonu: {ex}");
                }
            }

            // ============================================================
            // OCHRONA MIGRACJI STAREGO PBOX
            //
            // Etykiety dalekich kartonów mogą nie zostać jeszcze
            // zainicjalizowane przed zapisem. Nie wolno wtedy zgubić
            // niezużytych rekordów PBOX ze starego pliku.
            // ============================================================

            int preservedLegacyRecords = 0;

            foreach (var kvp in pendingLoadedBoxes)
            {
                int productId = kvp.Key;
                Queue<SavedBoxData> queue = kvp.Value;

                if (productId <= 0 ||
                    queue == null ||
                    queue.Count == 0)
                {
                    continue;
                }

                foreach (SavedBoxData pending in queue)
                {
                    if (pending == null ||
                        pending.Dates == null ||
                        pending.Dates.Count == 0)
                    {
                        continue;
                    }

                    int deliveryDay =
                        pending.DeliveryDay > 0
                            ? pending.DeliveryDay
                            : 1;

                    linesToSave.Add(
                        $"PBOX|{productId}|" +
                        $"{string.Join(",", pending.Dates)}|" +
                        $"{deliveryDay}");

                    preservedLegacyRecords++;
                }
            }

            try
            {
                string dir =
                    Path.GetDirectoryName(NewSaveFilePath);

                if (!string.IsNullOrEmpty(dir) &&
                    !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.WriteAllLines(
                    NewSaveFilePath,
                    linesToSave);

                StatisticMod.Plugin.DebugLog(
                    $"[SaveData] DONE. " +
                    $"Shelves={savedSlotsCount}, " +
                    $"PBOX2={savedBoxesCount}, " +
                    $"LegacyFallbackBoxes={legacyFallbackBoxesCount}, " +
                    $"PreservedLegacyPBOX={preservedLegacyRecords}, " +
                    $"SkippedBoxes={skippedBoxesCount}, " +
                    $"Lines={linesToSave.Count}");

                try
                {
                    if (File.Exists(LegacySaveFilePath))
                        File.Delete(LegacySaveFilePath);
                }
                catch { }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError(
                    $"[SaveData] BŁĄD ZAPISU: {ex}");
            }
        }

        public static void LoadData()
        {
            // Reset przy zmianie slotu i ponownym ładowaniu.
            SaveDataInitialized = false;
            SaveLoaded = false;

            slotDates.Clear();
            boxDates.Clear();
            boxDeliveryDays.Clear();

            pendingLoadedBoxes.Clear();
            pendingLoadedBoxesByUid.Clear();

            runtimeBoxDates.Clear();
            runtimeBoxDeliveryDays.Clear();
            runtimeBoxDatesFromSave.Clear();
            runtimeBoxConfigVersion.Clear();

            try
            {
                CustomExpirationLoader.Load();

                StatisticMod.Plugin.DebugLog(
                    "[LoadData] Custom expiration configuration loaded.");
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
                SaveDataInitialized = true;
                SaveLoaded = true;

                StatisticMod.Plugin.DebugLog(
                    "[LoadData] No expiration save file found. " +
                    "New empty state initialized.");

                return;
            }

            bool detailedLogs = false;

            try
            {
                detailedLogs =
                    PluginConfig.DetailedLoadLogs != null &&
                    PluginConfig.DetailedLoadLogs.Value;
            }
            catch
            {
                detailedLogs = false;
            }

            int loadedPbox2Records = 0;
            int loadedPboxRecords = 0;
            int loadedLegacyBoxes = 0;
            int loadedSlots = 0;
            int skippedLines = 0;
            int malformedLines = 0;

            try
            {
                foreach (string line in File.ReadLines(fileToLoad))
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(line) ||
                            !line.Contains("|"))
                        {
                            skippedLines++;
                            continue;
                        }

                        string[] parts =
                            line.Split('|');

                        // ==================================================
                        // NOWY FORMAT:
                        // PBOX2|boxUID|productId|dates|deliveryDay
                        // ==================================================
                        if (parts[0] == "PBOX2" &&
                            parts.Length >= 5)
                        {
                            if (!int.TryParse(
                                    parts[1],
                                    out int boxUid) ||
                                boxUid <= 0 ||
                                boxUid == InvalidLegacyBoxUid)
                            {
                                malformedLines++;
                                continue;
                            }

                            if (!int.TryParse(
                                    parts[2],
                                    out int productId) ||
                                productId <= 0)
                            {
                                malformedLines++;
                                continue;
                            }

                            List<int> dates =
                                ParseCsvInts(parts[3]);

                            if (dates == null ||
                                dates.Count == 0)
                            {
                                malformedLines++;
                                continue;
                            }

                            int deliveryDay = 1;

                            if (parts.Length >= 5)
                                int.TryParse(
                                    parts[4],
                                    out deliveryDay);

                            if (deliveryDay < 1)
                                deliveryDay = 1;

                            SavedBoxData savedData =
                                new SavedBoxData
                                {
                                    BoxUid = boxUid,
                                    ProductId = productId,
                                    Dates = new List<int>(dates),
                                    DeliveryDay = deliveryDay
                                };

                            if (pendingLoadedBoxesByUid.ContainsKey(boxUid))
                            {
                                StatisticMod.Plugin.DebugWarning(
                                    $"[LoadData] Duplicate PBOX2 UID skipped: " +
                                    $"uid={boxUid}, productId={productId}");

                                malformedLines++;
                                continue;
                            }

                            pendingLoadedBoxesByUid[boxUid] =
                                savedData;

                            // Trwały cache jest dostępny od razu,
                            // nawet zanim BoxExpirationLabel się uruchomi.
                            boxDates[boxUid] =
                                new List<int>(dates);

                            boxDeliveryDays[boxUid] =
                                deliveryDay;

                            loadedPbox2Records++;

                            if (detailedLogs)
                            {
                                StatisticMod.Plugin.DebugLog(
                                    $"[LoadData] PBOX2 uid={boxUid} " +
                                    $"productId={productId} " +
                                    $"dates={dates.Count} " +
                                    $"deliveryDay={deliveryDay}");
                            }
                        }

                        // ==================================================
                        // STARY PBOX - MIGRACJA
                        // PBOX|productId|dates|deliveryDay
                        // ==================================================
                        else if (parts[0] == "PBOX" &&
                                 parts.Length >= 3)
                        {
                            if (!int.TryParse(
                                    parts[1],
                                    out int productId) ||
                                productId <= 0)
                            {
                                malformedLines++;
                                continue;
                            }

                            List<int> dates =
                                ParseCsvInts(parts[2]);

                            if (dates == null ||
                                dates.Count == 0)
                            {
                                malformedLines++;
                                continue;
                            }

                            int deliveryDay = 1;

                            if (parts.Length >= 4)
                                int.TryParse(
                                    parts[3],
                                    out deliveryDay);

                            if (deliveryDay < 1)
                                deliveryDay = 1;

                            if (!pendingLoadedBoxes.ContainsKey(productId))
                            {
                                pendingLoadedBoxes[productId] =
                                    new Queue<SavedBoxData>();
                            }

                            pendingLoadedBoxes[productId].Enqueue(
                                new SavedBoxData
                                {
                                    BoxUid = 0,
                                    ProductId = productId,
                                    Dates = new List<int>(dates),
                                    DeliveryDay = deliveryDay
                                });

                            loadedPboxRecords++;

                            if (detailedLogs)
                            {
                                StatisticMod.Plugin.DebugLog(
                                    $"[LoadData] Legacy PBOX " +
                                    $"productId={productId} " +
                                    $"dates={dates.Count} " +
                                    $"deliveryDay={deliveryDay}");
                            }
                        }

                        // ==================================================
                        // JESZCZE STARSZY BOX|uid|...
                        // ==================================================
                        else if (parts[0] == "BOX" &&
                                 parts.Length >= 3)
                        {
                            if (!int.TryParse(
                                    parts[1],
                                    out int boxUID) ||
                                boxUID <= 0 ||
                                boxUID == InvalidLegacyBoxUid)
                            {
                                malformedLines++;
                                continue;
                            }

                            boxDates[boxUID] =
                                ParseCsvInts(parts[2]);

                            if (parts.Length >= 4 &&
                                int.TryParse(
                                    parts[3],
                                    out int deliveryDay) &&
                                deliveryDay > 0)
                            {
                                boxDeliveryDays[boxUID] =
                                    deliveryDay;
                            }

                            loadedLegacyBoxes++;

                            if (detailedLogs)
                            {
                                StatisticMod.Plugin.DebugLog(
                                    $"[LoadData] BOX uid={boxUID} " +
                                    $"dates={boxDates[boxUID].Count}");
                            }
                        }

                        // ==================================================
                        // PÓŁKA
                        // ==================================================
                        else if (parts.Length == 2)
                        {
                            string path =
                                parts[0];

                            if (string.IsNullOrEmpty(path))
                            {
                                malformedLines++;
                                continue;
                            }

                            List<int> loadedList =
                                ParseCsvInts(parts[1]);

                            slotDates[path] =
                                loadedList;

                            loadedSlots++;

                            if (detailedLogs)
                            {
                                StatisticMod.Plugin.DebugLog(
                                    $"[LoadData] SLOT path={path} " +
                                    $"dates={loadedList.Count}");
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
                        string dir =
                            Path.GetDirectoryName(NewSaveFilePath);

                        if (!string.IsNullOrEmpty(dir) &&
                            !Directory.Exists(dir))
                        {
                            Directory.CreateDirectory(dir);
                        }

                        File.Copy(
                            LegacySaveFilePath,
                            NewSaveFilePath,
                            true);

                        File.Delete(
                            LegacySaveFilePath);

                        StatisticMod.Plugin.DebugLog(
                            "[LoadData] Legacy save migrated.");
                    }
                    catch (Exception ex)
                    {
                        StatisticMod.Plugin.DebugWarning(
                            $"[LoadData] Legacy migration warning: {ex.Message}");
                    }
                }

                CustomExpirationLoader.Load();

                SaveDataInitialized = true;
                SaveLoaded = true;

                StatisticMod.Plugin.DebugLog(
                    $"[LoadData] DONE. " +
                    $"slots={loadedSlots}, " +
                    $"pbox2Records={loadedPbox2Records}, " +
                    $"legacyPboxRecords={loadedPboxRecords}, " +
                    $"legacyBoxes={loadedLegacyBoxes}, " +
                    $"exactBoxRecords={pendingLoadedBoxesByUid.Count}, " +
                    $"productQueues={pendingLoadedBoxes.Count}, " +
                    $"skipped={skippedLines}, " +
                    $"malformed={malformedLines}");
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
            var list =
                new List<int>();

            if (string.IsNullOrEmpty(csv))
                return list;

            var tokens =
                csv.Split(',');

            for (int i = 0; i < tokens.Length; i++)
            {
                if (int.TryParse(
                        tokens[i],
                        out int val))
                {
                    list.Add(val);
                }
            }

            return list;
        }

        private static int TryGetLegacyBoxUid(Box box)
        {
            if (box == null)
                return 0;

            try
            {
                var prop =
                    box.GetType().GetProperty(
                        "Data",
                        BindingFlags.Public |
                        BindingFlags.NonPublic |
                        BindingFlags.Instance);

                if (prop != null)
                {
                    var dataObj =
                        prop.GetValue(box, null);

                    if (dataObj != null)
                    {
                        var uidProp =
                            dataObj.GetType().GetProperty("UID") ??
                            dataObj.GetType().GetProperty("Uid") ??
                            dataObj.GetType().GetProperty("Id");

                        if (uidProp != null)
                        {
                            var val =
                                uidProp.GetValue(dataObj, null);

                            if (val is int i &&
                                i > 0 &&
                                i != InvalidLegacyBoxUid)
                            {
                                return i;
                            }
                        }
                    }
                }
            }
            catch { }

            return 0;
        }
    }
}
