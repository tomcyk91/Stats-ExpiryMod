using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using System;

namespace StatisticMod
{
    public class StatsRunner : MonoBehaviour
    {
        public StatsRunner(System.IntPtr ptr) : base(ptr) { }

        private int _lastDay = -1;
        private float _timer = 0f;
        private float _slowTimer = 0f;
        private bool _bootDone = false;

        [HideFromIl2Cpp]
        public static void Create()
        {
            if (GameObject.Find("StatisticMod.Runner") != null) return;
            ClassInjector.RegisterTypeInIl2Cpp<StatsRunner>();
            var go = new GameObject("StatisticMod.Runner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            go.AddComponent<StatsRunner>();
        }

        void Update()
        {
            try
            {
                if (!_bootDone)
                {
                    Application.targetFrameRate = 120; // ⚡ Wymuszamy odblokowanie FPS
                    StatsStore.Init();
                    try { GameDayOverlay.Create(); } catch { }
                    _bootDone = true;
                }

                if (StatsAppManager._instance != null)
                {
                    StatsAppManager._instance.TickTimers(Time.deltaTime);
                }

                _timer += Time.deltaTime;
                _slowTimer += Time.deltaTime;

                if (_timer >= 0.5f)
                {
                    _timer = 0f;
                    if (StatsAppManager._instance != null) StatsAppManager._instance.ManualUpdate(); // ⚡ Przeniesiono z co klatkę na 0.5s!
                    StatsAppManager.TickRealtimeUI(); // ⚡ Usunięto TickSlotDetect (oszczędza dysk)

                    if (DayCycleManager.Instance != null)
                    {
                        int day = DayCycleManager.Instance.CurrentDay;
                        StatsStore.SetCurrentDay(day);

                        if (_lastDay != day)
                        {
                            if (_lastDay != -1) StatsStore.SaveNow();
                            _lastDay = day;
                        }
                    }
                }

                if (_slowTimer >= 5.0f)
                {
                    _slowTimer = 0f;
                    StatsAppManager.InstanceTryInstall();
                }

                SmartExpiration.SEProfiler.EndFrame(); // PROFILER: raport klatek co 2s
            }
            catch { /* Pochłaniamy błędy by nie lagowały dysku */ }
        }

        void OnApplicationQuit() { }
    }
}