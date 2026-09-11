using UnityEngine;
using System;

namespace SmartExpiration
{
    public class ProductExpirationComponent : MonoBehaviour
    {
        // Absolute game day on which this physical product expires.
        public int ExpirationDay;

        // Game day on which this physical product entered the delivery flow.
        // This belongs to the PRODUCT, not to the cardboard box.
        public int DeliveryDay;

        public int ProductID;

        public ProductExpirationComponent(IntPtr ptr) : base(ptr) { }

        public int GetDaysLeft(int currentDay)
        {
            return ExpirationDay - currentDay;
        }

        void Awake()
        {
            // Component metadata itself must not become part of the native game save.
            this.hideFlags =
                HideFlags.DontSave |
                HideFlags.HideInInspector;
        }
    }
}
