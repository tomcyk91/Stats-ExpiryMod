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
        public static Dictionary<int, Vector3> OriginalScales = new Dictionary<int, Vector3>();

        public static void InitBox(int id)
        {
            Stored[id] = new Dictionary<int, Queue<global::Product>>();
            Sucking[id] = new List<global::Product>();
        }

        public static void RemoveBox(int id)
        {
            Dictionary<int, Queue<global::Product>> byProduct;
            if (Stored.TryGetValue(id, out byProduct) && byProduct != null)
            {
                foreach (KeyValuePair<int, Queue<global::Product>> kvp in byProduct)
                {
                    Queue<global::Product> queue = kvp.Value;
                    if (queue == null) continue;

                    foreach (global::Product product in queue)
                    {
                        if (product != null)
                            OriginalScales.Remove(product.GetInstanceID());
                    }
                }
            }

            Stored.Remove(id);
            Sucking.Remove(id);
        }

        public static void StoreProduct(int boxId, int productId, global::Product p)
        {
            if (!Stored.ContainsKey(boxId)) InitBox(boxId);
            if (!Stored[boxId].ContainsKey(productId))
                Stored[boxId][productId] = new Queue<global::Product>();

            if (p != null)
                OriginalScales[p.GetInstanceID()] = p.transform.localScale;

            Stored[boxId][productId].Enqueue(p);
        }

        public static Vector3 GetOriginalScale(global::Product product)
        {
            if (product == null) return Vector3.one;

            Vector3 scale;
            if (OriginalScales.TryGetValue(product.GetInstanceID(), out scale) &&
                scale != Vector3.zero)
            {
                return scale;
            }

            return Vector3.one;
        }

        public static void ForgetProduct(global::Product product)
        {
            if (product != null)
                OriginalScales.Remove(product.GetInstanceID());
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

        // Zbuforowany gracz: nie odpytujemy GetComponent co klatkę.
        private static global::BoxInteraction _cachedPlayerBoxInteraction = null;
        private float _warningTimer = 0f;
        private GameObject _basketVisual;

        // Po odłożeniu BoxInteraction potrafi jeszcze przez kilka klatek zmienić
        // obrót i pozycję natywnego pudełka. Ustawienie wykonane tylko w Start()
        // staje się wtedy nieaktualne i model OBJ może wejść pod podłogę.
        private float _basketPlacementFixUntil = 0f;
        private float _nextBasketPlacementFixTime = 0f;
        private float _nextReturnInputTime = 0f;

        void Awake()
        {
            _boxId = this.GetInstanceID();
            TrashBoxState.InitBox(_boxId);
        }

        void Start()
        {
            HideIcons();

            // Do ustawienia modelu używamy przede wszystkim fizycznego BoxCollidera.
            // Renderer natywnego kartonu bywa przesunięty względem punktu, na którym
            // gra stawia pudełko, co wcześniej chowało koszyk pod podłogą.
            Bounds nativeBounds;
            bool hasNativeBounds = TryGetNativePlacementBounds(out nativeBounds);
            HideNativeBoxVisuals();

            if (!TryCreateBasketVisual(hasNativeBounds, nativeBounds))
            {
                // Bezpieczny fallback: jeżeli OBJ nie jest osadzony i nie ma pliku obok DLL,
                // pokazujemy dawny czarny karton zamiast niewidzialnego obiektu.
                RestoreNativeBoxVisualsAsFallback();
                ApplyLegacyBlackBoxColor();
                StatisticMod.Plugin.Log?.LogWarning("[TrashBasket] Nie udało się wczytać basket.obj. Używam awaryjnie dawnego czarnego kartonu.");
            }
            else
            {
                // Pierwsze dopasowanie w Start() nie wystarcza, ponieważ zaraz po spawnieniu
                // gra może podnieść koszyk i zmienić transform natywnego Boxa.
                RequestBasketPlacementFix(1.5f);
            }
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
                    TickBasketPlacementFix();
                    _warningTimer = 0f;
                }

                // LPM odkłada z koszyka jeden produkt na wskazaną półkę,
                // tak jak przy zwykłym kartonie. PPM nadal zbiera produkt z półki.
                // Obsługujemy odkładanie sami, ponieważ natywne Box.Data pozostaje puste,
                // a produkty koszyka są przechowywane w TrashBoxState.
                if (_wasHeld)
                    HandleReturnProductInput();

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

                // BoxInteraction po puszczeniu ustawia pozycję/obrót w kilku etapach.
                // Korygujemy model kilka razy, aż animacja odkładania się zakończy.
                RequestBasketPlacementFix(2.0f);
            }
        }

        private void HandleReturnProductInput()
        {
            if (Time.time < _nextReturnInputTime) return;
            if (!Input.GetMouseButtonDown(0)) return;

            DisplaySlot targetSlot = ResolveTargetDisplaySlot();
            if (targetSlot == null || targetSlot.Full) return;

            int productId;
            global::Product product;
            if (!TryFindStoredProductForSlot(targetSlot, out productId, out product))
                return;

            if (RestoreStoredProductToSlot(targetSlot, productId, product))
                _nextReturnInputTime = Time.time + 0.12f;
        }

        private DisplaySlot ResolveTargetDisplaySlot()
        {
            try
            {
                if (_cachedPlayerBoxInteraction != null &&
                    _cachedPlayerBoxInteraction.m_CurrentDisplaySlot != null)
                {
                    return _cachedPlayerBoxInteraction.m_CurrentDisplaySlot;
                }
            }
            catch { }

            try
            {
                Camera camera = Camera.main;
                if (camera == null) return null;

                Ray ray = new Ray(camera.transform.position, camera.transform.forward);
                RaycastHit hit;

                if (!Physics.Raycast(ray, out hit, 4.0f))
                    return null;

                GameObject hitObject = hit.collider != null ? hit.collider.gameObject : null;
                return hitObject != null
                    ? hitObject.GetComponentInParent<DisplaySlot>()
                    : null;
            }
            catch
            {
                return null;
            }
        }

        private bool TryFindStoredProductForSlot(
            DisplaySlot targetSlot,
            out int productId,
            out global::Product product)
        {
            productId = 0;
            product = null;

            Dictionary<int, Queue<global::Product>> byProduct;
            if (!TrashBoxState.Stored.TryGetValue(_boxId, out byProduct) ||
                byProduct == null ||
                byProduct.Count == 0)
            {
                return false;
            }

            int slotProductId = 0;
            try { slotProductId = targetSlot.ProductID; } catch { slotProductId = 0; }

            // Zajęta/przypisana półka: można zwrócić tylko ten sam produkt.
            if (slotProductId > 0)
            {
                Queue<global::Product> exactQueue;
                if (!byProduct.TryGetValue(slotProductId, out exactQueue))
                    return false;

                if (!CanSlotAccept(targetSlot, slotProductId))
                    return false;

                product = PeekFirstValid(exactQueue);
                if (product == null) return false;

                productId = slotProductId;
                return true;
            }

            // Pusty slot: wybieramy pierwszy typ produktu, który slot akceptuje.
            foreach (KeyValuePair<int, Queue<global::Product>> kvp in byProduct)
            {
                int candidateId = kvp.Key;
                if (candidateId <= 0 || !CanSlotAccept(targetSlot, candidateId))
                    continue;

                global::Product candidate = PeekFirstValid(kvp.Value);
                if (candidate == null) continue;

                productId = candidateId;
                product = candidate;
                return true;
            }

            return false;
        }

        private static bool CanSlotAccept(DisplaySlot slot, int productId)
        {
            if (slot == null || productId <= 0 || slot.Full) return false;

            try { return slot.CanRestockWith(productId); }
            catch { return false; }
        }

        private static global::Product PeekFirstValid(Queue<global::Product> queue)
        {
            if (queue == null) return null;

            while (queue.Count > 0)
            {
                global::Product product = queue.Peek();
                if (product != null && product.gameObject != null)
                    return product;

                queue.Dequeue();
                TrashBoxState.ForgetProduct(product);
            }

            return null;
        }

        private bool RestoreStoredProductToSlot(
            DisplaySlot targetSlot,
            int productId,
            global::Product product)
        {
            if (targetSlot == null || product == null || productId <= 0)
                return false;

            int expirationDay = int.MinValue;
            ProductExpirationComponent expiration =
                product.GetComponent<ProductExpirationComponent>();

            if (expiration != null)
                expirationDay = expiration.ExpirationDay;

            Vector3 originalScale = TrashBoxState.GetOriginalScale(product);

            try
            {
                List<global::Product> sucking;
                if (TrashBoxState.Sucking.TryGetValue(_boxId, out sucking) &&
                    sucking != null)
                {
                    sucking.Remove(product);
                }

                product.transform.SetParent(null);
                product.gameObject.SetActive(true);
                product.transform.localScale =
                    originalScale != Vector3.zero ? originalScale : Vector3.one;

                Rigidbody rb = product.GetComponent<Rigidbody>();
                if (rb != null) rb.isKinematic = true;

                var colliders = product.GetComponentsInChildren<Collider>(true);
                for (int i = 0; i < colliders.Count; i++)
                {
                    if (colliders[i] != null)
                        colliders[i].enabled = true;
                }

                // Nie sprawdzamy terminu. Produkt z 0 dni do końca nadal może
                // zostać świadomie odłożony z powrotem na właściwą półkę.
                targetSlot.AddProduct(productId, product);

                ProductExpirationComponent restoredExpiration =
                    ExpirationManager.EnsureExpiration(product, targetSlot);

                if (restoredExpiration != null && expirationDay != int.MinValue)
                {
                    restoredExpiration.ProductID = productId;
                    restoredExpiration.ExpirationDay = expirationDay;
                }

                RemoveStoredProduct(productId, product);
                ExpirationManager.UpdateMemory(targetSlot);
                LabelExclamationOverlay.RefreshSlotNow(targetSlot);

                StatisticMod.Plugin.DebugLog(
                    "[TrashBasket] Odłożono produkt z koszyka na półkę. ID=" +
                    productId + " | ExpirationDay=" +
                    (expirationDay == int.MinValue ? -1 : expirationDay));

                return true;
            }
            catch (Exception ex)
            {
                // Przy błędzie przywróć ukrycie produktu, żeby nie został
                // jednocześnie w świecie i w kolejce koszyka.
                try
                {
                    product.transform.SetParent(transform);
                    product.transform.localScale = Vector3.zero;
                    product.gameObject.SetActive(false);
                }
                catch { }

                StatisticMod.Plugin.Log?.LogWarning(
                    "[TrashBasket] Nie udało się odłożyć produktu na półkę: " +
                    ex.Message);

                return false;
            }
        }

        private void RemoveStoredProduct(int productId, global::Product product)
        {
            Dictionary<int, Queue<global::Product>> byProduct;
            if (!TrashBoxState.Stored.TryGetValue(_boxId, out byProduct) ||
                byProduct == null)
            {
                return;
            }

            Queue<global::Product> queue;
            if (!byProduct.TryGetValue(productId, out queue) || queue == null)
                return;

            if (queue.Count > 0 && queue.Peek() == product)
            {
                queue.Dequeue();
            }
            else
            {
                // Bezpieczny fallback na wypadek usuniętego/nullowego wpisu.
                Queue<global::Product> rebuilt = new Queue<global::Product>();
                while (queue.Count > 0)
                {
                    global::Product current = queue.Dequeue();
                    if (current != product) rebuilt.Enqueue(current);
                }
                byProduct[productId] = rebuilt;
                queue = rebuilt;
            }

            TrashBoxState.ForgetProduct(product);

            if (queue.Count == 0)
                byProduct.Remove(productId);
        }

        private void RequestBasketPlacementFix(float durationSeconds)
        {
            float now = Time.realtimeSinceStartup;
            _basketPlacementFixUntil = Mathf.Max(_basketPlacementFixUntil, now + Mathf.Max(0.25f, durationSeconds));
            _nextBasketPlacementFixTime = 0f;
        }

        private void TickBasketPlacementFix()
        {
            if (_basketVisual == null || _wasHeld) return;

            float now = Time.realtimeSinceStartup;
            if (now > _basketPlacementFixUntil) return;
            if (now < _nextBasketPlacementFixTime) return;

            _nextBasketPlacementFixTime = now + 0.10f;
            AlignBasketToCurrentBoxBounds();
        }

        private void AlignBasketToCurrentBoxBounds()
        {
            try
            {
                Bounds nativeBounds;
                if (!TryGetNativePlacementBounds(out nativeBounds)) return;

                Bounds basketBounds;
                if (!TryGetBounds(_basketVisual, out basketBounds)) return;

                // 8 cm zapasu zamiast 3 cm. Najważniejsze jest jednak to, że obliczenie
                // wykonywane jest już PO obrocie i odłożeniu Boxa, w aktualnym układzie świata.
                const float floorClearance = 0.08f;
                float targetBottomY = nativeBounds.min.y + floorClearance;

                Vector3 delta = new Vector3(
                    nativeBounds.center.x - basketBounds.center.x,
                    targetBottomY - basketBounds.min.y,
                    nativeBounds.center.z - basketBounds.center.z);

                // Ograniczenie chroni przed dużym skokiem przy chwilowo błędnym boundsie
                // podczas samej animacji podnoszenia/odkładania.
                delta.x = Mathf.Clamp(delta.x, -0.50f, 0.50f);
                delta.y = Mathf.Clamp(delta.y, -0.50f, 0.50f);
                delta.z = Mathf.Clamp(delta.z, -0.50f, 0.50f);

                if (delta.sqrMagnitude > 0.000001f)
                {
                    _basketVisual.transform.position += delta;
                }
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.DebugLog("[TrashBasket] Korekta położenia koszyka: " + ex.Message);
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
                if (c != null && c.gameObject.activeSelf) c.gameObject.SetActive(false);
            }
        }

        private bool TryGetNativePlacementBounds(out Bounds bounds)
        {
            bounds = new Bounds(transform.position, Vector3.one * 0.5f);

            try
            {
                // Najpewniejszy punkt odniesienia: główny collider pudełka.
                // To jego dolna krawędź faktycznie opiera się o podłogę.
                BoxCollider rootBoxCollider = GetComponent<BoxCollider>();
                if (rootBoxCollider != null && rootBoxCollider.enabled && !rootBoxCollider.isTrigger)
                {
                    bounds = rootBoxCollider.bounds;
                    return true;
                }

                // Fallback dla prefabów, które collider mają na dziecku.
                var colliders = GetComponentsInChildren<Collider>(true);
                bool foundCollider = false;
                foreach (var collider in colliders)
                {
                    if (collider == null || !collider.enabled || collider.isTrigger) continue;
                    if (_basketVisual != null && collider.transform.IsChildOf(_basketVisual.transform)) continue;

                    if (!foundCollider)
                    {
                        bounds = collider.bounds;
                        foundCollider = true;
                    }
                    else
                    {
                        bounds.Encapsulate(collider.bounds);
                    }
                }

                if (foundCollider) return true;

                // Ostatni fallback: obrys natywnego renderera.
                var renderers = GetComponentsInChildren<Renderer>(true);
                bool foundRenderer = false;
                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;
                    if (_basketVisual != null && renderer.transform.IsChildOf(_basketVisual.transform)) continue;

                    if (!foundRenderer)
                    {
                        bounds = renderer.bounds;
                        foundRenderer = true;
                    }
                    else
                    {
                        bounds.Encapsulate(renderer.bounds);
                    }
                }

                return foundRenderer;
            }
            catch
            {
                return false;
            }
        }

        private void HideNativeBoxVisuals()
        {
            try
            {
                var renderers = GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    if (renderer != null) renderer.enabled = false;
                }
            }
            catch { }
        }

        private void RestoreNativeBoxVisualsAsFallback()
        {
            try
            {
                var renderers = GetComponentsInChildren<Renderer>(true);
                foreach (var renderer in renderers)
                {
                    if (renderer == null) continue;
                    if (_basketVisual != null && renderer.transform.IsChildOf(_basketVisual.transform)) continue;
                    renderer.enabled = true;
                }
            }
            catch { }
        }

        private bool TryCreateBasketVisual(bool hasNativeBounds, Bounds nativeBounds)
        {
            try
            {
                string source;
                _basketVisual = TrashBasketObjLoader.LoadBasket("ExpiredProductsBasket_OBJ", out source);
                if (_basketVisual == null) return false;

                _basketVisual.hideFlags = HideFlags.HideAndDontSave;
                _basketVisual.transform.SetParent(transform, false);
                _basketVisual.transform.localPosition = Vector3.zero;
                _basketVisual.transform.localRotation = Quaternion.Euler(0f, 90f, 0f);
                _basketVisual.transform.localScale = Vector3.one;

                // Model ma wymiary ok. 0.47 x 0.215 x 0.31 po obrocie Y=90.
                // Dopasowanie do obrysu natywnego kartonu zachowuje właściwy rozmiar
                // zarówno na ziemi, jak i podczas trzymania przez BoxInteraction.
                if (hasNativeBounds)
                {
                    FitBasketToNativeBounds(nativeBounds);
                }
                else
                {
                    _basketVisual.transform.localScale = Vector3.one * 1.8f;
                    _basketVisual.transform.localPosition = new Vector3(0f, 0.04f, 0f);
                }

                var colliders = _basketVisual.GetComponentsInChildren<Collider>(true);
                foreach (var collider in colliders)
                {
                    if (collider != null) collider.enabled = false;
                }

                StatisticMod.Plugin.DebugLog("[TrashBasket] Załadowano czerwony koszyk OBJ (czarna rączka): " + source);
                return true;
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log?.LogWarning("[TrashBasket] Błąd tworzenia koszyka OBJ: " + ex.Message);
                if (_basketVisual != null)
                {
                    UnityEngine.Object.Destroy(_basketVisual);
                    _basketVisual = null;
                }
                return false;
            }
        }

        private void FitBasketToNativeBounds(Bounds nativeBounds)
        {
            Bounds basketBounds;
            if (!TryGetBounds(_basketVisual, out basketBounds))
            {
                _basketVisual.transform.localScale = Vector3.one * 1.8f;
                _basketVisual.transform.localPosition = new Vector3(0f, 0.04f, 0f);
                return;
            }

            // Koszyk ma być odrobinę mniejszy od fizycznego obrysu pudełka,
            // żeby nie wychodził poza collider podczas noszenia i odkładania.
            float targetWidth = Mathf.Max(0.05f, nativeBounds.size.x * 0.92f);
            float targetDepth = Mathf.Max(0.05f, nativeBounds.size.z * 0.92f);
            float scaleX = basketBounds.size.x > 0.0001f ? targetWidth / basketBounds.size.x : 1f;
            float scaleZ = basketBounds.size.z > 0.0001f ? targetDepth / basketBounds.size.z : 1f;
            float scale = Mathf.Clamp(Mathf.Min(scaleX, scaleZ), 0.50f, 4.00f);
            _basketVisual.transform.localScale = Vector3.one * scale;

            if (!TryGetBounds(_basketVisual, out basketBounds)) return;

            // Najważniejsza poprawka: dół modelu ustawiamy względem dolnej
            // krawędzi COLLIDERA, a nie renderera kartonu. Dodatkowe 8 cm
            // daje zapas również dla animacji odkładania i nierównej powierzchni.
            const float floorClearance = 0.08f;
            Vector3 delta = new Vector3(
                nativeBounds.center.x - basketBounds.center.x,
                nativeBounds.min.y + floorClearance - basketBounds.min.y,
                nativeBounds.center.z - basketBounds.center.z);

            _basketVisual.transform.position += delta;
        }

        private static bool TryGetBounds(GameObject root, out Bounds bounds)
        {
            bounds = new Bounds();
            if (root == null) return false;

            bool found = false;
            var renderers = root.GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null) continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found;
        }

        private void ApplyLegacyBlackBoxColor()
        {
            var renderers = GetComponentsInChildren<Renderer>(true);
            foreach (var renderer in renderers)
            {
                if (renderer == null || renderer.material == null) continue;
                if (_basketVisual != null && renderer.transform.IsChildOf(_basketVisual.transform)) continue;
                if (!renderer.gameObject.name.Contains("Indicator"))
                    renderer.material.color = new Color(0.12f, 0.12f, 0.12f, 1f);
            }
        }
    }

    // ==============================================================
    // 2. SPAWNER KOSZYKA (klawisz z configu)
    // ==============================================================
    public class TrashBoxSpawner : MonoBehaviour
    {
        public TrashBoxSpawner(IntPtr ptr) : base(ptr) { }

        void Update()
        {
            KeyCode spawnKey = KeyCode.U;
            try
            {
                if (PluginConfig.TrashBasketSpawnKey != null)
                    spawnKey = PluginConfig.TrashBasketSpawnKey.Value;
            }
            catch { }

            if (spawnKey == KeyCode.None || !Input.GetKeyDown(spawnKey)) return;

            var player = PlayerManager.Instance?.LocalPlayer;
            if (player == null) return;

            // Nie twórz drugiego koszyka, gdy gracz już trzyma pudełko/koszyk.
            BoxInteraction playerBoxInteraction = null;
            try { playerBoxInteraction = player.GetComponent<BoxInteraction>(); } catch { }
            if (playerBoxInteraction != null && playerBoxInteraction.m_Box != null)
            {
                StatisticMod.Plugin.DebugLog("[TrashBasket] Nie tworzę koszyka: gracz już trzyma pudełko.");
                return;
            }

            Box boxPrefab = BoxGenerator.Instance?.m_ProduceBox;
            if (boxPrefab == null) return;

            // Obiekt powstaje blisko gracza tylko na ułamek klatki. Zaraz potem
            // przekazujemy go do zwykłego systemu PlayerInteraction, dokładnie jak
            // przy zabraniu kartonu z regału.
            Vector3 pos = player.transform.position + player.transform.forward * 0.70f + Vector3.up * 0.85f;
            Quaternion rot = Quaternion.LookRotation(player.transform.forward, Vector3.up);
            GameObject go = UnityEngine.Object.Instantiate(boxPrefab.gameObject, pos, rot);
            go.name = "ExpiredProductsBasket";

            var boxComp = go.GetComponent<Box>();
            if (boxComp != null && boxComp.Data != null)
            {
                boxComp.Data.ProductCount = 0;
                boxComp.Data.ProductID = 0;
            }

            go.AddComponent<TrashBoxComponent>();
            go.SetActive(true);

            bool pickedUp = TryPutBasketInPlayerHands(player.gameObject, boxComp);
            if (!pickedUp)
            {
                // Fallback: koszyk pozostaje przed graczem i można go normalnie podnieść.
                go.transform.position = player.transform.position + player.transform.forward * 1.15f + Vector3.up * 0.45f;
                StatisticMod.Plugin.Log?.LogWarning("[TrashBasket] Nie udało się automatycznie włożyć koszyka do rąk. Pozostawiam go przed graczem.");
            }

            StatisticMod.Plugin.DebugLog("[TrashBasket] Utworzono koszyk na przeterminowane produkty. Klawisz=" + spawnKey + " | w rękach=" + pickedUp);
        }

        private static bool TryPutBasketInPlayerHands(GameObject player, Box box)
        {
            if (player == null || box == null) return false;

            try
            {
                PlayerInteraction playerInteraction = player.GetComponent<PlayerInteraction>();
                if (playerInteraction == null)
                    playerInteraction = UnityEngine.Object.FindObjectOfType<PlayerInteraction>();
                if (playerInteraction == null) return false;

                // Box implementuje IInteractable. Użycie zwykłej ścieżki Interact
                // ustawia BoxInteraction, PlayerObjectHolder, pozycję w rękach i inputy.
                IInteractable interactable = box.TryCast<IInteractable>();
                if (interactable == null) return false;

                playerInteraction.CurrentInteractable = interactable;
                playerInteraction.Interact(false, false);
                return true;
            }
            catch (Exception ex)
            {
                StatisticMod.Plugin.Log?.LogWarning("[TrashBasket] Auto-podniesienie koszyka nie powiodło się: " + ex.Message);
                return false;
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
                        LabelExclamationOverlay.RefreshSlotNow(slot);
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