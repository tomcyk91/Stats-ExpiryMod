using StatisticMod;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace SmartExpiration
{
    // Legacy PBOX/PBOX2 record. Kept for one-time migration and compatibility.
    public class SavedBoxData
    {
        public int BoxUid;
        public int ProductId;
        public List<int> Dates;
        public int DeliveryDay;
        public List<int> DeliveryDays;
        public bool Matched;
    }

    // PBOX3: persistent identity belongs to Stats&Expiry, not to Box.Data.UID.
    // After restart a record is re-attached by the saved physical fingerprint
    // (box type + world transform + product/count), then the private GUID is
    // used for the rest of the session.
    public class SavedBoxDataV3
    {
        public string PersistentId;
        public int BoxId;
        public int ProductId;
        public Vector3 Position;
        public Quaternion Rotation;
        public List<int> Dates;
        public List<int> DeliveryDays;
        public bool Matched;
    }

    /// <summary>
    /// Store points are integer-only in the base game, while weighted products
    /// can represent fractions of a kilogram. Keep the fractional part here so
    /// 1 kg really equals 1 store point across multiple discard/penalty events.
    /// Rewards and penalties use separate accumulators.
    /// </summary>
    public static class ExpirationStorePointLedger
    {
        private static float _rewardRemainder;
        private static float _penaltyRemainder;

        public static float RewardRemainder => _rewardRemainder;
        public static float PenaltyRemainder => _penaltyRemainder;

        public static int ConsumeReward(float pointValue)
        {
            return Consume(ref _rewardRemainder, pointValue);
        }

        public static int ConsumePenalty(float pointValue)
        {
            return Consume(ref _penaltyRemainder, pointValue);
        }

        public static void Reset()
        {
            _rewardRemainder = 0f;
            _penaltyRemainder = 0f;
        }

        public static void Load(float rewardRemainder, float penaltyRemainder)
        {
            _rewardRemainder = NormalizeRemainder(rewardRemainder);
            _penaltyRemainder = NormalizeRemainder(penaltyRemainder);
        }

        private static int Consume(ref float remainder, float pointValue)
        {
            if (pointValue <= 0f)
                return 0;

            float total = Mathf.Max(0f, remainder) + pointValue;

            // Small epsilon avoids losing a point because of values such as
            // 0.99999994f produced by repeated float additions.
            int wholePoints = Mathf.FloorToInt(total + 0.0001f);
            remainder = total - wholePoints;

            if (remainder < 0f)
                remainder = 0f;
            else if (remainder >= 0.9999f)
            {
                wholePoints++;
                remainder = 0f;
            }

            return wholePoints;
        }

        private static float NormalizeRemainder(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                return 0f;

            value -= Mathf.Floor(value);
            return value >= 0.9999f ? 0f : value;
        }
    }

    public static class ExpirationSaveManager
    {
        private const int InvalidLegacyBoxUid = 807810400;
        private const float Pbox3LoadMaxDistance = 1.50f;
        private const float Pbox3LoadMaxAngle = 75f;
        private const float Pbox3SessionMaxDistance = 0.75f;
        private const float Pbox3SessionMaxAngle = 45f;

        public static string CurrentSlotName
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
                string slotFolder =
                    Path.Combine(Application.persistentDataPath, CurrentSlotName);

                return Path.Combine(slotFolder, "SmartExpiration.txt");
            }
        }

        private static string LegacySaveFilePath =>
            Path.Combine(
                Application.persistentDataPath,
                $"SmartExpiration_{CurrentSlotName}.txt");

        private static string Pbox3MigrationBackupPath
        {
            get
            {
                string slotFolder =
                    Path.Combine(Application.persistentDataPath, CurrentSlotName);

                return Path.Combine(
                    slotFolder,
                    "SmartExpiration.pre_PBOX3.bak");
            }
        }

        // Shelf expiration dates. Existing format remains:
        // DisplaySlotPath|expirationCsv
        public static Dictionary<string, List<int>> slotDates =
            new Dictionary<string, List<int>>();

        // New parallel shelf metadata:
        // SDEL|DisplaySlotPath|deliveryCsv
        public static Dictionary<string, List<int>> slotDeliveryDays =
            new Dictionary<string, List<int>>();

        // Compatibility caches for code that reads boxDates/boxDeliveryDays.
        // IMPORTANT: these are current-session mirrors only. They are NEVER
        // used as persistent PBOX3 identity.
        public static Dictionary<int, List<int>> boxDates =
            new Dictionary<int, List<int>>();

        public static Dictionary<int, int> boxDeliveryDays =
            new Dictionary<int, int>();

        // Runtime identity is Unity InstanceID and is valid only this session.
        public static Dictionary<int, List<int>> runtimeBoxDates =
            new Dictionary<int, List<int>>();

        // Compatibility scalar: delivery day associated with the earliest
        // expiration in runtimeBoxDates.
        public static Dictionary<int, int> runtimeBoxDeliveryDays =
            new Dictionary<int, int>();

        // PBOX3 source of truth: one delivery day per physical product.
        public static Dictionary<int, List<int>> runtimeBoxDeliveryDaysPerProduct =
            new Dictionary<int, List<int>>();

        public static Dictionary<int, bool> runtimeBoxDatesFromSave =
            new Dictionary<int, bool>();

        public static Dictionary<int, int> runtimeBoxConfigVersion =
            new Dictionary<int, int>();

        // InstanceID -> Stats&Expiry persistent GUID.
        public static Dictionary<int, string> runtimeBoxPersistentIds =
            new Dictionary<int, string>();

        // GUID -> latest known PBOX3 state. Kept when a runtime Box is rebuilt
        // by another mod so a new InstanceID can recover the old state.
        public static Dictionary<string, SavedBoxDataV3> activeBoxStatesById =
            new Dictionary<string, SavedBoxDataV3>();

        // Parsed PBOX3 records waiting for a physical box after scene load.
        public static List<SavedBoxDataV3> pendingLoadedBoxesV3 =
            new List<SavedBoxDataV3>();

        // Current-session bridge between native rack BoxData and PBOX3.
        // BoxData.UID is NOT used as persistent identity; it is only a runtime
        // lookup key so a materialized Box can recover the exact rack state
        // already assigned to its native RackSlot.Data.RackedBoxDatas record.
        public static Dictionary<int, SavedBoxDataV3> rackBoxStatesByUid =
            new Dictionary<int, SavedBoxDataV3>();

        // Exactly one state per native non-empty rack BoxData record.
        // The TERMINY UI reads this list directly, so virtualized rack boxes
        // do not disappear just because no full Box GameObject exists yet.
        public static List<SavedBoxDataV3> rackDataStates =
            new List<SavedBoxDataV3>();

        // Only cartons whose native BoxData count was changed while virtualized
        // need an extra visual rebuild when they are later materialized from a rack.
        // This avoids calling Box.RefreshSpawnedProducts() for every restocker pickup.
        public static HashSet<int> rackUidsNeedingVisualRefresh =
            new HashSet<int>();

        // Legacy fields kept so older code/migrations still compile.
        public static Dictionary<int, Queue<SavedBoxData>> pendingLoadedBoxes =
            new Dictionary<int, Queue<SavedBoxData>>();

        public static Dictionary<int, SavedBoxData> pendingLoadedBoxesByUid =
            new Dictionary<int, SavedBoxData>();

        // PBOX2/PBOX are migrated once by product/count. PBOX2 UID is only a
        // weak ordering hint; it is NOT treated as the same box after restart.
        private static readonly List<SavedBoxData> legacyBoxMigrationRecords =
            new List<SavedBoxData>();

        public static bool SaveDataInitialized = false;
        public static bool SaveLoaded = false;
        public static bool LastSaveSucceeded { get; private set; } = false;

        public static bool RuntimeWritesReady
        {
            get
            {
                try
                {
                    return SaveLoaded &&
                           ExpirationLoadFinalizer.InitialSyncComplete &&
                           !ExpirationLoadFinalizer.SyncInProgress;
                }
                catch
                {
                    return false;
                }
            }
        }

        /// <summary>
        /// Clears all active expiration sidecar save files when the expiration
        /// system is disabled. This is intentionally destructive for expiry
        /// metadata only; statistics and the native game save are untouched.
        ///
        /// The configuration is global, so disabling expiry resets the expiry
        /// sidecar for every save slot. If expiry is enabled again later, the
        /// normal load synchronizer will see no saved dates and will generate
        /// fresh dates from that save's current in-game day.
        /// </summary>
        public static void ClearAllExpirationSaveFiles()
        {
            ResetRuntimeStateForDisabledSystem();

            int deletedFiles = 0;

            try
            {
                string root = Application.persistentDataPath;

                if (!string.IsNullOrEmpty(root) && Directory.Exists(root))
                {
                    // Current sidecar format: <persistentData>/<slot>/SmartExpiration.txt
                    string[] currentFiles =
                        Directory.GetFiles(
                            root,
                            "SmartExpiration.txt",
                            SearchOption.AllDirectories);

                    for (int i = 0; i < currentFiles.Length; i++)
                    {
                        if (TryDeleteExpirationSaveFile(currentFiles[i]))
                            deletedFiles++;
                    }

                    // Legacy format: <persistentData>/SmartExpiration_<slot>.txt
                    string[] legacyFiles =
                        Directory.GetFiles(
                            root,
                            "SmartExpiration_*.txt",
                            SearchOption.TopDirectoryOnly);

                    for (int i = 0; i < legacyFiles.Length; i++)
                    {
                        if (TryDeleteExpirationSaveFile(legacyFiles[i]))
                            deletedFiles++;
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogWarning(
                    $"[Expiry Disabled] Could not enumerate expiration save files: {ex.Message}");
            }

            StatisticMod.Plugin.Log.LogInfo(
                $"[Expiry Disabled] Expiration save data cleared. Deleted files: {deletedFiles}. " +
                "Statistics and native game saves were not changed.");
        }

        private static bool TryDeleteExpirationSaveFile(string path)
        {
            if (string.IsNullOrEmpty(path))
                return false;

            try
            {
                if (!File.Exists(path))
                    return false;

                File.Delete(path);
                StatisticMod.Plugin.DebugLog(
                    $"[Expiry Disabled] Deleted expiration sidecar: {path}");
                return true;
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogWarning(
                    $"[Expiry Disabled] Could not delete expiration sidecar '{path}': {ex.Message}");
                return false;
            }
        }

        private static void ResetRuntimeStateForDisabledSystem()
        {
            SaveDataInitialized = false;
            SaveLoaded = false;
            LastSaveSucceeded = false;
            ExpirationStorePointLedger.Reset();

            slotDates.Clear();
            slotDeliveryDays.Clear();

            boxDates.Clear();
            boxDeliveryDays.Clear();

            runtimeBoxDates.Clear();
            runtimeBoxDeliveryDays.Clear();
            runtimeBoxDeliveryDaysPerProduct.Clear();
            runtimeBoxDatesFromSave.Clear();
            runtimeBoxConfigVersion.Clear();
            runtimeBoxPersistentIds.Clear();

            activeBoxStatesById.Clear();
            pendingLoadedBoxesV3.Clear();
            rackBoxStatesByUid.Clear();
            rackDataStates.Clear();
            rackUidsNeedingVisualRefresh.Clear();

            pendingLoadedBoxes.Clear();
            pendingLoadedBoxesByUid.Clear();
            legacyBoxMigrationRecords.Clear();

            try
            {
                ExpirationManager.syncedSlots.Clear();
            }
            catch { }
        }

        public static List<global::Product> GetSortedProducts(Transform parent)
        {
            var il2cppArray =
                parent != null
                    ? parent.GetComponentsInChildren<global::Product>(true)
                    : null;

            var products =
                new List<global::Product>(
                    il2cppArray != null
                        ? il2cppArray.Count
                        : 0);

            if (il2cppArray != null)
            {
                for (int i = 0; i < il2cppArray.Count; i++)
                {
                    if (il2cppArray[i] != null)
                        products.Add(il2cppArray[i]);
                }
            }

            products.Sort(
                (a, b) =>
                    a.transform.GetSiblingIndex()
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

        // Compatibility only. PBOX3 deliberately does not persist by this UID,
        // because Supermarket Simulator can renumber it after restart.
        public static int GetStableBoxUid(Box box)
        {
            if (box == null)
                return 0;

            try
            {
                if (box.Data != null)
                {
                    int uid = box.Data.UID;

                    if (uid > 0 &&
                        uid != InvalidLegacyBoxUid)
                    {
                        return uid;
                    }
                }
            }
            catch { }

            return TryGetLegacyBoxUid(box);
        }

        public static int GetBoxProductId(Box box)
        {
            if (box == null)
                return 0;

            try
            {
                if (box.Data != null &&
                    box.Data.ProductID > 0)
                {
                    return box.Data.ProductID;
                }
            }
            catch { }

            try
            {
                var products =
                    box.GetComponentsInChildren<global::Product>(true);

                if (products != null &&
                    products.Count > 0 &&
                    products[0] != null)
                {
                    return GetProductIdFromProduct(products[0]);
                }
            }
            catch { }

            return 0;
        }

        public static int GetBoxId(Box box)
        {
            if (box == null)
                return 0;

            try
            {
                int id = box.BoxID;
                return id > 0 ? id : 0;
            }
            catch
            {
                return 0;
            }
        }

        public static int GetCurrentDaySafe()
        {
            try
            {
                var dcm =
                    DayCycleManager.HasInstance
                        ? DayCycleManager.Instance
                        : null;

                if (dcm != null &&
                    dcm.CurrentDay > 0)
                {
                    return dcm.CurrentDay;
                }
            }
            catch { }

            try
            {
                var sm =
                    SaveManager.HasInstance
                        ? SaveManager.Instance
                        : null;

                if (sm != null &&
                    sm.Progression != null &&
                    sm.Progression.CurrentDay > 0)
                {
                    return sm.Progression.CurrentDay;
                }
            }
            catch { }

            return 1;
        }

        public static int InferDeliveryDay(
            int productId,
            int expirationDay)
        {
            if (expirationDay <= 0)
                return GetCurrentDaySafe();

            try
            {
                int shelfLife =
                    ExpirationCalculator.GetDaysForProduct(
                        null,
                        productId);

                int inferred =
                    expirationDay - shelfLife;

                return inferred > 0
                    ? inferred
                    : 1;
            }
            catch
            {
                return 1;
            }
        }

        public static int NormalizeDeliveryDay(
            int productId,
            int expirationDay,
            int deliveryDay)
        {
            if (deliveryDay > 0)
                return deliveryDay;

            return InferDeliveryDay(
                productId,
                expirationDay);
        }

        private static List<int> NormalizeDeliveryList(
            int productId,
            List<int> dates,
            List<int> deliveryDays,
            int legacyDeliveryDay)
        {
            var result =
                new List<int>(
                    dates != null
                        ? dates.Count
                        : 0);

            if (dates == null)
                return result;

            for (int i = 0; i < dates.Count; i++)
            {
                int delivery = 0;

                if (deliveryDays != null &&
                    i < deliveryDays.Count)
                {
                    delivery = deliveryDays[i];
                }

                if (delivery <= 0)
                    delivery = legacyDeliveryDay;

                result.Add(
                    NormalizeDeliveryDay(
                        productId,
                        dates[i],
                        delivery));
            }

            return result;
        }

        private static SavedBoxDataV3 CloneState(
            SavedBoxDataV3 source)
        {
            if (source == null)
                return null;

            return new SavedBoxDataV3
            {
                PersistentId = source.PersistentId,
                BoxId = source.BoxId,
                ProductId = source.ProductId,
                Position = source.Position,
                Rotation = source.Rotation,
                Dates =
                    source.Dates != null
                        ? new List<int>(source.Dates)
                        : new List<int>(),
                DeliveryDays =
                    source.DeliveryDays != null
                        ? new List<int>(source.DeliveryDays)
                        : new List<int>(),
                Matched = source.Matched
            };
        }

        private static float StateMatchScore(
            Box box,
            SavedBoxDataV3 state,
            float maxDistance,
            float maxAngle,
            bool requireExactCount,
            int currentCount)
        {
            if (box == null ||
                state == null ||
                state.Dates == null ||
                state.DeliveryDays == null ||
                state.Dates.Count != state.DeliveryDays.Count)
            {
                return float.MaxValue;
            }

            int productId =
                GetBoxProductId(box);

            if (productId <= 0)
                return float.MaxValue;

            if (state.ProductId > 0 &&
                state.ProductId != productId)
            {
                return float.MaxValue;
            }

            int boxId =
                GetBoxId(box);

            if (state.BoxId > 0 &&
                boxId > 0 &&
                state.BoxId != boxId)
            {
                return float.MaxValue;
            }

            int boxCount = 0;

            try
            {
                boxCount = box.ProductCount;
            }
            catch { }

            int comparisonCount =
                currentCount >= 0
                    ? currentCount
                    : boxCount;

            if (requireExactCount)
            {
                if (state.Dates.Count != boxCount)
                    return float.MaxValue;
            }
            else
            {
                // AddProduct prefix runs before the native count is incremented.
                if (comparisonCount < 0 ||
                    comparisonCount >= state.Dates.Count)
                {
                    return float.MaxValue;
                }
            }

            Vector3 currentPosition =
                box.transform.position;

            Quaternion currentRotation =
                box.transform.rotation;

            float distance =
                Vector3.Distance(
                    currentPosition,
                    state.Position);

            if (distance > maxDistance)
                return float.MaxValue;

            float angle =
                Quaternion.Angle(
                    currentRotation,
                    state.Rotation);

            if (angle > maxAngle)
                return float.MaxValue;

            // Position dominates; rotation is only a tie-breaker.
            return
                (distance * distance) +
                ((angle / 180f) * 0.10f);
        }

        private static SavedBoxDataV3 FindBestPendingPbox3(
            Box box,
            bool requireExactCount,
            int currentCount)
        {
            SavedBoxDataV3 best = null;
            float bestScore = float.MaxValue;

            for (int i = 0;
                 i < pendingLoadedBoxesV3.Count;
                 i++)
            {
                SavedBoxDataV3 state =
                    pendingLoadedBoxesV3[i];

                if (state == null ||
                    state.Matched)
                {
                    continue;
                }

                float score =
                    StateMatchScore(
                        box,
                        state,
                        Pbox3LoadMaxDistance,
                        Pbox3LoadMaxAngle,
                        requireExactCount,
                        currentCount);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = state;
                }
            }

            return best;
        }

        private static SavedBoxDataV3 FindBestSessionState(
            Box box,
            int currentCount)
        {
            SavedBoxDataV3 best = null;
            float bestScore = float.MaxValue;

            foreach (var kvp in activeBoxStatesById)
            {
                SavedBoxDataV3 state =
                    kvp.Value;

                if (state == null)
                    continue;

                float score =
                    StateMatchScore(
                        box,
                        state,
                        Pbox3SessionMaxDistance,
                        Pbox3SessionMaxAngle,
                        false,
                        currentCount);

                if (score < bestScore)
                {
                    bestScore = score;
                    best = state;
                }
            }

            return best;
        }

        private static void UpdateDerivedBoxDeliveryDay(
            int runtimeKey)
        {
            if (!runtimeBoxDates.TryGetValue(
                    runtimeKey,
                    out List<int> dates) ||
                dates == null ||
                dates.Count == 0 ||
                !runtimeBoxDeliveryDaysPerProduct.TryGetValue(
                    runtimeKey,
                    out List<int> deliveries) ||
                deliveries == null ||
                deliveries.Count != dates.Count)
            {
                runtimeBoxDeliveryDays.Remove(runtimeKey);
                return;
            }

            int bestIndex = 0;
            int bestDate = dates[0];

            for (int i = 1; i < dates.Count; i++)
            {
                if (dates[i] < bestDate)
                {
                    bestDate = dates[i];
                    bestIndex = i;
                }
            }

            int delivery =
                deliveries[bestIndex] > 0
                    ? deliveries[bestIndex]
                    : 1;

            runtimeBoxDeliveryDays[runtimeKey] =
                delivery;
        }

        private static void MirrorCompatibilityCaches(
            Box box)
        {
            if (box == null)
                return;

            int runtimeKey =
                box.GetInstanceID();

            int uid =
                GetStableBoxUid(box);

            if (uid <= 0)
                return;

            if (runtimeBoxDates.TryGetValue(
                    runtimeKey,
                    out List<int> dates) &&
                dates != null)
            {
                boxDates[uid] =
                    new List<int>(dates);
            }

            UpdateDerivedBoxDeliveryDay(runtimeKey);

            if (runtimeBoxDeliveryDays.TryGetValue(
                    runtimeKey,
                    out int deliveryDay) &&
                deliveryDay > 0)
            {
                boxDeliveryDays[uid] =
                    deliveryDay;
            }
        }

        private static void ApplyRuntimeState(
            Box box,
            SavedBoxDataV3 state,
            bool fromSave,
            bool applyPhysicalComponents)
        {
            if (box == null ||
                state == null ||
                state.Dates == null ||
                state.Dates.Count == 0)
            {
                return;
            }

            int runtimeKey =
                box.GetInstanceID();

            List<int> deliveries =
                NormalizeDeliveryList(
                    state.ProductId,
                    state.Dates,
                    state.DeliveryDays,
                    0);

            runtimeBoxDates[runtimeKey] =
                new List<int>(state.Dates);

            runtimeBoxDeliveryDaysPerProduct[runtimeKey] =
                new List<int>(deliveries);

            runtimeBoxDatesFromSave[runtimeKey] =
                fromSave;

            runtimeBoxConfigVersion[runtimeKey] =
                -1;

            string persistentId =
                !string.IsNullOrEmpty(state.PersistentId)
                    ? state.PersistentId
                    : Guid.NewGuid().ToString("N");

            runtimeBoxPersistentIds[runtimeKey] =
                persistentId;

            // Do not mutate a pending record loaded from disk here. During
            // Box.AddProduct prefix it may still be needed by the finalizer as
            // the original saved fingerprint. Create a separate current-session
            // snapshot instead.
            var activeState =
                new SavedBoxDataV3
                {
                    PersistentId = persistentId,
                    BoxId = GetBoxId(box),
                    ProductId = GetBoxProductId(box),
                    Position = box.transform.position,
                    Rotation = box.transform.rotation,
                    Dates = new List<int>(runtimeBoxDates[runtimeKey]),
                    DeliveryDays = new List<int>(runtimeBoxDeliveryDaysPerProduct[runtimeKey]),
                    Matched = true
                };

            activeBoxStatesById[persistentId] =
                activeState;

            // Current-session native identity bridge. Never serialized as
            // persistent identity, but it is authoritative while this save is
            // running and prevents transform-based state stealing.
            int activeUid = GetStableBoxUid(box);
            if (activeUid > 0 && activeUid != InvalidLegacyBoxUid)
            {
                rackBoxStatesByUid[activeUid] = activeState;
            }

            UpdateDerivedBoxDeliveryDay(runtimeKey);
            MirrorCompatibilityCaches(box);

            if (!applyPhysicalComponents)
                return;

            try
            {
                List<global::Product> products =
                    GetSortedProducts(box.transform);

                int count =
                    Math.Min(
                        products.Count,
                        state.Dates.Count);

                for (int i = 0; i < count; i++)
                {
                    global::Product product =
                        products[i];

                    if (product == null)
                        continue;

                    var comp =
                        product.GetComponent<ProductExpirationComponent>();

                    if (comp == null)
                    {
                        comp =
                            product.gameObject
                                .AddComponent<ProductExpirationComponent>();

                        comp.hideFlags =
                            HideFlags.DontSave |
                            HideFlags.HideInInspector;
                    }

                    comp.ProductID =
                        activeState.ProductId > 0
                            ? activeState.ProductId
                            : state.ProductId;

                    comp.ExpirationDay =
                        state.Dates[i];

                    comp.DeliveryDay =
                        deliveries[i];
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    $"[PBOX3] Physical component restore warning: {ex.Message}");
            }
        }

        private static bool IsStartupHydrationWindow()
        {
            try
            {
                return !ExpirationLoadFinalizer.InitialSyncComplete ||
                       ExpirationLoadFinalizer.SyncInProgress;
            }
            catch
            {
                // During very early startup prefer the conservative load path.
                return true;
            }
        }

        public static bool TryHydrateRuntimeFromKnownState(
            Box box,
            int productId,
            int currentCount)
        {
            if (box == null ||
                productId <= 0 ||
                currentCount < 0)
            {
                return false;
            }

            int runtimeKey =
                box.GetInstanceID();

            if (runtimeBoxDates.TryGetValue(
                    runtimeKey,
                    out List<int> existingDates) &&
                existingDates != null &&
                runtimeBoxDeliveryDaysPerProduct.TryGetValue(
                    runtimeKey,
                    out List<int> existingDeliveries) &&
                existingDeliveries != null &&
                existingDates.Count == existingDeliveries.Count &&
                currentCount <= existingDates.Count)
            {
                return true;
            }

            // CURRENT SESSION: exact native BoxData.UID is the only allowed
            // identity bridge. This prevents a freshly purchased carton from
            // inheriting an old PBOX3 merely because Warehouse Refill placed it
            // in the same rack position as a previous carton of the same product.
            int currentUid =
                GetStableBoxUid(box);

            if (currentUid > 0 &&
                rackBoxStatesByUid.TryGetValue(
                    currentUid,
                    out SavedBoxDataV3 uidState) &&
                IsValidPbox3State(uidState) &&
                uidState.ProductId == productId &&
                uidState.Dates.Count >= currentCount)
            {
                ApplyRuntimeState(
                    box,
                    CloneState(uidState),
                    true,
                    false);

                return true;
            }

            // Transform/proximity matching is intentionally restricted to the
            // initial save reconstruction window. After InitialSyncComplete a
            // new UID means a new carton and must receive a fresh delivery date.
            if (!IsStartupHydrationWindow())
                return false;

            // Initial load: exact PBOX3 record from disk.
            SavedBoxDataV3 pending =
                FindBestPendingPbox3(
                    box,
                    false,
                    currentCount);

            if (pending != null &&
                (pending.ProductId <= 0 ||
                 pending.ProductId == productId))
            {
                ApplyRuntimeState(
                    box,
                    pending,
                    true,
                    false);

                return true;
            }

            // Initial load only: same-session reconstruction by transform.
            SavedBoxDataV3 session =
                FindBestSessionState(
                    box,
                    currentCount);

            if (session != null &&
                (session.ProductId <= 0 ||
                 session.ProductId == productId))
            {
                ApplyRuntimeState(
                    box,
                    CloneState(session),
                    true,
                    false);

                return true;
            }

            return false;
        }

        public static bool EnsureRuntimeBoxState(Box box)
        {
            if (box == null)
                return false;

            int productCount = 0;

            try
            {
                productCount = box.ProductCount;
            }
            catch { }

            if (productCount <= 0)
                return false;

            int runtimeKey =
                box.GetInstanceID();

            int productId =
                GetBoxProductId(box);

            if (productId <= 0)
                return false;

            if (runtimeBoxDates.TryGetValue(
                    runtimeKey,
                    out List<int> dates) &&
                dates != null &&
                dates.Count == productCount &&
                runtimeBoxDeliveryDaysPerProduct.TryGetValue(
                    runtimeKey,
                    out List<int> deliveries) &&
                deliveries != null &&
                deliveries.Count == productCount)
            {
                TouchRuntimeBoxState(box);
                return true;
            }

            // Native rack bridge. The game can virtualize boxes on storage
            // racks: RackSlot.Data still owns BoxData while the full Box /
            // Product hierarchy may be incomplete. During bootstrap every
            // native BoxData is assigned one PBOX3 state and indexed by its
            // current-session BoxData.UID. When that Box GameObject exists,
            // use this mapping before transform-based matching.
            int currentBoxUid = GetStableBoxUid(box);

            if (currentBoxUid > 0 &&
                rackBoxStatesByUid.TryGetValue(
                    currentBoxUid,
                    out SavedBoxDataV3 rackState) &&
                IsValidPbox3State(rackState) &&
                rackState.ProductId == productId &&
                rackState.Dates.Count == productCount)
            {
                ApplyRuntimeState(
                    box,
                    CloneState(rackState),
                    true,
                    true);

                return true;
            }

            // IMPORTANT: transform/proximity matching is a save-load migration
            // mechanism only. During normal gameplay a Box without an exact
            // current-session BoxData.UID mapping must never steal a PBOX3 record
            // from another carton that happens to share product/count/position.
            // That was the source of old (even already-expired) dates reappearing
            // after OvernightWorkers materialized boxes.
            if (IsStartupHydrationWindow())
            {
                SavedBoxDataV3 pending =
                    FindBestPendingPbox3(
                        box,
                        true,
                        productCount);

                if (pending != null)
                {
                    pending.Matched = true;

                    ApplyRuntimeState(
                        box,
                        pending,
                        true,
                        true);

                    return true;
                }

                // Same-session rebuilt object is also allowed only while the
                // initial save reconstruction is still in progress.
                SavedBoxDataV3 sessionBest = null;
                float sessionBestScore = float.MaxValue;

                foreach (var kvp in activeBoxStatesById)
                {
                    SavedBoxDataV3 state = kvp.Value;

                    if (state == null ||
                        state.Dates == null ||
                        state.Dates.Count != productCount)
                    {
                        continue;
                    }

                    float score =
                        StateMatchScore(
                            box,
                            state,
                            Pbox3SessionMaxDistance,
                            Pbox3SessionMaxAngle,
                            true,
                            productCount);

                    if (score < sessionBestScore)
                    {
                        sessionBestScore = score;
                        sessionBest = state;
                    }
                }

                if (sessionBest != null)
                {
                    ApplyRuntimeState(
                        box,
                        CloneState(sessionBest),
                        true,
                        true);

                    return true;
                }
            }

            // Last safe runtime rebuild: physical Product components.
            try
            {
                List<global::Product> products =
                    GetSortedProducts(box.transform);

                if (products.Count != productCount)
                    return false;

                var rebuiltDates =
                    new List<int>(productCount);

                var rebuiltDeliveries =
                    new List<int>(productCount);

                for (int i = 0; i < products.Count; i++)
                {
                    global::Product product =
                        products[i];

                    if (product == null)
                        return false;

                    var comp =
                        product.GetComponent<ProductExpirationComponent>();

                    if (comp == null)
                    {
                        comp =
                            ExpirationManager.EnsureExpiration(
                                product,
                                null);
                    }

                    if (comp == null ||
                        comp.ExpirationDay <= 0)
                    {
                        return false;
                    }

                    comp.ProductID =
                        productId;

                    comp.DeliveryDay =
                        NormalizeDeliveryDay(
                            productId,
                            comp.ExpirationDay,
                            comp.DeliveryDay);

                    rebuiltDates.Add(
                        comp.ExpirationDay);

                    rebuiltDeliveries.Add(
                        comp.DeliveryDay);
                }

                runtimeBoxDates[runtimeKey] =
                    rebuiltDates;

                runtimeBoxDeliveryDaysPerProduct[runtimeKey] =
                    rebuiltDeliveries;

                runtimeBoxDatesFromSave[runtimeKey] =
                    false;

                runtimeBoxConfigVersion[runtimeKey] =
                    -1;

                if (!runtimeBoxPersistentIds.ContainsKey(runtimeKey))
                {
                    runtimeBoxPersistentIds[runtimeKey] =
                        Guid.NewGuid().ToString("N");
                }

                TouchRuntimeBoxState(box);

                return true;
            }
            catch
            {
                return false;
            }
        }

        // Re-apply the authoritative runtime PBOX3 pairs to already spawned
        // Product objects without rebuilding the whole carton. This is cheaper
        // than Box.RefreshSpawnedProducts() and prevents pooled/materialized
        // Product GameObjects from carrying stale dates from an older carton.
        // Returns the number of physical Product objects currently found.
        public static int SyncRuntimeBoxComponents(Box box)
        {
            if (box == null)
                return 0;

            int runtimeKey =
                box.GetInstanceID();

            if (!runtimeBoxDates.TryGetValue(
                    runtimeKey,
                    out List<int> dates) ||
                dates == null ||
                !runtimeBoxDeliveryDaysPerProduct.TryGetValue(
                    runtimeKey,
                    out List<int> deliveries) ||
                deliveries == null ||
                dates.Count == 0 ||
                dates.Count != deliveries.Count)
            {
                return 0;
            }

            int productId =
                GetBoxProductId(box);

            if (productId <= 0)
                return 0;

            try
            {
                List<global::Product> products =
                    GetSortedProducts(box.transform);

                int physicalCount =
                    products.Count;

                int count =
                    Math.Min(
                        physicalCount,
                        dates.Count);

                for (int i = 0; i < count; i++)
                {
                    global::Product product =
                        products[i];

                    if (product == null)
                        continue;

                    var comp =
                        product.GetComponent<ProductExpirationComponent>();

                    if (comp == null)
                    {
                        comp =
                            product.gameObject
                                .AddComponent<ProductExpirationComponent>();

                        comp.hideFlags =
                            HideFlags.DontSave |
                            HideFlags.HideInInspector;
                    }

                    comp.ProductID =
                        productId;

                    comp.ExpirationDay =
                        dates[i];

                    comp.DeliveryDay =
                        NormalizeDeliveryDay(
                            productId,
                            dates[i],
                            deliveries[i]);
                }

                return physicalCount;
            }
            catch
            {
                return 0;
            }
        }

        public static void TouchRuntimeBoxState(Box box)
        {
            if (box == null)
                return;

            int runtimeKey =
                box.GetInstanceID();

            if (!runtimeBoxDates.TryGetValue(
                    runtimeKey,
                    out List<int> dates) ||
                dates == null ||
                !runtimeBoxDeliveryDaysPerProduct.TryGetValue(
                    runtimeKey,
                    out List<int> deliveries) ||
                deliveries == null ||
                dates.Count == 0 ||
                dates.Count != deliveries.Count)
            {
                return;
            }

            string persistentId = null;

            if (!runtimeBoxPersistentIds.TryGetValue(
                    runtimeKey,
                    out persistentId) ||
                string.IsNullOrEmpty(persistentId))
            {
                persistentId =
                    Guid.NewGuid().ToString("N");

                runtimeBoxPersistentIds[runtimeKey] =
                    persistentId;
            }

            SavedBoxDataV3 state =
                new SavedBoxDataV3
                {
                    PersistentId = persistentId,
                    BoxId = GetBoxId(box),
                    ProductId = GetBoxProductId(box),
                    Position = box.transform.position,
                    Rotation = box.transform.rotation,
                    Dates = new List<int>(dates),
                    DeliveryDays = new List<int>(deliveries),
                    Matched = true
                };

            activeBoxStatesById[persistentId] =
                state;

            int currentUid = GetStableBoxUid(box);
            if (currentUid > 0 && currentUid != InvalidLegacyBoxUid)
            {
                rackBoxStatesByUid[currentUid] = state;
            }

            UpdateDerivedBoxDeliveryDay(runtimeKey);
            MirrorCompatibilityCaches(box);
        }

        public static void RemoveRuntimeBoxInstance(
            Box box,
            bool contentGone)
        {
            if (box == null)
                return;

            int runtimeKey =
                box.GetInstanceID();

            string persistentId = null;

            runtimeBoxPersistentIds.TryGetValue(
                runtimeKey,
                out persistentId);

            runtimeBoxDates.Remove(runtimeKey);
            runtimeBoxDeliveryDays.Remove(runtimeKey);
            runtimeBoxDeliveryDaysPerProduct.Remove(runtimeKey);
            runtimeBoxDatesFromSave.Remove(runtimeKey);
            runtimeBoxConfigVersion.Remove(runtimeKey);
            runtimeBoxPersistentIds.Remove(runtimeKey);

            int uid =
                GetStableBoxUid(box);

            if (uid > 0)
            {
                boxDates.Remove(uid);
                boxDeliveryDays.Remove(uid);

                if (contentGone)
                    rackBoxStatesByUid.Remove(uid);
            }

            if (contentGone &&
                !string.IsNullOrEmpty(persistentId))
            {
                activeBoxStatesById.Remove(persistentId);

                // A matched PBOX3 record loaded from disk must disappear too
                // when the physical box loses all content. Otherwise a later
                // fail-safe save could resurrect an already consumed box.
                for (int i = pendingLoadedBoxesV3.Count - 1; i >= 0; i--)
                {
                    SavedBoxDataV3 pending = pendingLoadedBoxesV3[i];

                    if (pending != null &&
                        string.Equals(
                            pending.PersistentId,
                            persistentId,
                            StringComparison.Ordinal))
                    {
                        pendingLoadedBoxesV3.RemoveAt(i);
                    }
                }
            }
        }

        public static bool TryGetBoxDisplayPair(
            Box box,
            out int expirationDay,
            out int deliveryDay)
        {
            expirationDay = -1;
            deliveryDay = -1;

            if (!EnsureRuntimeBoxState(box))
                return false;

            int runtimeKey =
                box.GetInstanceID();

            if (!runtimeBoxDates.TryGetValue(
                    runtimeKey,
                    out List<int> dates) ||
                dates == null ||
                dates.Count == 0 ||
                !runtimeBoxDeliveryDaysPerProduct.TryGetValue(
                    runtimeKey,
                    out List<int> deliveries) ||
                deliveries == null ||
                deliveries.Count != dates.Count)
            {
                return false;
            }

            int bestIndex = 0;

            for (int i = 1; i < dates.Count; i++)
            {
                if (dates[i] < dates[bestIndex])
                    bestIndex = i;
            }

            expirationDay =
                dates[bestIndex];

            deliveryDay =
                NormalizeDeliveryDay(
                    GetBoxProductId(box),
                    expirationDay,
                    deliveries[bestIndex]);

            return true;
        }

        private static SavedBoxDataV3 BuildCurrentBoxState(
            Box box)
        {
            if (box == null ||
                !EnsureRuntimeBoxState(box))
            {
                return null;
            }

            int runtimeKey =
                box.GetInstanceID();

            if (!runtimeBoxDates.TryGetValue(
                    runtimeKey,
                    out List<int> dates) ||
                dates == null ||
                dates.Count == 0 ||
                !runtimeBoxDeliveryDaysPerProduct.TryGetValue(
                    runtimeKey,
                    out List<int> deliveries) ||
                deliveries == null ||
                deliveries.Count != dates.Count)
            {
                return null;
            }

            string id = null;

            if (!runtimeBoxPersistentIds.TryGetValue(
                    runtimeKey,
                    out id) ||
                string.IsNullOrEmpty(id))
            {
                id =
                    Guid.NewGuid().ToString("N");

                runtimeBoxPersistentIds[runtimeKey] =
                    id;
            }

            return new SavedBoxDataV3
            {
                PersistentId = id,
                BoxId = GetBoxId(box),
                ProductId = GetBoxProductId(box),
                Position = box.transform.position,
                Rotation = box.transform.rotation,
                Dates = new List<int>(dates),
                DeliveryDays = new List<int>(deliveries),
                Matched = true
            };
        }

        private static bool TryRestoreLegacyRecord(
            Box box,
            SavedBoxData legacy)
        {
            if (box == null ||
                legacy == null ||
                legacy.Dates == null ||
                legacy.Dates.Count == 0)
            {
                return false;
            }

            int productCount = 0;

            try
            {
                productCount = box.ProductCount;
            }
            catch { }

            int productId =
                GetBoxProductId(box);

            if (productId <= 0 ||
                productId != legacy.ProductId ||
                productCount != legacy.Dates.Count)
            {
                return false;
            }

            var migrated =
                new SavedBoxDataV3
                {
                    PersistentId =
                        Guid.NewGuid().ToString("N"),
                    BoxId = GetBoxId(box),
                    ProductId = productId,
                    Position = box.transform.position,
                    Rotation = box.transform.rotation,
                    Dates = new List<int>(legacy.Dates),
                    DeliveryDays =
                        NormalizeDeliveryList(
                            productId,
                            legacy.Dates,
                            legacy.DeliveryDays,
                            legacy.DeliveryDay),
                    Matched = true
                };

            ApplyRuntimeState(
                box,
                migrated,
                true,
                true);

            legacy.Matched = true;
            return true;
        }

        // Returns every physical box we can reach in the loaded gameplay
        // scene, including boxes stored inside RackSlot.Boxes and inactive
        // hierarchy objects. RackSlot.Boxes is important because the game may
        // hide / instance warehouse boxes and then FindObjectsOfType<Box>()
        // sees them only after the player takes a box off the rack.
        // RackSlot.Data.RackedBoxDatas is the native save-backed source of truth
        // for warehouse boxes. RackSlot.Boxes / Box.ProductCount can be incomplete
        // while the game virtualizes boxes sitting on storage racks.
        public static int CountRackDataBoxes()
        {
            int total = 0;

            try
            {
                var rackSlots =
                    UnityEngine.Resources.FindObjectsOfTypeAll<RackSlot>();

                if (rackSlots == null)
                    return 0;

                for (int s = 0; s < rackSlots.Length; s++)
                {
                    RackSlot slot = rackSlots[s];
                    if (slot == null || slot.gameObject == null)
                        continue;

                    try
                    {
                        var scene = slot.gameObject.scene;
                        if (!scene.IsValid() || !scene.isLoaded)
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    RackSlotData data = null;
                    try { data = slot.Data; } catch { }

                    if (data == null || data.RackedBoxDatas == null)
                        continue;

                    var boxes = data.RackedBoxDatas;
                    for (int i = 0; i < boxes.Count; i++)
                    {
                        BoxData boxData = boxes[i];
                        if (boxData == null ||
                            boxData.ProductID <= 0 ||
                            boxData.ProductCount <= 0)
                        {
                            continue;
                        }

                        total++;
                    }
                }
            }
            catch { }

            return total;
        }

        private static bool IsValidPbox3State(SavedBoxDataV3 state)
        {
            return state != null &&
                   !string.IsNullOrEmpty(state.PersistentId) &&
                   state.ProductId > 0 &&
                   state.Dates != null &&
                   state.DeliveryDays != null &&
                   state.Dates.Count > 0 &&
                   state.Dates.Count == state.DeliveryDays.Count;
        }

        // Synchronize a persisted PBOX3 rack state with the native BoxData count.
        // Box.GetProductFromBox consumes the newest active metadata entry from
        // the end of the list, so if the native count is lower than PBOX3 we can
        // safely discard only the stale tail. If PBOX3 has fewer entries than the
        // native box, we cannot reconstruct the missing dates and must fail soft.
        public static bool TryReconcileRackStateCount(
            SavedBoxDataV3 state,
            int productCount,
            out int trimmedEntries)
        {
            trimmedEntries = 0;

            if (state == null ||
                productCount <= 0 ||
                state.Dates == null ||
                state.DeliveryDays == null ||
                state.Dates.Count != state.DeliveryDays.Count ||
                state.Dates.Count <= 0)
            {
                return false;
            }

            int savedCount = state.Dates.Count;

            if (savedCount < productCount)
            {
                return false;
            }

            if (savedCount == productCount)
            {
                return true;
            }

            trimmedEntries = savedCount - productCount;

            state.Dates.RemoveRange(
                productCount,
                trimmedEntries);

            state.DeliveryDays.RemoveRange(
                productCount,
                trimmedEntries);

            return true;
        }

        // Old releases only persisted Box GameObjects visible to a scene scan.
        // The native rack save, however, keeps every stored box in
        // RackSlot.Data.RackedBoxDatas even when no full Box object/products are
        // materialized. This migration fills ONLY genuinely missing rack records.
        // Existing PBOX3 records always win; no existing dates are overwritten.
        public static int BootstrapMissingRackBoxStates()
        {
            int generated = 0;
            int rackDataBoxes = 0;
            int matchedExisting = 0;
            int exactUidMatches = 0;
            int runtimeNewUidStates = 0;
            int uidMapped = 0;
            int uidInvalid = 0;
            int uidCollisions = 0;
            int reconciledStates = 0;
            int reconciledEntries = 0;
            int undersizedStates = 0;
            int orphanedStatesPruned = 0;
            int currentDay = GetCurrentDaySafe();

            bool runtimeIdentityOnly = false;
            try
            {
                runtimeIdentityOnly =
                    ExpirationLoadFinalizer.InitialSyncComplete &&
                    !ExpirationLoadFinalizer.SyncInProgress;
            }
            catch { }

            // Read/parse the shelf-life config once for this complete rack pass.
            // Load() is already throttled, but calling it outside the box loop also
            // avoids thousands of repeated Time/config checks on large warehouses.
            CustomExpirationLoader.Load();

            // Preserve the CURRENT-SESSION UID bridge before rebuilding the rack
            // snapshot. After startup this bridge is authoritative: a new UID is
            // a genuinely new carton and must NOT steal an old transform-matched
            // PBOX3 from another carton that used to occupy the same rack slot.
            var previousUidStates =
                new Dictionary<int, SavedBoxDataV3>(rackBoxStatesByUid);

            rackBoxStatesByUid.Clear();
            rackDataStates.Clear();

            // Transform/proximity matching is needed only during initial save-load
            // migration. Grouping by ProductId changes that startup pass from roughly
            // O(rackBoxes * allStates) to O(rackBoxes * statesForSameProduct).
            Dictionary<int, List<SavedBoxDataV3>> knownStatesByProduct = null;
            HashSet<string> knownIds = null;
            HashSet<string> usedKnownIds = null;

            if (!runtimeIdentityOnly)
            {
                knownStatesByProduct =
                    new Dictionary<int, List<SavedBoxDataV3>>();
                knownIds =
                    new HashSet<string>(StringComparer.Ordinal);
                usedKnownIds =
                    new HashSet<string>(StringComparer.Ordinal);

                Action<SavedBoxDataV3, string> addKnown =
                    (state, fallbackId) =>
                    {
                        if (!IsValidPbox3State(state))
                            return;

                        string id =
                            !string.IsNullOrEmpty(state.PersistentId)
                                ? state.PersistentId
                                : fallbackId;

                        if (string.IsNullOrEmpty(id) || !knownIds.Add(id))
                            return;

                        if (!knownStatesByProduct.TryGetValue(
                                state.ProductId,
                                out List<SavedBoxDataV3> list))
                        {
                            list = new List<SavedBoxDataV3>();
                            knownStatesByProduct[state.ProductId] = list;
                        }

                        list.Add(state);
                    };

                foreach (var kvp in activeBoxStatesById)
                    addKnown(kvp.Value, kvp.Key);

                for (int i = 0; i < pendingLoadedBoxesV3.Count; i++)
                    addKnown(pendingLoadedBoxesV3[i], null);
            }

            var currentRackUids = new HashSet<int>();
            var liveRackPersistentIds = new HashSet<string>(StringComparer.Ordinal);
            var shelfLifeCache = new Dictionary<int, int>();

            try
            {
                var rackSlots =
                    UnityEngine.Resources.FindObjectsOfTypeAll<RackSlot>();

                if (rackSlots == null)
                    return 0;

                for (int s = 0; s < rackSlots.Length; s++)
                {
                    RackSlot slot = rackSlots[s];
                    if (slot == null || slot.gameObject == null)
                        continue;

                    try
                    {
                        var scene = slot.gameObject.scene;
                        if (!scene.IsValid() || !scene.isLoaded)
                            continue;
                    }
                    catch
                    {
                        continue;
                    }

                    RackSlotData rackData = null;
                    try { rackData = slot.Data; } catch { }

                    if (rackData == null || rackData.RackedBoxDatas == null)
                        continue;

                    var rackBoxes = rackData.RackedBoxDatas;
                    Vector3 slotPosition = slot.transform.position;
                    Quaternion slotRotation = slot.transform.rotation;
                    int rackBoxId = rackData.BoxID;

                    for (int b = 0; b < rackBoxes.Count; b++)
                    {
                        BoxData boxData = rackBoxes[b];
                        if (boxData == null ||
                            boxData.ProductID <= 0 ||
                            boxData.ProductCount <= 0)
                        {
                            continue;
                        }

                        rackDataBoxes++;

                        int productId = boxData.ProductID;
                        int productCount = boxData.ProductCount;

                        int rackUid = 0;
                        try { rackUid = boxData.UID; } catch { }

                        bool validRackUid =
                            rackUid > 0 &&
                            rackUid != InvalidLegacyBoxUid;

                        if (validRackUid)
                            currentRackUids.Add(rackUid);

                        SavedBoxDataV3 assignedState = null;

                        // Exact current-session identity always wins.
                        if (validRackUid &&
                            previousUidStates.TryGetValue(
                                rackUid,
                                out SavedBoxDataV3 exactUidState) &&
                            IsValidPbox3State(exactUidState) &&
                            exactUidState.ProductId == productId &&
                            (exactUidState.BoxId <= 0 ||
                             rackBoxId <= 0 ||
                             exactUidState.BoxId == rackBoxId))
                        {
                            assignedState = exactUidState;
                            matchedExisting++;
                            exactUidMatches++;

                            if (!runtimeIdentityOnly &&
                                usedKnownIds != null &&
                                !string.IsNullOrEmpty(assignedState.PersistentId))
                            {
                                usedKnownIds.Add(assignedState.PersistentId);
                            }
                        }

                        // Transform/proximity matching is a SAVE-LOAD migration
                        // tool only. Candidate states are already grouped by product.
                        if (assignedState == null &&
                            !runtimeIdentityOnly &&
                            knownStatesByProduct != null &&
                            knownStatesByProduct.TryGetValue(
                                productId,
                                out List<SavedBoxDataV3> candidates))
                        {
                            SavedBoxDataV3 best = null;
                            float bestScore = float.MaxValue;

                            for (int k = 0; k < candidates.Count; k++)
                            {
                                SavedBoxDataV3 state = candidates[k];
                                if (!IsValidPbox3State(state) ||
                                    usedKnownIds.Contains(state.PersistentId))
                                {
                                    continue;
                                }

                                if (state.BoxId > 0 &&
                                    rackBoxId > 0 &&
                                    state.BoxId != rackBoxId)
                                {
                                    continue;
                                }

                                Vector3 delta = state.Position - slotPosition;
                                float distanceSq = delta.sqrMagnitude;
                                if (distanceSq > 12.25f) // 3.50m squared
                                    continue;

                                int savedCount = state.Dates.Count;

                                float countPenalty;
                                if (savedCount == productCount)
                                {
                                    countPenalty = 0f;
                                }
                                else if (savedCount > productCount)
                                {
                                    countPenalty =
                                        1000f +
                                        ((savedCount - productCount) * 0.01f);
                                }
                                else
                                {
                                    countPenalty =
                                        2000f +
                                        ((productCount - savedCount) * 0.01f);
                                }

                                float score =
                                    countPenalty +
                                    distanceSq;

                                if (score < bestScore)
                                {
                                    bestScore = score;
                                    best = state;
                                }
                            }

                            if (best != null)
                            {
                                assignedState = best;
                                usedKnownIds.Add(best.PersistentId);
                                matchedExisting++;
                            }
                        }

                        if (assignedState != null)
                        {
                            int savedCount =
                                assignedState.Dates != null
                                    ? assignedState.Dates.Count
                                    : 0;

                            if (savedCount > productCount)
                            {
                                if (TryReconcileRackStateCount(
                                        assignedState,
                                        productCount,
                                        out int trimmed))
                                {
                                    reconciledStates++;
                                    reconciledEntries += trimmed;
                                }
                            }
                            else if (savedCount < productCount)
                            {
                                // This can legitimately exist in old sidecars. Do not
                                // emit one warning per carton on every rack scan; the
                                // aggregate counter is enough for optional diagnostics.
                                undersizedStates++;
                            }
                        }

                        // No exact current-session identity exists. During startup
                        // this is migration; during normal gameplay it is a NEW carton.
                        if (assignedState == null)
                        {
                            if (!shelfLifeCache.TryGetValue(
                                    productId,
                                    out int shelfLife))
                            {
                                shelfLife =
                                    SmartExpiration.Patches.BoxLabelPatch
                                        .GetConfigOverrideDirectly(productId);

                                if (shelfLife < 0)
                                {
                                    shelfLife =
                                        ExpirationCalculator
                                            .GetDaysForProduct(null, productId);
                                }

                                shelfLifeCache[productId] = shelfLife;
                            }

                            int expirationDay = currentDay + shelfLife;
                            var dates = new List<int>(productCount);
                            var deliveries = new List<int>(productCount);

                            for (int i = 0; i < productCount; i++)
                            {
                                dates.Add(expirationDay);
                                deliveries.Add(currentDay);
                            }

                            var generatedState =
                                new SavedBoxDataV3
                                {
                                    PersistentId = Guid.NewGuid().ToString("N"),
                                    BoxId = rackBoxId,
                                    ProductId = productId,
                                    Position =
                                        slotPosition +
                                        (Vector3.up * (0.01f * b)),
                                    Rotation = slotRotation,
                                    Dates = dates,
                                    DeliveryDays = deliveries,
                                    Matched = runtimeIdentityOnly
                                };

                            pendingLoadedBoxesV3.Add(generatedState);
                            assignedState = generatedState;
                            generated++;

                            if (!runtimeIdentityOnly &&
                                knownStatesByProduct != null)
                            {
                                if (!knownStatesByProduct.TryGetValue(
                                        productId,
                                        out List<SavedBoxDataV3> list))
                                {
                                    list = new List<SavedBoxDataV3>();
                                    knownStatesByProduct[productId] = list;
                                }

                                list.Add(generatedState);
                                knownIds?.Add(generatedState.PersistentId);
                                usedKnownIds?.Add(generatedState.PersistentId);
                            }

                            if (runtimeIdentityOnly)
                            {
                                runtimeNewUidStates++;
                                if (StatisticMod.Plugin.EnableLogs)
                                {
                                    StatisticMod.Plugin.DebugLog(
                                        $"[PBOX3] Fresh rack state for new runtime UID: " +
                                        $"uid={rackUid}, product={productId}, count={productCount}, " +
                                        $"deliveryDay={currentDay}, expirationDay={expirationDay}");
                                }
                            }
                        }

                        // The assigned state now describes this live native rack box.
                        assignedState.BoxId = rackBoxId;
                        assignedState.ProductId = productId;
                        assignedState.Position =
                            slotPosition +
                            (Vector3.up * (0.01f * b));
                        assignedState.Rotation = slotRotation;

                        rackDataStates.Add(assignedState);

                        if (!string.IsNullOrEmpty(assignedState.PersistentId))
                            liveRackPersistentIds.Add(assignedState.PersistentId);

                        if (validRackUid)
                        {
                            if (!rackBoxStatesByUid.ContainsKey(rackUid))
                            {
                                rackBoxStatesByUid[rackUid] = assignedState;
                                uidMapped++;
                            }
                            else
                            {
                                uidCollisions++;
                            }
                        }
                        else
                        {
                            uidInvalid++;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogWarning(
                    $"[PBOX3] Rack-data bootstrap error: {ex.Message}");
            }

            // Runtime-only cleanup of exact UID mappings whose native rack box no
            // longer exists. Remove pending states in one reverse pass instead of
            // scanning the whole pending list once per orphan.
            if (runtimeIdentityOnly && previousUidStates.Count > 0)
            {
                var runtimeAttachedIds =
                    new HashSet<string>(StringComparer.Ordinal);

                foreach (var kvp in runtimeBoxPersistentIds)
                {
                    if (!string.IsNullOrEmpty(kvp.Value))
                        runtimeAttachedIds.Add(kvp.Value);
                }

                var orphanPersistentIds =
                    new HashSet<string>(StringComparer.Ordinal);

                foreach (var kvp in previousUidStates)
                {
                    if (currentRackUids.Contains(kvp.Key))
                        continue;

                    SavedBoxDataV3 oldState = kvp.Value;
                    if (oldState == null ||
                        string.IsNullOrEmpty(oldState.PersistentId) ||
                        liveRackPersistentIds.Contains(oldState.PersistentId) ||
                        runtimeAttachedIds.Contains(oldState.PersistentId))
                    {
                        continue;
                    }

                    orphanPersistentIds.Add(oldState.PersistentId);
                }

                if (orphanPersistentIds.Count > 0)
                {
                    foreach (string persistentId in orphanPersistentIds)
                        activeBoxStatesById.Remove(persistentId);

                    for (int i = pendingLoadedBoxesV3.Count - 1; i >= 0; i--)
                    {
                        SavedBoxDataV3 pending = pendingLoadedBoxesV3[i];
                        if (pending != null &&
                            !string.IsNullOrEmpty(pending.PersistentId) &&
                            orphanPersistentIds.Contains(pending.PersistentId))
                        {
                            pendingLoadedBoxesV3.RemoveAt(i);
                            orphanedStatesPruned++;
                        }
                    }
                }
            }

            // Healthy inventory scans are intentionally silent in release builds.
            // Only an actual UID collision remains a normal warning.
            if (uidCollisions > 0)
            {
                StatisticMod.Plugin.Log?.LogWarning(
                    $"[PBOX3] Runtime UID collisions detected: {uidCollisions}.");
            }
            else if (StatisticMod.Plugin.EnableLogs)
            {
                StatisticMod.Plugin.DebugLog(
                    $"[PBOX3] Rack-data inventory: boxes={rackDataBoxes}, " +
                    $"existing={matchedExisting}, generatedMissing={generated}, " +
                    $"runtimeIdentityOnly={runtimeIdentityOnly}, " +
                    $"exactUidMatches={exactUidMatches}, runtimeNewUidStates={runtimeNewUidStates}, " +
                    $"orphanedPruned={orphanedStatesPruned}, reconciledStates={reconciledStates}, " +
                    $"reconciledEntries={reconciledEntries}, undersizedStates={undersizedStates}, " +
                    $"directStates={rackDataStates.Count}, uidMapped={uidMapped}, " +
                    $"invalidUid={uidInvalid}, uidCollisions={uidCollisions}");
            }

            return generated;
        }

        // After PBOX3/rack-data restoration, attach known states to any Box
        // GameObjects that are currently materialized. This is intentionally a
        // separate pass because the game and warehouse mods can spawn/rebuild
        // racked boxes several frames after the main scene becomes available.
        public static int HydrateLoadedBoxesFromKnownStates()
        {
            int hydrated = 0;

            try
            {
                var boxes = GetLoadedSceneBoxesIncludingRackStorage();
                if (boxes == null)
                    return 0;

                for (int i = 0; i < boxes.Count; i++)
                {
                    Box box = boxes[i];
                    if (box == null)
                        continue;

                    int count = 0;
                    try { count = box.ProductCount; } catch { }

                    if (count <= 0)
                        continue;

                    if (EnsureRuntimeBoxState(box))
                        hydrated++;
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogWarning(
                    $"[PBOX3] Hydrate loaded boxes error: {ex.Message}");
            }

            return hydrated;
        }

        private static List<Box> GetLoadedSceneBoxesIncludingRackStorage()
        {
            var result = new List<Box>();
            var seen = new HashSet<int>();

            Action<Box> tryAdd =
                box =>
                {
                    if (box == null || box.gameObject == null)
                        return;

                    int instanceId = 0;
                    try { instanceId = box.GetInstanceID(); } catch { }

                    if (instanceId == 0 || !seen.Add(instanceId))
                        return;

                    try
                    {
                        var scene = box.gameObject.scene;
                        if (!scene.IsValid() || !scene.isLoaded)
                        {
                            seen.Remove(instanceId);
                            return;
                        }
                    }
                    catch
                    {
                        seen.Remove(instanceId);
                        return;
                    }

                    result.Add(box);
                };

            // Rack storage first: this is the authoritative list for boxes
            // currently placed on warehouse racks.
            try
            {
                var rackSlots =
                    UnityEngine.Resources.FindObjectsOfTypeAll<RackSlot>();

                if (rackSlots != null)
                {
                    for (int s = 0; s < rackSlots.Length; s++)
                    {
                        var slot = rackSlots[s];
                        if (slot == null || slot.gameObject == null)
                            continue;

                        try
                        {
                            var scene = slot.gameObject.scene;
                            if (!scene.IsValid() || !scene.isLoaded)
                                continue;
                        }
                        catch
                        {
                            continue;
                        }

                        Il2CppSystem.Collections.Generic.List<Box> boxes = null;
                        try { boxes = slot.Boxes; } catch { }

                        if (boxes == null)
                            continue;

                        for (int b = 0; b < boxes.Count; b++)
                            tryAdd(boxes[b]);
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    $"[PBOX3] Rack box enumeration error: {ex.Message}");
            }

            // Active scene boxes.
            try
            {
                var active =
                    UnityEngine.Object.FindObjectsOfType<Box>();

                if (active != null)
                {
                    for (int i = 0; i < active.Length; i++)
                        tryAdd(active[i]);
                }
            }
            catch { }

            // Inactive loaded scene boxes not exposed by RackSlot.
            try
            {
                var all =
                    UnityEngine.Resources.FindObjectsOfTypeAll<Box>();

                if (all != null)
                {
                    for (int i = 0; i < all.Length; i++)
                        tryAdd(all[i]);
                }
            }
            catch { }

            return result;
        }

        // Main startup restore. PBOX3 is exact by transform fingerprint.
        // PBOX2 is only a one-time best-effort ordinal migration.
        public static int RestoreLoadedBoxesFromPbox3()
        {
            int restored = 0;

            try
            {
                var boxes =
                    GetLoadedSceneBoxesIncludingRackStorage();

                if (boxes == null || boxes.Count == 0)
                    return 0;

                var unmatchedBoxes =
                    new List<Box>();

                for (int i = 0; i < boxes.Count; i++)
                {
                    Box box = boxes[i];

                    if (box == null)
                        continue;

                    int count = 0;
                    try { count = box.ProductCount; } catch { }

                    if (count <= 0)
                        continue;

                    SavedBoxDataV3 state =
                        FindBestPendingPbox3(
                            box,
                            true,
                            count);

                    if (state != null)
                    {
                        state.Matched = true;

                        ApplyRuntimeState(
                            box,
                            state,
                            true,
                            true);

                        restored++;
                    }
                    else
                    {
                        unmatchedBoxes.Add(box);
                    }
                }

                // One-time PBOX2 migration. Old UID values are not identities.
                // Sorting old and current UIDs preserves game load order where
                // possible (e.g. old 21/22 -> new 1/2), but this remains only
                // a migration fallback. After the next save all records are PBOX3.
                var usedLegacy =
                    new HashSet<SavedBoxData>();

                unmatchedBoxes.Sort(
                    (a, b) =>
                    {
                        int pa = GetBoxProductId(a);
                        int pb = GetBoxProductId(b);

                        int cmp = pa.CompareTo(pb);
                        if (cmp != 0) return cmp;

                        int ca = 0;
                        int cb = 0;

                        try { ca = a.ProductCount; } catch { }
                        try { cb = b.ProductCount; } catch { }

                        cmp = ca.CompareTo(cb);
                        if (cmp != 0) return cmp;

                        return GetStableBoxUid(a)
                            .CompareTo(GetStableBoxUid(b));
                    });

                legacyBoxMigrationRecords.Sort(
                    (a, b) =>
                    {
                        int cmp =
                            a.ProductId.CompareTo(b.ProductId);

                        if (cmp != 0)
                            return cmp;

                        int ac =
                            a.Dates != null
                                ? a.Dates.Count
                                : 0;

                        int bc =
                            b.Dates != null
                                ? b.Dates.Count
                                : 0;

                        cmp = ac.CompareTo(bc);

                        if (cmp != 0)
                            return cmp;

                        return a.BoxUid.CompareTo(b.BoxUid);
                    });

                for (int i = 0; i < unmatchedBoxes.Count; i++)
                {
                    Box box = unmatchedBoxes[i];

                    for (int j = 0;
                         j < legacyBoxMigrationRecords.Count;
                         j++)
                    {
                        SavedBoxData legacy =
                            legacyBoxMigrationRecords[j];

                        if (legacy == null ||
                            usedLegacy.Contains(legacy))
                        {
                            continue;
                        }

                        if (TryRestoreLegacyRecord(
                                box,
                                legacy))
                        {
                            usedLegacy.Add(legacy);
                            restored++;


                            break;
                        }
                    }
                }


                return restored;
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    $"[PBOX3] Scene restore error: {ex.Message}");

                return restored;
            }
        }

        // Compatibility aliases for code compiled against the PBOX2 hotfix.
        public static int RestoreLoadedBoxesFromStableUid()
        {
            return RestoreLoadedBoxesFromPbox3();
        }

        public static bool TryRestoreLoadedBoxFromStableUid(Box box)
        {
            if (box == null)
                return false;

            int before =
                runtimeBoxDates.ContainsKey(
                    box.GetInstanceID())
                    ? 1
                    : 0;

            EnsureRuntimeBoxState(box);

            int after =
                runtimeBoxDates.ContainsKey(
                    box.GetInstanceID())
                    ? 1
                    : 0;

            return after > before;
        }

        private static bool TryCreatePbox3MigrationBackupIfNeeded()
        {
            try
            {
                if (!File.Exists(NewSaveFilePath) ||
                    File.Exists(Pbox3MigrationBackupPath))
                {
                    return true;
                }

                bool containsLegacyBoxRecord = false;

                foreach (string line in File.ReadLines(NewSaveFilePath))
                {
                    if (string.IsNullOrWhiteSpace(line))
                        continue;

                    if (line.StartsWith("PBOX2|", StringComparison.Ordinal) ||
                        line.StartsWith("PBOX|", StringComparison.Ordinal) ||
                        line.StartsWith("BOX|", StringComparison.Ordinal))
                    {
                        containsLegacyBoxRecord = true;
                        break;
                    }
                }

                if (!containsLegacyBoxRecord)
                    return true;

                string dir =
                    Path.GetDirectoryName(Pbox3MigrationBackupPath);

                if (!string.IsNullOrEmpty(dir) &&
                    !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                File.Copy(
                    NewSaveFilePath,
                    Pbox3MigrationBackupPath,
                    false);


                return true;
            }
            catch (Exception ex)
            {
                // Backup failure must be visible and fail closed for the
                // sidecar. The native game save is unaffected.
                StatisticMod.Plugin.Log.LogWarning(
                    $"[PBOX3] Could not create migration backup: {ex.Message}. " +
                    "SmartExpiration.txt was NOT overwritten.");

                return false;
            }
        }

        public static void SaveData()
        {
            LastSaveSucceeded = false;

            if (!RuntimeWritesReady)
            {

                return;
            }


            var linesToSave =
                new List<string>();

            // POINTS|rewardFraction|penaltyFraction
            // The game stores Store Points as integers. These fractions preserve
            // partial kilograms between discard/nightly-penalty events.
            linesToSave.Add(
                $"POINTS|{FloatToString(ExpirationStorePointLedger.RewardRemainder)}|" +
                $"{FloatToString(ExpirationStorePointLedger.PenaltyRemainder)}");

            int savedSlotsCount = 0;
            int savedBoxesCount = 0;
            int skippedBoxesCount = 0;
            int preservedActivePbox3 = 0;
            int preservedPendingPbox3 = 0;
            int preservedLegacyBoxes = 0;

            // =========================================================
            // SHELVES
            //
            // Keep the old expiration line for compatibility with the
            // existing ExpiryRescueV3 file migration:
            //   path|expirationCsv
            // Add a parallel delivery line:
            //   SDEL|path|deliveryCsv
            // =========================================================
            try
            {
                var allSlots =
                    UnityEngine.Object.FindObjectsOfType<DisplaySlot>();

                foreach (var slot in allSlots)
                {
                    try
                    {
                        if (slot == null ||
                            !slot.HasProduct)
                        {
                            continue;
                        }

                        ExpirationManager.SyncShelf(slot);

                        var products =
                            new List<global::Product>();

                        int nativeCount =
                            ExpirationManager.GetProductCount(slot);

                        if (nativeCount > 0)
                        {
                            for (int i = 0;
                                 i < nativeCount;
                                 i++)
                            {
                                global::Product p =
                                    ExpirationManager.GetProductAt(
                                        slot,
                                        i);

                                if (p != null)
                                    products.Add(p);
                            }
                        }
                        else
                        {
                            products =
                                GetSortedProducts(slot.transform);
                        }

                        var dates =
                            new List<int>();

                        var deliveries =
                            new List<int>();

                        for (int i = 0;
                             i < products.Count;
                             i++)
                        {
                            global::Product product =
                                products[i];

                            if (product == null)
                                continue;

                            var comp =
                                product.GetComponent<ProductExpirationComponent>();

                            if (comp == null)
                            {
                                comp =
                                    ExpirationManager.EnsureExpiration(
                                        product,
                                        slot);
                            }

                            if (comp == null ||
                                comp.ExpirationDay <= 0)
                            {
                                continue;
                            }

                            comp.ProductID =
                                slot.ProductID;

                            comp.DeliveryDay =
                                NormalizeDeliveryDay(
                                    slot.ProductID,
                                    comp.ExpirationDay,
                                    comp.DeliveryDay);

                            dates.Add(
                                comp.ExpirationDay);

                            deliveries.Add(
                                comp.DeliveryDay);
                        }

                        if (dates.Count == 0 ||
                            dates.Count != deliveries.Count)
                        {
                            continue;
                        }

                        string path =
                            GetSlotPath(slot);

                        linesToSave.Add(
                            $"{path}|{string.Join(",", dates)}");

                        linesToSave.Add(
                            $"SDEL|{path}|{string.Join(",", deliveries)}");

                        slotDates[path] =
                            new List<int>(dates);

                        slotDeliveryDays[path] =
                            new List<int>(deliveries);

                        savedSlotsCount++;
                    }
                    catch (Exception ex)
                    {
                        StatisticMod.Plugin.Log.LogError(
                            $"[SaveData] Shelf save error: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError(
                    $"[SaveData] Shelf scan error: {ex.Message}");
            }

            // =========================================================
            // BOXES
            //
            // PBOX3|persistentGuid|boxId|productId|
            //       px|py|pz|qx|qy|qz|qw|expirationCsv|deliveryCsv
            //
            // Game Box.Data.UID is intentionally absent.
            // =========================================================
            var usedPersistentIds =
                new HashSet<string>();

            try
            {
                var allBoxes =
                    GetLoadedSceneBoxesIncludingRackStorage();

                foreach (var box in allBoxes)
                {
                    try
                    {
                        if (box == null)
                            continue;

                        int productCount = 0;

                        try
                        {
                            productCount =
                                box.ProductCount;
                        }
                        catch { }

                        if (productCount <= 0)
                            continue;

                        SavedBoxDataV3 state =
                            BuildCurrentBoxState(box);

                        if (state == null ||
                            state.ProductId <= 0 ||
                            state.Dates == null ||
                            state.DeliveryDays == null ||
                            state.Dates.Count != productCount ||
                            state.DeliveryDays.Count != productCount)
                        {
                            skippedBoxesCount++;

                            StatisticMod.Plugin.DebugWarning(
                                $"[SaveData] PBOX3 box skipped - exact paired state unavailable. " +
                                $"product={GetBoxProductId(box)}, " +
                                $"count={productCount}, " +
                                $"instance={box.GetInstanceID()}");

                            continue;
                        }

                        if (string.IsNullOrEmpty(
                                state.PersistentId) ||
                            usedPersistentIds.Contains(
                                state.PersistentId))
                        {
                            state.PersistentId =
                                Guid.NewGuid().ToString("N");

                            runtimeBoxPersistentIds[
                                box.GetInstanceID()] =
                                state.PersistentId;
                        }

                        usedPersistentIds.Add(
                            state.PersistentId);

                        state.Position =
                            box.transform.position;

                        state.Rotation =
                            box.transform.rotation;

                        activeBoxStatesById[
                            state.PersistentId] =
                            CloneState(state);

                        string p =
                            FloatToString(state.Position.x);

                        string py =
                            FloatToString(state.Position.y);

                        string pz =
                            FloatToString(state.Position.z);

                        string qx =
                            FloatToString(state.Rotation.x);

                        string qy =
                            FloatToString(state.Rotation.y);

                        string qz =
                            FloatToString(state.Rotation.z);

                        string qw =
                            FloatToString(state.Rotation.w);

                        linesToSave.Add(
                            $"PBOX3|{state.PersistentId}|" +
                            $"{state.BoxId}|{state.ProductId}|" +
                            $"{p}|{py}|{pz}|" +
                            $"{qx}|{qy}|{qz}|{qw}|" +
                            $"{string.Join(",", state.Dates)}|" +
                            $"{string.Join(",", state.DeliveryDays)}");

                        savedBoxesCount++;
                    }
                    catch (Exception ex)
                    {
                        skippedBoxesCount++;

                        StatisticMod.Plugin.Log.LogError(
                            $"[SaveData] PBOX3 box save error: {ex}");
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log.LogError(
                    $"[SaveData] Box scan error: {ex.Message}");
            }

            // A rack can virtualize a Box after its PBOX3 was successfully
            // restored. In that case the physical scan above may no longer
            // expose a usable ProductCount, but activeBoxStatesById still owns
            // the exact paired dates. Preserve those records before consulting
            // the original pending list.
            foreach (var kvp in activeBoxStatesById)
            {
                SavedBoxDataV3 state = kvp.Value;

                if (!IsValidPbox3State(state))
                    continue;

                string persistentId =
                    !string.IsNullOrEmpty(state.PersistentId)
                        ? state.PersistentId
                        : kvp.Key;

                if (string.IsNullOrEmpty(persistentId) ||
                    usedPersistentIds.Contains(persistentId))
                {
                    continue;
                }

                linesToSave.Add(
                    $"PBOX3|{persistentId}|" +
                    $"{state.BoxId}|{state.ProductId}|" +
                    $"{FloatToString(state.Position.x)}|" +
                    $"{FloatToString(state.Position.y)}|" +
                    $"{FloatToString(state.Position.z)}|" +
                    $"{FloatToString(state.Rotation.x)}|" +
                    $"{FloatToString(state.Rotation.y)}|" +
                    $"{FloatToString(state.Rotation.z)}|" +
                    $"{FloatToString(state.Rotation.w)}|" +
                    $"{string.Join(",", state.Dates)}|" +
                    $"{string.Join(",", state.DeliveryDays)}");

                usedPersistentIds.Add(persistentId);
                preservedActivePbox3++;
            }

            // Fail-safe: every still-existing loaded PBOX3 that was not
            // emitted above is preserved verbatim. Matched records are NOT
            // discarded merely because their Box GameObject is virtualized.
            // RemoveRuntimeBoxInstance(contentGone:true) deletes the pending
            // record when a box is actually consumed, preventing resurrection.
            for (int i = 0; i < pendingLoadedBoxesV3.Count; i++)
            {
                SavedBoxDataV3 state =
                    pendingLoadedBoxesV3[i];

                if (state == null ||
                    string.IsNullOrEmpty(state.PersistentId) ||
                    usedPersistentIds.Contains(state.PersistentId) ||
                    state.ProductId <= 0 ||
                    state.Dates == null ||
                    state.DeliveryDays == null ||
                    state.Dates.Count == 0 ||
                    state.Dates.Count != state.DeliveryDays.Count)
                {
                    continue;
                }

                linesToSave.Add(
                    $"PBOX3|{state.PersistentId}|" +
                    $"{state.BoxId}|{state.ProductId}|" +
                    $"{FloatToString(state.Position.x)}|" +
                    $"{FloatToString(state.Position.y)}|" +
                    $"{FloatToString(state.Position.z)}|" +
                    $"{FloatToString(state.Rotation.x)}|" +
                    $"{FloatToString(state.Rotation.y)}|" +
                    $"{FloatToString(state.Rotation.z)}|" +
                    $"{FloatToString(state.Rotation.w)}|" +
                    $"{string.Join(",", state.Dates)}|" +
                    $"{string.Join(",", state.DeliveryDays)}");

                usedPersistentIds.Add(state.PersistentId);
                preservedPendingPbox3++;
            }

            // Same safety rule for legacy PBOX2/PBOX during the one-time
            // transition. Once a legacy record is matched, it is replaced by
            // the PBOX3 written for that physical box. Unmatched records are
            // retained so migration does not silently lose stock metadata.
            for (int i = 0; i < legacyBoxMigrationRecords.Count; i++)
            {
                SavedBoxData legacy =
                    legacyBoxMigrationRecords[i];

                if (legacy == null ||
                    legacy.Matched ||
                    legacy.ProductId <= 0 ||
                    legacy.Dates == null ||
                    legacy.Dates.Count == 0)
                {
                    continue;
                }

                int legacyDelivery =
                    legacy.DeliveryDay > 0
                        ? legacy.DeliveryDay
                        : 1;

                if (legacy.BoxUid > 0)
                {
                    linesToSave.Add(
                        $"PBOX2|{legacy.BoxUid}|{legacy.ProductId}|" +
                        $"{string.Join(",", legacy.Dates)}|{legacyDelivery}");
                }
                else
                {
                    linesToSave.Add(
                        $"PBOX|{legacy.ProductId}|" +
                        $"{string.Join(",", legacy.Dates)}|{legacyDelivery}");
                }

                preservedLegacyBoxes++;
            }

            try
            {
                string dir =
                    Path.GetDirectoryName(
                        NewSaveFilePath);

                if (!string.IsNullOrEmpty(dir) &&
                    !Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                if (!TryCreatePbox3MigrationBackupIfNeeded())
                {
                    LastSaveSucceeded = false;
                    return;
                }

                File.WriteAllLines(
                    NewSaveFilePath,
                    linesToSave);

                LastSaveSucceeded = true;


                try
                {
                    if (File.Exists(LegacySaveFilePath))
                        File.Delete(LegacySaveFilePath);
                }
                catch { }
            }
            catch (Exception ex)
            {
                LastSaveSucceeded = false;

                StatisticMod.Plugin.Log.LogError(
                    $"[SaveData] WRITE ERROR: {ex}");
            }
        }

        public static void LoadData()
        {
            SaveDataInitialized = false;
            SaveLoaded = false;
            ExpirationStorePointLedger.Reset();

            slotDates.Clear();
            slotDeliveryDays.Clear();

            boxDates.Clear();
            boxDeliveryDays.Clear();

            runtimeBoxDates.Clear();
            runtimeBoxDeliveryDays.Clear();
            runtimeBoxDeliveryDaysPerProduct.Clear();
            runtimeBoxDatesFromSave.Clear();
            runtimeBoxConfigVersion.Clear();
            runtimeBoxPersistentIds.Clear();

            activeBoxStatesById.Clear();
            pendingLoadedBoxesV3.Clear();
            rackBoxStatesByUid.Clear();
            rackDataStates.Clear();
            rackUidsNeedingVisualRefresh.Clear();

            pendingLoadedBoxes.Clear();
            pendingLoadedBoxesByUid.Clear();
            legacyBoxMigrationRecords.Clear();

            try
            {
                CustomExpirationLoader.Load();
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    $"[LoadData] Config load warning: {ex.Message}");
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


                return;
            }

            bool detailedLogs = false;

            try
            {
                detailedLogs =
                    PluginConfig.DetailedLoadLogs != null &&
                    PluginConfig.DetailedLoadLogs.Value;
            }
            catch { }

            int loadedPbox3 = 0;
            int loadedLegacyBoxes = 0;
            int loadedSlots = 0;
            int loadedShelfDelivery = 0;
            int skipped = 0;
            int malformed = 0;

            var seenPbox3Ids =
                new HashSet<string>();

            try
            {
                foreach (string line in File.ReadLines(fileToLoad))
                {
                    try
                    {
                        if (string.IsNullOrWhiteSpace(line) ||
                            !line.Contains("|"))
                        {
                            skipped++;
                            continue;
                        }

                        string[] parts =
                            line.Split('|');

                        // POINTS|rewardFraction|penaltyFraction
                        if (parts[0] == "POINTS" &&
                            parts.Length >= 3)
                        {
                            float rewardFraction = 0f;
                            float penaltyFraction = 0f;

                            TryParseFloat(parts[1], out rewardFraction);
                            TryParseFloat(parts[2], out penaltyFraction);

                            ExpirationStorePointLedger.Load(
                                rewardFraction,
                                penaltyFraction);
                        }
                        // PBOX3|guid|boxId|productId|px|py|pz|qx|qy|qz|qw|dates|deliveries
                        else if (parts[0] == "PBOX3" &&
                            parts.Length >= 13)
                        {
                            string persistentId =
                                parts[1];

                            if (string.IsNullOrWhiteSpace(
                                    persistentId) ||
                                seenPbox3Ids.Contains(
                                    persistentId))
                            {
                                malformed++;
                                continue;
                            }

                            if (!int.TryParse(
                                    parts[2],
                                    out int boxId))
                            {
                                boxId = 0;
                            }

                            if (!int.TryParse(
                                    parts[3],
                                    out int productId) ||
                                productId <= 0)
                            {
                                malformed++;
                                continue;
                            }

                            if (!TryParseFloat(parts[4], out float px) ||
                                !TryParseFloat(parts[5], out float py) ||
                                !TryParseFloat(parts[6], out float pz) ||
                                !TryParseFloat(parts[7], out float qx) ||
                                !TryParseFloat(parts[8], out float qy) ||
                                !TryParseFloat(parts[9], out float qz) ||
                                !TryParseFloat(parts[10], out float qw))
                            {
                                malformed++;
                                continue;
                            }

                            List<int> dates =
                                ParseCsvInts(parts[11]);

                            List<int> deliveries =
                                ParseCsvInts(parts[12]);

                            if (dates.Count == 0 ||
                                dates.Count != deliveries.Count)
                            {
                                malformed++;
                                continue;
                            }

                            deliveries =
                                NormalizeDeliveryList(
                                    productId,
                                    dates,
                                    deliveries,
                                    0);

                            var state =
                                new SavedBoxDataV3
                                {
                                    PersistentId = persistentId,
                                    BoxId = boxId,
                                    ProductId = productId,
                                    Position =
                                        new Vector3(px, py, pz),
                                    Rotation =
                                        new Quaternion(qx, qy, qz, qw),
                                    Dates =
                                        new List<int>(dates),
                                    DeliveryDays =
                                        new List<int>(deliveries),
                                    Matched = false
                                };

                            pendingLoadedBoxesV3.Add(state);
                            seenPbox3Ids.Add(persistentId);
                            loadedPbox3++;

                            if (detailedLogs)
                            {
                            }
                        }
                        else if (parts[0] == "SDEL" &&
                                 parts.Length >= 3)
                        {
                            string path =
                                parts[1];

                            if (string.IsNullOrEmpty(path))
                            {
                                malformed++;
                                continue;
                            }

                            slotDeliveryDays[path] =
                                ParseCsvInts(parts[2]);

                            loadedShelfDelivery++;
                        }
                        else if (parts[0] == "PBOX2" &&
                                 parts.Length >= 5)
                        {
                            if (!int.TryParse(
                                    parts[1],
                                    out int oldUid))
                            {
                                oldUid = 0;
                            }

                            if (!int.TryParse(
                                    parts[2],
                                    out int productId) ||
                                productId <= 0)
                            {
                                malformed++;
                                continue;
                            }

                            List<int> dates =
                                ParseCsvInts(parts[3]);

                            if (dates.Count == 0)
                            {
                                malformed++;
                                continue;
                            }

                            int deliveryDay = 1;
                            int.TryParse(
                                parts[4],
                                out deliveryDay);

                            if (deliveryDay <= 0)
                                deliveryDay = 1;

                            var legacy =
                                new SavedBoxData
                                {
                                    BoxUid = oldUid,
                                    ProductId = productId,
                                    Dates = new List<int>(dates),
                                    DeliveryDay = deliveryDay,
                                    DeliveryDays =
                                        NormalizeDeliveryList(
                                            productId,
                                            dates,
                                            null,
                                            deliveryDay)
                                };

                            legacyBoxMigrationRecords.Add(legacy);

                            if (oldUid > 0 &&
                                !pendingLoadedBoxesByUid.ContainsKey(oldUid))
                            {
                                pendingLoadedBoxesByUid[oldUid] =
                                    legacy;
                            }

                            loadedLegacyBoxes++;
                        }
                        else if (parts[0] == "PBOX" &&
                                 parts.Length >= 3)
                        {
                            if (!int.TryParse(
                                    parts[1],
                                    out int productId) ||
                                productId <= 0)
                            {
                                malformed++;
                                continue;
                            }

                            List<int> dates =
                                ParseCsvInts(parts[2]);

                            if (dates.Count == 0)
                            {
                                malformed++;
                                continue;
                            }

                            int deliveryDay = 1;

                            if (parts.Length >= 4)
                                int.TryParse(
                                    parts[3],
                                    out deliveryDay);

                            if (deliveryDay <= 0)
                                deliveryDay = 1;

                            var legacy =
                                new SavedBoxData
                                {
                                    BoxUid = 0,
                                    ProductId = productId,
                                    Dates = new List<int>(dates),
                                    DeliveryDay = deliveryDay,
                                    DeliveryDays =
                                        NormalizeDeliveryList(
                                            productId,
                                            dates,
                                            null,
                                            deliveryDay)
                                };

                            legacyBoxMigrationRecords.Add(legacy);

                            if (!pendingLoadedBoxes.ContainsKey(productId))
                            {
                                pendingLoadedBoxes[productId] =
                                    new Queue<SavedBoxData>();
                            }

                            pendingLoadedBoxes[productId].Enqueue(legacy);

                            loadedLegacyBoxes++;
                        }
                        else if (parts[0] == "BOX" &&
                                 parts.Length >= 3)
                        {
                            // Very old UID-only records have no reliable
                            // ProductId/fingerprint. Keep their cache for
                            // compatibility but do not use them as PBOX3 identity.
                            if (int.TryParse(
                                    parts[1],
                                    out int oldUid) &&
                                oldUid > 0 &&
                                oldUid != InvalidLegacyBoxUid)
                            {
                                boxDates[oldUid] =
                                    ParseCsvInts(parts[2]);

                                if (parts.Length >= 4 &&
                                    int.TryParse(
                                        parts[3],
                                        out int oldDelivery) &&
                                    oldDelivery > 0)
                                {
                                    boxDeliveryDays[oldUid] =
                                        oldDelivery;
                                }
                            }

                            loadedLegacyBoxes++;
                        }
                        else if (parts.Length == 2)
                        {
                            string path =
                                parts[0];

                            if (string.IsNullOrEmpty(path))
                            {
                                malformed++;
                                continue;
                            }

                            slotDates[path] =
                                ParseCsvInts(parts[1]);

                            loadedSlots++;
                        }
                        else
                        {
                            // Unknown future/foreign line - keep parser fail-soft.
                            skipped++;
                        }
                    }
                    catch (Exception ex)
                    {
                        malformed++;

                        StatisticMod.Plugin.DebugWarning(
                            $"[LoadData] Record skipped: {ex.Message}");
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
                    }
                    catch (Exception ex)
                    {
                        StatisticMod.Plugin.DebugWarning(
                            $"[LoadData] Legacy path migration warning: {ex.Message}");
                    }
                }

                SaveDataInitialized = true;
                SaveLoaded = true;

            }
            catch (Exception ex)
            {
                SaveDataInitialized = false;
                SaveLoaded = false;

                StatisticMod.Plugin.Log.LogError(
                    $"[LoadData] MAIN READ ERROR: {ex}");
            }
        }

        private static string FloatToString(float value)
        {
            return value.ToString(
                "R",
                CultureInfo.InvariantCulture);
        }

        private static bool TryParseFloat(
            string value,
            out float result)
        {
            return float.TryParse(
                value,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out result);
        }

        private static List<int> ParseCsvInts(string csv)
        {
            var list =
                new List<int>();

            if (string.IsNullOrEmpty(csv))
                return list;

            string[] tokens =
                csv.Split(',');

            for (int i = 0;
                 i < tokens.Length;
                 i++)
            {
                if (int.TryParse(
                        tokens[i],
                        out int value))
                {
                    list.Add(value);
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
                        prop.GetValue(
                            box,
                            null);

                    if (dataObj != null)
                    {
                        var uidProp =
                            dataObj.GetType().GetProperty("UID") ??
                            dataObj.GetType().GetProperty("Uid") ??
                            dataObj.GetType().GetProperty("Id");

                        if (uidProp != null)
                        {
                            var val =
                                uidProp.GetValue(
                                    dataObj,
                                    null);

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
