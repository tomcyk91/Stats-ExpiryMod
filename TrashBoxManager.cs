using System;
using System.Collections.Generic;
using UnityEngine;
using HarmonyLib;
using StatisticMod;
using Il2CppInterop.Runtime;
using Il2CppInterop.Runtime.InteropTypes;

namespace SmartExpiration
{
    // ==============================================================
    // 0. BEZPIECZNY STAN
    // ==============================================================
    public static class TrashBoxState
    {
        public static Dictionary<int, Dictionary<int, Queue<global::Product>>> Stored = new Dictionary<int, Dictionary<int, Queue<global::Product>>>();
        public static Dictionary<int, List<global::Product>> Sucking = new Dictionary<int, List<global::Product>>();

        public static void InitBox(int id)
        {
            Stored[id] = new Dictionary<int, Queue<global::Product>>();
            Sucking[id] = new List<global::Product>();
        }

        public static void RemoveBox(int id)
        {
            Stored.Remove(id);
            Sucking.Remove(id);
        }

        public static void StoreProduct(int boxId, int productId, global::Product p)
        {
            if (!Stored.ContainsKey(boxId)) InitBox(boxId);
            if (!Stored[boxId].ContainsKey(productId)) Stored[boxId][productId] = new Queue<global::Product>();
            Stored[boxId][productId].Enqueue(p);
        }
    }

    // ==============================================================
    // 1. KOMPONENT KARTONU
    // ==============================================================
    public class TrashBoxComponent : MonoBehaviour
    {
        public TrashBoxComponent(IntPtr ptr) : base(ptr) { }

        private int _boxId;
        private bool _wasHeld = false;
        private static Canvas _cachedWarningCanvas = null;

        // ⚡ ZBUFOROWANY GRACZ: Aby nie odpalać ciężkiego GetComponent co klatkę!
        private static global::BoxInteraction _cachedPlayerBoxInteraction = null;
        private float _warningTimer = 0f;

        void Awake()
        {
            _boxId = this.GetInstanceID();
            TrashBoxState.InitBox(_boxId);
        }

        void Start()
        {
            ApplyColor();
            HideIcons();
        }

        void OnDestroy()
        {
            TrashBoxState.RemoveBox(_boxId);
            RestoreWarningCanvas();
        }

        void OnDisable()
        {
            RestoreWarningCanvas();
        }

        void Update()
        {
            try
            {
                _warningTimer += Time.deltaTime;
                if (_warningTimer > 0.1f)
                {
                    HandleWarningCanvas();
                    _warningTimer = 0f;
                }

                if (TrashBoxState.Sucking.ContainsKey(_boxId))
                {
                    var list = TrashBoxState.Sucking[_boxId];
                    for (int i = list.Count - 1; i >= 0; i--)
                    {
                        var p = list[i];
                        if (p == null || p.gameObject == null) { list.RemoveAt(i); continue; }

                        p.transform.SetParent(transform);
                        p.transform.localPosition = Vector3.Lerp(p.transform.localPosition, Vector3.up * 0.2f, Time.deltaTime * 12f);
                        p.transform.localScale = Vector3.Lerp(p.transform.localScale, Vector3.zero, Time.deltaTime * 12f);

                        if (p.transform.localScale.x < 0.05f)
                        {
                            p.gameObject.SetActive(false);
                            list.RemoveAt(i);
                        }
                    }
                }
            }
            catch { }
        }

        private void HandleWarningCanvas()
        {
            bool isHeld = false;

            // ⚡ ATOMOWA OPTYMALIZACJA CPU: Odpytujemy ręce gracza szybciej.
            if (_cachedPlayerBoxInteraction == null && PlayerManager.Instance != null && PlayerManager.Instance.LocalPlayer != null)
            {
                _cachedPlayerBoxInteraction = PlayerManager.Instance.LocalPlayer.GetComponent<global::BoxInteraction>();
            }

            if (_cachedPlayerBoxInteraction != null && _cachedPlayerBoxInteraction.m_Box != null && _cachedPlayerBoxInteraction.m_Box.gameObject == this.gameObject)
            {
                isHeld = true;
            }

            if (isHeld && !_wasHeld)
            {
                _wasHeld = true;
                if (_cachedWarningCanvas == null)
                {
                    var wc = UnityEngine.Object.FindObjectOfType<WarningCanvas>();
                    if (wc != null) _cachedWarningCanvas = wc.GetComponent<Canvas>();
                }

                if (_cachedWarningCanvas != null) _cachedWarningCanvas.enabled = false;
            }
            else if (!isHeld && _wasHeld)
            {
                _wasHeld = false;
                RestoreWarningCanvas();
            }
        }

        private void RestoreWarningCanvas()
        {
            if (_cachedWarningCanvas != null) _cachedWarningCanvas.enabled = true;
        }

        private void HideIcons()
        {
            var canvases = GetComponentsInChildren<Canvas>(true);
            foreach (var c in canvases)
            {
                if (c.gameObject.activeSelf) c.gameObject.SetActive(false);
            }
        }

        private void ApplyColor()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var r in renderers)
                if (r.material != null && !r.gameObject.name.Contains("Indicator"))
                    r.material.color = new Color(0.12f, 0.12f, 0.12f, 1f);
        }
    }

    // ==============================================================
    // 2. SPAWNER (Klawisz U)
    // ==============================================================
    public class TrashBoxSpawner : MonoBehaviour
    {
        public TrashBoxSpawner(IntPtr ptr) : base(ptr) { }
        void Update()
        {
            if (Input.GetKeyDown(KeyCode.U))
            {
                var player = PlayerManager.Instance?.LocalPlayer;
                if (player == null) return;

                Box boxPrefab = BoxGenerator.Instance?.m_ProduceBox;
                if (boxPrefab != null)
                {
                    Vector3 pos = player.transform.position + player.transform.forward * 1.5f + Vector3.up * 1.0f;
                    GameObject go = UnityEngine.Object.Instantiate(boxPrefab.gameObject, pos, Quaternion.identity);

                    var boxComp = go.GetComponent<Box>();
                    if (boxComp != null && boxComp.Data != null)
                    {
                        boxComp.Data.ProductCount = 0;
                        boxComp.Data.ProductID = 0;
                    }

                    go.AddComponent<TrashBoxComponent>();
                    go.SetActive(true);
                    StatisticMod.Plugin.DebugLog("[TrashBox] Zrespawnowano nowy czarny karton!");
                }
            }
        }
    }
}

namespace SmartExpiration.Patches
{
    // ==============================================================
    // 3. BLOKADA WKŁADANIA RĘCZNEGO
    // ==============================================================
    [HarmonyPatch(typeof(Box), nameof(Box.AddProduct))]
    internal static class TrashBox_BlockManual_Patch
    {
        [HarmonyPrefix]
        static bool Prefix(Box __instance)
        {
            var trash = __instance.GetComponent<SmartExpiration.TrashBoxComponent>();
            if (trash != null)
            {
                return false;
            }
            return true;
        }
    }

    // ==============================================================
    // 4. PAKOWANIE Z PÓŁKI DO KARTONU 
    // ==============================================================
    [HarmonyPatch(typeof(BoxInteraction), "TryTakeProductFromSlot")]
    internal static class TrashBox_Take_Patch
    {
        private static float _cooldown = 0f;

        [HarmonyPrefix]
        static bool Prefix(BoxInteraction __instance)
        {
            try
            {
                if (__instance.m_Box == null) return true;

                var trash = __instance.m_Box.GetComponent<SmartExpiration.TrashBoxComponent>();
                if (trash == null) return true;

                if (Time.time < _cooldown) return false;

                DisplaySlot slot = __instance.m_CurrentDisplaySlot;
                if (slot == null || !slot.HasProduct) return false;

                int day = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;

                ExpirationManager.SyncShelf(slot);

                var products = ExpirationSaveManager.GetSortedProducts(slot.transform);
                List<int> remainingDates = new List<int>();

                bool foundExpired = false;
                int expiredDate = -1;

                for (int i = 0; i < products.Count; i++)
                {
                    var p = products[i];
                    var exp = p.GetComponent<ProductExpirationComponent>();

                    if (exp != null)
                    {
                        int daysLeft = exp.ExpirationDay - day;

                        if (!foundExpired && daysLeft <= 0)
                        {
                            foundExpired = true;
                            expiredDate = exp.ExpirationDay;
                        }
                        else
                        {
                            remainingDates.Add(exp.ExpirationDay);
                        }
                    }
                }

                if (!foundExpired) return false;

                int safeProductId = slot.ProductID;
                var takeMethod = AccessTools.Method(typeof(DisplaySlot), "TakeProductFromDisplay");

                if (takeMethod != null)
                {
                    var result = takeMethod.Invoke(slot, null);

                    if (result != null)
                    {
                        IntPtr ptr = IL2CPP.Il2CppObjectBaseToPtrNotNull((Il2CppObjectBase)result);
                        global::Product pulled = new global::Product(ptr);

                        var pulledExp = pulled.GetComponent<ProductExpirationComponent>();
                        if (pulledExp != null) pulledExp.ExpirationDay = expiredDate;

                        pulled.transform.SetParent(trash.transform);
                        var rb = pulled.GetComponent<Rigidbody>();
                        if (rb != null) rb.isKinematic = true;
                        foreach (var c in pulled.GetComponentsInChildren<Collider>(true)) c.enabled = false;

                        int boxId = trash.GetInstanceID();
                        TrashBoxState.StoreProduct(boxId, safeProductId, pulled);
                        TrashBoxState.Sucking[boxId].Add(pulled);

                        _cooldown = Time.time + 0.12f;

                        var remainingProducts = ExpirationSaveManager.GetSortedProducts(slot.transform);

                        for (int i = 0; i < remainingProducts.Count && i < remainingDates.Count; i++)
                        {
                            var rComp = ExpirationManager.EnsureExpiration(remainingProducts[i], slot);
                            if (rComp != null)
                            {
                                rComp.ExpirationDay = remainingDates[i];
                            }
                        }

                        ExpirationManager.UpdateMemory(slot);
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
        }
    }

    // ==============================================================
    // 5. WYRZUCANIE DO KOSZA
    // ==============================================================
    [HarmonyPatch(typeof(BoxInteraction), "ThrowIntoTrashBin")]
    internal static class TrashBox_Final_Patch
    {
        [HarmonyPrefix]
        static bool Prefix(BoxInteraction __instance)
        {
            try
            {
                if (__instance.m_Box == null) return true;
                var trash = __instance.m_Box.GetComponent<SmartExpiration.TrashBoxComponent>();

                if (trash != null)
                {
                    int boxId = trash.GetInstanceID();
                    int day = DayCycleManager.Instance != null ? DayCycleManager.Instance.CurrentDay : 1;
                    int totalItemsThrown = 0;

                    if (TrashBoxState.Stored.ContainsKey(boxId))
                    {
                        foreach (var kvp in TrashBoxState.Stored[boxId])
                        {
                            int pid = kvp.Key;
                            int qty = kvp.Value.Count;
                            if (qty <= 0) continue;

                            totalItemsThrown += qty;
                            float price = PriceManager.Instance != null ? PriceManager.Instance.SellingPrice(pid) : 0f;

                            if (SalesUnifiedFinal.WeightPerUnit != null && SalesUnifiedFinal.WeightPerUnit.TryGetValue(pid, out float weightOfSingleItem))
                            {
                                float kgSpoiled = qty * weightOfSingleItem;
                                float loss = price * kgSpoiled;
                                StatsStore.AddThrownF(day, pid, kgSpoiled, loss, true);
                            }
                            else
                            {
                                float loss = price * qty;
                                StatsStore.AddThrownF(day, pid, (float)qty, loss, false);
                            }

                            foreach (var p in kvp.Value)
                            {
                                if (p != null && p.gameObject != null) UnityEngine.Object.Destroy(p.gameObject);
                            }
                        }
                    }

                    if (totalItemsThrown > 0 && StoreLevelManager.Instance != null)
                    {
                        StoreLevelManager.Instance.AddPoint(totalItemsThrown);
                    }

                    StatsStore.SaveNow();
                    __instance.DropBox();
                    TrashBoxState.RemoveBox(boxId);

                    trash.gameObject.SetActive(false);
                    UnityEngine.Object.Destroy(trash.gameObject, 0.1f);

                    return false;
                }
                return true;
            }
            catch
            {
                return true;
            }
        }
    }
}