#nullable disable
using UnityEngine;

namespace SmartExpiration
{
    /// <summary>
    /// PERF: jeden wspoldzielony cache wynikow FindObjectsOfType&lt;DisplaySlot&gt;.
    /// Wczesniej kilka systemow (RefreshAll, RestockerScanner) skanowalo cala scene
    /// niezaleznie - teraz scena jest skanowana maks. raz na TTL sekund dla wszystkich.
    /// </summary>
    public static class SceneSlotCache
    {
        private const float Ttl = 10.0f;
        private static DisplaySlot[] _slots = new DisplaySlot[0];
        private static float _lastScan = -999f;

        public static DisplaySlot[] GetSlots()
        {
            float now = Time.time;
            if (now - _lastScan > Ttl || _slots == null || _slots.Length == 0)
            {
                _slots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
                _lastScan = now;
            }
            return _slots;
        }

        /// <summary>Wymuszenie ponownego skanu przy nastepnym GetSlots (np. po duzej zmianie sceny).</summary>
        public static void Invalidate()
        {
            _lastScan = -999f;
        }
    }
}
