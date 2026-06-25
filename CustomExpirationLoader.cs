using StatisticMod;
using System.Collections.Generic;
using UnityEngine;

namespace SmartExpiration
{
    public static class CustomExpirationLoader
    {
        public static readonly Dictionary<int, int> CustomDays = new Dictionary<int, int>();
        public static bool NeedsReload = true;
        public static int ConfigVersion = 0;

        private static string _lastRaw = "";
        private static float _lastCheckTime = 0f; // ⚡ TARCZA FPS

        public static void Load()
        {
            // ⚡ ATOMOWA OPTYMALIZACJA: Odczyt configu maksymalnie raz na 2 sekundy.
            // Zabezpiecza przed "zadławieniem" przez wywołania z innych skryptów.
            if (Time.realtimeSinceStartup - _lastCheckTime < 2.0f) return;
            _lastCheckTime = Time.realtimeSinceStartup;

            string raw = PluginConfig.CustomShelfLifeList != null ? PluginConfig.CustomShelfLifeList.Value : "";

            if (raw == _lastRaw) return;

            _lastRaw = raw;
            ConfigVersion++;
            CustomDays.Clear();

            Plugin.DebugLog($"[EXP DEBUG] CONFIG CHANGED -> version {ConfigVersion}");

            if (string.IsNullOrWhiteSpace(raw)) return;

            foreach (var entry in raw.Split(','))
            {
                var parts = entry.Split(':');
                if (parts.Length != 2) continue;

                if (int.TryParse(parts[0].Trim(), out int id) &&
                    int.TryParse(parts[1].Trim(), out int days))
                {
                    CustomDays[id] = days;
                }
            }
        }

        public static bool TryGet(int productID, out int days)
        {
            return CustomDays.TryGetValue(productID, out days);
        }

        public static void ForceReload()
        {
            _lastCheckTime = 0f;
            Load();
        }
    }
}