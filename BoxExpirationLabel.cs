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
        private static TMP_FontAsset _cachedFont;

        private bool _isInitialized = false;
        public int BoxKey = -1;

        // ⚡ Zmienna do dławienia częstotliwości skanowania kartonów
        private float _tickTimer = 0f;

        void Start()
        {
            _box = GetComponent<Box>();
            if (_box != null) BoxKey = _box.GetInstanceID();

            GameObject textObj = new GameObject("BoxExpLabel");
            textObj.transform.SetParent(this.transform, false);

            float yOffset = 0.45f;
            BoxCollider boxCol = GetComponent<BoxCollider>();
            if (boxCol != null) yOffset = boxCol.center.y + (boxCol.size.y / 2f) + 0.15f;

            textObj.transform.localPosition = new Vector3(0f, yOffset, 0f);
            textObj.transform.localRotation = Quaternion.Euler(30f, 0f, 0f);

            _textMesh = textObj.AddComponent<TextMeshPro>();
            _textMesh.alignment = TextAlignmentOptions.Center;
            _textMesh.fontSize = 4f;
            _textMesh.rectTransform.sizeDelta = new Vector2(10f, 4f);
            textObj.transform.localScale = new Vector3(0.05f, 0.05f, 0.05f);
            _textMesh.fontStyle = FontStyles.Bold;
            _textMesh.color = Color.white;
            _textMesh.outlineWidth = 0.2f;
            _textMesh.outlineColor = new Color32(0, 0, 0, 255);

            GameObject bgObj = GameObject.CreatePrimitive(PrimitiveType.Quad);
            UnityEngine.Object.Destroy(bgObj.GetComponent<MeshCollider>());
            bgObj.transform.SetParent(textObj.transform, false);
            bgObj.transform.localPosition = new Vector3(0f, 0f, 0.1f);
            bgObj.transform.localScale = new Vector3(6f, 1f, 1f);

            MeshRenderer bgRenderer = bgObj.GetComponent<MeshRenderer>();
            Material bgMat = new Material(Shader.Find("Sprites/Default"));
            bgMat.color = new Color(0f, 0f, 0f, 0.7f);
            bgRenderer.material = bgMat;

            if (_cachedFont == null)
            {
                if (TMP_Settings.defaultFontAsset != null) _cachedFont = TMP_Settings.defaultFontAsset;
                else
                {
                    var anyText = UnityEngine.Object.FindFirstObjectByType<TextMeshProUGUI>();
                    if (anyText != null) _cachedFont = anyText.font;
                }
            }

            if (_cachedFont != null) _textMesh.font = _cachedFont;
            _textMesh.gameObject.SetActive(false);
            BoxLabelPatch.AllLabels.Add(this);
        }

        void Update()
        {
            // ⚡ ATOMOWA OPTYMALIZACJA: Karton odświeża logikę tylko 2 razy na sekundę!
            _tickTimer += Time.deltaTime;
            if (_tickTimer < 0.5f) return;
            // Zapobiega przeliczaniu wszystkich kartonów w tej samej klatce:
            _tickTimer = UnityEngine.Random.Range(0f, 0.1f);

            if (_box == null || _box.ProductCount <= 0) return;

            int productId = GetProductId();
            if (productId <= 0) return;

            if (BoxKey <= 0) BoxKey = _box.GetInstanceID();

            if (!_isInitialized)
            {
                InitializeDatesFromSave(BoxKey, productId);
                CustomExpirationLoader.Load();
                _lastConfigVersion = CustomExpirationLoader.ConfigVersion;
                _isInitialized = true;
            }

            int currentDay = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;

            if (!ExpirationSaveManager.runtimeBoxDates.ContainsKey(BoxKey))
            {
                ExpirationSaveManager.runtimeBoxDates[BoxKey] = new List<int>();
                ExpirationSaveManager.runtimeBoxDeliveryDays[BoxKey] = currentDay;
                ExpirationSaveManager.runtimeBoxDatesFromSave[BoxKey] = false;
            }

            var dates = ExpirationSaveManager.runtimeBoxDates[BoxKey];

            CustomExpirationLoader.Load();
            bool configChanged = _lastConfigVersion != CustomExpirationLoader.ConfigVersion;

            if (configChanged)
            {
                _lastConfigVersion = CustomExpirationLoader.ConfigVersion;
                int overrideDays = BoxLabelPatch.GetConfigOverrideDirectly(productId);
                if (overrideDays != -1)
                {
                    int deliveryDay = ExpirationSaveManager.runtimeBoxDeliveryDays.ContainsKey(BoxKey) ? ExpirationSaveManager.runtimeBoxDeliveryDays[BoxKey] : currentDay;
                    int expDay = deliveryDay + overrideDays;

                    bool fromSave = ExpirationSaveManager.runtimeBoxDatesFromSave.ContainsKey(BoxKey) && ExpirationSaveManager.runtimeBoxDatesFromSave[BoxKey];
                    if (!fromSave)
                    {
                        for (int i = 0; i < dates.Count; i++)
                        {
                            dates[i] = expDay;
                        }
                    }
                }
            }

            bool countChanged = dates.Count != _box.ProductCount;

            if (countChanged)
            {
                if (dates.Count < _box.ProductCount)
                {
                    int trueShelfLife = BoxLabelPatch.GetConfigOverrideDirectly(productId);
                    if (trueShelfLife == -1)
                    {
                        trueShelfLife = ExpirationCalculator.GetDaysForProduct(null, productId);
                    }

                    int deliveryDay = ExpirationSaveManager.runtimeBoxDeliveryDays.ContainsKey(BoxKey) ? ExpirationSaveManager.runtimeBoxDeliveryDays[BoxKey] : currentDay;
                    int standardExpDay = deliveryDay + trueShelfLife;
                    int expDayToUse = standardExpDay;

                    if (BoxLabelPatch.ClipboardDate != -1)
                    {
                        if (Time.frameCount - BoxLabelPatch.ClipboardFrame <= 15)
                        {
                            expDayToUse = BoxLabelPatch.ClipboardDate;
                        }
                        BoxLabelPatch.ClipboardDate = -1;
                    }

                    while (dates.Count < _box.ProductCount)
                    {
                        dates.Add(expDayToUse);
                        expDayToUse = standardExpDay;
                    }
                }
                else if (dates.Count > _box.ProductCount)
                {
                    while (dates.Count > _box.ProductCount) dates.RemoveAt(dates.Count - 1);
                }
            }

            // ⚡ OPTYMALIZACJA PAMIĘCI: Tworzymy nową listę do zapisu TYLKO, gdy ilość towaru w kartonie lub config uległy zmianie!
            if (countChanged || configChanged || !ExpirationSaveManager.boxDates.ContainsKey(BoxKey))
            {
                ExpirationSaveManager.boxDates[BoxKey] = new List<int>(dates);
            }
        }

        public int GetProductId()
        {
            try
            {
                if (_box != null && _box.Data != null && _box.Data.ProductID > 0) return _box.Data.ProductID;
            }
            catch { }
            return -1;
        }

        private void InitializeDatesFromSave(int boxKey, int productId)
        {
            try
            {
                if (ExpirationSaveManager.runtimeBoxDates.ContainsKey(boxKey)) return;

                if (productId > 0 && ExpirationSaveManager.pendingLoadedBoxes.ContainsKey(productId))
                {
                    var queue = ExpirationSaveManager.pendingLoadedBoxes[productId];
                    if (queue.Count > 0)
                    {
                        var savedData = queue.Dequeue();
                        ExpirationSaveManager.runtimeBoxDates[boxKey] = new List<int>(savedData.Dates);
                        ExpirationSaveManager.runtimeBoxDeliveryDays[boxKey] = savedData.DeliveryDay;
                        ExpirationSaveManager.runtimeBoxDatesFromSave[boxKey] = true;
                        return;
                    }
                }
                else
                {
                    int oldUid = 0;
                    try { if (_box != null && _box.Data != null) oldUid = _box.Data.UID; } catch { }

                    if (oldUid > 0 && ExpirationSaveManager.boxDates.ContainsKey(oldUid))
                    {
                        ExpirationSaveManager.runtimeBoxDates[boxKey] = new List<int>(ExpirationSaveManager.boxDates[oldUid]);
                        if (ExpirationSaveManager.boxDeliveryDays.ContainsKey(oldUid))
                            ExpirationSaveManager.runtimeBoxDeliveryDays[boxKey] = ExpirationSaveManager.boxDeliveryDays[oldUid];

                        ExpirationSaveManager.runtimeBoxDatesFromSave[boxKey] = true;
                        ExpirationSaveManager.boxDates.Remove(oldUid);
                        return;
                    }
                }

                ExpirationSaveManager.runtimeBoxDates[boxKey] = new List<int>();
                ExpirationSaveManager.runtimeBoxDeliveryDays[boxKey] = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;
                ExpirationSaveManager.runtimeBoxDatesFromSave[boxKey] = false;
            }
            catch { }
        }

        void OnDestroy()
        {
            BoxLabelPatch.AllLabels.Remove(this);

            if (BoxKey > 0)
            {
                if (ExpirationSaveManager.boxDates.ContainsKey(BoxKey)) ExpirationSaveManager.boxDates.Remove(BoxKey);
                if (ExpirationSaveManager.runtimeBoxDates.ContainsKey(BoxKey)) ExpirationSaveManager.runtimeBoxDates.Remove(BoxKey);
                if (ExpirationSaveManager.runtimeBoxDeliveryDays.ContainsKey(BoxKey)) ExpirationSaveManager.runtimeBoxDeliveryDays.Remove(BoxKey);
                if (ExpirationSaveManager.runtimeBoxDatesFromSave.ContainsKey(BoxKey)) ExpirationSaveManager.runtimeBoxDatesFromSave.Remove(BoxKey);
            }
        }

        public void SetTextEnabled(bool state)
        {
            if (_textMesh != null && _textMesh.gameObject.activeSelf != state)
                _textMesh.gameObject.SetActive(state);
        }

        public void ProcessLogicUpdate()
        {
            if (!_isInitialized) return;
            int currentDay = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;

            if (_box.ProductCount != _lastProductCount || currentDay != _lastDay)
            {
                _lastProductCount = _box.ProductCount;
                _lastDay = currentDay;
                RefreshLabel();
            }
        }

        private void RefreshLabel()
        {
            if (_box == null || _box.ProductCount <= 0)
            {
                _textMesh.text = $"<color=#000000>{StatisticMod.Plugin.T("Pusty karton", "Empty box")}</color>";
                return;
            }

            int currentDay = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;

            if (ExpirationSaveManager.runtimeBoxDates.TryGetValue(BoxKey, out List<int> dates) && dates.Count > 0)
            {
                int expDay = dates[0];
                int daysLeft = expDay - currentDay;
                int deliveryDay = ExpirationSaveManager.runtimeBoxDeliveryDays.ContainsKey(BoxKey)
                    ? ExpirationSaveManager.runtimeBoxDeliveryDays[BoxKey]
                    : currentDay;

                string color = daysLeft <= 0 ? "#FF0000" : (daysLeft == 1 ? "#FFA500" : "#00FF00");
                string textDostawa = StatisticMod.Plugin.T("Dostawa:", "Delivery:");
                string textTermin = StatisticMod.Plugin.T("Termin:", "Expiry:");
                string textZapas = StatisticMod.Plugin.T("Zapas:", "Stock:");
                string textSzt = StatisticMod.Plugin.T("szt.", "pcs.");

                _textMesh.text =
                    $"<color=#C0C0C0>{textDostawa} {deliveryDay}</color> | <color={color}>{textTermin} {expDay}</color>\n" +
                    $"<size=80%><color=#C0C0C0>{textZapas} {_box.ProductCount} {textSzt}</color></size>";
            }
        }
    }
}