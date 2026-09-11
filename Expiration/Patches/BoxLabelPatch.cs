using HarmonyLib;
using Il2CppInterop.Runtime;
using StatisticMod;
using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace SmartExpiration.Patches
{
    [HarmonyPatch(typeof(Box), "Start")]
    public static class BoxLabelPatch
    {

        public static bool Prepare()
        {
            return AccessTools.Method(typeof(Box), "Start") != null;
        }

        public static List<BoxExpirationLabel> AllLabels = new List<BoxExpirationLabel>();
        private static bool _updaterSpawned = false;

        public static BoxExpirationLabel HeldBoxLabel = null;
        public static int ClipboardDate = -1;
        public static int ClipboardFrame = -1;

        public static void EnqueueClipboardDate(int date)
        {
            try
            {
                ClipboardDate = date;
                ClipboardFrame = Time.frameCount;
                StatisticMod.Plugin.DebugLog($"[EXP DEBUG] Enqueued clipboard date {date} at frame {ClipboardFrame}");
            }
            catch { }
        }

        public static bool TryDequeueClipboardDate(out int date)
        {
            date = -1;
            try
            {
                if (ClipboardDate == -1) return false;

                int age = Time.frameCount - ClipboardFrame;
                if (age <= 15)
                {
                    date = ClipboardDate;
                    ClipboardDate = -1;
                    ClipboardFrame = -1;
                    StatisticMod.Plugin.DebugLog($"[EXP DEBUG] Dequeued clipboard date {date} at frame {Time.frameCount} (age {age})");
                    return true;
                }
                else
                {
                    ClipboardDate = -1;
                    ClipboardFrame = -1;
                    StatisticMod.Plugin.DebugLog($"[EXP DEBUG] Clipboard date expired (age {age}), ignored.");
                    return false;
                }
            }
            catch
            {
                ClipboardDate = -1;
                ClipboardFrame = -1;
                return false;
            }
        }

        public static int GetConfigOverrideDirectly(int productId)
        {
            try
            {
                Type loaderType = typeof(CustomExpirationLoader);

                foreach (var field in loaderType.GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
                {
                    if (field.FieldType == typeof(Dictionary<int, int>))
                    {
                        var dict = (Dictionary<int, int>)field.GetValue(null);
                        if (dict != null && dict.TryGetValue(productId, out int days)) return days;
                    }
                }

                foreach (var prop in loaderType.GetProperties(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static))
                {
                    if (prop.PropertyType == typeof(Dictionary<int, int>))
                    {
                        var dict = (Dictionary<int, int>)prop.GetValue(null, null);
                        if (dict != null && dict.TryGetValue(productId, out int days)) return days;
                    }
                }
            }
            catch { }

            return -1;
        }

        public static void Postfix(Box __instance)
        {
            try
            {
                if (__instance == null || __instance.gameObject == null) return;

                if (!_updaterSpawned)
                {
                    GameObject updaterGo = new GameObject("BoxLabelGlobalUpdater");
                    UnityEngine.Object.DontDestroyOnLoad(updaterGo);
                    updaterGo.AddComponent(Il2CppType.Of<BoxLabelGlobalUpdater>());
                    _updaterSpawned = true;
                }

                if (__instance.gameObject.GetComponent<BoxExpirationLabel>() == null)
                {
                    __instance.gameObject.AddComponent(Il2CppType.Of<BoxExpirationLabel>());
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugLog($"[BoxLabelPatch] Postfix error: {ex.Message}");
            }
        }
    }

    // BoxExpirationLabel and BoxLabelGlobalUpdater classes are in BoxExpirationLabel.cs (provided above).
}
