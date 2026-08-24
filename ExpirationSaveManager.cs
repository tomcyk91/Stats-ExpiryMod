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
                currentCount < existingDates.Count)
            {
                return true;
            }

            // First: exact PBOX3 record from disk (initial game load).
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

            // Second: a state from this session (Warehouse Refill may rebuild
            // the Box and create a new InstanceID at the same transform).
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

            // Same-session rebuilt object.
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
            }

            if (contentGone &&
                !string.IsNullOrEmpty(persistentId))
            {
                activeBoxStatesById.Remove(persistentId);
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

        // Main startup restore. PBOX3 is exact by transform fingerprint.
        // PBOX2 is only a one-time best-effort ordinal migration.
        public static int RestoreLoadedBoxesFromPbox3()
        {
            int restored = 0;

            try
            {
                var boxes =
                    UnityEngine.Object.FindObjectsOfType<Box>();

                if (boxes == null)
                    return 0;

                var unmatchedBoxes =
                    new List<Box>();

                for (int i = 0; i < boxes.Length; i++)
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

            int savedSlotsCount = 0;
            int savedBoxesCount = 0;
            int skippedBoxesCount = 0;
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
                    UnityEngine.Object.FindObjectsOfType<Box>();

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

            // Fail-safe: a PBOX3 loaded from disk but not currently visible as
            // an active Box must not disappear merely because another mod or
            // an inactive hierarchy kept it out of FindObjectsOfType<Box>().
            // Preserve unmatched records verbatim until they can be matched.
            for (int i = 0; i < pendingLoadedBoxesV3.Count; i++)
            {
                SavedBoxDataV3 state =
                    pendingLoadedBoxesV3[i];

                if (state == null ||
                    state.Matched ||
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

                        // PBOX3|guid|boxId|productId|px|py|pz|qx|qy|qz|qw|dates|deliveries
                        if (parts[0] == "PBOX3" &&
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
