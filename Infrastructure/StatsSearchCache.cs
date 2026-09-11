using System;
using UnityEngine;

namespace StatisticMod
{
    public static class StatsSearchCache
    {
        private static DisplaySlot[] _slots;
        private static Box[] _boxes;
        private static float _lastFetchTime = -999f;
        private static int _lastDay = -1;

        // TTL bufora = 1.5 sekundy. Wpisanie słowa "jabłko" zajmuje człowiekowi ~700ms.
        // Ciężki skan fizyki C++ wykona się TYLKO przy pierwszej literze "j"!
        private const float CacheTTL = 1.5f;

        public static DisplaySlot[] GetSlots()
        {
            EnsureValid();
            return _slots ?? new DisplaySlot[0];
        }

        public static Box[] GetBoxes()
        {
            EnsureValid();
            return _boxes ?? new Box[0];
        }

        public static void ForceInvalidate()
        {
            _lastFetchTime = -999f;
        }

        private static void EnsureValid()
        {
            float now = Time.time;

            // C5 FIX: Pancerna bramka natywna przed odpytywaniem instancji dnia
            var dcm = DayCycleManager.HasInstance ? DayCycleManager.Instance : null;
            int currentDay = dcm != null ? dcm.CurrentDay : 1;

            bool dayChanged = currentDay != _lastDay;
            bool ttlExpired = (now - _lastFetchTime) > CacheTTL;

            if (!dayChanged && !ttlExpired && _slots != null && _boxes != null)
                return;

            _lastDay = currentDay;
            _lastFetchTime = now;

            // 1. Priorytet wydajnościowy: ciągniemy półki z gotowego bufora sceny
            try { _slots = SmartExpiration.SceneSlotCache.GetSlots(); } catch { }
            if (_slots == null) _slots = UnityEngine.Object.FindObjectsOfType<DisplaySlot>();

            // 2. Skrzynki odpytujemy natywnie z silnika
            _boxes = UnityEngine.Object.FindObjectsOfType<Box>();
        }
    }
}