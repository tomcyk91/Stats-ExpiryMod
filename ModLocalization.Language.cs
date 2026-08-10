using System;
using System.Globalization;
using System.Reflection;

namespace StatisticMod
{
    internal static partial class ModLocalization
    {
        private static Type _localizationSettingsType;
        private static PropertyInfo _selectedLocaleProperty;

        public static string DayLabel(int day)
        {
            if (!_initialized) Initialize();
            if (day < 0) return Upper(Translate("DZIEŃ", "DAY")) + " --";

            return CurrentCode switch
            {
                "zh" => $"第{day}天",
                "ja" => $"{day}日目",
                "ko" => $"{day}일차",
                "tr" => $"{day}. {Upper(Translate("Dzień", "Day"))}",
                "hu" => $"{day}. {Upper(Translate("Dzień", "Day"))}",
                "lt" => $"{day} {Upper(Translate("Dzień", "Day"))}",
                _ => $"{Upper(Translate("Dzień", "Day"))} {day}"
            };
        }

        public static string DayShortLabel(int day)
        {
            if (!_initialized) Initialize();
            if (CurrentCode == "zh") return $"第{day}天";
            if (CurrentCode == "ja") return $"{day}日";
            if (CurrentCode == "ko") return $"{day}일";

            string prefix = CurrentCode == "pl"
                ? "D"
                : Packs.TryGetValue(CurrentCode, out LanguagePack pack) ? pack.DayShort : "D";
            return prefix + day;
        }

        public static string InDays(int days)
        {
            if (!_initialized) Initialize();
            return CurrentCode switch
            {
                "pl" => days == 1 ? "za 1 dzień" : $"za {days} dni",
                "fr" => $"dans {days} " + (days == 1 ? "jour" : "jours"),
                "it" => $"tra {days} " + (days == 1 ? "giorno" : "giorni"),
                "de" => $"in {days} " + (days == 1 ? "Tag" : "Tagen"),
                "es" => $"en {days} " + (days == 1 ? "día" : "días"),
                "zh" => $"{days}天后",
                "pt-BR" => $"em {days} " + (days == 1 ? "dia" : "dias"),
                "pt-PT" => $"em {days} " + (days == 1 ? "dia" : "dias"),
                "nl" => $"over {days} " + (days == 1 ? "dag" : "dagen"),
                "ja" => $"{days}日後",
                "ko" => $"{days}일 후",
                "ru" => $"через {days} {RussianDays(days)}",
                "tr" => $"{days} gün içinde",
                "da" => $"om {days} " + (days == 1 ? "dag" : "dage"),
                "fi" => $"{days} päivän kuluttua",
                "hu" => $"{days} nap múlva",
                "ro" => $"peste {days} " + (days == 1 ? "zi" : "zile"),
                "cs" => $"za {days} {CzechDays(days)}",
                "lt" => $"po {days} {LithuanianDays(days)}",
                _ => $"in {days} " + (days == 1 ? "day" : "days")
            };
        }

        public static string DaysCount(int days)
        {
            if (!_initialized) Initialize();
            return CurrentCode switch
            {
                "pl" => $"{days} dni", "fr" => $"{days} jours", "it" => $"{days} giorni",
                "de" => $"{days} Tage", "es" => $"{days} días", "zh" => $"{days}天",
                "pt-BR" => $"{days} dias", "pt-PT" => $"{days} dias", "nl" => $"{days} dagen",
                "ja" => $"{days}日", "ko" => $"{days}일", "ru" => $"{days} {RussianDays(days)}",
                "tr" => $"{days} gün", "da" => $"{days} dage", "fi" => $"{days} päivää",
                "hu" => $"{days} nap", "ro" => $"{days} zile", "cs" => $"{days} {CzechDays(days)}",
                "lt" => $"{days} {LithuanianDays(days)}", _ => $"{days} days"
            };
        }

        public static string ProductName(int productId, ProductSO fallback = null)
        {
            try
            {
                var manager = global::LocalizationManager.HasInstance
                    ? global::LocalizationManager.Instance
                    : null;
                if (manager != null)
                {
                    string localized = manager.LocalizedProductName(productId);
                    if (!string.IsNullOrWhiteSpace(localized)) return localized.Trim();
                }
            }
            catch { }

            if (fallback != null)
            {
                try
                {
                    string complex = fallback.ComplexName(1f);
                    if (!string.IsNullOrWhiteSpace(complex)) return complex.Trim();
                }
                catch { }
                try
                {
                    if (!string.IsNullOrWhiteSpace(fallback.TempProductName))
                        return fallback.TempProductName.Trim();
                }
                catch { }
                try
                {
                    if (!string.IsNullOrWhiteSpace(fallback.ProductName))
                        return fallback.ProductName.Trim();
                }
                catch { }
            }

            return ProductFallback(productId);
        }

        public static string ProductFallback(int productId) =>
            $"{Translate("Produkt", "Product")} #{productId}";

        public static string UnknownId(int productId) =>
            $"{Translate("Nieznane ID", "Unknown ID")}: {productId}";

        private static bool TryTranslateDynamicDays(string source, out string result)
        {
            result = null;
            string value = source.Trim();

            if (value.StartsWith("in ", StringComparison.OrdinalIgnoreCase) &&
                value.EndsWith(" days", StringComparison.OrdinalIgnoreCase))
            {
                string number = value.Substring(3, value.Length - 8).Trim();
                if (int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int days))
                {
                    result = InDays(days);
                    return true;
                }
            }

            if (value.EndsWith(" days", StringComparison.OrdinalIgnoreCase))
            {
                string number = value.Substring(0, value.Length - 5).Trim();
                if (int.TryParse(number, NumberStyles.Integer, CultureInfo.InvariantCulture, out int days))
                {
                    result = DaysCount(days);
                    return true;
                }
            }

            return false;
        }

        private static string RussianDays(int n)
        {
            int n10 = n % 10;
            int n100 = n % 100;
            if (n10 == 1 && n100 != 11) return "день";
            if (n10 >= 2 && n10 <= 4 && (n100 < 12 || n100 > 14)) return "дня";
            return "дней";
        }

        private static string CzechDays(int n)
        {
            if (n == 1) return "den";
            if (n >= 2 && n <= 4) return "dny";
            return "dní";
        }

        private static string LithuanianDays(int n) => n == 1 ? "diena" : "dienų";

        private static string DetectLanguageCode()
        {
            string code = DetectFromUnityLocalization();
            if (!string.IsNullOrWhiteSpace(code)) return NormalizeCode(code);

            try
            {
                var container = SettingsManager.Container;
                if (container != null && container.Language != null)
                    return LanguageFromIndex(container.Language.Value);
            }
            catch { }

            try { return NormalizeCode(CultureInfo.CurrentUICulture.Name); }
            catch { return "en"; }
        }

        private static string DetectFromUnityLocalization()
        {
            try
            {
                if (_localizationSettingsType == null)
                {
                    _localizationSettingsType =
                        Type.GetType("UnityEngine.Localization.Settings.LocalizationSettings, Unity.Localization");

                    if (_localizationSettingsType == null)
                    {
                        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
                        {
                            _localizationSettingsType = assembly.GetType(
                                "UnityEngine.Localization.Settings.LocalizationSettings", false);
                            if (_localizationSettingsType != null) break;
                        }
                    }

                    _selectedLocaleProperty = _localizationSettingsType?.GetProperty(
                        "SelectedLocale", BindingFlags.Public | BindingFlags.Static);
                }

                object locale = _selectedLocaleProperty?.GetValue(null, null);
                if (locale == null) return null;

                object identifier = locale.GetType()
                    .GetProperty("Identifier", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(locale, null);
                if (identifier == null) return null;

                object code = identifier.GetType()
                    .GetProperty("Code", BindingFlags.Public | BindingFlags.Instance)?
                    .GetValue(identifier, null);
                return code?.ToString();
            }
            catch { return null; }
        }

        private static string NormalizeCode(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return "en";

            string code = raw.Trim().Replace('_', '-').ToLowerInvariant();
            if (code.StartsWith("zh")) return "zh";
            if (code.StartsWith("pt-br")) return "pt-BR";
            if (code.StartsWith("pt")) return "pt-PT";
            if (code.StartsWith("fr")) return "fr";
            if (code.StartsWith("it")) return "it";
            if (code.StartsWith("de")) return "de";
            if (code.StartsWith("es")) return "es";
            if (code.StartsWith("nl")) return "nl";
            if (code.StartsWith("ja")) return "ja";
            if (code.StartsWith("ko")) return "ko";
            if (code.StartsWith("ru")) return "ru";
            if (code.StartsWith("tr")) return "tr";
            if (code.StartsWith("da")) return "da";
            if (code.StartsWith("fi")) return "fi";
            if (code.StartsWith("pl")) return "pl";
            if (code.StartsWith("hu")) return "hu";
            if (code.StartsWith("ro")) return "ro";
            if (code.StartsWith("cs") || code.StartsWith("cz")) return "cs";
            if (code.StartsWith("lt")) return "lt";
            return "en";
        }

        private static string LanguageFromIndex(int index)
        {
            string[] order =
            {
                "en", "fr", "it", "de", "es", "zh", "pt-BR", "nl", "ja", "ko",
                "pt-PT", "ru", "tr", "da", "fi", "pl", "hu", "ro", "cs", "lt"
            };
            return index >= 0 && index < order.Length ? order[index] : "en";
        }
    }
}
