#nullable disable
using System.Collections.Generic;
using UnityEngine;

namespace SmartExpiration
{
    /// <summary>
    /// Shared scene cache. Normal gameplay uses the game's native DisplayManager
    /// registry. FindObjectsOfType is only a fail-soft fallback when the manager
    /// is not available yet during very early scene construction.
    /// </summary>
    public static class SceneSlotCache
    {
        private static DisplaySlot[] _slots = new DisplaySlot[0];
        private static Box[] _boxes = new Box[0];
        private static bool _slotsDirty = true;
        private static bool _boxesDirty = true;

        public static DisplaySlot[] GetSlots()
        {
            if (!_slotsDirty && _slots != null)
                return _slots;

            _slots = BuildSlotsFromNativeRegistry();
            _slotsDirty = false;
            return _slots;
        }

        private static DisplaySlot[] BuildSlotsFromNativeRegistry()
        {
            try
            {
                if (DisplayManager.HasInstance && DisplayManager.Instance != null)
                {
                    var displayed = DisplayManager.Instance.DisplayedProducts;
                    if (displayed != null)
                    {
                        var result = new List<DisplaySlot>();
                        var seen = new HashSet<int>();

                        foreach (var pair in displayed)
                        {
                            var list = pair.Value;
                            if (list == null) continue;

                            for (int i = 0; i < list.Count; i++)
                            {
                                DisplaySlot slot = list[i];
                                if (slot == null) continue;

                                int id = slot.GetInstanceID();
                                if (seen.Add(id)) result.Add(slot);
                            }
                        }

                        // Empty registry is a valid state for an empty/new shop.
                        return result.ToArray();
                    }
                }
            }
            catch { }

            try { return UnityEngine.Object.FindObjectsOfType<DisplaySlot>(); }
            catch { return new DisplaySlot[0]; }
        }

        public static Box[] GetBoxes()
        {
            if (_boxesDirty || _boxes == null)
            {
                try { _boxes = UnityEngine.Object.FindObjectsOfType<Box>(); }
                catch { _boxes = new Box[0]; }
                _boxesDirty = false;
            }
            return _boxes;
        }

        public static void Invalidate()
        {
            _slotsDirty = true;
            _boxesDirty = true;
        }

        public static void InvalidateSlots() => _slotsDirty = true;
        public static void InvalidateBoxes() => _boxesDirty = true;
    }
}
