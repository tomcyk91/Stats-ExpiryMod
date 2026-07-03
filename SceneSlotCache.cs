#nullable disable
using UnityEngine;

namespace SmartExpiration
{
    /// <summary>
    /// PERF: jeden wspoldzielony cache wynikow FindObjectsOfType.
    /// Wczesniej kilka systemow skanowalo cala scene niezaleznie.
    /// Teraz sloty i kartony sa skanowane maks. raz na TTL sekund dla wszystkich.
    /// </summary>
    public static class SceneSlotCache
    {
        private const float SlotTtl = 10.0f;
        private const float BoxTtl = 10.0f;

        private static DisplaySlot[] _slots = new DisplaySlot[0];
        private static Box[] _boxes = new Box[0];

        private static float _lastSlotScan = -999f;
        private static float _lastBoxScan = -999f;

        public static DisplaySlot[] GetSlots()
        {
            float now = Time.time;
            if (now - _lastSlotScan > SlotTtl || _slots == null || _slots.Length == 0)
            {
                _slots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();
                _lastSlotScan = now;
            }
            return _slots;
        }

        public static Box[] GetBoxes()
        {
            float now = Time.time;
            if (now - _lastBoxScan > BoxTtl || _boxes == null || _boxes.Length == 0)
            {
                _boxes = UnityEngine.Object.FindObjectsOfType<Box>();
                _lastBoxScan = now;
            }
            return _boxes;
        }

        /// <summary>Wymuszenie ponownego skanu przy nastepnym GetSlots/GetBoxes, np. po duzej zmianie sceny.</summary>
        public static void Invalidate()
        {
            _lastSlotScan = -999f;
            _lastBoxScan = -999f;
        }

        public static void InvalidateSlots()
        {
            _lastSlotScan = -999f;
        }

        public static void InvalidateBoxes()
        {
            _lastBoxScan = -999f;
        }
    }
}