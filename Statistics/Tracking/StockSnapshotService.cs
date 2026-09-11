using System.Collections.Generic;
using UnityEngine;

namespace StatisticMod
{
    public sealed class ProductStockState
    {
        public int ProductId;
        public int ShopUnits;
        public int WarehouseUnits;
        public bool IsDisplayed;
        public int TotalUnits => ShopUnits + WarehouseUnits;
    }

    public sealed class StockSnapshot
    {
        private readonly Dictionary<int, ProductStockState> _byProduct = new();

        public ProductStockState Get(int productId)
        {
            if (!_byProduct.TryGetValue(productId, out ProductStockState state))
            {
                state = new ProductStockState { ProductId = productId };
                _byProduct[productId] = state;
            }
            return state;
        }

        public bool TryGet(int productId, out ProductStockState state)
            => _byProduct.TryGetValue(productId, out state);
    }

    public static class StockSnapshotService
    {
        // Kilku klientów może kończyć zakupy w tej samej klatce, szczególnie
        // przy zamykaniu sklepu. Nie skanujemy wtedy setek półek osobno dla
        // każdego klienta, tylko współdzielimy krótko ważny snapshot.
        private const float CacheLifetimeSeconds = 0.25f;
        private static StockSnapshot _cachedSnapshot;
        private static float _cachedAt = -1000f;

        public static StockSnapshot Capture(bool forceRefresh = false)
        {
            float now = Time.realtimeSinceStartup;
            if (!forceRefresh &&
                _cachedSnapshot != null &&
                now - _cachedAt <= CacheLifetimeSeconds)
            {
                return _cachedSnapshot;
            }

            var snapshot = new StockSnapshot();

            DisplaySlot[] slots = StatsSearchCache.GetSlots();
            if (slots != null)
            {
                for (int i = 0; i < slots.Length; i++)
                {
                    DisplaySlot slot = slots[i];
                    if (slot == null) continue;

                    int productId = 0;
                    try { productId = slot.ProductID; } catch { }
                    if (productId <= 0) continue;

                    ProductStockState state = snapshot.Get(productId);
                    state.IsDisplayed = true;
                    try { state.ShopUnits += slot.ProductCount; } catch { }
                }
            }

            Box[] boxes = StatsSearchCache.GetBoxes();
            if (boxes != null)
            {
                for (int i = 0; i < boxes.Length; i++)
                {
                    Box box = boxes[i];
                    if (box == null) continue;

                    try
                    {
                        var data = box.Data;
                        if (data == null || data.ProductID <= 0) continue;
                        ProductStockState state = snapshot.Get(data.ProductID);
                        state.WarehouseUnits += box.ProductCount;
                    }
                    catch { }
                }
            }

            _cachedSnapshot = snapshot;
            _cachedAt = now;
            return snapshot;
        }

        public static void Invalidate()
        {
            _cachedSnapshot = null;
            _cachedAt = -1000f;
        }

        public static MissReason ClassifyMissing(ProductStockState state)
        {
            if (state == null || state.TotalUnits <= 0) return MissReason.GlobalOutOfStock;
            if (!state.IsDisplayed) return MissReason.NotDisplayed;
            if (state.ShopUnits <= 0 && state.WarehouseUnits > 0) return MissReason.ShelfEmpty;
            return MissReason.Other;
        }
    }
}
