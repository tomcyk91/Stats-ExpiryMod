using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using UnityEngine;

namespace StatisticMod
{
    internal static partial class ModLocalization
    {
        internal sealed class LanguagePack
        {
            internal readonly string BuyShort;
            internal readonly string SellShort;
            internal readonly string ShopShort;
            internal readonly string WarehouseShort;
            internal readonly string DayShort;
            internal readonly Dictionary<string, string> Terms =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            internal LanguagePack(string buyShort, string sellShort, string shopShort, string warehouseShort, string dayShort)
            {
                BuyShort = buyShort;
                SellShort = sellShort;
                ShopShort = shopShort;
                WarehouseShort = warehouseShort;
                DayShort = dayShort;
            }
        }

        private static readonly Dictionary<string, LanguagePack> Packs =
            new Dictionary<string, LanguagePack>(StringComparer.OrdinalIgnoreCase);

        private static bool _packsLoaded;
        private static bool _initialized;
        private static float _nextLanguageCheck;
        private static float _pendingSecondRefreshAt = -1f;

        public static string CurrentCode { get; private set; } = "en";
        public static int Version { get; private set; }

        public static CultureInfo CurrentCulture
        {
            get
            {
                if (!_initialized) Initialize();
                string cultureName = CurrentCode switch
                {
                    "fr" => "fr-FR", "it" => "it-IT", "de" => "de-DE", "es" => "es-ES",
                    "zh" => "zh-CN", "pt-BR" => "pt-BR", "nl" => "nl-NL", "ja" => "ja-JP",
                    "ko" => "ko-KR", "pt-PT" => "pt-PT", "ru" => "ru-RU", "tr" => "tr-TR",
                    "da" => "da-DK", "fi" => "fi-FI", "pl" => "pl-PL", "hu" => "hu-HU",
                    "ro" => "ro-RO", "cs" => "cs-CZ", "lt" => "lt-LT", _ => "en-US"
                };
                try { return CultureInfo.GetCultureInfo(cultureName); }
                catch { return CultureInfo.InvariantCulture; }
            }
        }

        public static void Initialize()
        {
            if (_initialized) return;
            EnsurePacks();
            CurrentCode = DetectLanguageCode();
            Version = 1;
            Plugin.IsPolish = string.Equals(CurrentCode, "pl", StringComparison.OrdinalIgnoreCase);
            _initialized = true;
        }

        public static void Tick()
        {
            if (!_initialized) Initialize();
            if (Time.realtimeSinceStartup < _nextLanguageCheck) return;
            _nextLanguageCheck = Time.realtimeSinceStartup + 0.5f;

            try { StatsAppManager.TickLocalizedDynamicLabels(); } catch { }

            if (_pendingSecondRefreshAt > 0f && Time.realtimeSinceStartup >= _pendingSecondRefreshAt)
            {
                _pendingSecondRefreshAt = -1f;
                RefreshLocalizedContent();
            }

            string detected = DetectLanguageCode();
            if (string.IsNullOrEmpty(detected) ||
                string.Equals(detected, CurrentCode, StringComparison.OrdinalIgnoreCase))
                return;

            CurrentCode = detected;
            Version++;
            Plugin.IsPolish = string.Equals(CurrentCode, "pl", StringComparison.OrdinalIgnoreCase);
            RefreshLocalizedContent();

            // Unity Localization może przełączyć tabele chwilę po zmianie SelectedLocale.
            _pendingSecondRefreshAt = Time.realtimeSinceStartup + 0.75f;
        }

        private static void RefreshLocalizedContent()
        {
            try { Plugin.ProductCache?.Invalidate(); } catch { }
            try { StatsAppManager.NotifyLanguageChanged(); } catch { }
            try { RefreshWorldLabels(); } catch { }
        }

        public static string BuyShortLabel
        {
            get
            {
                if (!_initialized) Initialize();
                if (CurrentCode == "pl") return "Z";
                if (CurrentCode == "en") return "B";
                return Packs.TryGetValue(CurrentCode, out LanguagePack pack) ? pack.BuyShort : "B";
            }
        }

        public static string SellShortLabel
        {
            get
            {
                if (!_initialized) Initialize();
                if (CurrentCode == "pl" || CurrentCode == "en") return "S";
                return Packs.TryGetValue(CurrentCode, out LanguagePack pack) ? pack.SellShort : "S";
            }
        }

        public static string ShopShortLabel
        {
            get
            {
                if (!_initialized) Initialize();
                if (CurrentCode == "pl" || CurrentCode == "en") return "S";
                return Packs.TryGetValue(CurrentCode, out LanguagePack pack) ? pack.ShopShort : "S";
            }
        }

        public static string WarehouseShortLabel
        {
            get
            {
                if (!_initialized) Initialize();
                if (CurrentCode == "pl") return "M";
                if (CurrentCode == "en") return "W";
                return Packs.TryGetValue(CurrentCode, out LanguagePack pack) ? pack.WarehouseShort : "W";
            }
        }

        public static string Translate(string polish, string english)
        {
            if (!_initialized) Initialize();

            if (CurrentCode == "pl") return polish ?? english ?? string.Empty;
            if (CurrentCode == "en") return english ?? polish ?? string.Empty;

            string source = english ?? polish ?? string.Empty;
            if (!Packs.TryGetValue(CurrentCode, out LanguagePack pack)) return source;

            if (source == "B") return pack.BuyShort;
            // W aktualnym managerze "S" oznacza zarówno Sell, jak i Shop.
            if (source == "S") return "S";
            if (source == "W") return pack.WarehouseShort;
            if (source == "D") return pack.DayShort;

            if (TryTranslateDynamicDays(source, out string dynamicResult))
                return dynamicResult;

            return TranslateDecorated(source, pack);
        }

        private static string TranslateDecorated(string source, LanguagePack pack)
        {
            int trailingSpaces = source.Length - source.TrimEnd().Length;
            string working = source.Trim();
            string prefix = string.Empty;

            if (working.StartsWith("• ", StringComparison.Ordinal))
            {
                prefix = "• ";
                working = working.Substring(2);
            }

            if (pack.Terms.TryGetValue(working, out string exact))
                return prefix + MatchCase(working, exact) + new string(' ', trailingSpaces);

            string suffix = string.Empty;
            if (working.EndsWith("...", StringComparison.Ordinal))
            {
                suffix = "...";
                working = working.Substring(0, working.Length - 3);
            }
            else if (working.EndsWith(":", StringComparison.Ordinal) ||
                     working.EndsWith("!", StringComparison.Ordinal) ||
                     working.EndsWith(".", StringComparison.Ordinal))
            {
                suffix = working.Substring(working.Length - 1);
                working = working.Substring(0, working.Length - 1);
            }

            if (!pack.Terms.TryGetValue(working, out string translated)) return source;
            return prefix + MatchCase(working, translated) + suffix + new string(' ', trailingSpaces);
        }

        private static string Upper(string value)
        {
            try { return CurrentCulture.TextInfo.ToUpper(value ?? string.Empty); }
            catch { return (value ?? string.Empty).ToUpperInvariant(); }
        }

        private static string MatchCase(string source, string translated)
        {
            bool hasLetter = false;
            bool allUpper = true;
            for (int i = 0; i < source.Length; i++)
            {
                char c = source[i];
                if (!char.IsLetter(c)) continue;
                hasLetter = true;
                if (!char.IsUpper(c)) { allUpper = false; break; }
            }
            return hasLetter && allUpper ? Upper(translated) : translated;
        }

        private static void RefreshWorldLabels()
        {
            try
            {
                var labels = SmartExpiration.Patches.BoxLabelPatch.AllLabels;
                if (labels == null) return;

                MethodInfo refresh = typeof(SmartExpiration.Patches.BoxExpirationLabel)
                    .GetMethod("RefreshLabel", BindingFlags.Instance | BindingFlags.NonPublic);
                if (refresh == null) return;

                for (int i = 0; i < labels.Count; i++)
                {
                    var label = labels[i];
                    if (label == null) continue;
                    try { refresh.Invoke(label, null); } catch { }
                }
            }
            catch { }
        }

        private static void EnsurePacks()
        {
            if (_packsLoaded) return;
            _packsLoaded = true;
            AddGeneratedPacks();
        }

        private static LanguagePack AddPack(
            string code, string buy, string sell, string shop, string warehouse, string day)
        {
            var pack = new LanguagePack(buy, sell, shop, warehouse, day);
            Packs[code] = pack;
            return pack;
        }

        static partial void AddGeneratedPacks();
    }
}
