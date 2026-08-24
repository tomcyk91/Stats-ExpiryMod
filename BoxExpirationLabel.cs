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

            // PBOX3 state is restored by physical fingerprint, never by
            // Box.Data.UID. This also repairs a runtime Box rebuilt by another
            // mod if the old PBOX3 session snapshot still matches its transform.
            if (!ExpirationSaveManager
                    .EnsureRuntimeBoxState(_box))
            {
                return;
            }

            CustomExpirationLoader.Load();

            bool configChanged =
                _lastConfigVersion !=
                CustomExpirationLoader.ConfigVersion;

            _lastConfigVersion =
                CustomExpirationLoader.ConfigVersion;

            if (configChanged)
            {
                bool fromSave =
                    ExpirationSaveManager
                        .runtimeBoxDatesFromSave
                        .TryGetValue(
                            BoxKey,
                            out bool savedFlag) &&
                    savedFlag;

                int overrideDays =
                    BoxLabelPatch
                        .GetConfigOverrideDirectly(productId);

                // Config changes affect runtime-created products, but a loaded
                // save keeps its exact historical metadata.
                if (!fromSave &&
                    overrideDays >= 0 &&
                    ExpirationSaveManager
                        .runtimeBoxDates
                        .TryGetValue(
                            BoxKey,
                            out List<int> dates) &&
                    dates != null &&
                    ExpirationSaveManager
                        .runtimeBoxDeliveryDaysPerProduct
                        .TryGetValue(
                            BoxKey,
                            out List<int> deliveries) &&
                    deliveries != null &&
                    dates.Count == deliveries.Count)
                {
                    for (int i = 0;
                         i < dates.Count;
                         i++)
                    {
                        int deliveryDay =
                            ExpirationSaveManager
                                .NormalizeDeliveryDay(
                                    productId,
                                    dates[i],
                                    deliveries[i]);

                        deliveries[i] =
                            deliveryDay;

                        dates[i] =
                            deliveryDay +
                            overrideDays;
                    }

                    // Keep physical Product metadata paired with runtime lists.
                    try
                    {
                        List<global::Product> products =
                            ExpirationSaveManager
                                .GetSortedProducts(_box.transform);

                        int pairCount =
                            Math.Min(
                                products.Count,
                                dates.Count);

                        for (int i = 0;
                             i < pairCount;
                             i++)
                        {
                            var product =
                                products[i];

                            if (product == null)
                                continue;

                            var comp =
                                product
                                    .GetComponent<ProductExpirationComponent>();

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
                                deliveries[i];
                        }
                    }
                    catch { }

                    ExpirationSaveManager
                        .TouchRuntimeBoxState(_box);
                }
            }

            _isInitialized = true;
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

        void OnDestroy()
        {
            BoxLabelPatch.AllLabels.Remove(this);

            if (SmartExpiration.SEProfiler.BoxTextCount > 0)
            {
                SmartExpiration.SEProfiler.BoxTextCount--;
            }

            // Remove only this runtime InstanceID. The PBOX3 session snapshot
            // remains available so a box rebuilt by another mod can recover it.
            if (_box != null)
            {
                ExpirationSaveManager
                    .RemoveRuntimeBoxInstance(
                        _box,
                        false);
            }
        }

        public void ForceRefreshAfterPbox3Restore()
        {
            try
            {
                if (_box == null)
                    _box = GetComponent<Box>();

                if (_box == null ||
                    _box.ProductCount <= 0)
                {
                    return;
                }

                if (BoxKey <= 0)
                    BoxKey = _box.GetInstanceID();

                _isInitialized = true;
                _lastProductCount = -1;
                _lastDay = -1;

                ProcessRuntimeUpdate();
                ProcessLogicUpdate();
            }
            catch { }
        }

        // Compatibility with the previous PBOX2 hotfix finalizer.
        public void ForceRefreshAfterPbox2Restore()
        {
            ForceRefreshAfterPbox3Restore();
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
            if (_textMesh == null)
                return;

            if (_box == null ||
                _box.ProductCount <= 0)
            {
                _textMesh.text =
                    $"<color=#000000>" +
                    $"{StatisticMod.Plugin.T("Pusty karton", "Empty box")}" +
                    $"</color>";

                return;
            }

            int currentDay =
                ExpirationSaveManager
                    .GetCurrentDaySafe();

            if (!ExpirationSaveManager
                    .TryGetBoxDisplayPair(
                        _box,
                        out int expDay,
                        out int deliveryDay))
            {
                return;
            }

            int daysLeft =
                expDay -
                currentDay;

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

            string expiryDisplay;

            if (daysLeft < 0)
            {
                int overdueDays =
                    -daysLeft;

                expiryDisplay =
                    StatisticMod.Plugin.T(
                        $"przeterminowany {overdueDays} d.",
                        $"expired {overdueDays} d.");
            }
            else if (daysLeft == 0)
            {
                expiryDisplay =
                    StatisticMod.Plugin.T(
                        "dzisiaj",
                        "today");
            }
            else if (daysLeft == 1)
            {
                expiryDisplay =
                    StatisticMod.Plugin.T(
                        "1 dzień",
                        "1 day");
            }
            else
            {
                expiryDisplay =
                    StatisticMod.Plugin.T(
                        $"{daysLeft} dni",
                        $"{daysLeft} days");
            }

            _textMesh.text =
                $"<color=#C0C0C0>{textDostawa} {deliveryDay}</color> | " +
                $"<color={color}>{textTermin} {expiryDisplay}</color>\n" +
                $"<size=80%><color=#C0C0C0>" +
                $"{textZapas} {_box.ProductCount} {textSzt}" +
                $"</color></size>";
        }
    }
}
