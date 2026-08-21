using HarmonyLib;
using Il2CppInterop.Runtime;
using StatisticMod;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SmartExpiration.Patches
{
    public class BoxExpirationLabel : MonoBehaviour
    {
        public BoxExpirationLabel(IntPtr ptr) : base(ptr) { }

        public Box _box;
        public TextMeshPro _textMesh;

        private int _lastProductCount = -1;
        private int _lastDay = -1;
        private int _lastConfigVersion = -1;

        // Zachowuje poprawkę lokalizacji z nowszej wersji moda.
        private int _lastLanguageVersion = -1;

        private static TMP_FontAsset _cachedFont;

        private bool _isInitialized = false;
        public int BoxKey = -1;

        // Rzadki fallback. Główna praca idzie przez BoxLabelGlobalUpdater.
        private float _tickTimer = 0f;

        void Start()
        {
            _box = GetComponent<Box>();

            if (_box != null)
                BoxKey = _box.GetInstanceID();

            GameObject textObj =
                new GameObject("BoxExpLabel");

            textObj.transform.SetParent(
                this.transform,
                false);

            float yOffset = 0.45f;

            BoxCollider boxCol =
                GetComponent<BoxCollider>();

            if (boxCol != null)
            {
                yOffset =
                    boxCol.center.y +
                    (boxCol.size.y / 2f) +
                    0.15f;
            }

            textObj.transform.localPosition =
                new Vector3(
                    0f,
                    yOffset,
                    0f);

            textObj.transform.localRotation =
                Quaternion.Euler(
                    30f,
                    0f,
                    0f);

            _textMesh =
                textObj.AddComponent<TextMeshPro>();

            SmartExpiration.SEProfiler.BoxTextCount++;

            _textMesh.alignment =
                TextAlignmentOptions.Center;

            _textMesh.fontSize = 4f;

            _textMesh.rectTransform.sizeDelta =
                new Vector2(
                    10f,
                    4f);

            textObj.transform.localScale =
                new Vector3(
                    0.05f,
                    0.05f,
                    0.05f);

            _textMesh.fontStyle =
                FontStyles.Bold;

            _textMesh.color =
                Color.white;

            _textMesh.outlineWidth =
                0.2f;

            _textMesh.outlineColor =
                new Color32(
                    0,
                    0,
                    0,
                    255);

            GameObject bgObj =
                GameObject.CreatePrimitive(
                    PrimitiveType.Quad);

            UnityEngine.Object.Destroy(
                bgObj.GetComponent<MeshCollider>());

            bgObj.transform.SetParent(
                textObj.transform,
                false);

            bgObj.transform.localPosition =
                new Vector3(
                    0f,
                    0f,
                    0.1f);

            bgObj.transform.localScale =
                new Vector3(
                    6f,
                    1f,
                    1f);

            MeshRenderer bgRenderer =
                bgObj.GetComponent<MeshRenderer>();

            Material bgMat =
                new Material(
                    Shader.Find("Sprites/Default"));

            bgMat.color =
                new Color(
                    0f,
                    0f,
                    0f,
                    0.7f);

            bgRenderer.material =
                bgMat;

            if (_cachedFont == null)
            {
                if (TMP_Settings.defaultFontAsset != null)
                {
                    _cachedFont =
                        TMP_Settings.defaultFontAsset;
                }
                else
                {
                    var anyText =
                        UnityEngine.Object
                            .FindFirstObjectByType<TextMeshProUGUI>();

                    if (anyText != null)
                        _cachedFont = anyText.font;
                }
            }

            if (_cachedFont != null)
                _textMesh.font = _cachedFont;

            _textMesh.gameObject.SetActive(false);

            BoxLabelPatch.AllLabels.Add(this);

            _tickTimer =
                UnityEngine.Random.Range(
                    0f,
                    2.5f);

            ProcessRuntimeUpdate();
        }

        void Update()
        {
            _tickTimer +=
                Time.deltaTime;

            if (_tickTimer < 2.5f)
                return;

            _tickTimer =
                UnityEngine.Random.Range(
                    0f,
                    0.5f);

            if (_box == null ||
                _box.ProductCount <= 0)
            {
                return;
            }

            var playerTf =
                SmartExpiration.Patches
                    .BoxLabelGlobalUpdater
                    .PlayerTransform;

            if (playerTf != null)
            {
                Vector3 d =
                    transform.position -
                    playerTf.position;

                if (d.sqrMagnitude > 81f)
                    return;
            }

            ProcessRuntimeUpdate();
        }

        public void ProcessRuntimeUpdate()
        {
            if (_box == null ||
                _box.ProductCount <= 0)
            {
                return;
            }

            int productId =
                GetProductId();

            if (productId <= 0)
                return;

            if (BoxKey <= 0)
                BoxKey = _box.GetInstanceID();

            if (!_isInitialized)
            {
                InitializeDatesFromSave(
                    BoxKey,
                    productId);

                CustomExpirationLoader.Load();

                _lastConfigVersion =
                    CustomExpirationLoader.ConfigVersion;

                _isInitialized = true;
            }

            var dcm =
                DayCycleManager.HasInstance
                    ? DayCycleManager.Instance
                    : null;

            int currentDay =
                dcm != null
                    ? dcm.CurrentDay
                    : 1;

            if (!ExpirationSaveManager
                    .runtimeBoxDates
                    .ContainsKey(BoxKey))
            {
                ExpirationSaveManager
                    .runtimeBoxDates[BoxKey] =
                    new List<int>();

                ExpirationSaveManager
                    .runtimeBoxDeliveryDays[BoxKey] =
                    currentDay;

                ExpirationSaveManager
                    .runtimeBoxDatesFromSave[BoxKey] =
                    false;
            }

            var dates =
                ExpirationSaveManager
                    .runtimeBoxDates[BoxKey];

            CustomExpirationLoader.Load();

            bool configChanged =
                _lastConfigVersion !=
                CustomExpirationLoader.ConfigVersion;

            if (configChanged)
            {
                _lastConfigVersion =
                    CustomExpirationLoader.ConfigVersion;

                int overrideDays =
                    BoxLabelPatch
                        .GetConfigOverrideDirectly(productId);

                if (overrideDays != -1)
                {
                    int deliveryDay =
                        ExpirationSaveManager
                            .runtimeBoxDeliveryDays
                            .ContainsKey(BoxKey)
                            ? ExpirationSaveManager
                                .runtimeBoxDeliveryDays[BoxKey]
                            : currentDay;

                    int expDay =
                        deliveryDay +
                        overrideDays;

                    bool fromSave =
                        ExpirationSaveManager
                            .runtimeBoxDatesFromSave
                            .ContainsKey(BoxKey) &&
                        ExpirationSaveManager
                            .runtimeBoxDatesFromSave[BoxKey];

                    if (!fromSave)
                    {
                        for (int i = 0;
                             i < dates.Count;
                             i++)
                        {
                            dates[i] = expDay;
                        }
                    }
                }
            }

            bool countChanged =
                dates.Count !=
                _box.ProductCount;

            if (countChanged)
            {
                if (dates.Count <
                    _box.ProductCount)
                {
                    int trueShelfLife =
                        BoxLabelPatch
                            .GetConfigOverrideDirectly(productId);

                    if (trueShelfLife == -1)
                    {
                        trueShelfLife =
                            ExpirationCalculator
                                .GetDaysForProduct(
                                    null,
                                    productId);
                    }

                    int deliveryDay =
                        ExpirationSaveManager
                            .runtimeBoxDeliveryDays
                            .ContainsKey(BoxKey)
                            ? ExpirationSaveManager
                                .runtimeBoxDeliveryDays[BoxKey]
                            : currentDay;

                    int standardExpDay =
                        deliveryDay +
                        trueShelfLife;

                    int expDayToUse =
                        standardExpDay;

                    if (BoxLabelPatch.ClipboardDate != -1)
                    {
                        if (Time.frameCount -
                            BoxLabelPatch.ClipboardFrame <= 15)
                        {
                            expDayToUse =
                                BoxLabelPatch.ClipboardDate;
                        }

                        BoxLabelPatch.ClipboardDate = -1;
                    }

                    while (dates.Count <
                           _box.ProductCount)
                    {
                        dates.Add(expDayToUse);

                        expDayToUse =
                            standardExpDay;
                    }
                }
                else if (dates.Count >
                         _box.ProductCount)
                {
                    while (dates.Count >
                           _box.ProductCount)
                    {
                        dates.RemoveAt(
                            dates.Count - 1);
                    }
                }
            }

            // Aktualizujemy cache tylko przy realnej zmianie.
            if (countChanged ||
                configChanged ||
                !ExpirationSaveManager
                    .boxDates
                    .ContainsKey(BoxKey))
            {
                int deliveryDay =
                    ExpirationSaveManager
                        .runtimeBoxDeliveryDays
                        .TryGetValue(
                            BoxKey,
                            out int knownDeliveryDay)
                            ? knownDeliveryDay
                            : currentDay;

                if (deliveryDay < 1)
                    deliveryDay = currentDay;

                // Cache runtime/kompatybilności.
                ExpirationSaveManager
                    .boxDates[BoxKey] =
                    new List<int>(dates);

                ExpirationSaveManager
                    .boxDeliveryDays[BoxKey] =
                    deliveryDay;

                // Trwały cache po BoxData.UID.
                int stableUid =
                    ExpirationSaveManager
                        .GetStableBoxUid(_box);

                if (stableUid > 0)
                {
                    ExpirationSaveManager
                        .boxDates[stableUid] =
                        new List<int>(dates);

                    ExpirationSaveManager
                        .boxDeliveryDays[stableUid] =
                        deliveryDay;
                }
            }
        }

        public int GetProductId()
        {
            try
            {
                if (_box != null &&
                    _box.Data != null &&
                    _box.Data.ProductID > 0)
                {
                    return _box.Data.ProductID;
                }
            }
            catch { }

            return -1;
        }

        private void InitializeDatesFromSave(
            int boxKey,
            int productId)
        {
            try
            {
                if (ExpirationSaveManager
                        .runtimeBoxDates
                        .ContainsKey(boxKey))
                {
                    return;
                }

                int stableUid =
                    ExpirationSaveManager
                        .GetStableBoxUid(_box);

                // ======================================================
                // 1. PBOX2 - dokładne dopasowanie po trwałym UID.
                // ======================================================
                if (stableUid > 0 &&
                    ExpirationSaveManager
                        .pendingLoadedBoxesByUid
                        .TryGetValue(
                            stableUid,
                            out SavedBoxData exactSavedData))
                {
                    if (exactSavedData != null &&
                        exactSavedData.Dates != null &&
                        exactSavedData.Dates.Count > 0 &&
                        (exactSavedData.ProductId <= 0 ||
                         exactSavedData.ProductId == productId))
                    {
                        ApplySavedBoxData(
                            boxKey,
                            stableUid,
                            exactSavedData);

                        ExpirationSaveManager
                            .pendingLoadedBoxesByUid
                            .Remove(stableUid);

                        StatisticMod.Plugin.DebugLog(
                            $"[BoxExpiration] Exact PBOX2 restored. " +
                            $"uid={stableUid} " +
                            $"productId={productId} " +
                            $"dates={exactSavedData.Dates.Count}");

                        return;
                    }

                    // UID istnieje, ale zapis nie pasuje do produktu.
                    StatisticMod.Plugin.DebugWarning(
                        $"[BoxExpiration] PBOX2 mismatch. " +
                        $"uid={stableUid} " +
                        $"currentProduct={productId} " +
                        $"savedProduct=" +
                        $"{(exactSavedData != null ? exactSavedData.ProductId : 0)}");

                    ExpirationSaveManager
                        .pendingLoadedBoxesByUid
                        .Remove(stableUid);

                    ExpirationSaveManager
                        .boxDates
                        .Remove(stableUid);

                    ExpirationSaveManager
                        .boxDeliveryDays
                        .Remove(stableUid);
                }

                // ======================================================
                // 2. Stary BOX|uid|...
                // ======================================================
                if (stableUid > 0 &&
                    ExpirationSaveManager
                        .boxDates
                        .TryGetValue(
                            stableUid,
                            out List<int> legacyUidDates) &&
                    legacyUidDates != null &&
                    legacyUidDates.Count > 0)
                {
                    int deliveryDay = 1;

                    if (ExpirationSaveManager
                            .boxDeliveryDays
                            .TryGetValue(
                                stableUid,
                                out int loadedDeliveryDay) &&
                        loadedDeliveryDay > 0)
                    {
                        deliveryDay =
                            loadedDeliveryDay;
                    }

                    SavedBoxData legacyUidData =
                        new SavedBoxData
                        {
                            BoxUid = stableUid,
                            ProductId = productId,
                            Dates = new List<int>(legacyUidDates),
                            DeliveryDay = deliveryDay
                        };

                    ApplySavedBoxData(
                        boxKey,
                        stableUid,
                        legacyUidData);

                    StatisticMod.Plugin.DebugLog(
                        $"[BoxExpiration] Legacy BOX restored by UID. " +
                        $"uid={stableUid} " +
                        $"productId={productId} " +
                        $"dates={legacyUidDates.Count}");

                    return;
                }

                // ======================================================
                // 3. Stary PBOX - wyłącznie migracja.
                // ======================================================
                if (productId > 0 &&
                    ExpirationSaveManager
                        .pendingLoadedBoxes
                        .TryGetValue(
                            productId,
                            out Queue<SavedBoxData> queue) &&
                    queue != null &&
                    queue.Count > 0)
                {
                    SavedBoxData legacyPboxData =
                        queue.Dequeue();

                    if (legacyPboxData != null &&
                        legacyPboxData.Dates != null &&
                        legacyPboxData.Dates.Count > 0)
                    {
                        ApplySavedBoxData(
                            boxKey,
                            stableUid,
                            legacyPboxData);

                        StatisticMod.Plugin.DebugLog(
                            $"[BoxExpiration] Legacy PBOX migrated. " +
                            $"uid={stableUid} " +
                            $"productId={productId} " +
                            $"dates={legacyPboxData.Dates.Count}");

                        return;
                    }
                }

                // ======================================================
                // 4. Nowy karton - brak danych w save.
                // ======================================================
                InitializeFreshBoxState(
                    boxKey,
                    stableUid);
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugWarning(
                    $"[BoxExpiration] InitializeDatesFromSave error: " +
                    $"{ex.Message}");

                int stableUid =
                    ExpirationSaveManager
                        .GetStableBoxUid(_box);

                InitializeFreshBoxState(
                    boxKey,
                    stableUid);
            }
        }

        private void ApplySavedBoxData(
            int boxKey,
            int stableUid,
            SavedBoxData savedData)
        {
            if (savedData == null)
                return;

            List<int> dates =
                savedData.Dates != null
                    ? new List<int>(savedData.Dates)
                    : new List<int>();

            int deliveryDay =
                savedData.DeliveryDay;

            if (deliveryDay < 1)
                deliveryDay = 1;

            // Runtime nadal korzysta z InstanceID.
            ExpirationSaveManager
                .runtimeBoxDates[boxKey] =
                new List<int>(dates);

            ExpirationSaveManager
                .runtimeBoxDeliveryDays[boxKey] =
                deliveryDay;

            ExpirationSaveManager
                .runtimeBoxDatesFromSave[boxKey] =
                true;

            // Cache po InstanceID.
            ExpirationSaveManager
                .boxDates[boxKey] =
                new List<int>(dates);

            ExpirationSaveManager
                .boxDeliveryDays[boxKey] =
                deliveryDay;

            // Cache trwały po UID.
            if (stableUid > 0)
            {
                ExpirationSaveManager
                    .boxDates[stableUid] =
                    new List<int>(dates);

                ExpirationSaveManager
                    .boxDeliveryDays[stableUid] =
                    deliveryDay;
            }
        }

        private void InitializeFreshBoxState(
            int boxKey,
            int stableUid)
        {
            var dcm =
                DayCycleManager.HasInstance
                    ? DayCycleManager.Instance
                    : null;

            int currentDay =
                dcm != null
                    ? dcm.CurrentDay
                    : 1;

            if (currentDay < 1)
                currentDay = 1;

            ExpirationSaveManager
                .runtimeBoxDates[boxKey] =
                new List<int>();

            ExpirationSaveManager
                .runtimeBoxDeliveryDays[boxKey] =
                currentDay;

            ExpirationSaveManager
                .runtimeBoxDatesFromSave[boxKey] =
                false;

            ExpirationSaveManager
                .boxDeliveryDays[boxKey] =
                currentDay;

            if (stableUid > 0)
            {
                ExpirationSaveManager
                    .boxDeliveryDays[stableUid] =
                    currentDay;
            }
        }

        void OnDestroy()
        {
            BoxLabelPatch.AllLabels.Remove(this);

            if (SmartExpiration.SEProfiler.BoxTextCount > 0)
            {
                SmartExpiration.SEProfiler.BoxTextCount--;
            }

            if (BoxKey > 0)
            {
                // Czyścimy wyłącznie runtime InstanceID.
                // Trwałego cache UID nie usuwamy tutaj - może być potrzebny
                // do zapisu/odtworzenia stanu w tej samej sesji.
                ExpirationSaveManager
                    .boxDates
                    .Remove(BoxKey);

                ExpirationSaveManager
                    .boxDeliveryDays
                    .Remove(BoxKey);

                ExpirationSaveManager
                    .runtimeBoxDates
                    .Remove(BoxKey);

                ExpirationSaveManager
                    .runtimeBoxDeliveryDays
                    .Remove(BoxKey);

                ExpirationSaveManager
                    .runtimeBoxDatesFromSave
                    .Remove(BoxKey);
            }
        }

        public void SetTextEnabled(bool state)
        {
            if (_textMesh != null &&
                _textMesh.gameObject.activeSelf != state)
            {
                _textMesh.gameObject.SetActive(state);
            }
        }

        public void ProcessLogicUpdate()
        {
            if (!_isInitialized)
                return;

            var dcm =
                DayCycleManager.HasInstance
                    ? DayCycleManager.Instance
                    : null;

            int currentDay =
                dcm != null
                    ? dcm.CurrentDay
                    : 1;

            int languageVersion =
                ModLocalization.Version;

            if (_box.ProductCount != _lastProductCount ||
                currentDay != _lastDay ||
                languageVersion != _lastLanguageVersion)
            {
                _lastProductCount =
                    _box.ProductCount;

                _lastDay =
                    currentDay;

                _lastLanguageVersion =
                    languageVersion;

                RefreshLabel();
            }
        }

        private void RefreshLabel()
        {
            if (_box == null ||
                _box.ProductCount <= 0)
            {
                _textMesh.text =
                    $"<color=#000000>" +
                    $"{StatisticMod.Plugin.T("Pusty karton", "Empty box")}" +
                    $"</color>";

                return;
            }

            var dcm =
                DayCycleManager.HasInstance
                    ? DayCycleManager.Instance
                    : null;

            int currentDay =
                dcm != null
                    ? dcm.CurrentDay
                    : 1;

            if (ExpirationSaveManager
                    .runtimeBoxDates
                    .TryGetValue(
                        BoxKey,
                        out List<int> dates) &&
                dates.Count > 0)
            {
                int expDay =
                    dates[0];

                int daysLeft =
                    expDay -
                    currentDay;

                int deliveryDay =
                    ExpirationSaveManager
                        .runtimeBoxDeliveryDays
                        .ContainsKey(BoxKey)
                        ? ExpirationSaveManager
                            .runtimeBoxDeliveryDays[BoxKey]
                        : currentDay;

                string color =
                    daysLeft <= 0
                        ? "#FF0000"
                        : (daysLeft == 1
                            ? "#FFA500"
                            : "#00FF00");

                string textDostawa =
                    StatisticMod.Plugin.T(
                        "Dostawa:",
                        "Delivery:");

                string textTermin =
                    StatisticMod.Plugin.T(
                        "Termin:",
                        "Expiry:");

                string textZapas =
                    StatisticMod.Plugin.T(
                        "Zapas:",
                        "Stock:");

                string textSzt =
                    StatisticMod.Plugin.T(
                        "szt.",
                        "pcs.");

                _textMesh.text =
                    $"<color=#C0C0C0>{textDostawa} {deliveryDay}</color> | " +
                    $"<color={color}>{textTermin} {expDay}</color>\n" +
                    $"<size=80%><color=#C0C0C0>" +
                    $"{textZapas} {_box.ProductCount} {textSzt}" +
                    $"</color></size>";
            }
        }
    }
}
