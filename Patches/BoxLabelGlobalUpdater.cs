﻿using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SmartExpiration.Patches
{
    public class BoxLabelGlobalUpdater : MonoBehaviour
    {
        public BoxLabelGlobalUpdater(IntPtr ptr) : base(ptr) { }

        private float _tickTimer = 0f;

        // PERF: cache transformu gracza - odswiezany rzadko, bez skanu sceny co tick.
        private static Transform _playerTf;
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
            try
            {
                var lp = PlayerManager.Instance != null ? PlayerManager.Instance.LocalPlayer : null;
                if (lp != null) { _playerTf = lp.transform; return _playerTf; }
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

        void Update()
        {
            // ⚡ Sprawdzamy etykiety 5 razy na sekunde, a nie 120.
            _tickTimer += Time.deltaTime;
            if (_tickTimer < 0.2f) return;
            _tickTimer = 0f;

            long __pf = SmartExpiration.SEProfiler.Begin();
            try {
            BoxLabelPatch.HeldBoxLabel = null;

            int total = BoxLabelPatch.AllLabels.Count;
            if (total == 0) return;

            bool showDates = (PluginConfig.ShowDatesOnBoxes != null && PluginConfig.ShowDatesOnBoxes.Value);

            Transform player = GetPlayer();
            Vector3 playerPos = player != null ? player.position : Vector3.zero;
            bool havePlayer = player != null;

            for (int i = 0; i < total; i++)
            {
                var label = BoxLabelPatch.AllLabels[i];

                if (label == null || label.gameObject == null) continue;

                // PERF: CULLING PO ODLEGLOSCI (sqrMagnitude wg zasad projektu).
                // Daleki karton -> wylacz jego TMP i pomin caly skan. To eliminuje koszt 2000+ etykiet.
                if (havePlayer)
                {
                    Vector3 d = label.transform.position - playerPos;
                    if (d.sqrMagnitude > CullDistanceSqr)
                    {
                        label.SetTextEnabled(false);
                        continue;
                    }
                }

                bool isHeld = false;

                try
                {
                    var parent = label.transform.parent;
                    if (parent != null)
                    {
                        string pName = parent.name;
                        if (!pName.StartsWith("Rack", StringComparison.OrdinalIgnoreCase) &&
                            !pName.StartsWith("Storage", StringComparison.OrdinalIgnoreCase) &&
                            !pName.StartsWith("Slot", StringComparison.OrdinalIgnoreCase))
                        {
                            isHeld = true;
                            BoxLabelPatch.HeldBoxLabel = label;
                        }
                    }
                }
                catch { /* ignoruj bledy transformow */ }

                try
                {
                    if (showDates) label.SetTextEnabled(isHeld);
                    else label.SetTextEnabled(false);

                    if (isHeld) label.ProcessLogicUpdate();
                }
                catch { /* ignoruj bledy przy aktualizacji etykiety */ }
            }
            } finally { SmartExpiration.SEProfiler.End("BoxLabels", __pf); }
        }
    }
}
