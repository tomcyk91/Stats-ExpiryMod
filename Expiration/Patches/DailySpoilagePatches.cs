using HarmonyLib;
using System;
using System.Collections.Generic;
using UnityEngine;
using StatisticMod;

namespace SmartExpiration.Patches
{
    [HarmonyPatch(typeof(DayCycleManager))]
    internal static class DailySpoilagePatches
    {
        private static int _dayBeingClosed = -1;

        public static bool Prepare()
        {
            return AccessTools.Method(
                typeof(DayCycleManager),
                "FinishTheDay") != null;
        }

        [HarmonyPrefix]
        [HarmonyPatch(nameof(DayCycleManager.FinishTheDay))]
        private static void FinishTheDay_Prefix()
        {
            try
            {
                var dcm =
                    DayCycleManager.HasInstance
                        ? DayCycleManager.Instance
                        : null;

                _dayBeingClosed =
                    dcm != null && dcm.CurrentDay > 0
                        ? dcm.CurrentDay
                        : 1;
            }
            catch
            {
                _dayBeingClosed = -1;
            }
            StatisticMod.Plugin.DebugLog($"[Nocne Sprzątanie] FinishTheDay PREFIX day={_dayBeingClosed}.");
        }

        [HarmonyPostfix]
        [HarmonyPatch(nameof(DayCycleManager.FinishTheDay))]
        private static void FinishTheDay_Postfix()
        {
            int closedDay = _dayBeingClosed;
            _dayBeingClosed = -1;

            if (closedDay <= 0)
            {
                try
                {
                    var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                    int currentDay = dcm != null ? dcm.CurrentDay : 1;
                    closedDay = currentDay > 1 ? currentDay - 1 : currentDay;
                }
                catch { closedDay = 1; }
            }

            RunCleanupForDay(closedDay, "FinishTheDay");
        }

        // v14.2 fallback for v1.6.x / alternate day-transition flows.
        private static void StartNextDay_Postfix()
        {
            int currentDay = 1;
            try
            {
                var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                if (dcm != null && dcm.CurrentDay > 0)
                    currentDay = dcm.CurrentDay;
            }
            catch { }

            int closedDay = currentDay > 1 ? currentDay - 1 : currentDay;
            RunCleanupForDay(closedDay, "StartNextDay");
        }

        private static int _lastCleanedDay = -1;

        public static void ResetCleanupGuard()
        {
            _lastCleanedDay = -1;
            _dayBeingClosed = -1;
        }

        public static void RunCleanupForDay(int closedDay, string source)
        {
            if (closedDay <= 0)
                return;

            if (_lastCleanedDay == closedDay)
            {
                StatisticMod.Plugin.DebugLog(
                    $"[Nocne Sprzątanie] Pomijam duplikat dnia {closedDay} ({source}).");
                return;
            }

            _lastCleanedDay = closedDay;

            StatisticMod.Plugin.Log?.LogInfo(
                $"[Nocne Sprzątanie] START dzień={closedDay}, hook={source}.");
            int totalSpoiledCount = 0;
            int spoiledFromShelves = 0;
            int spoiledFromBoxes = 0;
            int spoiledFromVirtualRack = 0;
            float storePointPenaltyValue = 0f;

            Dictionary<int, int> spoiledProductsDaily =
                new Dictionary<int, int>();

            RemoveExpiredFromShelves(
                closedDay,
                spoiledProductsDaily,
                ref spoiledFromShelves,
                ref totalSpoiledCount);

            RemoveExpiredFromPhysicalBoxes(
                closedDay,
                spoiledProductsDaily,
                ref spoiledFromBoxes,
                ref totalSpoiledCount);

            // v14.4: most warehouse boxes in v1.6.x are virtualized and exist
            // only as RackSlot.Data.RackedBoxDatas + PBOX3 state. Physical Box
            // GameObjects can be completely absent, so clean the native rack
            // records directly after the physical pass.
            RemoveExpiredFromVirtualRackData(
                closedDay,
                spoiledProductsDaily,
                ref spoiledFromVirtualRack,
                ref totalSpoiledCount);

            // Do not let an internal removal leave transfer clipboard metadata alive.
            BoxLabelPatch.ClipboardDate = -1;
            BoxLabelPatch.ClipboardFrame = -1;

            StatisticMod.Plugin.Log?.LogInfo(
                $"[Nocne Sprzątanie] KONIEC dzień={closedDay}: " +
                $"półki={spoiledFromShelves}, " +
                $"kartonyFizyczne={spoiledFromBoxes}, " +
                $"magazynWirtualny={spoiledFromVirtualRack}, " +
                $"łącznie={totalSpoiledCount}.");

            if (spoiledProductsDaily.Count > 0)
            {
                foreach (KeyValuePair<int, int> kvp in spoiledProductsDaily)
                {
                    int pid = kvp.Key;
                    int count = kvp.Value;

                    float price = 0f;
                    try
                    {
                        if (PriceManager.HasInstance &&
                            PriceManager.Instance != null)
                        {
                            price =
                                PriceManager.Instance
                                    .SellingPrice(pid);
                        }
                    }
                    catch { }

                    if (SalesUnifiedFinal.WeightPerUnit != null &&
                        SalesUnifiedFinal.WeightPerUnit.TryGetValue(
                            pid,
                            out float weightPerUnit))
                    {
                        float kgSpoiled =
                            count * weightPerUnit;

                        float lostValue =
                            price * kgSpoiled;

                        StatsStore.AddThrownF(
                            closedDay,
                            pid,
                            kgSpoiled,
                            lostValue,
                            true);

                        storePointPenaltyValue +=
                            kgSpoiled;
                    }
                    else
                    {
                        float lostValue =
                            price * count;

                        StatsStore.AddThrown(
                            closedDay,
                            pid,
                            count,
                            lostValue);

                        storePointPenaltyValue +=
                            count;
                    }
                }

                StatsStore.SaveNow();

                StatisticMod.Plugin.DebugLog(
                    $"[Statystyki Strat] Zapisano straty z dnia {closedDay}.");
            }

            if (storePointPenaltyValue > 0f &&
                StoreLevelManager.Instance != null)
            {
                int pointsToRemove =
                    ExpirationStorePointLedger
                        .ConsumePenalty(
                            storePointPenaltyValue);

                if (pointsToRemove > 0)
                {
                    StoreLevelManager.Instance
                        .RemovePoint(pointsToRemove);
                }

                StatisticMod.Plugin.DebugLog(
                    $"[Punkty Sklepu] Nocna kara: " +
                    $"wartoscPunktowa={storePointPenaltyValue:0.###}, " +
                    $"odjeto={pointsToRemove}, " +
                    $"resztaKg={ExpirationStorePointLedger.PenaltyRemainder:0.###}.");
            }

            // Save once, after all shelf + box removals and penalties.
            ExpirationSaveManager.SaveData();
        }

        private static void RemoveExpiredFromShelves(
            int closedDay,
            Dictionary<int, int> spoiledProductsDaily,
            ref int removedFromShelves,
            ref int totalSpoiledCount)
        {
            try
            {
                SceneSlotCache.InvalidateSlots();

                DisplaySlot[] allSlots =
                    SceneSlotCache.GetSlots();

                if (allSlots == null)
                    return;

                for (int s = 0;
                     s < allSlots.Length;
                     s++)
                {
                    DisplaySlot slot =
                        allSlots[s];

                    if (slot == null ||
                        !slot.HasProduct)
                    {
                        continue;
                    }

                    int productId =
                        slot.ProductID;

                    int removedFromSlot =
                        0;

                    int guard =
                        0;

                    while (slot.HasProduct &&
                           guard++ < 4096 &&
                           ExpirationManager
                               .PrepareExpiredProductForNativeTake(
                                   slot,
                                   closedDay,
                                   out int expiredDate))
                    {
                        global::Product poppedProduct =
                            slot.TakeProductFromDisplay();

                        if (poppedProduct == null)
                            break;

                        try
                        {
                            poppedProduct.transform
                                .SetParent(null);
                        }
                        catch { }

                        UnityEngine.Object.Destroy(
                            poppedProduct.gameObject);

                        removedFromSlot++;
                        removedFromShelves++;
                        totalSpoiledCount++;
                    }

                    if (removedFromSlot <= 0)
                        continue;

                    AddSpoiledCount(
                        spoiledProductsDaily,
                        productId,
                        removedFromSlot);

                    LabelExclamationOverlay
                        .QueueSlot(slot);
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    "[Nocne Sprzątanie] Shelf cleanup warning: " +
                    ex.Message);
            }
        }

        private static void RemoveExpiredFromPhysicalBoxes(
            int closedDay,
            Dictionary<int, int> spoiledProductsDaily,
            ref int removedFromBoxes,
            ref int totalSpoiledCount)
        {
            try
            {
                Box[] boxes =
                    UnityEngine.Object
                        .FindObjectsOfType<Box>();

                if (boxes == null)
                    return;

                for (int b = 0;
                     b < boxes.Length;
                     b++)
                {
                    Box box =
                        boxes[b];

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

                    int productId =
                        ExpirationSaveManager
                            .GetBoxProductId(box);

                    if (productId <= 0)
                        continue;

                    // Hydrates PBOX3/runtime state and physical expiration components
                    // when the box was reconstructed from a save.
                    if (!ExpirationSaveManager
                            .EnsureRuntimeBoxState(box))
                    {
                        continue;
                    }

                    int removedThisBox =
                        0;

                    int guard =
                        0;

                    while (guard++ < 4096 &&
                           TryPrepareExpiredBoxProductForNativeTake(
                               box,
                               closedDay))
                    {
                        global::Product poppedProduct = null;

                        try
                        {
                            poppedProduct =
                                box.GetProductFromBox(false);
                        }
                        catch
                        {
                            break;
                        }

                        if (poppedProduct == null)
                            break;

                        try
                        {
                            poppedProduct.transform
                                .SetParent(null);
                        }
                        catch { }

                        UnityEngine.Object.Destroy(
                            poppedProduct.gameObject);

                        removedThisBox++;
                        removedFromBoxes++;
                        totalSpoiledCount++;
                    }

                    if (removedThisBox > 0)
                    {
                        AddSpoiledCount(
                            spoiledProductsDaily,
                            productId,
                            removedThisBox);

                        // BoxPatches updates paired runtime metadata on every native
                        // GetProductFromBox. Touch the remaining state for save stability.
                        try
                        {
                            if (box.ProductCount > 0)
                            {
                                ExpirationSaveManager
                                    .TouchRuntimeBoxState(box);
                            }
                        }
                        catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    "[Nocne Sprzątanie] Box cleanup warning: " +
                    ex.Message);
            }
        }

        private static void RemoveExpiredFromVirtualRackData(
            int closedDay,
            Dictionary<int, int> spoiledProductsDaily,
            ref int removedFromVirtualRack,
            ref int totalSpoiledCount)
        {
            int boxesChecked = 0;
            int statesMatched = 0;
            int statesMissing = 0;
            int boxesChanged = 0;
            int boxesRemoved = 0;

            try
            {
                // Build an exact one-state-per-native-rack-box bridge first.
                // This is the same source used by the TERMINY application.
                ExpirationSaveManager.BootstrapMissingRackBoxStates();

                var usedStates =
                    new HashSet<SavedBoxDataV3>();

                RackSlot[] rackSlots =
                    UnityEngine.Resources
                        .FindObjectsOfTypeAll<RackSlot>();

                if (rackSlots == null)
                    return;

                for (int s = 0; s < rackSlots.Length; s++)
                {
                    RackSlot slot = rackSlots[s];
                    if (slot == null ||
                        slot.gameObject == null)
                    {
                        continue;
                    }

                    try
                    {
                        var scene = slot.gameObject.scene;
                        if (!scene.IsValid() ||
                            !scene.isLoaded)
                        {
                            continue;
                        }
                    }
                    catch
                    {
                        continue;
                    }

                    RackSlotData rackData = null;
                    try { rackData = slot.Data; } catch { }

                    if (rackData == null ||
                        rackData.RackedBoxDatas == null)
                    {
                        continue;
                    }

                    var rackBoxes =
                        rackData.RackedBoxDatas;

                    // Reverse iteration because an entirely expired virtual box
                    // is removed from the native rack-data list.
                    for (int b = rackBoxes.Count - 1;
                         b >= 0;
                         b--)
                    {
                        BoxData boxData =
                            rackBoxes[b];

                        if (boxData == null ||
                            boxData.ProductID <= 0 ||
                            boxData.ProductCount <= 0)
                        {
                            continue;
                        }

                        boxesChecked++;

                        int productId =
                            boxData.ProductID;

                        int productCount =
                            boxData.ProductCount;

                        SavedBoxDataV3 state =
                            FindVirtualRackState(
                                slot,
                                rackData,
                                boxData,
                                usedStates);

                        if (state == null ||
                            state.Dates == null ||
                            state.DeliveryDays == null ||
                            state.Dates.Count != productCount ||
                            state.DeliveryDays.Count != productCount)
                        {
                            statesMissing++;
                            continue;
                        }

                        usedStates.Add(state);
                        statesMatched++;

                        int expiredCount = 0;

                        var remainingDates =
                            new List<int>(productCount);

                        var remainingDeliveries =
                            new List<int>(productCount);

                        for (int i = 0;
                             i < productCount;
                             i++)
                        {
                            int expirationDay =
                                state.Dates[i];

                            int deliveryDay =
                                state.DeliveryDays[i];

                            if (expirationDay <= closedDay)
                            {
                                expiredCount++;
                                continue;
                            }

                            remainingDates.Add(
                                expirationDay);

                            remainingDeliveries.Add(
                                deliveryDay);
                        }

                        if (expiredCount <= 0)
                            continue;

                        boxesChanged++;

                        state.Dates =
                            remainingDates;

                        state.DeliveryDays =
                            remainingDeliveries;

                        int remainingCount =
                            remainingDates.Count;

                        if (remainingCount > 0)
                        {
                            // BoxData is the native, save-backed source of truth
                            // for virtualized warehouse stock.
                            boxData.ProductCount =
                                remainingCount;

                            SyncMaterializedRackBox(
                                slot,
                                boxData,
                                remainingCount,
                                false);
                        }
                        else
                        {
                            boxesRemoved++;

                            int uid = 0;
                            try { uid = boxData.UID; } catch { }

                            rackBoxes.RemoveAt(b);

                            SyncMaterializedRackBox(
                                slot,
                                boxData,
                                0,
                                true);

                            if (uid > 0)
                            {
                                ExpirationSaveManager
                                    .rackBoxStatesByUid
                                    .Remove(uid);
                            }

                            RemoveEmptyPbox3State(
                                state);
                        }

                        AddSpoiledCount(
                            spoiledProductsDaily,
                            productId,
                            expiredCount);

                        removedFromVirtualRack +=
                            expiredCount;

                        totalSpoiledCount +=
                            expiredCount;
                    }

                    RefreshVirtualRackSlotAfterCleanup(
                        slot);
                }

                // Rebuild the direct PBOX3/rack bridge against the modified
                // native rack records. This prevents a stale pre-cleanup state
                // from being preserved by the next SaveData().
                ExpirationSaveManager
                    .BootstrapMissingRackBoxStates();

                StatisticMod.Plugin.Log?.LogInfo(
                    $"[Nocne Sprzątanie] VirtualRack: " +
                    $"boxesChecked={boxesChecked}, " +
                    $"statesMatched={statesMatched}, " +
                    $"statesMissing={statesMissing}, " +
                    $"boxesChanged={boxesChanged}, " +
                    $"boxesRemoved={boxesRemoved}, " +
                    $"unitsRemoved={removedFromVirtualRack}.");
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log?.LogWarning(
                    "[Nocne Sprzątanie] Virtual rack cleanup warning: " +
                    ex);
            }
        }

        private static SavedBoxDataV3 FindVirtualRackState(
            RackSlot slot,
            RackSlotData rackData,
            BoxData boxData,
            HashSet<SavedBoxDataV3> usedStates)
        {
            if (boxData == null)
                return null;

            int uid = 0;
            try { uid = boxData.UID; } catch { }

            if (uid > 0 &&
                ExpirationSaveManager
                    .rackBoxStatesByUid
                    .TryGetValue(
                        uid,
                        out SavedBoxDataV3 byUid) &&
                byUid != null &&
                (usedStates == null ||
                 !usedStates.Contains(byUid)))
            {
                return byUid;
            }

            int productId =
                boxData.ProductID;

            int productCount =
                boxData.ProductCount;

            int rackBoxId = 0;
            try
            {
                if (rackData != null)
                    rackBoxId = rackData.BoxID;
            }
            catch { }

            Vector3 slotPosition =
                slot != null
                    ? slot.transform.position
                    : Vector3.zero;

            SavedBoxDataV3 best =
                null;

            float bestScore =
                float.MaxValue;

            List<SavedBoxDataV3> states =
                ExpirationSaveManager
                    .rackDataStates;

            if (states == null)
                return null;

            for (int i = 0;
                 i < states.Count;
                 i++)
            {
                SavedBoxDataV3 candidate =
                    states[i];

                if (candidate == null ||
                    (usedStates != null &&
                     usedStates.Contains(candidate)) ||
                    candidate.ProductId != productId ||
                    candidate.Dates == null ||
                    candidate.DeliveryDays == null ||
                    candidate.Dates.Count != productCount ||
                    candidate.DeliveryDays.Count != productCount)
                {
                    continue;
                }

                if (rackBoxId > 0 &&
                    candidate.BoxId > 0 &&
                    candidate.BoxId != rackBoxId)
                {
                    continue;
                }

                float distance =
                    Vector3.Distance(
                        candidate.Position,
                        slotPosition);

                // Bootstrap stores rack-box positions at/very near the rack slot.
                // Keep unrelated equal product/count states out of the match.
                if (distance > 3.50f)
                    continue;

                float score =
                    distance * distance;

                if (score < bestScore)
                {
                    bestScore =
                        score;

                    best =
                        candidate;
                }
            }

            return best;
        }

        private static void RemoveEmptyPbox3State(
            SavedBoxDataV3 state)
        {
            if (state == null)
                return;

            string persistentId =
                state.PersistentId;

            if (!string.IsNullOrEmpty(
                    persistentId))
            {
                ExpirationSaveManager
                    .activeBoxStatesById
                    .Remove(persistentId);
            }

            List<SavedBoxDataV3> pending =
                ExpirationSaveManager
                    .pendingLoadedBoxesV3;

            if (pending == null)
                return;

            for (int i = pending.Count - 1;
                 i >= 0;
                 i--)
            {
                SavedBoxDataV3 candidate =
                    pending[i];

                if (candidate == null)
                    continue;

                if (ReferenceEquals(
                        candidate,
                        state) ||
                    (!string.IsNullOrEmpty(
                         persistentId) &&
                     string.Equals(
                         candidate.PersistentId,
                         persistentId,
                         StringComparison.Ordinal)))
                {
                    pending.RemoveAt(i);
                }
            }
        }

        private static void SyncMaterializedRackBox(
            RackSlot slot,
            BoxData boxData,
            int remainingCount,
            bool removeBox)
        {
            if (slot == null ||
                boxData == null ||
                slot.m_Boxes == null)
            {
                return;
            }

            int wantedUid = 0;
            try { wantedUid = boxData.UID; } catch { }

            for (int i = slot.m_Boxes.Count - 1;
                 i >= 0;
                 i--)
            {
                Box box =
                    slot.m_Boxes[i];

                if (box == null ||
                    box.Data == null)
                {
                    continue;
                }

                bool same =
                    ReferenceEquals(
                        box.Data,
                        boxData);

                if (!same &&
                    wantedUid > 0)
                {
                    int boxUid = 0;
                    try { boxUid = box.Data.UID; } catch { }

                    same =
                        boxUid == wantedUid;
                }

                if (!same)
                    continue;

                if (!removeBox)
                {
                    box.Data.ProductCount =
                        remainingCount;

                    try
                    {
                        box.RefreshSpawnedProducts();
                    }
                    catch { }

                    return;
                }

                try
                {
                    slot.m_Boxes.RemoveAt(i);
                }
                catch { }

                try
                {
                    box.ToggleInstanced(false);
                }
                catch { }

                try
                {
                    if (slot.m_Highlightable != null)
                    {
                        slot.m_Highlightable
                            .AddOrRemoveRenderer(
                                box.RenderersToHighlight,
                                false);
                    }
                }
                catch { }

                try
                {
                    UnityEngine.Object.Destroy(
                        box.gameObject);
                }
                catch { }

                return;
            }
        }

        private static void RefreshVirtualRackSlotAfterCleanup(
            RackSlot slot)
        {
            if (slot == null ||
                slot.m_Data == null)
            {
                return;
            }

            int productId = 0;
            try { productId = slot.m_Data.ProductID; } catch { }

            try
            {
                if (slot.m_Data.BoxCount <= 0)
                {
                    slot.m_Data.Clear();

                    RackManager rackManager =
                        NoktaSingleton<RackManager>
                            .Instance;

                    if (rackManager != null &&
                        productId > 0)
                    {
                        rackManager
                            .RemoveRackSlot(
                                productId,
                                slot);
                    }
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    "[Nocne Sprzątanie] RackSlot clear warning: " +
                    ex.Message);
            }

            try
            {
                if (slot.m_Label != null &&
                    slot.m_Data != null)
                {
                    slot.m_Label.ProductCount =
                        slot.m_Data.TotalProductCount;
                }
            }
            catch { }

            try
            {
                slot.RefreshLabel();
            }
            catch { }

            try
            {
                slot.RePositionBoxes();
            }
            catch { }
        }

        private static bool TryPrepareExpiredBoxProductForNativeTake(
            Box box,
            int closedDay)
        {
            if (box == null)
                return false;

            int productCount = 0;

            try
            {
                productCount =
                    box.ProductCount;
            }
            catch { }

            if (productCount <= 0)
                return false;

            if (!ExpirationSaveManager
                    .EnsureRuntimeBoxState(box))
            {
                return false;
            }

            int runtimeKey =
                box.GetInstanceID();

            if (!ExpirationSaveManager
                    .runtimeBoxDates
                    .TryGetValue(
                        runtimeKey,
                        out List<int> dates) ||
                dates == null ||
                !ExpirationSaveManager
                    .runtimeBoxDeliveryDaysPerProduct
                    .TryGetValue(
                        runtimeKey,
                        out List<int> deliveries) ||
                deliveries == null)
            {
                return false;
            }

            int pairedCount =
                Math.Min(
                    dates.Count,
                    deliveries.Count);

            if (pairedCount <= 0)
                return false;

            int expiredIndex =
                -1;

            int bestDate =
                int.MaxValue;

            for (int i = 0;
                 i < pairedCount;
                 i++)
            {
                if (dates[i] <= closedDay &&
                    dates[i] < bestDate)
                {
                    bestDate =
                        dates[i];

                    expiredIndex =
                        i;
                }
            }

            if (expiredIndex < 0)
                return false;

            List<global::Product> products =
                ExpirationSaveManager
                    .GetSortedProducts(
                        box.transform);

            // If the box has no physical Product hierarchy (virtualized rack box),
            // do not mutate its logical ProductCount. It will be handled when the
            // game materializes that box.
            if (products == null ||
                products.Count <= 0)
            {
                return false;
            }

            int lastIndex =
                Math.Min(
                    pairedCount,
                    products.Count) - 1;

            if (lastIndex < 0)
                return false;

            // If metadata has a stale tail, only use a product index that physically
            // exists. Search for another expired pair inside the physical range.
            if (expiredIndex > lastIndex)
            {
                expiredIndex = -1;
                bestDate = int.MaxValue;

                for (int i = 0;
                     i <= lastIndex;
                     i++)
                {
                    if (dates[i] <= closedDay &&
                        dates[i] < bestDate)
                    {
                        bestDate =
                            dates[i];

                        expiredIndex =
                            i;
                    }
                }

                if (expiredIndex < 0)
                    return false;
            }

            global::Product expiredProduct =
                products[expiredIndex];

            global::Product lastProduct =
                products[lastIndex];

            if (expiredProduct == null ||
                lastProduct == null)
            {
                return false;
            }

            ProductExpirationComponent expiredComp =
                expiredProduct
                    .GetComponent<ProductExpirationComponent>();

            ProductExpirationComponent lastComp =
                lastProduct
                    .GetComponent<ProductExpirationComponent>();

            int productId =
                ExpirationSaveManager
                    .GetBoxProductId(box);

            if (expiredComp == null)
            {
                expiredComp =
                    expiredProduct.gameObject
                        .AddComponent<ProductExpirationComponent>();

                expiredComp.hideFlags =
                    HideFlags.DontSave |
                    HideFlags.HideInInspector;

                expiredComp.ProductID =
                    productId;

                expiredComp.ExpirationDay =
                    dates[expiredIndex];

                expiredComp.DeliveryDay =
                    ExpirationSaveManager
                        .NormalizeDeliveryDay(
                            productId,
                            dates[expiredIndex],
                            deliveries[expiredIndex]);
            }

            if (lastComp == null)
            {
                lastComp =
                    lastProduct.gameObject
                        .AddComponent<ProductExpirationComponent>();

                lastComp.hideFlags =
                    HideFlags.DontSave |
                    HideFlags.HideInInspector;

                lastComp.ProductID =
                    productId;

                lastComp.ExpirationDay =
                    dates[lastIndex];

                lastComp.DeliveryDay =
                    ExpirationSaveManager
                        .NormalizeDeliveryDay(
                            productId,
                            dates[lastIndex],
                            deliveries[lastIndex]);
            }

            if (expiredIndex != lastIndex)
            {
                int expiredDate =
                    expiredComp.ExpirationDay;

                int expiredDelivery =
                    ExpirationSaveManager
                        .NormalizeDeliveryDay(
                            productId,
                            expiredComp.ExpirationDay,
                            expiredComp.DeliveryDay);

                int lastDate =
                    lastComp.ExpirationDay;

                int lastDelivery =
                    ExpirationSaveManager
                        .NormalizeDeliveryDay(
                            productId,
                            lastComp.ExpirationDay,
                            lastComp.DeliveryDay);

                // Native Box.GetProductFromBox removes the newest/end Product.
                // Move only the complete expiry metadata pair to that Product.
                lastComp.ExpirationDay =
                    expiredDate;

                lastComp.DeliveryDay =
                    expiredDelivery;

                expiredComp.ExpirationDay =
                    lastDate;

                expiredComp.DeliveryDay =
                    lastDelivery;

                int tempDate =
                    dates[expiredIndex];

                dates[expiredIndex] =
                    dates[lastIndex];

                dates[lastIndex] =
                    tempDate;

                int tempDelivery =
                    deliveries[expiredIndex];

                deliveries[expiredIndex] =
                    deliveries[lastIndex];

                deliveries[lastIndex] =
                    tempDelivery;

                ExpirationSaveManager
                    .TouchRuntimeBoxState(box);
            }

            return true;
        }

        private static void AddSpoiledCount(
            Dictionary<int, int> map,
            int productId,
            int count)
        {
            if (map == null ||
                productId <= 0 ||
                count <= 0)
            {
                return;
            }

            if (map.TryGetValue(
                    productId,
                    out int existing))
            {
                map[productId] =
                    existing + count;
            }
            else
            {
                map[productId] =
                    count;
            }
        }
    }
}
