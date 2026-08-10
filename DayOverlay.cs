using UnityEngine;
using TMPro;
using UnityEngine.UI;

namespace StatisticMod
{
    public class GameDayOverlay : MonoBehaviour
    {
        private static bool _enabled = false;
        private static GameObject _overlayGO;
        private static TextMeshProUGUI _dayText;
        private TextMeshProUGUI _text;

        public static void Create()
        {
            if (!_enabled) return;
            if (_overlayGO != null) return;

            try
            {
                _overlayGO = new GameObject("DayOverlay_Canvas");
                UnityEngine.Object.DontDestroyOnLoad(_overlayGO);

                var canvas = _overlayGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvas.sortingOrder = 99999;

                var scaler = _overlayGO.AddComponent<CanvasScaler>();
                scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                scaler.referenceResolution = new Vector2(1920, 1080);

                var textGO = new GameObject("DayTextDisplay");
                textGO.transform.SetParent(_overlayGO.transform, false);

                _dayText = textGO.AddComponent<TextMeshProUGUI>();
                _dayText.text = Plugin.DayLabel(-1);
                _dayText.fontSize = 35;

                var rt = _dayText.GetComponent<RectTransform>();
                rt.anchorMin = new Vector2(1, 1);
                rt.anchorMax = new Vector2(1, 1);
                rt.pivot = new Vector2(1, 1);
                rt.anchoredPosition = new Vector2(-20, -20);
                rt.sizeDelta = new Vector2(300, 50);

                _dayText.alignment = TextAlignmentOptions.TopRight;
                _dayText.color = new Color(1, 1, 1, 0.8f);
                _overlayGO.AddComponent<GameDayOverlay>();
            }
            catch { }
        }

        private float _timer = 0;
        private float _fontTimer = 0;
        private bool _fontLoaded = false;
        private int _fontRetries = 0;

        void Start()
        {
            _text = GetComponentInChildren<TextMeshProUGUI>();
        }

        void Update()
        {
            if (!_fontLoaded && _text != null && _fontRetries < 3)
            {
                _fontTimer += Time.deltaTime;
                if (_fontTimer >= 3.0f)
                {
                    _fontTimer = 0f;
                    _fontRetries++;

                    if (TMP_Settings.defaultFontAsset != null)
                    {
                        _text.font = TMP_Settings.defaultFontAsset;
                        _fontLoaded = true;
                    }
                    else
                    {
                        var anyText = UnityEngine.Object.FindFirstObjectByType<TextMeshProUGUI>();
                        if (anyText != null && anyText.font != null)
                        {
                            _text.font = anyText.font;
                            _fontLoaded = true;
                        }
                    }
                }
            }

            _timer += Time.deltaTime;
            if (_timer >= 2.0f)
            {
                _timer = 0;
                if (DayCycleManager.Instance != null && _text != null)
                    _text.text = Plugin.DayLabel(DayCycleManager.Instance.CurrentDay);
            }
        }
    }
}
