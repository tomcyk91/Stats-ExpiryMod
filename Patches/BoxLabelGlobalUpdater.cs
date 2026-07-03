using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SmartExpiration.Patches
{
    public class BoxLabelGlobalUpdater : MonoBehaviour
    {
        public BoxLabelGlobalUpdater(IntPtr ptr) : base(ptr) { }

        private float _tickTimer = 0f;
        private int _scanCursor = 0;

        // PERF: zamiast petli po wszystkich kartonach co 0.2s skanujemy mala porcje.
        // Trzymany karton jest wykrywany bezposrednio z BoxInteraction, wiec nie czeka na batch.
        private const int LabelsPerTick = 96;

        // PERF: cache transformu gracza - odswiezany rzadko, bez skanu sceny co tick.
        private static Transform _playerTf;
        private static global::BoxInteraction _playerBoxInteraction;
        private static float _playerRefreshTimer = 0f;

        // Poza tym promieniem etykieta kartonu jest zbedna (gracz jej nie widzi/nie trzyma).
        // Wylaczamy wtedy jej TextMeshPro -> 0 draw calls i 0 pracy CPU dla dalekich kartonow.
        private const float CullDistance = 9f;
        private static readonly float CullDistanceSqr = CullDistance * CullDistance;

        // Udostepnione dla BoxExpirationLabel - wspoldzielony cache, bez dodatkowego skanu.
        public static Transform PlayerTransform => _playerTf;

        private static Transform GetPlayer()
        {
            _playerRefreshTimer -= Time.deltaTime;
            if (_playerTf != null && _playerRefreshTimer > 0f) return _playerTf;
            _playerRefreshTimer = 1.0f;
            _playerBoxInteraction = null;

            try
            {
                var lp = PlayerManager.Instance != null ? PlayerManager.Instance.LocalPlayer : null;
                if (lp != null)
                {
                    _playerTf = lp.transform;
                    _playerBoxInteraction = lp.GetComponent<global::BoxInteraction>();
                    return _playerTf;
                }
            }
            catch { }

            // FALLBACK: kamera gracza zawsze istnieje - uzywamy jej jako pozycji odniesienia.
            try
            {
                var cam = Camera.main;
                if (cam != null) _playerTf = cam.transform;
            }
            catch { }
            return _playerTf;
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
            // Sprawdzamy etykiety 5 razy na sekunde, ale tylko w porcjach.
            _tickTimer += Time.deltaTime;
            if (_tickTimer < 0.2f) return;
            _tickTimer = 0f;

            long __pf = SmartExpiration.SEProfiler.Begin();
            try
            {
                int total = BoxLabelPatch.AllLabels.Count;
                if (total == 0)
                {
                    BoxLabelPatch.HeldBoxLabel = null;
                    return;
                }

                bool showDates = (PluginConfig.ShowDatesOnBoxes != null && PluginConfig.ShowDatesOnBoxes.Value);

                Transform player = GetPlayer();
                Vector3 playerPos = player != null ? player.position : Vector3.zero;
                bool havePlayer = player != null;

                // Najwazniejsza zmiana: trzymany karton bierzemy bezposrednio z BoxInteraction,
                // zamiast szukac go petla po wszystkich etykietach.
                var heldLabel = GetHeldLabel();
                BoxLabelPatch.HeldBoxLabel = heldLabel;

                if (heldLabel != null && heldLabel.gameObject != null)
                {
                    heldLabel.SetTextEnabled(showDates);
                    heldLabel.ProcessRuntimeUpdate();
                    if (showDates) heldLabel.ProcessLogicUpdate();
                }

                if (_scanCursor >= total) _scanCursor = 0;

                int processed = 0;
                int examined = 0;
                int idx = _scanCursor;

                while (examined < total && processed < LabelsPerTick)
                {
                    if (idx >= BoxLabelPatch.AllLabels.Count) idx = 0;
                    if (BoxLabelPatch.AllLabels.Count == 0) break;

                    var label = BoxLabelPatch.AllLabels[idx];
                    idx++;
                    examined++;
                    processed++;

                    if (label == null || label.gameObject == null) continue;
                    if (heldLabel != null && label == heldLabel) continue;

                    if (havePlayer)
                    {
                        Vector3 d = label.transform.position - playerPos;
                        if (d.sqrMagnitude > CullDistanceSqr)
                        {
                            label.SetTextEnabled(false);
                            continue;
                        }
                    }

                    // Daty pokazujemy tylko na trzymanym kartonie. Dla pobliskich kartonow
                    // lekko podtrzymujemy runtime state w batchu, bez pelnego skanu wszystkich.
                    label.SetTextEnabled(false);
                    label.ProcessRuntimeUpdate();
                }

                _scanCursor = idx;
            }
            finally { SmartExpiration.SEProfiler.End("BoxLabels", __pf); }
        }
    }
}