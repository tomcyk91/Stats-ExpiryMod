using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using PG;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using static UnityEngine.UI.ContentSizeFitter;
using SmartExpiration;

namespace StatisticMod
{
    public partial class StatsAppManager : FakeMonoBehaviour
    {
        private int GetNearestDaysLeft(SortedDictionary<int, int> batches)
        {
            if (batches == null || batches.Count == 0) return int.MaxValue;

            // SortedDictionary jest posortowany po key, więc First() = najmniejszy daysLeft
            foreach (var kv in batches)
            {
                if (kv.Value > 0) return kv.Key;
            }
            return int.MaxValue;
        }

        private void BuildExpirationTilesNow()
        {
            ClearTilesOnly();

            var visual = Plugin.ProductCache;
            if (visual == null) return;
            if (visual.Count == 0)
            {
                var idm = UnityEngine.Object.FindFirstObjectByType<global::IDManager>();
                if (idm != null) visual.Build(idm);
            }

            BuildExpirationMaps(
                out var shelfExpirations,
                out var boxExpirations,
                out var globalExpirations);

            if (globalExpirations.Count == 0)
            {
                ForceTilesLayout(0);
                return;
            }

            var ids = new List<int>(globalExpirations.Keys);
            var polishCulture = new System.Globalization.CultureInfo("pl-PL");

            ids.Sort((a, b) =>
            {
                int dir = _sortAsc ? 1 : -1;
                int cmp = 0;

                if (_simpleSort == SimpleSortMode.ProductId)
                    cmp = a.CompareTo(b);
                else if (_simpleSort == SimpleSortMode.NearestExpiry)
                    cmp = GetNearestDaysLeft(globalExpirations[a]).CompareTo(GetNearestDaysLeft(globalExpirations[b]));
                else
                    cmp = string.Compare(
                        GetProductNameSafe(a),
                        GetProductNameSafe(b),
                        polishCulture,
                        System.Globalization.CompareOptions.IgnoreCase);

                if (cmp == 0) cmp = a.CompareTo(b);
                return cmp * dir;
            });

            int built = 0;

            foreach (var pid in ids)
            {
                visual.TryGet(pid, out var name, out var icon);

                var tile = Instantiate(_tileTemplate, _tilesContent, false);
                tile.SetActive(true);
                DisableGameScriptsOnTile(tile.transform);
                tile.name = "ExpirationTile_" + pid;

                var nameTmp = GetTmpComponent(tile.transform, "Product Name");
                if (nameTmp != null)
                {
                    nameTmp.text = FormatTileProductName(name, pid, false);
                    nameTmp.enableAutoSizing = true;
                    nameTmp.fontSizeMin = 6f;
                    nameTmp.fontSizeMax = 14f;
                    nameTmp.enableWordWrapping = false;
                    nameTmp.alignment = TextAlignmentOptions.Left;
                }

                var iconTr = tile.transform.Find("Product Icon");
                if (iconTr != null)
                {
                    var img = iconTr.GetComponent<UnityEngine.UI.Image>();
                    if (img != null)
                    {
                        img.sprite = icon;
                        img.enabled = icon != null;
                        img.preserveAspect = true;
                    }
                }

                shelfExpirations.TryGetValue(pid, out var shelfBatches);
                boxExpirations.TryGetValue(pid, out var boxBatches);

                var infoTmp = GetTmpComponent(tile.transform, "Product Brand");
                if (infoTmp != null)
                    infoTmp.text = BuildExpirationText(pid, shelfBatches, boxBatches);

                AdjustProductTileContent(tile.transform);

                if (infoTmp != null)
                {
                    ConfigureExpirationLocationText(infoTmp);
                    AttachExpirationInfoTooltipZones(tile.transform, infoTmp, shelfBatches, boxBatches);
                }

                ApplyExpirationAccent(tile.transform, globalExpirations[pid]);
                ForceProfessionalTileVisuals(tile.transform);
                built++;
            }

            ForceTilesLayout(built);
        }

        private void RetryBuildExpiration()
        {
            if (!_isOpen) return;
            if (_hubMode != HubMode.Expiration) return;
            BuildExpirationTilesNow();
        }

        private void BuildExpirationMaps(
            out Dictionary<int, SortedDictionary<int, int>> shelfExpirations,
            out Dictionary<int, SortedDictionary<int, int>> boxExpirations,
            out Dictionary<int, SortedDictionary<int, int>> globalExpirations)
        {
            shelfExpirations = new Dictionary<int, SortedDictionary<int, int>>();
            boxExpirations = new Dictionary<int, SortedDictionary<int, int>>();
            globalExpirations = new Dictionary<int, SortedDictionary<int, int>>();

            var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
            int currentDay = dcm != null ? dcm.CurrentDay : 1;

            // =========================================================
            // 1. PÓŁKI / EKSPOZYCJE
            // =========================================================
            var allSlots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
            if (allSlots != null)
            {
                for (int i = 0; i < allSlots.Count; i++)
                {
                    var slot = allSlots[i];
                    if (slot == null || !slot.HasProduct) continue;

                    ExpirationManager.SyncShelf(slot);

                    var products = slot.GetComponentsInChildren<global::Product>(true);
                    if (products == null) continue;

                    for (int pIdx = 0; pIdx < products.Count; pIdx++)
                    {
                        var product = products[pIdx];
                        if (product == null) continue;

                        var comp = product.GetComponent<ProductExpirationComponent>();
                        if (comp == null) continue;

                        int pid = comp.ProductID;
                        if (pid <= 0 || comp.ExpirationDay <= 0) continue;

                        int daysLeft = comp.ExpirationDay - currentDay;

                        AddExpirationCount(shelfExpirations, pid, daysLeft);
                        AddExpirationCount(globalExpirations, pid, daysLeft);
                    }
                }
            }

            // =========================================================
            // 2. KARTONY / MAGAZYN
            //
            // PBOX3 NIE używa Box.Data.UID jako źródła prawdy.
            // Poprzednia wersja ekranu czytała legacy boxDates[UID],
            // przez co kartony w magazynie często znikały ze statystyk.
            //
            // Tutaj korzystamy z tego samego źródła co system PBOX3:
            // EnsureRuntimeBoxState() + runtimeBoxDates[InstanceID].
            // Dodatkowo skanujemy nieaktywne Box-y oraz zapisane stany
            // PBOX3, aby objąć kartony schowane na regałach/inactive.
            // =========================================================
            var scannedRuntimeInstances = new HashSet<int>();
            var scannedPersistentIds = new HashSet<string>();

            // Rebuild the native RackSlot.Data -> PBOX3 bridge immediately
            // before the screen is built. This guarantees that virtualized
            // warehouse boxes are represented even without a Box GameObject.
            try
            {
                ExpirationSaveManager.BootstrapMissingRackBoxStates();
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning(
                    $"[ExpiryUI] Rack-data bridge refresh failed: {ex.Message}");
            }

            int directRackStates = 0;
            int directRackUnits = 0;

            try
            {
                var rackStates = ExpirationSaveManager.rackDataStates;

                if (rackStates != null)
                {
                    for (int r = 0; r < rackStates.Count; r++)
                    {
                        var state = rackStates[r];

                        if (state == null ||
                            state.ProductId <= 0 ||
                            state.Dates == null ||
                            state.Dates.Count == 0)
                        {
                            continue;
                        }

                        for (int i = 0; i < state.Dates.Count; i++)
                        {
                            int expirationDay = state.Dates[i];
                            if (expirationDay <= 0) continue;

                            int daysLeft = expirationDay - currentDay;

                            AddExpirationCount(
                                boxExpirations,
                                state.ProductId,
                                daysLeft);

                            AddExpirationCount(
                                globalExpirations,
                                state.ProductId,
                                daysLeft);

                            directRackUnits++;
                        }

                        if (!string.IsNullOrEmpty(state.PersistentId))
                            scannedPersistentIds.Add(state.PersistentId);

                        directRackStates++;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning(
                    $"[ExpiryUI] Direct rack-data scan failed: {ex.Message}");
            }

            int rackBoxes = 0;
            int physicalBoxes = 0;
            int inactiveBoxes = 0;
            int physicalUnits = 0;
            int fallbackStates = 0;
            int pendingStates = 0;

            // Najpierw magazyn: RackSlot.Boxes jest źródłem prawdy dla
            // kartonów stojących na regałach. Część z nich jest ukryta /
            // nieaktywna i nie trafia do FindObjectsOfType<Box>().
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
                        {
                            var box = boxes[b];
                            if (box == null)
                                continue;

                            int added = AddPhysicalBoxExpiration(
                                box,
                                currentDay,
                                boxExpirations,
                                globalExpirations,
                                scannedRuntimeInstances,
                                scannedPersistentIds);

                            if (added > 0)
                            {
                                rackBoxes++;
                                physicalBoxes++;
                                physicalUnits += added;
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning(
                    $"[ExpiryUI] Rack warehouse scan failed: {ex.Message}");
            }

            // Potem zwykły aktywny scan sceny.
            var activeBoxes = UnityEngine.Object.FindObjectsOfType<Box>();
            if (activeBoxes != null)
            {
                for (int i = 0; i < activeBoxes.Count; i++)
                {
                    var box = activeBoxes[i];
                    if (box == null) continue;

                    int added = AddPhysicalBoxExpiration(
                        box,
                        currentDay,
                        boxExpirations,
                        globalExpirations,
                        scannedRuntimeInstances,
                        scannedPersistentIds);

                    if (added > 0)
                    {
                        physicalBoxes++;
                        physicalUnits += added;
                    }
                }
            }

            // Regały / magazyn mogą trzymać Box-y w nieaktywnej hierarchii.
            // Resources.FindObjectsOfTypeAll znajduje również takie obiekty.
            try
            {
                var allRuntimeBoxes =
                    UnityEngine.Resources.FindObjectsOfTypeAll<Box>();

                if (allRuntimeBoxes != null)
                {
                    foreach (var box in allRuntimeBoxes)
                    {
                        if (box == null) continue;

                        int instanceId = 0;
                        try { instanceId = box.GetInstanceID(); } catch { }

                        if (instanceId == 0 ||
                            scannedRuntimeInstances.Contains(instanceId))
                        {
                            continue;
                        }

                        // Odrzuć prefab/assets - interesują nas tylko obiekty
                        // należące do załadowanej sceny gry.
                        try
                        {
                            if (box.gameObject == null)
                                continue;

                            var scene = box.gameObject.scene;
                            if (!scene.IsValid() || !scene.isLoaded)
                                continue;
                        }
                        catch
                        {
                            continue;
                        }

                        int added = AddPhysicalBoxExpiration(
                            box,
                            currentDay,
                            boxExpirations,
                            globalExpirations,
                            scannedRuntimeInstances,
                            scannedPersistentIds);

                        if (added > 0)
                        {
                            physicalBoxes++;
                            inactiveBoxes++;
                            physicalUnits += added;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning(
                    $"[ExpiryUI] Inactive warehouse box scan failed: {ex.Message}");
            }

            // Jeżeli fizyczny Box jest chwilowo niewidoczny dla Unity
            // (np. został przebudowany przez grę/mod albo siedzi w ukrytej
            // hierarchii), PBOX3 nadal przechowuje jego aktualny stan.
            try
            {
                foreach (var kvp in ExpirationSaveManager.activeBoxStatesById)
                {
                    var state = kvp.Value;
                    if (state == null ||
                        state.ProductId <= 0 ||
                        state.Dates == null ||
                        state.Dates.Count == 0)
                    {
                        continue;
                    }

                    string persistentId =
                        !string.IsNullOrEmpty(state.PersistentId)
                            ? state.PersistentId
                            : kvp.Key;

                    if (!string.IsNullOrEmpty(persistentId) &&
                        scannedPersistentIds.Contains(persistentId))
                    {
                        continue;
                    }

                    for (int i = 0; i < state.Dates.Count; i++)
                    {
                        int expirationDay = state.Dates[i];
                        if (expirationDay <= 0) continue;

                        int daysLeft = expirationDay - currentDay;

                        AddExpirationCount(
                            boxExpirations,
                            state.ProductId,
                            daysLeft);

                        AddExpirationCount(
                            globalExpirations,
                            state.ProductId,
                            daysLeft);
                    }

                    if (!string.IsNullOrEmpty(persistentId))
                        scannedPersistentIds.Add(persistentId);

                    fallbackStates++;
                }
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning(
                    $"[ExpiryUI] Active PBOX3 fallback scan failed: {ex.Message}");
            }

            // PBOX3-y wczytane z dysku, które jeszcze nie zostały dopasowane
            // do aktywnego GameObjectu Box, również reprezentują realny zapas.
            try
            {
                var pending = ExpirationSaveManager.pendingLoadedBoxesV3;
                if (pending != null)
                {
                    for (int p = 0; p < pending.Count; p++)
                    {
                        var state = pending[p];
                        if (state == null ||
                            state.Matched ||
                            state.ProductId <= 0 ||
                            state.Dates == null ||
                            state.Dates.Count == 0)
                        {
                            continue;
                        }

                        string persistentId = state.PersistentId;

                        if (!string.IsNullOrEmpty(persistentId) &&
                            scannedPersistentIds.Contains(persistentId))
                        {
                            continue;
                        }

                        for (int i = 0; i < state.Dates.Count; i++)
                        {
                            int expirationDay = state.Dates[i];
                            if (expirationDay <= 0) continue;

                            int daysLeft = expirationDay - currentDay;

                            AddExpirationCount(
                                boxExpirations,
                                state.ProductId,
                                daysLeft);

                            AddExpirationCount(
                                globalExpirations,
                                state.ProductId,
                                daysLeft);
                        }

                        if (!string.IsNullOrEmpty(persistentId))
                            scannedPersistentIds.Add(persistentId);

                        pendingStates++;
                    }
                }
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning(
                    $"[ExpiryUI] Pending PBOX3 fallback scan failed: {ex.Message}");
            }

            Plugin.Log.LogInfo(
                $"[ExpiryUI] Warehouse scan: directRackStates={directRackStates}, " +
                $"directRackUnits={directRackUnits}, rackBoxes={rackBoxes}, " +
                $"physicalBoxes={physicalBoxes}, inactiveBoxes={inactiveBoxes}, " +
                $"physicalUnits={physicalUnits}, fallbackStates={fallbackStates}, " +
                $"pendingStates={pendingStates}, productsWithBoxDates={boxExpirations.Count}");
        }

        private int AddPhysicalBoxExpiration(
            Box box,
            int currentDay,
            Dictionary<int, SortedDictionary<int, int>> boxExpirations,
            Dictionary<int, SortedDictionary<int, int>> globalExpirations,
            HashSet<int> scannedRuntimeInstances,
            HashSet<string> scannedPersistentIds)
        {
            if (box == null)
                return 0;

            int instanceId = 0;
            int productCount = 0;
            int productId = 0;

            try { instanceId = box.GetInstanceID(); } catch { }
            if (instanceId == 0)
                return 0;

            // If this Box belongs to RackSlot.Data.RackedBoxDatas, it has
            // already been counted by the direct native rack-data pass above.
            // UID is used only to prevent duplicate counting in this session.
            try
            {
                int rackUid =
                    ExpirationSaveManager.GetStableBoxUid(box);

                if (rackUid > 0 &&
                    ExpirationSaveManager.rackBoxStatesByUid.ContainsKey(rackUid))
                {
                    if (scannedRuntimeInstances != null)
                        scannedRuntimeInstances.Add(instanceId);

                    // Still hydrate runtime state so the physical box label can
                    // display the same date as the application.
                    ExpirationSaveManager.EnsureRuntimeBoxState(box);
                    return 0;
                }
            }
            catch { }

            if (scannedRuntimeInstances != null &&
                scannedRuntimeInstances.Contains(instanceId))
            {
                return 0;
            }

            try { productCount = box.ProductCount; } catch { }
            if (productCount <= 0)
            {
                if (scannedRuntimeInstances != null)
                    scannedRuntimeInstances.Add(instanceId);

                return 0;
            }

            try
            {
                productId =
                    ExpirationSaveManager.GetBoxProductId(box);
            }
            catch { }

            if (productId <= 0)
            {
                if (scannedRuntimeInstances != null)
                    scannedRuntimeInstances.Add(instanceId);

                return 0;
            }

            // To jest kluczowa zmiana względem poprzedniej wersji:
            // najpierw odbuduj / odtwórz stan PBOX3 dla danego kartonu.
            if (!ExpirationSaveManager.EnsureRuntimeBoxState(box))
            {
                if (scannedRuntimeInstances != null)
                    scannedRuntimeInstances.Add(instanceId);

                return 0;
            }

            if (!ExpirationSaveManager.runtimeBoxDates.TryGetValue(
                    instanceId,
                    out var dates) ||
                dates == null ||
                dates.Count == 0)
            {
                if (scannedRuntimeInstances != null)
                    scannedRuntimeInstances.Add(instanceId);

                return 0;
            }

            int added = 0;

            for (int i = 0; i < dates.Count; i++)
            {
                int expirationDay = dates[i];
                if (expirationDay <= 0) continue;

                int daysLeft = expirationDay - currentDay;

                AddExpirationCount(
                    boxExpirations,
                    productId,
                    daysLeft);

                AddExpirationCount(
                    globalExpirations,
                    productId,
                    daysLeft);

                added++;
            }

            if (scannedRuntimeInstances != null)
                scannedRuntimeInstances.Add(instanceId);

            try
            {
                if (ExpirationSaveManager.runtimeBoxPersistentIds.TryGetValue(
                        instanceId,
                        out var persistentId) &&
                    !string.IsNullOrEmpty(persistentId) &&
                    scannedPersistentIds != null)
                {
                    scannedPersistentIds.Add(persistentId);
                }
            }
            catch { }

            return added;
        }

        private Dictionary<int, SortedDictionary<int, int>> BuildGlobalExpirationMap()
        {
            BuildExpirationMaps(
                out _,
                out _,
                out var globalExpirations);

            return globalExpirations;
        }

        private static void AddExpirationCount(
            Dictionary<int, SortedDictionary<int, int>> target,
            int productId,
            int daysLeft)
        {
            if (target == null || productId <= 0) return;

            if (!target.TryGetValue(productId, out var batches))
            {
                batches = new SortedDictionary<int, int>();
                target[productId] = batches;
            }

            batches.TryGetValue(daysLeft, out int current);
            batches[daysLeft] = current + 1;
        }

        private string BuildExpirationText(
            int productId,
            SortedDictionary<int, int> shelfBatches,
            SortedDictionary<int, int> boxBatches)
        {
            // Show every actual expiry group, from the most urgent date onward.
            var sb = new System.Text.StringBuilder();

            AppendExpirationLocationText(
                sb,
                productId,
                Plugin.T("PÓŁKA", "SHELF"),
                StatsAppTheme.InfoHex,
                shelfBatches);

            sb.Append('\n');

            AppendExpirationLocationText(
                sb,
                productId,
                Plugin.T("KARTONY", "BOXES"),
                StatsAppTheme.PurpleHex,
                boxBatches);

            return sb.ToString();
        }

        private void AppendExpirationLocationText(
            System.Text.StringBuilder sb,
            int productId,
            string locationLabel,
            string locationColor,
            SortedDictionary<int, int> batches)
        {
            sb.Append($"<color={locationColor}><b>{locationLabel}</b></color>\n");

            int validGroups = CountExpirationGroups(batches);
            if (validGroups <= 0)
            {
                sb.Append($"<color=#7C8794>{Plugin.T("brak", "none")}</color>");
                return;
            }

            bool firstEntry = true;

            if (batches != null)
            {
                foreach (var kv in batches)
                {
                    if (kv.Value <= 0) continue;
                    if (!firstEntry)
                        sb.Append('\n');

                    sb.Append(FormatExpirationCompactEntry(productId, kv.Key, kv.Value));

                    firstEntry = false;
                }
            }
        }

        private string FormatExpirationCompactEntry(int productId, int daysLeft, int count)
        {
            string label;
            string colorHex;

            if (daysLeft < 0)
            {
                label = $"{Plugin.T("PO TERM.", "EXPIRED")} ({-daysLeft}{Plugin.T("d", "d")})";
                colorHex = StatsAppTheme.NegativeHex;
            }
            else if (daysLeft == 0)
            {
                label = Plugin.T("DZIŚ", "TODAY");
                colorHex = StatsAppTheme.NegativeHex;
            }
            else if (daysLeft == 1)
            {
                label = Plugin.T("JUTRO", "TOMORROW");
                colorHex = StatsAppTheme.WarningHex;
            }
            else
            {
                label = ModLocalization.InDays(daysLeft);
                colorHex = StatsAppTheme.PositiveHex;
            }

            string quantity = FormatExpirationQuantity(productId, count);

            return $"<color={colorHex}><b>{label}</b></color>: <b>{quantity}</b>";
        }

        private string FormatExpirationQuantity(int productId, int count)
        {
            if (IsWeightProduct(productId))
            {
                float kgPerUnit = SalesUnifiedFinal.WeightPerUnit.TryGetValue(productId, out float w)
                    ? w
                    : 1.0f;

                return (count * kgPerUnit).ToString("N2") + " kg";
            }

            return count.ToString("N0") + " " + Plugin.T("szt.", "pcs");
        }

        private static int CountExpirationGroups(SortedDictionary<int, int> batches)
        {
            if (batches == null || batches.Count == 0) return 0;

            int count = 0;
            foreach (var kv in batches)
            {
                if (kv.Value > 0) count++;
            }

            return count;
        }

        private static int CountExpirationSectionLines(SortedDictionary<int, int> batches)
        {
            int groups = CountExpirationGroups(batches);

            // One heading plus one line per batch, or a single 'none' line.
            return 1 + Math.Max(1, groups);
        }

        private static void ConfigureExpirationLocationText(TextMeshProUGUI tmp)
        {
            if (tmp == null) return;

            tmp.enableAutoSizing = false;
            tmp.fontSize = 7.5f;
            tmp.fontSizeMin = 5.5f;
            tmp.fontSizeMax = 7.5f;
            tmp.lineSpacing = 0f;
            tmp.paragraphSpacing = 0f;
            tmp.enableWordWrapping = true;
            tmp.maxVisibleLines = int.MaxValue;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.alignment = TextAlignmentOptions.TopLeft;
            tmp.margin = new Vector4(1f, 0f, 2f, 0f);
            tmp.color = StatsAppTheme.TileText;
        }

        private void ApplyExpirationAccent(Transform tile, SortedDictionary<int, int> batches)
        {
            if (tile == null || batches == null || batches.Count == 0) return;

            int minDay = int.MaxValue;
            foreach (var d in batches.Keys)
                if (d < minDay) minDay = d;

            Color accent;
            if (minDay <= 0) accent = StatsAppTheme.Negative;
            else if (minDay == 1) accent = StatsAppTheme.Warning;
            else accent = StatsAppTheme.Positive;

            var existing = tile.Find("ExpiryAccent");
            if (existing == null)
            {
                var bar = new GameObject("ExpiryAccent");
                bar.transform.SetParent(tile, false);

                var rt = bar.AddComponent<RectTransform>();
                rt.anchorMin = new Vector2(0f, 0f);
                rt.anchorMax = new Vector2(0.018f, 1f);
                rt.offsetMin = Vector2.zero;
                rt.offsetMax = Vector2.zero;

                bar.AddComponent<CanvasRenderer>();
                var img = bar.AddComponent<Image>();
                img.raycastTarget = false;
                img.color = accent;
            }
            else
            {
                var img = existing.GetComponent<Image>();
                if (img != null) img.color = accent;
            }
        }

    }
}
