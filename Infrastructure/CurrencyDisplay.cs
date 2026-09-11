using BepInEx;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace StatisticMod
{
    /// <summary>
    /// Formats money in Stats & Expiry using the same [Currency] settings as
    /// Products & Texture Customizer when that mod is installed.
    ///
    /// The integration is intentionally file-based so Stats & Expiry does not
    /// need a hard assembly dependency on ProductCustomizer.
    /// </summary>
    internal static class CurrencyDisplay
    {
        private const string DefaultSuffix = "$";
        private const string DefaultDecimalSeparator = ".";

        private static readonly object Sync = new object();

        private static bool _loaded;
        private static DateTime _lastCheckUtc = DateTime.MinValue;
        private static string _configPath;
        private static DateTime _configWriteUtc = DateTime.MinValue;

        private static bool _customEnabled;
        private static string _prefix = string.Empty;
        private static string _suffix = DefaultSuffix;
        private static string _decimalSeparator = DefaultDecimalSeparator;

        /// <summary>
        /// Symbol/code suitable for compact labels such as chart legends.
        /// </summary>
        public static string UnitLabel
        {
            get
            {
                RefreshIfNeeded();

                if (!string.IsNullOrWhiteSpace(_suffix))
                    return _suffix.Trim();
                if (!string.IsNullOrWhiteSpace(_prefix))
                    return _prefix.Trim();
                return string.Empty;
            }
        }

        public static string Format(float value, int decimals = 2, bool useGrouping = false)
        {
            RefreshIfNeeded();

            if (decimals < 0) decimals = 0;
            if (decimals > 6) decimals = 6;

            string separator = NormalizeDecimalSeparator(_decimalSeparator);
            var nfi = (NumberFormatInfo)CultureInfo.InvariantCulture.NumberFormat.Clone();
            nfi.NumberDecimalSeparator = separator;
            nfi.NumberGroupSeparator = separator == "," ? "." : ",";

            string format = useGrouping ? "N" + decimals : "F" + decimals;
            string number = value.ToString(format, nfi);

            string prefix = (_prefix ?? string.Empty).Trim();
            string suffix = (_suffix ?? string.Empty).Trim();

            if (string.IsNullOrEmpty(prefix) && string.IsNullOrEmpty(suffix))
                return number;

            if (!string.IsNullOrEmpty(prefix) && !string.IsNullOrEmpty(suffix))
                return prefix + " " + number + " " + suffix;

            if (!string.IsNullOrEmpty(prefix))
                return prefix + " " + number;

            return number + " " + suffix;
        }

        private static void RefreshIfNeeded()
        {
            DateTime now = DateTime.UtcNow;
            if (_loaded && (now - _lastCheckUtc).TotalSeconds < 2.0)
                return;

            lock (Sync)
            {
                now = DateTime.UtcNow;
                if (_loaded && (now - _lastCheckUtc).TotalSeconds < 2.0)
                    return;

                _lastCheckUtc = now;

                string found = FindCustomizerConfig();
                DateTime writeUtc = DateTime.MinValue;
                if (!string.IsNullOrEmpty(found))
                {
                    try { writeUtc = File.GetLastWriteTimeUtc(found); }
                    catch { writeUtc = DateTime.MinValue; }
                }

                if (_loaded &&
                    string.Equals(found, _configPath, StringComparison.OrdinalIgnoreCase) &&
                    writeUtc == _configWriteUtc)
                {
                    return;
                }

                LoadSettings(found, writeUtc);
            }
        }

        private static void LoadSettings(string path, DateTime writeUtc)
        {
            // Vanilla/fallback formatting.
            _customEnabled = false;
            _prefix = string.Empty;
            _suffix = DefaultSuffix;
            _decimalSeparator = DefaultDecimalSeparator;
            _configPath = path;
            _configWriteUtc = writeUtc;
            _loaded = true;

            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return;

            try
            {
                Dictionary<string, string> values = ReadCurrencySection(path);
                if (values.Count == 0)
                    return;

                if (values.TryGetValue("Enabled", out string enabledText) &&
                    bool.TryParse(enabledText, out bool enabled))
                {
                    _customEnabled = enabled;
                }
                else
                {
                    // Products & Texture Customizer defaults this setting to true.
                    _customEnabled = true;
                }

                if (!_customEnabled)
                    return;

                if (values.TryGetValue("Prefix", out string prefix))
                    _prefix = prefix ?? string.Empty;

                if (values.TryGetValue("Suffix", out string suffix))
                    _suffix = suffix ?? string.Empty;

                if (values.TryGetValue("Decimal Separator", out string decimalSeparator))
                    _decimalSeparator = NormalizeDecimalSeparator(decimalSeparator);
            }
            catch (Exception ex)
            {
                try { Plugin.DebugWarning("[CurrencyDisplay] Could not read Product Customizer currency config: " + ex.Message); }
                catch { }
            }
        }

        private static Dictionary<string, string> ReadCurrencySection(string path)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            bool inCurrency = false;

            foreach (string raw in File.ReadAllLines(path))
            {
                string line = raw == null ? string.Empty : raw.Trim();
                if (line.Length == 0 || line.StartsWith("#") || line.StartsWith(";"))
                    continue;

                if (line.StartsWith("[") && line.EndsWith("]"))
                {
                    string section = line.Substring(1, line.Length - 2).Trim();
                    inCurrency = string.Equals(section, "Currency", StringComparison.OrdinalIgnoreCase);
                    continue;
                }

                if (!inCurrency)
                    continue;

                int eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                string key = line.Substring(0, eq).Trim();
                string value = line.Substring(eq + 1).Trim();
                result[key] = value;
            }

            return result;
        }

        private static string FindCustomizerConfig()
        {
            try
            {
                string dir = Paths.ConfigPath;
                if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir))
                    return null;

                // Current Products & Texture Customizer (v2.x) uses this GUID/name.
                // Prefer it explicitly so an old legacy ProductCustomizer.cfg cannot
                // override the active currency settings.
                string current = Path.Combine(dir, "ProductandTextureCustomizer.cfg");
                if (File.Exists(current) && IsCustomizerConfig(current, "ProductandTextureCustomizer"))
                    return current;

                // Be robust if BepInEx changes the generated file name: identify the
                // active config by its plugin GUID rather than by display name.
                string[] files = Directory.GetFiles(dir, "*.cfg", SearchOption.TopDirectoryOnly);
                for (int i = 0; i < files.Length; i++)
                {
                    if (IsCustomizerConfig(files[i], "ProductandTextureCustomizer"))
                        return files[i];
                }

                // Legacy compatibility only. These are checked after the current GUID
                // so stale configs from older releases can never win.
                string[] legacyNames =
                {
                    "Product & Texture Customizer.cfg",
                    "ProductCustomizer.cfg"
                };

                for (int i = 0; i < legacyNames.Length; i++)
                {
                    string candidate = Path.Combine(dir, legacyNames[i]);
                    if (File.Exists(candidate) && HasCurrencySection(candidate))
                        return candidate;
                }

                for (int i = 0; i < files.Length; i++)
                {
                    string name = Path.GetFileNameWithoutExtension(files[i]);
                    if (string.IsNullOrEmpty(name))
                        continue;

                    string compact = name.Replace(" ", string.Empty)
                                         .Replace("&", string.Empty)
                                         .Replace("_", string.Empty)
                                         .Replace("-", string.Empty)
                                         .ToLowerInvariant();

                    if (compact.Contains("product") &&
                        compact.Contains("texture") &&
                        compact.Contains("customizer") &&
                        HasCurrencySection(files[i]))
                    {
                        return files[i];
                    }
                }
            }
            catch { }

            return null;
        }

        private static bool IsCustomizerConfig(string path, string expectedGuid)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                bool guidMatch = false;
                bool hasCurrency = false;

                foreach (string raw in File.ReadLines(path))
                {
                    string line = raw == null ? string.Empty : raw.Trim();

                    if (line.StartsWith("## Plugin GUID:", StringComparison.OrdinalIgnoreCase))
                    {
                        string guid = line.Substring("## Plugin GUID:".Length).Trim();
                        guidMatch = string.Equals(guid, expectedGuid, StringComparison.OrdinalIgnoreCase);
                    }
                    else if (string.Equals(line, "[Currency]", StringComparison.OrdinalIgnoreCase))
                    {
                        hasCurrency = true;
                    }

                    if (guidMatch && hasCurrency)
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static bool HasCurrencySection(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
                return false;

            try
            {
                foreach (string raw in File.ReadLines(path))
                {
                    string line = raw == null ? string.Empty : raw.Trim();
                    if (string.Equals(line, "[Currency]", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch { }

            return false;
        }

        private static string NormalizeDecimalSeparator(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return DefaultDecimalSeparator;

            string trimmed = value.Trim();
            if (trimmed == ",") return ",";
            if (trimmed == ".") return ".";

            // Keep formatting predictable if somebody enters a longer value.
            return trimmed.Substring(0, 1);
        }
    }
}
