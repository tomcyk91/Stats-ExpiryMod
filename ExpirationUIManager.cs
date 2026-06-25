using System.Collections.Generic;
using System.Linq;
using System.Text;
// Referencja do Twojego drugiego moda, gdzie zdefiniowana jest tabela wag
using StatisticMod;

namespace SmartExpiration
{
    public static class ExpirationUIManager
    {
        // Lista ID produktów, które gra traktuje jako owoce/warzywa (sprzedawane na wagę)
        // DODAŁEM BRAKUJĄCE: 164 (ziemniaki) i 170 (cytryny)
        private static readonly HashSet<int> WeightProductIDs = new HashSet<int>
        {
            164, 165, 166, 167, 168, 169, 170, 171, 172, 173, 174, 175, 176, 177, 178, 179, 180, 181, 182, 183, 184, 185, 186, 187, 188
        };

        public static string BuildShelfText(DisplaySlot slot, int currentDay)
        {
            string noDataText = StatisticMod.Plugin.T("Brak danych o terminie", "No expiration data");

            if (slot == null || !slot.HasProduct) return noDataText;

            // Zapewniamy aktualność danych przed budowaniem tekstu
            ExpirationManager.SyncShelf(slot);

            var products = slot.GetComponentsInChildren<global::Product>(true);
            if (products == null || products.Length == 0) return noDataText;

            Dictionary<int, int> daysLeftCounts = new Dictionary<int, int>();

            foreach (var product in products)
            {
                var comp = product.GetComponent<ProductExpirationComponent>();
                if (comp != null)
                {
                    int daysLeft = comp.GetDaysLeft(currentDay);
                    if (daysLeftCounts.ContainsKey(daysLeft))
                        daysLeftCounts[daysLeft]++;
                    else
                        daysLeftCounts[daysLeft] = 1;
                }
            }

            if (daysLeftCounts.Count == 0) return noDataText;

            var sortedDays = daysLeftCounts.Keys.ToList();
            sortedDays.Sort();

            StringBuilder sb = new StringBuilder();
            sb.AppendLine($"<b>{StatisticMod.Plugin.T("Terminy ważności:", "Expiration dates:")}</b>");

            int productId = slot.ProductID;
            bool isWeight = WeightProductIDs.Contains(productId);
            float kgPerUnit = 1.0f;

            // Pobieramy mnożnik wagi z tabeli w StatisticMod.SalesUnifiedFinal
            if (isWeight)
            {
                try
                {
                    float w = 0f;
                    // Używamy tabeli WeightPerUnit z Twojego pliku SalesUnifiedFinal.cs
                    if (SalesUnifiedFinal.WeightPerUnit != null && SalesUnifiedFinal.WeightPerUnit.TryGetValue(productId, out w))
                    {
                        kgPerUnit = w;
                    }
                }
                catch { } // Fallback do 1.0f w przypadku problemów z dostępem do danych
            }

            string unit = isWeight ? "kg" : StatisticMod.Plugin.T("szt.", "pcs");

            foreach (int days in sortedDays)
            {
                int count = daysLeftCounts[days];
                string colorHex = GetColorForDays(days);

                // Formatowanie ilości: dla wagowych 2 miejsca po przecinku, dla sztuk liczba całkowita
                string valStr = isWeight ? (count * kgPerUnit).ToString("N2") : count.ToString();

                if (days < 0)
                {
                    sb.AppendLine($"<color={colorHex}>{valStr} {unit} {StatisticMod.Plugin.T("PO TERMINIE!", "EXPIRED!")}</color>");
                }
                else if (days == 0)
                {
                    sb.AppendLine($"<color={colorHex}>{valStr} {unit} {StatisticMod.Plugin.T("DZIŚ!", "TODAY!")}</color>");
                }
                else if (days == 1)
                {
                    sb.AppendLine($"<color={colorHex}>{valStr} {unit} {StatisticMod.Plugin.T("JUTRO!", "TOMORROW!")}</color>");
                }
                else
                {
                    // Używamy liczby dni dla terminów dalszych niż jutro
                    sb.AppendLine($"<color={colorHex}>{valStr} {unit} {StatisticMod.Plugin.T($"za {days} dni", $"in {days} days")}</color>");
                }
            }
            return sb.ToString();
        }

        private static string GetColorForDays(int daysLeft)
        {
            if (daysLeft <= 0) return "#FF0000"; // Czerwony (DZIŚ i PO TERMINIE)
            if (daysLeft == 1) return "#FFA500"; // Pomarańczowy (JUTRO)
            return "#00FF00"; // Zielony (Pojutrze i dalej)
        }
    }
}