using UnityEngine;
using System;

namespace SmartExpiration
{
    public class ProductExpirationComponent : MonoBehaviour
    {
        public int ExpirationDay;
        public int ProductID;

        public ProductExpirationComponent(IntPtr ptr) : base(ptr) { }

        public int GetDaysLeft(int currentDay)
        {
            return ExpirationDay - currentDay;
        }

        void Awake()
        {
            // BEZPIECZNA FLAGA: Ignoruje skrypt, ale nie niszczy samego modelu produktu w zapisie gry!
            this.hideFlags = HideFlags.DontSave | HideFlags.HideInInspector;
        }
    }
}