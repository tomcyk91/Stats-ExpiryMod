using System.Collections.Generic;
using UnityEngine;
using MyBox;

namespace SmartExpiration
{
    public static class ExpirationCalculator
    {
        // --- STARE KATEGORIE (Poprawione usunięte duplikaty) ---
        private static readonly HashSet<int> FruitIDs = new HashSet<int>
        {
            165, 168, 171, 173, 174, 175, 176, 177, 180, 181, 182,
            183, 184, 185, 186, 187, 188
        };

        private static readonly HashSet<int> VegetableIDs = new HashSet<int>
        {
            166, 167, 169, 170, 172, 178, 179
        };

        // --- KATEGORIE Z POPRZEDNIEJ WIADOMOŚCI ---
        private static readonly HashSet<int> AlcoholIDs = new HashSet<int> { 1, 2, 3, 4, 5, 6, 7, 8, 9, 77, 82, 159, 160 };
        private static readonly HashSet<int> MeatIDs = new HashSet<int> { 104, 106, 127, 131, 140, 164 };
        private static readonly HashSet<int> ToiletPaperIDs = new HashSet<int> { 75, 153, 154, 157 };

        // --- NAJNOWSZE KATEGORIE Z PLIKU ---
        private static readonly HashSet<int> ClothesIDs = new HashSet<int> { 189, 190, 191, 192, 193, 194, 195, 196, 197, 198, 199, 200, 201, 202, 203, 204, 205, 206, 207 };
        private static readonly HashSet<int> TechIDs = new HashSet<int> { 208, 209, 210, 211, 212, 213, 214, 215, 216, 217, 218, 219, 220, 221, 222, 223, 224, 225, 226, 227, 228, 229, 230, 231, 232, 233, 234, 235, 236, 237, 238, 239, 240 };
        private static readonly HashSet<int> BabiesIDs = new HashSet<int> { 241, 242, 243, 244, 245, 246, 247, 248, 249, 250, 251, 252 };
        private static readonly HashSet<int> CansIDs = new HashSet<int> { 253, 254, 255, 256, 257, 258, 259, 260, 261, 262 };
        private static readonly HashSet<int> AgdIDs = new HashSet<int> { 263, 264, 265, 266, 267, 268, 269, 271, 272 };
        private static readonly HashSet<int> FrozenBakeryIDs = new HashSet<int> { 273, 275, 277, 279, 281, 283, 285, 287, 289, 291, 293, 295, 297, 299, 301 };
        private static readonly HashSet<int> BakeryIDs = new HashSet<int> { 274, 276, 278, 280, 282, 284, 286, 288, 290, 292, 294, 296, 298, 300, 302 };
        private static readonly HashSet<int> IceCreamIDs = new HashSet<int> { 303, 304, 305, 306, 307, 308, 309, 310, 311 };

        public static int GetDaysForProduct(DisplaySlot slot, int productId)
        {
            // 1. Sprawdzamy sztywne listy ID
            if (AlcoholIDs.Contains(productId)) return PluginConfig.AlcoholDays.Value;
            if (MeatIDs.Contains(productId)) return PluginConfig.MeatDays.Value;
            if (ToiletPaperIDs.Contains(productId)) return PluginConfig.ToiletPaperDays.Value;

            if (ClothesIDs.Contains(productId)) return PluginConfig.ClothesDays.Value;
            if (TechIDs.Contains(productId)) return PluginConfig.TechDays.Value;
            if (BabiesIDs.Contains(productId)) return PluginConfig.BabiesDays.Value;
            if (CansIDs.Contains(productId)) return PluginConfig.CansDays.Value;
            if (AgdIDs.Contains(productId)) return PluginConfig.AgdDays.Value;
            if (FrozenBakeryIDs.Contains(productId)) return PluginConfig.FrozenBakeryDays.Value;
            if (BakeryIDs.Contains(productId)) return PluginConfig.BakeryDays.Value;
            if (IceCreamIDs.Contains(productId)) return PluginConfig.IceCreamDays.Value;

            if (FruitIDs.Contains(productId)) return PluginConfig.FruitDays.Value;
            if (VegetableIDs.Contains(productId)) return PluginConfig.VegetableDays.Value;

            // 2. Jeśli nie ma na listach, sprawdzamy meble / atrybuty
            ProductSO productData = null;
            try
            {
                if (productId > 0)
                {
                    productData = Singleton<IDManager>.Instance.ProductSO(productId);
                }
            }
            catch { }

            if (productData != null)
            {
                try
                {
                    if (productData.ProductDisplayType == DisplayType.FREEZER) return PluginConfig.FreezerDays.Value;
                    if (productData.ProductDisplayType == DisplayType.FRIDGE) return PluginConfig.FridgeDays.Value;
                }
                catch { }
            }

            if (slot != null)
            {
                try
                {
                    Display displayFurniture = slot.GetComponentInParent<Display>();
                    if (displayFurniture != null)
                    {
                        if (displayFurniture.DisplayType == DisplayType.FREEZER) return PluginConfig.FreezerDays.Value;
                        if (displayFurniture.DisplayType == DisplayType.FRIDGE) return PluginConfig.FridgeDays.Value;
                    }
                }
                catch { }
            }

            if (productData != null)
            {
                switch (productData.Category)
                {
                    case ProductSO.ProductCategory.DRINK: return PluginConfig.DrinkDays.Value;
                    case ProductSO.ProductCategory.CLEANING: return PluginConfig.CleaningDays.Value;
                    case ProductSO.ProductCategory.BOOK: return PluginConfig.BookDays.Value;
                }
            }

            // 3. Fallback: Domyślny termin
            return PluginConfig.DefaultShelfDays.Value;
        }
    }
}