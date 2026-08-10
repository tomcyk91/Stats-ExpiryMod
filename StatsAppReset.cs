using Il2CppInterop.Runtime.Attributes;

namespace StatisticMod
{
    public partial class StatsAppManager
    {
        [HideFromIl2Cpp]
        public static void NotifyStatisticsReset()
        {
            StatsStore.SuspendReload = false;

            try
            {
                if (_instance == null) return;

                _instance.HideStats();
                _instance._selectedDay = -1;
                _instance._lastRefreshDay = -1;
                _instance._nextUiRefresh = 0f;
                _instance._buildQueued = false;
            }
            catch { }
        }
    }
}
