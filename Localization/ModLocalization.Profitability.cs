using System;

namespace StatisticMod
{
    internal static partial class ModLocalization
    {
        /// <summary>
        /// Profitability vocabulary for every non-English/non-Polish language
        /// currently supported by the game/mod localization bridge.
        /// English and Polish are supplied directly through Plugin.T(...).
        /// </summary>
        private static void AddProfitabilityTerms()
        {
            SetProfitabilityTerms("fr", "Rentabilité", "Bénéfice brut", "Marge", "Bénéfice");
            SetProfitabilityTerms("it", "Redditività", "Utile lordo", "Margine", "Utile");
            SetProfitabilityTerms("de", "Rentabilität", "Bruttogewinn", "Marge", "Gewinn");
            SetProfitabilityTerms("es", "Rentabilidad", "Beneficio bruto", "Margen", "Beneficio");
            SetProfitabilityTerms("zh", "盈利能力", "毛利润", "利润率", "利润");
            SetProfitabilityTerms("pt-BR", "Rentabilidade", "Lucro bruto", "Margem", "Lucro");
            SetProfitabilityTerms("nl", "Winstgevendheid", "Brutowinst", "Marge", "Winst");
            SetProfitabilityTerms("ja", "収益性", "粗利益", "利益率", "利益");
            SetProfitabilityTerms("ko", "수익성", "매출총이익", "이익률", "이익");
            SetProfitabilityTerms("pt-PT", "Rentabilidade", "Lucro bruto", "Margem", "Lucro");
            SetProfitabilityTerms("ru", "Рентабельность", "Валовая прибыль", "Маржа", "Прибыль");
            SetProfitabilityTerms("tr", "Kârlılık", "Brüt kâr", "Marj", "Kâr");
            SetProfitabilityTerms("da", "Rentabilitet", "Bruttofortjeneste", "Margin", "Fortjeneste");
            SetProfitabilityTerms("fi", "Kannattavuus", "Bruttokate", "Kateprosentti", "Voitto");
            SetProfitabilityTerms("hu", "Jövedelmezőség", "Bruttó nyereség", "Árrés", "Nyereség");
            SetProfitabilityTerms("ro", "Rentabilitate", "Profit brut", "Marjă", "Profit");
            SetProfitabilityTerms("cs", "Ziskovost", "Hrubý zisk", "Marže", "Zisk");
            SetProfitabilityTerms("lt", "Pelningumas", "Bendrasis pelnas", "Marža", "Pelnas");
        }

        private static void SetProfitabilityTerms(
            string code,
            string profitability,
            string grossProfit,
            string margin,
            string profit)
        {
            if (!Packs.TryGetValue(code, out LanguagePack pack) || pack == null)
                return;

            pack.Terms["profitability"] = profitability;
            pack.Terms["gross profit"] = grossProfit;
            pack.Terms["margin"] = margin;
            pack.Terms["profit"] = profit;
        }
    }
}
