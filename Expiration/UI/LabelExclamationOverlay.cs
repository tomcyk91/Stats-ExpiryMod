#nullable disable

using UnityEngine;
using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using Il2CppInterop.Runtime.InteropTypes.Arrays;

namespace SmartExpiration
{
    /// <summary>
    /// Zdarzeniowy system trójkątów ostrzegawczych bez skanowania w trakcie dnia.
    ///
    /// Pełny przebieg półek wykonywany jest wyłącznie:
    /// 1) po zakończeniu jednego budżetowanego przebiegu startowego,
    /// 2) po zmianie numeru dnia,
    /// 3) po ręcznym RequestFullRefresh().
    ///
    /// W trakcie dnia aktualizowane są tylko konkretne sloty zmienione przez
    /// DisplaySlot.AddProduct / TakeProductFromDisplay albo operacje koszyka.
    /// Nie ma już okresowego skanu bezpieczeństwa ani odświeżania cache co 2 s.
    /// </summary>
    public static class LabelExclamationOverlay
    {
        private sealed class SlotVisualState
        {
            public DisplaySlot Slot;
            public Transform Anchor;
            public Transform Marker;
            public bool MarkerLookupDone;
        }

        private static Sprite _iconSprite;
        private static Material _sharedSpriteMaterial;
        private static Material _sharedLineMaterial;

        private static readonly Queue<DisplaySlot> _dirtySlots = new Queue<DisplaySlot>();
        private static readonly HashSet<int> _queuedSlotIds = new HashSet<int>();
        private static readonly Dictionary<int, SlotVisualState> _slotStates = new Dictionary<int, SlotVisualState>();

        private static DisplaySlot[] _cachedSlots = new DisplaySlot[0];
        private static int _lastDay = -1;
        private static bool _saveWasLoaded = false;
        private static bool _lastShowTriangles = true;
        private static bool _fullRefreshRequested = false;

        private static float _nextAnimationTick = 0f;
        private static float _animTime = 0f;

        // Maksymalny czas pracy kolejki markerów w jednej klatce.
        // Dzięki temu nawet duży sklep nie powinien dostać jednorazowego hitcha.
        private const double DirtyQueueBudgetMs = 0.25;
        private const int MaxDirtySlotsPerFrame = 4;

        // Animacja nie musi działać 60-120 razy na sekundę.
        private const float AnimationInterval = 0.08f;

        public static List<Transform> ActiveMarkers = new List<Transform>();

        private static Sprite GetEmbeddedIcon()
        {
            if (_iconSprite != null) return _iconSprite;

            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();
                string resourceName = assembly.GetManifestResourceNames()
                    .FirstOrDefault(r => r.EndsWith("icon.png", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrEmpty(resourceName)) return null;

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream == null) return null;

                byte[] bytes = new byte[stream.Length];
                stream.Read(bytes, 0, bytes.Length);

                Texture2D tex = new Texture2D(2, 2);
                tex.hideFlags = HideFlags.DontUnloadUnusedAsset;
                ImageConversion.LoadImage(tex, (Il2CppStructArray<byte>)bytes);

                _iconSprite = Sprite.Create(
                    tex,
                    new Rect(0, 0, tex.width, tex.height),
                    new Vector2(0.5f, 0.5f),
                    100f);

                _iconSprite.hideFlags = HideFlags.HideAndDontSave;
                return _iconSprite;
            }
            catch
            {
                return null;
            }
        }

        private static Material GetSharedSpriteMaterial()
        {
            if (_sharedSpriteMaterial != null) return _sharedSpriteMaterial;

            try
            {
                Shader shader = Shader.Find("Sprites/Default");
                if (shader == null) return null;

                _sharedSpriteMaterial = new Material(shader);
                _sharedSpriteMaterial.name = "ExpiryWarning_SharedSpriteMaterial";
                _sharedSpriteMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _sharedSpriteMaterial.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
                _sharedSpriteMaterial.SetInt("_ZWrite", 0);
                _sharedSpriteMaterial.renderQueue = 4000;
            }
            catch
            {
                _sharedSpriteMaterial = null;
            }

            return _sharedSpriteMaterial;
        }

        private static Material GetSharedLineMaterial()
        {
            if (_sharedLineMaterial != null) return _sharedLineMaterial;

            try
            {
                Shader shader = Shader.Find("UI/Default");
                if (shader == null) shader = Shader.Find("Sprites/Default");
                if (shader == null) shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) return null;

                _sharedLineMaterial = new Material(shader);
                _sharedLineMaterial.name = "ExpiryWarning_SharedLineMaterial";
                _sharedLineMaterial.hideFlags = HideFlags.DontUnloadUnusedAsset;
                _sharedLineMaterial.renderQueue = 4000;
                _sharedLineMaterial.SetInt("_ZTest", 8);
                _sharedLineMaterial.SetInt("_ZWrite", 0);
                _sharedLineMaterial.color = Color.red;
            }
            catch
            {
                _sharedLineMaterial = null;
            }

            return _sharedLineMaterial;
        }

        private static Transform AddExclamation(Transform anchor)
        {
            if (anchor == null) return null;

            try
            {
                GameObject root = new GameObject("ExpiryExclamation");
                root.hideFlags = HideFlags.HideAndDontSave;
                root.transform.SetParent(anchor, false);
                root.transform.localPosition = new Vector3(0.01f, 0.005f, -0.12f);
                root.transform.localScale = Vector3.one * 0.008f;
                root.transform.localRotation = Quaternion.Euler(0f, 270f, 0f);

                SpriteRenderer renderer = root.AddComponent<SpriteRenderer>();
                renderer.sprite = GetEmbeddedIcon();
                renderer.color = renderer.sprite != null ? Color.white : Color.red;

                Material spriteMaterial = GetSharedSpriteMaterial();
                if (spriteMaterial != null)
                    renderer.sharedMaterial = spriteMaterial;

                LineRenderer lineRenderer = root.AddComponent<LineRenderer>();
                lineRenderer.alignment = LineAlignment.Local;
                lineRenderer.useWorldSpace = false;
                lineRenderer.positionCount = 4;
                lineRenderer.startColor = Color.red;
                lineRenderer.endColor = Color.red;

                Material lineMaterial = GetSharedLineMaterial();
                if (lineMaterial != null)
                    lineRenderer.sharedMaterial = lineMaterial;

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

                float bottomY = -sizeY;
                float bottomWidth = sizeX * 0.7f;

                lineRenderer.SetPosition(0, new Vector3(-bottomWidth, bottomY, 0f));
                lineRenderer.SetPosition(1, new Vector3(0f, sizeY, 0f));
                lineRenderer.SetPosition(2, new Vector3(bottomWidth, bottomY, 0f));
                lineRenderer.SetPosition(3, new Vector3(-bottomWidth, bottomY, 0f));

                ActiveMarkers.Add(root.transform);
                SEProfiler.MarkerCount++;
                return root.transform;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Oznacza pojedynczy slot jako wymagający ponownego sprawdzenia.
        /// Wywołanie jest tanie i de-duplikowane.
        /// </summary>
        public static void QueueSlot(DisplaySlot slot)
        {
            if (slot == null) return;

            try
            {
                int id = slot.GetInstanceID();
                if (_queuedSlotIds.Add(id))
                    _dirtySlots.Enqueue(slot);
            }
            catch { }
        }

        private static void QueueAllSlots(bool clearExistingQueue)
        {
            if (clearExistingQueue)
            {
                _dirtySlots.Clear();
                _queuedSlotIds.Clear();
            }

            DisplaySlot[] slots = _cachedSlots;
            if (slots == null) return;

            for (int i = 0; i < slots.Length; i++)
            {
                DisplaySlot slot = slots[i];
                if (slot != null) QueueSlot(slot);
            }
        }

        private static void RefreshCachedSlots(bool forceInvalidate)
        {
            try
            {
                if (forceInvalidate)
                    SceneSlotCache.InvalidateSlots();

                DisplaySlot[] newSlots = SceneSlotCache.GetSlots();
                if (newSlots == null) newSlots = new DisplaySlot[0];

                bool changed = !ReferenceEquals(newSlots, _cachedSlots) ||
                               newSlots.Length != (_cachedSlots != null ? _cachedSlots.Length : 0);

                _cachedSlots = newSlots;

                if (changed || forceInvalidate)
                    QueueAllSlots(forceInvalidate);
            }
            catch { }
        }

        public static void RefreshAll()
        {
            long profilerStart = SEProfiler.Begin();

            try
            {
                bool saveLoaded = ExpirationSaveManager.SaveLoaded;

                if (!saveLoaded)
                {
                    if (_saveWasLoaded)
                        ResetRuntimeState(true);

                    _saveWasLoaded = false;
                    return;
                }

                // During load the ExpirationLoadFinalizer owns the only full startup pass.
                // Marker work starts only after that pass, reusing its cached slot snapshot.
                if (!ExpirationLoadFinalizer.InitialSyncComplete)
                    return;

                int currentDay = 1;
                try
                {
                    var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                    currentDay = dcm != null ? dcm.CurrentDay : 1;
                }
                catch { }

                if (!_saveWasLoaded)
                {
                    _saveWasLoaded = true;
                    _lastDay = currentDay;
                    _fullRefreshRequested = false;

                    // No invalidation here: reuse the exact snapshot created by startup sync.
                    RefreshCachedSlots(false);
                }

                bool showTriangles = PluginConfig.ShowWarningTriangles != null &&
                                     PluginConfig.ShowWarningTriangles.Value;

                if (!showTriangles)
                {
                    if (_lastShowTriangles || ActiveMarkers.Count > 0)
                        RemoveAllMarkers();

                    _lastShowTriangles = false;
                    return;
                }

                if (!_lastShowTriangles)
                {
                    _lastShowTriangles = true;
                    _fullRefreshRequested = true;
                }

                if (currentDay != _lastDay)
                {
                    _lastDay = currentDay;
                    _fullRefreshRequested = true;
                }

                if (_fullRefreshRequested)
                {
                    _fullRefreshRequested = false;
                    RefreshCachedSlots(true);
                }

                ProcessDirtyQueue(currentDay);
            }
            finally
            {
                SEProfiler.End("RefreshAll_EventOnly", profilerStart);
            }
        }

        private static void ProcessDirtyQueue(int currentDay)
        {
            if (_dirtySlots.Count == 0) return;

            long startTicks = System.Diagnostics.Stopwatch.GetTimestamp();
            double tickToMs = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            int processed = 0;

            while (_dirtySlots.Count > 0 && processed < MaxDirtySlotsPerFrame)
            {
                DisplaySlot slot = _dirtySlots.Dequeue();

                if (slot != null)
                {
                    try { _queuedSlotIds.Remove(slot.GetInstanceID()); } catch { }
                    RefreshSlotInternal(slot, currentDay, true);
                }

                processed++;

                double elapsedMs =
                    (System.Diagnostics.Stopwatch.GetTimestamp() - startTicks) * tickToMs;

                if (elapsedMs >= DirtyQueueBudgetMs)
                    break;
            }
        }

        // Wywoływane bezpośrednio po operacji koszyka. Jedno odświeżenie
        // pojedynczego slotu jest tanie i daje natychmiastową reakcję UI.
        public static void RefreshSlotNow(DisplaySlot slot)
        {
            if (slot == null) return;

            try
            {
                int currentDay = 1;
                var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
                if (dcm != null) currentDay = dcm.CurrentDay;

                bool showTriangles = PluginConfig.ShowWarningTriangles != null &&
                                     PluginConfig.ShowWarningTriangles.Value;

                RefreshSlotInternal(slot, currentDay, showTriangles);

                try { _queuedSlotIds.Remove(slot.GetInstanceID()); } catch { }
            }
            catch { }
        }

        public static void RequestFullRefresh()
        {
            // Tylko ustawia flagę. Cięższa praca zostanie rozłożona przez kolejkę
            // w Update ExpirationEngine, bez wykonywania jej wewnątrz patcha.
            _fullRefreshRequested = true;
        }

        private static SlotVisualState GetOrCreateState(DisplaySlot slot)
        {
            int id = slot.GetInstanceID();
            SlotVisualState state;

            if (!_slotStates.TryGetValue(id, out state) || state == null)
            {
                state = new SlotVisualState();
                _slotStates[id] = state;
            }

            state.Slot = slot;
            return state;
        }

        private static Transform ResolveAnchor(SlotVisualState state, DisplaySlot slot)
        {
            if (state.Anchor != null) return state.Anchor;

            // DisplaySlot exposes its native Label directly. Avoid recursive hierarchy scans
            // during startup when hundreds of slots are initialized.
            try
            {
                Label label = slot != null ? slot.Label : null;
                if (label != null) state.Anchor = label.transform;
            }
            catch { }

            return state.Anchor;
        }

        private static void RefreshSlotInternal(DisplaySlot slot, int currentDay, bool showTriangles)
        {
            if (slot == null) return;

            try
            {
                SlotVisualState state = GetOrCreateState(slot);

                Transform anchor = ResolveAnchor(state, slot);
                if (anchor == null) return;

                if (!state.MarkerLookupDone)
                {
                    state.MarkerLookupDone = true;
                    try { state.Marker = anchor.Find("ExpiryExclamation"); } catch { }

                    if (state.Marker != null && !ActiveMarkers.Contains(state.Marker))
                        ActiveMarkers.Add(state.Marker);
                }

                bool hasProduct = false;
                try { hasProduct = slot.HasProduct; } catch { }

                if (!showTriangles || !hasProduct)
                {
                    RemoveMarker(state);
                    return;
                }

                // PERF: no recursive GetComponentsInChildren allocation here. The native
                // DisplaySlot.m_Products list is authoritative and can be checked directly.
                bool isCritical = ExpirationManager.HasExpiredProduct(slot, currentDay);

                if (isCritical)
                {
                    if (state.Marker == null)
                        state.Marker = AddExclamation(anchor);
                }
                else
                {
                    RemoveMarker(state);
                }
            }
            catch { }
        }

        private static void RemoveMarker(SlotVisualState state)
        {
            if (state == null || state.Marker == null) return;

            Transform marker = state.Marker;
            state.Marker = null;

            ActiveMarkers.Remove(marker);

            try { UnityEngine.Object.Destroy(marker.gameObject); } catch { }

            if (SEProfiler.MarkerCount > 0)
                SEProfiler.MarkerCount--;
        }

        private static void RemoveAllMarkers()
        {
            for (int i = ActiveMarkers.Count - 1; i >= 0; i--)
            {
                Transform marker = ActiveMarkers[i];
                if (marker != null)
                {
                    try { UnityEngine.Object.Destroy(marker.gameObject); } catch { }
                }
            }

            ActiveMarkers.Clear();

            foreach (KeyValuePair<int, SlotVisualState> entry in _slotStates)
            {
                if (entry.Value != null)
                    entry.Value.Marker = null;
            }

            SEProfiler.MarkerCount = 0;
        }

        private static void ResetRuntimeState(bool removeMarkers)
        {
            if (removeMarkers) RemoveAllMarkers();

            _dirtySlots.Clear();
            _queuedSlotIds.Clear();
            _slotStates.Clear();
            _cachedSlots = new DisplaySlot[0];
            _lastDay = -1;
            _fullRefreshRequested = false;
        }

        public static void AnimateMarkers()
        {
            if (ActiveMarkers.Count == 0) return;

            float now = Time.realtimeSinceStartup;
            if (now < _nextAnimationTick) return;
            _nextAnimationTick = now + AnimationInterval;

            _animTime += AnimationInterval * 6f;
            float scaleMultiplier = 1.0f + Mathf.Sin(_animTime) * 0.2f;
            Vector3 targetScale = Vector3.one * (0.008f * scaleMultiplier);

            for (int i = ActiveMarkers.Count - 1; i >= 0; i--)
            {
                Transform marker = ActiveMarkers[i];
                if (marker == null)
                {
                    ActiveMarkers.RemoveAt(i);
                    continue;
                }

                marker.localScale = targetScale;
            }
        }
    }

    public class ExpirationEngine : MonoBehaviour
    {
        public ExpirationEngine(System.IntPtr ptr) : base(ptr) { }

        private static readonly Queue<DisplaySlot> _syncQueue = new Queue<DisplaySlot>();

        public static void StartBackgroundSync()
        {
            _syncQueue.Clear();

            // Wspólny cache zamiast kolejnego pełnego FindObjectsOfType.
            DisplaySlot[] allSlots = SceneSlotCache.GetSlots();
            if (allSlots == null) return;

            for (int i = 0; i < allSlots.Length; i++)
            {
                DisplaySlot slot = allSlots[i];
                if (slot != null && slot.HasProduct)
                    _syncQueue.Enqueue(slot);
            }
        }

        private void Update()
        {
            long profilerStart = SmartExpiration.SEProfiler.Begin();

            try
            {
                if (_syncQueue.Count > 0)
                {
                    // Mniejsza porcja niż wcześniej; marker jest kolejkowany dopiero
                    // po zakończeniu synchronizacji konkretnego slotu.
                    int processLimit = 3;

                    while (_syncQueue.Count > 0 && processLimit > 0)
                    {
                        DisplaySlot slot = _syncQueue.Dequeue();

                        if (slot != null && slot.HasProduct)
                        {
                            ExpirationManager.SyncShelf(slot);
                            LabelExclamationOverlay.QueueSlot(slot);
                        }

                        processLimit--;
                    }
                }

                LabelExclamationOverlay.RefreshAll();
                LabelExclamationOverlay.AnimateMarkers();

                // Event patches on DisplaySlot.AddProduct/TakeProductFromDisplay already
                // keep expiration state current. No 1.5-second shelf safety scan here.
            }
            catch { }
            finally
            {
                SmartExpiration.SEProfiler.End("EngineUpdate", profilerStart);
            }
        }
    }
}

namespace SmartExpiration.Patches
{
    /// <summary>
    /// Lekki postfix dla natywnych zmian półki. Nie skanuje slotu w samym patchu;
    /// tylko dodaje go do de-duplikowanej kolejki na następną klatkę.
    /// </summary>
    public static class ExpirationMarkerSlotChangedPatch
    {
        public static void Postfix(DisplaySlot __instance)
        {
            SmartExpiration.LabelExclamationOverlay.QueueSlot(__instance);
        }
    }
}
