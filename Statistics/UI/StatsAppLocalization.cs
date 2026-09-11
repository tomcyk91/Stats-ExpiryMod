using Il2CppInterop.Runtime.Attributes;
using TMPro;

namespace StatisticMod
{
    public partial class StatsAppManager
    {
        [HideFromIl2Cpp]
        public static void NotifyLanguageChanged()
        {
            try { _instance?.ApplyLanguageChanged(); }
            catch { }
        }

        [HideFromIl2Cpp]
        public static void TickLocalizedDynamicLabels()
        {
            try { _instance?.RefreshLocalizedDynamicLabels(); }
            catch { }
        }

        private void ApplyLanguageChanged()
        {
            // Otwarte listy zawierają tekst utworzony jednorazowo. Zamykamy je,
            // aby przy następnym otwarciu zostały zbudowane w nowym języku.
            try { HideHeaderDropdown(); } catch { }
            try { HideChartDropdown(); } catch { }

            RefreshDesktopShortcutLabel();

            try { RefreshTitleModeText(); } catch { }
            try
            {
                if (_selectedDay >= 1) UpdateDayLabel();
                else if (_dayLabelTmp != null) _dayLabelTmp.text = Plugin.DayLabel(-1);
            }
            catch { }
            try { UpdateFilterButtonUI(); } catch { }
            try { UpdateSortHeaderUI(); } catch { }
            RefreshSearchPlaceholder();

            // Przebudowanie otwartej aplikacji od razu odświeża kafelki,
            // ekran Analizy, Wykresy i wszystkie opisy bez restartu gry.
            try
            {
                if (_statsApp != null && _statsApp.activeInHierarchy)
                    BuildForHubMode();
            }
            catch { }

            RefreshLocalizedDynamicLabels();
        }

        private void RefreshDesktopShortcutLabel()
        {
            try
            {
                if (_statsShortcutButton == null) return;

                var labels =
                    _statsShortcutButton.GetComponentsInChildren<TextMeshProUGUI>(true);

                string labelText = Plugin.T("STATYSTYKI", "STATISTICS");

                for (int i = 0; i < labels.Length; i++)
                {
                    if (labels[i] != null)
                        labels[i].text = labelText;
                }
            }
            catch { }
        }

        private void RefreshSearchPlaceholder()
        {
            try
            {
                if (_searchInputField != null && _searchInputField.placeholder is TMP_Text placeholder)
                    placeholder.text = Plugin.T("Szukaj nazwy lub ID", "Search by name or ID");
            }
            catch { }
        }

        private void RefreshLocalizedDynamicLabels()
        {
            try
            {
                if (_statsApp == null || !_statsApp.activeInHierarchy) return;

                // StatsAppCharts.cs ma dynamiczny tekst produktu zawierający ID i nazwę.
                // Odtwarzamy go tutaj, ponieważ sam prefiks nie przechodzi przez Plugin.T.
                if (_hubMode == HubMode.Charts && _productPickLabel != null)
                {
                    if (_chartSelectedProductId <= 0)
                    {
                        _productPickLabel.text = Plugin.T(
                            "Produkt: (kliknij aby wybrać)",
                            "Product: (click to select)");
                    }
                    else
                    {
                        string name = GetProductNameSafe(_chartSelectedProductId);
                        string product = Plugin.T("Produkt", "Product");
                        _productPickLabel.text =
                            $"{product}: <color=#AAAAAA>[{_chartSelectedProductId}]</color> {name}";
                    }
                }
            }
            catch { }
        }
    }
}
