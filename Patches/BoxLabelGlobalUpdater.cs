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

        void Update()
        {
            // ⚡ ATOMOWA OPTYMALIZACJA: Sprawdzamy etykiety 5 razy na sekundę, a nie 120!
            // To całkowicie eliminuje "stuttering" i Garbage Collection od skanowania kartonów.
            _tickTimer += Time.deltaTime;
            if (_tickTimer < 0.2f) return;
            _tickTimer = 0f;

            BoxLabelPatch.HeldBoxLabel = null;

            int total = BoxLabelPatch.AllLabels.Count;
            if (total == 0) return;

            bool showDates = (PluginConfig.ShowDatesOnBoxes != null && PluginConfig.ShowDatesOnBoxes.Value);

            for (int i = 0; i < total; i++)
            {
                var label = BoxLabelPatch.AllLabels[i];

                if (label == null || label.gameObject == null) continue;

                bool isHeld = false;

                try
                {
                    var parent = label.transform.parent;
                    if (parent != null)
                    {
                        // ⚡ OPTYMALIZACJA PAMIĘCI: Zamiast .ToLower() i Contains() (co generuje śmieci w RAM),
                        // używamy szybkiego i natywnego StartsWith bez alokacji nowych stringów.
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
                catch { /* ignoruj błędy transformów */ }

                try
                {
                    if (showDates) label.SetTextEnabled(isHeld);
                    else label.SetTextEnabled(false);

                    if (isHeld) label.ProcessLogicUpdate();
                }
                catch { /* ignoruj błędy przy aktualizacji etykiety */ }
            }
        }
    }
}