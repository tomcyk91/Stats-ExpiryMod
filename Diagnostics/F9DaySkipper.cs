using System;
using UnityEngine;

namespace SmartExpiration
{
    public class F9DaySkipper : MonoBehaviour
    {
        public F9DaySkipper(IntPtr ptr) : base(ptr) { }

        void Update()
        {
            if (Input.GetKeyDown(KeyCode.F9))
            {
                if (DayCycleManager.Instance != null)
                {
                    if (SaveManager.Instance != null) SaveManager.Instance.Save();
                    DayCycleManager.Instance.FinishTheDay();
                }
            }
        }
    }
}