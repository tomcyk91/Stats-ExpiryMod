#nullable disable

using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;

namespace SmartExpiration
{
    public static class LabelExclamationOverlay
    {
        private static float _refreshTimer = 0f;
        private static Sprite _iconSprite;
        private static DisplaySlot[] _cachedSlots = new DisplaySlot[0];
        private static float _cacheTimer = 0f;
        // PERF: dirty-check po liczbie dzieci slotu - pomijamy ciezki skan GetComponents na polkach bez zmian.
        private static readonly Dictionary<int, int> _slotChildCounts = new Dictionary<int, int>();
        private static int _lastScanDay = -1;

        public static List<Transform> ActiveMarkers = new List<Transform>();
        // PERF: cache LineRenderer per marker - wczesniej GetComponent<LineRenderer> lecial CO KLATKE na kazdym markerze.
        private static readonly Dictionary<Transform, LineRenderer> _markerLineRenderers = new Dictionary<Transform, LineRenderer>();
        private static float _animTime = 0f;

        private static Sprite GetEmbeddedIcon()
        {
            if (_iconSprite != null) return _iconSprite;
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string resourceName = assembly.GetManifestResourceNames().FirstOrDefault(r => r.EndsWith("icon.png", StringComparison.OrdinalIgnoreCase));
                if (string.IsNullOrEmpty(resourceName)) return null;

                using var stream = assembly.GetManifestResourceStream(resourceName);
                byte[] ba = new byte[stream.Length];
                stream.Read(ba, 0, ba.Length);
                Texture2D tex = new Texture2D(2, 2);
                ImageConversion.LoadImage(tex, ba);
                _iconSprite = Sprite.Create(tex, new Rect(0, 0, tex.width, tex.height), new Vector2(0.5f, 0.5f), 100f);
                return _iconSprite;
            }
            catch { return null; }
        }

        private static void AddExclamation(GameObject parent, DisplaySlot slot)
        {
            try
            {
                var root = new GameObject("ExpiryExclamation");
                root.transform.SetParent(parent.transform, false);

                root.transform.localPosition = new Vector3(0.01f, 0.005f, -0.12f);
                root.transform.localScale = Vector3.one * 0.008f;
                root.transform.localRotation = Quaternion.Euler(0f, 270f, 0f);

                var renderer = root.AddComponent<SpriteRenderer>();
                renderer.sprite = GetEmbeddedIcon();
                if (renderer.sprite == null) renderer.color = Color.red;

                var shader = Shader.Find("Sprites/Default");
                if (shader != null)
                {
                    var mat = new Material(shader);
                    mat.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                    renderer.material = mat;
                }

                Shader lineShader = Shader.Find("UI/Default") ?? Shader.Find("Sprites/Default");
                if (lineShader == null) lineShader = Shader.Find("Universal Render Pipeline/Unlit");

                Material lineMat = new Material(lineShader);
                lineMat.renderQueue = 4000;
                lineMat.SetInt("_ZTest", 8);
                lineMat.SetInt("_ZWrite", 0);
                lineMat.EnableKeyword("_EMISSION");

                LineRenderer lineRenderer = root.AddComponent<LineRenderer>();
                lineRenderer.alignment = LineAlignment.Local;
                lineRenderer.useWorldSpace = false;
                lineRenderer.positionCount = 4;
                lineRenderer.material = lineMat;

                float sizeX = 1f;
                float sizeY = 1f;

                if (renderer.sprite != null)
                {
                    sizeX = renderer.sprite.bounds.extents.x * 1.1f;
                    sizeY = renderer.sprite.bounds.extents.y * 1.1f;
                }

                float lineWidth = Mathf.Max(sizeX, sizeY) * 0.1f;
                lineRenderer.startWidth = lineWidth;
                lineRenderer.endWidth = lineWidth;

                float bottomY = -sizeY * 1f;
                float bottomWidth = sizeX * 0.7f;

                lineRenderer.SetPosition(0, new Vector3(-bottomWidth, bottomY, 0f));
                lineRenderer.SetPosition(1, new Vector3(0f, sizeY, 0f));
                lineRenderer.SetPosition(2, new Vector3(bottomWidth, bottomY, 0f));
                lineRenderer.SetPosition(3, new Vector3(-bottomWidth, bottomY, 0f));

                ActiveMarkers.Add(root.transform);
            }
            catch { }
        }

        public static void RefreshAll()
        {
            _refreshTimer -= Time.deltaTime;
            if (_refreshTimer > 0) return;
            _refreshTimer = 2.0f;
            long __pf = SEProfiler.Begin();
            try {

            // PERF: wspoldzielony cache zamiast wlasnego skanu sceny.
            _cachedSlots = SceneSlotCache.GetSlots();

            int currentDay = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;
            bool showTriangles = PluginConfig.ShowWarningTriangles != null && PluginConfig.ShowWarningTriangles.Value;

            // PERF/poprawnosc: przy zmianie dnia ExpirationDay-currentDay maleje, wiec polka moze stac sie
            // krytyczna bez zmiany liczby dzieci. Zerujemy dirty-cache raz na dzien, by przeskanowac wszystko.
            if (currentDay != _lastScanDay)
            {
                _lastScanDay = currentDay;
                _slotChildCounts.Clear();
            }

            // Gdy triangles wylaczone - nie skanujemy w ogole produktow; sciagamy istniejace markery i konczymy.
            foreach (var slot in _cachedSlots)
            {
                if (slot == null || !slot.HasProduct) continue;

                int instanceId = slot.GetInstanceID();
                int currentChildren = slot.transform.childCount;

                // PERF: jezeli liczba produktow na polce sie nie zmienila od ostatniego razu,
                // stan krytycznosci tez sie nie zmienil -> pomijamy caly ciezki skan GetComponents.
                // (Uplyw dni obslugiwany osobno: cache czyscimy raz dziennie nizej.)
                int prevChildren;
                bool unchanged = _slotChildCounts.TryGetValue(instanceId, out prevChildren) && prevChildren == currentChildren;
                if (unchanged) continue;
                _slotChildCounts[instanceId] = currentChildren;

                var labelComponent = slot.GetComponentInChildren<Label>();
                if (labelComponent == null) continue;

                Transform anchor = labelComponent.transform;
                var marker = anchor.Find("ExpiryExclamation");

                bool isCritical = false;

                if (showTriangles)
                {
                    var products = slot.GetComponentsInChildren<global::Product>();
                    for (int i = 0; i < products.Count; i++)
                    {
                        var comp = products[i].GetComponent<ProductExpirationComponent>();
                        if (comp != null && comp.ExpirationDay - currentDay <= 0)
                        {
                            isCritical = true;
                            break;
                        }
                    }
                }

                if (isCritical)
                {
                    if (marker == null) AddExclamation(anchor.gameObject, slot);
                }
                else if (marker != null)
                {
                    ActiveMarkers.Remove(marker);
                    _markerLineRenderers.Remove(marker);
                    UnityEngine.Object.Destroy(marker.gameObject);
                }
            }
            } finally { SEProfiler.End("RefreshAll", __pf); }
        }

        public static void AnimateMarkers()
        {
            if (ActiveMarkers.Count == 0) return;

            _animTime += Time.deltaTime * 6f;

            float scaleMult = 1.0f + (Mathf.Sin(_animTime) * 0.2f);
            Vector3 targetScale = (Vector3.one * 0.008f) * scaleMult;

            float glowPower = 5f;
            float intensity = glowPower + Mathf.Sin(_animTime) * (glowPower * 0.3f);
            float alpha = 0.8f + Mathf.Sin(_animTime) * 0.2f;

            Color baseColor = Color.red;
            Color pulseColor = new Color(baseColor.r * intensity, baseColor.g * intensity, baseColor.b * intensity, alpha);

            for (int i = ActiveMarkers.Count - 1; i >= 0; i--)
            {
                var marker = ActiveMarkers[i];
                if (marker == null)
                {
                    ActiveMarkers.RemoveAt(i);
                    continue;
                }

                marker.localScale = targetScale;

                // PERF: pobierz LineRenderer raz i trzymaj w cache zamiast GetComponent co klatke.
                LineRenderer lr;
                if (!_markerLineRenderers.TryGetValue(marker, out lr))
                {
                    lr = marker.GetComponent<LineRenderer>();
                    _markerLineRenderers[marker] = lr; // cache'ujemy tez null
                }

                if (lr != null && lr.material != null)
                {
                    lr.startColor = pulseColor;
                    lr.endColor = pulseColor;
                    lr.material.color = pulseColor;
                }
            }
        }
    }

    public class ExpirationEngine : MonoBehaviour
    {
        public ExpirationEngine(System.IntPtr ptr) : base(ptr) { }

        private static Queue<DisplaySlot> _syncQueue = new Queue<DisplaySlot>();

        public static void StartBackgroundSync()
        {
            _syncQueue.Clear();
            var allSlots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
            foreach (var slot in allSlots)
            {
                if (slot != null && slot.HasProduct) _syncQueue.Enqueue(slot);
            }
        }

        private void Update()
        {
            // ⚡ TARCZA ANTY-CRASHOWA: Zabezpieczenie pętli Update przed ubijaniem klatek przez błędy
            long __ef = SmartExpiration.SEProfiler.Begin();
            try
            {
                if (_syncQueue.Count > 0)
                {
                    int processLimit = 5;
                    while (_syncQueue.Count > 0 && processLimit > 0)
                    {
                        var slot = _syncQueue.Dequeue();
                        if (slot != null && slot.HasProduct) ExpirationManager.SyncShelf(slot);
                        processLimit--;
                    }
                }

                LabelExclamationOverlay.RefreshAll();
                long __am = SmartExpiration.SEProfiler.Begin();
                LabelExclamationOverlay.AnimateMarkers();
                SmartExpiration.SEProfiler.End("AnimateMarkers", __am);

                SmartExpiration.Patches.RestockerScanner.Process();
            }
            catch { /* Cicha ignorancja błędów = stałe FPSy */ }
            finally { SmartExpiration.SEProfiler.End("EngineUpdate", __ef); }
        }
    }
}