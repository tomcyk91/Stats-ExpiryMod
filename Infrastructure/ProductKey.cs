using UnityEngine;

namespace StatisticMod
{
    public static class ProductKey
    {
        // Zwraca ProductSO.ID (to jest prawdziwy "productId" w tej grze)
        public static int GetId(Product p)
        {
            if (p == null) return -1;

            ProductSO so = null;
            try { so = p.ProductSO; } catch { }

            if (so == null) return -1;

            int id = 0;
            try { id = so.ID; } catch { }

            return id > 0 ? id : -1;
        }
    }
}
