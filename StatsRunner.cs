using Il2CppInterop.Runtime.Attributes;
using Il2CppInterop.Runtime.Injection;
using UnityEngine;
using System;

namespace StatisticMod
{
    public class StatsRunner : MonoBehaviour
    {
        public StatsRunner(System.IntPtr ptr) : base(ptr) { }

        private static StatsRunner _instance;

        private int _lastDay = -1;
        private float _timer = 0f;
        private float _slowTimer = 0f;
        private bool _bootDone = false;
        private bool _pendingDayChangeSave = false;
        private float _saveDayChangeAfter = 0f;
        private bool _newGameResetPending = false;
        private float _newGameResetDeadline = 0f;

        [HideFromIl2Cpp]
        public static void Create()
        {
            var existing = GameObject.Find("StatisticMod.Runner");
            if (existing != null)
            {
                _instance = existing.GetComponent<StatsRunner>();
                return;
            }

            ClassInjector.RegisterTypeInIl2Cpp<StatsRunner>();
            var go = new GameObject("StatisticMod.Runner");
            UnityEngine.Object.DontDestroyOnLoad(go);
            _instance = go.AddComponent<StatsRunner>();
        }

        void Awake()
        {
            _instance = this;
        }

        [HideFromIl2Cpp]
        public static void NotifyNewGameReset()
        {
            if (_instance == null)
            {
                var go = GameObject.Find("StatisticMod.Runner");
                if (go != null) _instance = go.GetComponent<StatsRunner>();
            }

            if (_instance == null) return;

            _instance._lastDay = -1;
            _instance._pendingDayChangeSave = false;
            _instance._saveDayChangeAfter = 0f;
            _instance._newGameResetPending = true;
            _instance._newGameResetDeadline = Time.realtimeSinceStartup + 30f;
            _instance._timer = 0f;
            _instance._slowTimer = 0f;
        }

        void Update()
        {
            try
            {
                if (!_bootDone)
                {
                    Application.targetFrameRate = 120;
                    StatsStore.Init();
                    BusinessAnalysisStore.Init();
                    DailySummaryStore.Init();
                    Plugin.Log.LogInfo($"[StatisticMod] AKTYWNY SLOT={StatsStore.CurrentSlot} | PLIK STATYSTYK={StatsStore.AbsoluteFilePath}");
                    try { GameDayOverlay.Create(); } catch { }
                    _bootDone = true;
                }

                if (Input.GetKeyDown(KeyCode.F8))
                {
                    try
                    {
                        StatsStore.Load();
                        BusinessAnalysisStore.Load();
                        DailySummaryStore.Load();
                        if (_lastDay > 0) BusinessAnalysisStore.SetCurrentDay(_lastDay);
                        Plugin.Log.LogInfo($"[StatisticMod] F8: przeladowano. Plik = {StatsStore.AbsoluteFilePath}");
                    }
                    catch (Exception exF8)
                    {
                        Plugin.Log.LogWarning("[StatisticMod] F8 reload error: " + exF8.Message);
                    }
                }

                if (StatsAppManager._instance != null)
                    StatsAppManager._instance.TickTimers(Time.deltaTime);

                _timer += Time.deltaTime;
                _slowTimer += Time.deltaTime;

                if (_timer >= 0.5f)
                {
                    _timer = 0f;
                    ModLocalization.Tick();

                    if (StatsAppManager._instance != null)
                        StatsAppManager._instance.ManualUpdate();

                    StatsAppManager.TickRealtimeUI();
                    StatsStore.TickSlotDetectFromGame();
                    DailySummaryStore.TickPath();

                    if (DayCycleManager.Instance != null)
                    {
                        int day = Mathf.Max(1, DayCycleManager.Instance.CurrentDay);

                        // Po kliknięciu „Nowa gra” stara scena może jeszcze przez kilka
                        // klatek zgłaszać poprzedni numer dnia. Nie wolno wtedy zamknąć
                        // starego dnia i odtworzyć go w świeżo wyczyszczonych danych.
                        if (_newGameResetPending)
                        {
                            bool newGameReady = day <= 1;
                            bool timedOut = Time.realtimeSinceStartup >= _newGameResetDeadline;

                            if (newGameReady || timedOut)
                            {
                                _newGameResetPending = false;
                                _newGameResetDeadline = 0f;
                                _lastDay = day;
                                StatsStore.SetCurrentDay(day);
                                BusinessAnalysisStore.SetCurrentDay(day);
                            }
                        }
                        else
                        {
                            StatsStore.SetCurrentDay(day);

                            if (_lastDay == -1)
                            {
                                _lastDay = day;
                                BusinessAnalysisStore.SetCurrentDay(day);
                            }
                            else if (_lastDay != day)
                            {
                                int closedDay = _lastDay;
                                BusinessAnalysisStore.CloseDay(closedDay);
                                _lastDay = day;
                                BusinessAnalysisStore.SetCurrentDay(day);

                                _pendingDayChangeSave = true;
                                _saveDayChangeAfter = Time.realtimeSinceStartup + 2f;
                                StockSnapshotService.Invalidate();
                            }
                            else
                            {
                                BusinessAnalysisStore.SetCurrentDay(day);
                            }
                        }
                    }
                }

                if (_slowTimer >= 5.0f)
                {
                    _slowTimer = 0f;
                    StatsAppManager.InstanceTryInstall();
                    BusinessAnalysisStore.TickPath();
                    // PERF: no periodic JSON serialization/write during gameplay.
                    // BusinessAnalysisStore is flushed by real game saves and day changes.
                }

                if (_pendingDayChangeSave && Time.realtimeSinceStartup >= _saveDayChangeAfter)
                {
                    _pendingDayChangeSave = false;
                    _saveDayChangeAfter = 0f;

                    try { StatsStore.SaveNow(); }
                    catch (Exception ex)
                    {
                        Plugin.DebugWarning("[StatisticMod] Delayed stats save failed: " + ex.Message);
                    }

                    try { BusinessAnalysisStore.SaveNow(); }
                    catch (Exception ex)
                    {
                        Plugin.DebugWarning("[StatisticMod] Delayed analysis save failed: " + ex.Message);
                    }

                    try { DailySummaryStore.SaveNow(); }
                    catch (Exception ex)
                    {
                        Plugin.DebugWarning("[StatisticMod] Delayed daily summary save failed: " + ex.Message);
                    }
                }

                SmartExpiration.SEProfiler.EndFrame();
            }
            catch { }
        }

        void OnApplicationQuit()
        {
            try { BusinessAnalysisStore.SaveNow(); } catch { }
            try { DailySummaryStore.SaveNow(); } catch { }
        }
    }
}
