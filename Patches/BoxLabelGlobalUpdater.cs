using System;
using System.Collections.Generic;
using System.Diagnostics;
using UnityEngine;

namespace SmartExpiration.Patches
{
    /// <summary>
    /// One lightweight updater for box expiration state and the held-box label.
    /// Box state is initialized under a tiny per-frame budget; visual objects are
    /// created only for the currently held box.
    /// </summary>
    public class BoxLabelGlobalUpdater : MonoBehaviour
    {
        public BoxLabelGlobalUpdater(IntPtr ptr) : base(ptr) { }

        private static readonly Queue<BoxExpirationLabel> _pendingInit = new Queue<BoxExpirationLabel>();
        private static readonly HashSet<int> _pendingIds = new HashSet<int>();
        private const double InitBudgetMsPerFrame = 0.15;
        private const int MaxInitBoxesPerFrame = 2;

        private float _tickTimer;
        private static Transform _playerTf;
        private static global::BoxInteraction _playerBoxInteraction;
        private static float _playerRefreshTimer;
        private BoxExpirationLabel _lastHeldLabel;

        public static Transform PlayerTransform => _playerTf;

        public static void QueueInitialization(BoxExpirationLabel label)
        {
            if (label == null) return;
            try
            {
                int id = label.GetInstanceID();
                if (_pendingIds.Add(id)) _pendingInit.Enqueue(label);
            }
            catch { }
        }

        private static void ProcessInitializationBudget()
        {
            if (!ExpirationSaveManager.SaveLoaded || _pendingInit.Count == 0) return;

            long started = Stopwatch.GetTimestamp();
            double tickToMs = 1000.0 / Stopwatch.Frequency;
            int processed = 0;

            while (_pendingInit.Count > 0 && processed < MaxInitBoxesPerFrame)
            {
                BoxExpirationLabel label = _pendingInit.Dequeue();
                if (label != null)
                {
                    try { _pendingIds.Remove(label.GetInstanceID()); } catch { }
                    try { label.ProcessRuntimeUpdate(); } catch { }
                }

                processed++;
                double elapsed = (Stopwatch.GetTimestamp() - started) * tickToMs;
                if (elapsed >= InitBudgetMsPerFrame) break;
            }
        }

        private static void RefreshPlayerCache()
        {
            _playerRefreshTimer -= Time.deltaTime;
            if (_playerTf != null && _playerRefreshTimer > 0f) return;

            _playerRefreshTimer = 1.0f;
            _playerBoxInteraction = null;

            try
            {
                var lp = PlayerManager.Instance != null ? PlayerManager.Instance.LocalPlayer : null;
                if (lp != null)
                {
                    _playerTf = lp.transform;
                    _playerBoxInteraction = lp.GetComponent<global::BoxInteraction>();
                    return;
                }
            }
            catch { }

            try
            {
                var cam = Camera.main;
                if (cam != null) _playerTf = cam.transform;
            }
            catch { }
        }

        private static BoxExpirationLabel GetHeldLabel()
        {
            try
            {
                if (_playerBoxInteraction == null && _playerTf != null)
                    _playerBoxInteraction = _playerTf.GetComponent<global::BoxInteraction>();

                var heldBox = _playerBoxInteraction != null ? _playerBoxInteraction.m_Box : null;
                if (heldBox == null) return null;

                return heldBox.GetComponent<BoxExpirationLabel>();
            }
            catch { return null; }
        }

        void Update()
        {
            // This work is intentionally independent from the visual 4 Hz tick.
            // It completes loaded box state quickly without creating a startup spike.
            ProcessInitializationBudget();

            _tickTimer += Time.deltaTime;
            if (_tickTimer < 0.25f) return;
            _tickTimer = 0f;

            long profilerStart = SmartExpiration.SEProfiler.Begin();
            try
            {
                RefreshPlayerCache();

                bool showDates = PluginConfig.ShowDatesOnBoxes != null &&
                                 PluginConfig.ShowDatesOnBoxes.Value;

                BoxExpirationLabel heldLabel = GetHeldLabel();
                BoxLabelPatch.HeldBoxLabel = heldLabel;

                if (_lastHeldLabel != null && _lastHeldLabel != heldLabel)
                    _lastHeldLabel.SetTextEnabled(false);

                _lastHeldLabel = heldLabel;
                if (heldLabel == null) return;

                // Held box gets priority even if its background initialization has not
                // reached it yet.
                heldLabel.ProcessRuntimeUpdate();
                heldLabel.SetTextEnabled(showDates);
                if (showDates) heldLabel.ProcessLogicUpdate();
            }
            finally
            {
                SmartExpiration.SEProfiler.End("BoxLabels_HeldOnly", profilerStart);
            }
        }
    }
}
