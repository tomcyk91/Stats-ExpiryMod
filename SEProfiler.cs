#nullable disable
using System.Collections.Generic;

namespace SmartExpiration
{
    /// <summary>
    /// Lekki profiler klatek + sekcji (identyczny format jak APAprof).
    /// Co 2s loguje najgorsza klatke, liczbe hitchy oraz czasy zmierzonych sekcji moda.
    /// Wlaczany flaga Enabled (domyslnie ON do diagnostyki).
    /// </summary>
    internal static class SEProfiler
    {
        public static bool Enabled = true;
        private static BepInEx.Logging.ManualLogSource _log;
        private static readonly Dictionary<string, double> _acc = new Dictionary<string, double>();
        private static readonly Dictionary<string, int> _calls = new Dictionary<string, int>();
        private static readonly System.Diagnostics.Stopwatch _sw = System.Diagnostics.Stopwatch.StartNew();
        private static float _lastReport;
        private const double SpikeMs = 2.0;

        public static void Init()
        {
            if (_log == null) _log = BepInEx.Logging.Logger.CreateLogSource("SEprof");
        }

        // Zwraca znacznik startu; uzyj z End(). Bez alokacji (long).
        public static long Begin() => Enabled ? _sw.ElapsedTicks : 0L;

        public static void End(string name, long startTicks)
        {
            if (!Enabled || _log == null) return;
            double ms = (_sw.ElapsedTicks - startTicks) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            _acc.TryGetValue(name, out var cur); _acc[name] = cur + ms;
            _calls.TryGetValue(name, out var c); _calls[name] = c + 1;
            if (ms >= SpikeMs)
                _log.LogWarning($"[SEprof] SPIKE '{name}' = {ms:F2} ms");
        }

        private static float _worstFrameMs;
        private static int _hitchFrames;
        private static int _frameCount;

        public static void EndFrame()
        {
            if (!Enabled || _log == null) return;

            float dtMs = UnityEngine.Time.unscaledDeltaTime * 1000f;
            _frameCount++;
            if (dtMs > _worstFrameMs) _worstFrameMs = dtMs;
            if (dtMs > 33f) _hitchFrames++; // klatka ponizej ~30 fps = odczuwalny hitch

            float now = UnityEngine.Time.unscaledTime;
            if (now - _lastReport < 2f) return;
            _lastReport = now;

            // Naglowek: stan klatek niezaleznie od moda - od razu widac czy to ten mod czy nie.
            _log.LogInfo($"[SEprof] KLATKI 2s: najgorsza={_worstFrameMs:F1}ms, hitche(>33ms)={_hitchFrames}/{_frameCount}");

            if (_acc.Count > 0)
            {
                var parts = new List<string>();
                foreach (var kv in _acc)
                {
                    _calls.TryGetValue(kv.Key, out var c);
                    parts.Add($"{kv.Key}: {kv.Value:F1}ms / {c}x");
                }
                parts.Sort();
                _log.LogInfo("[SEprof] SE sekcje 2s: " + string.Join(" | ", parts));
            }

            _acc.Clear(); _calls.Clear();
            _worstFrameMs = 0; _hitchFrames = 0; _frameCount = 0;
        }
    }
}
