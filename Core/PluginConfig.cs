using BepInEx.Configuration;

namespace SmartExpiration
{
    public static class PluginConfig
    {
        public static ConfigEntry<bool> EnableExpirySystem;

        public static bool ExpiryEnabled =>
            EnableExpirySystem == null || EnableExpirySystem.Value;

        public static ConfigEntry<int> DefaultShelfDays;
        public static ConfigEntry<int> FridgeDays;
        public static ConfigEntry<int> FreezerDays;
        public static ConfigEntry<int> FruitDays;
        public static ConfigEntry<int> VegetableDays;
        public static ConfigEntry<int> DrinkDays;
        public static ConfigEntry<int> CleaningDays;
        public static ConfigEntry<int> BookDays;
        public static ConfigEntry<int> AlcoholDays;
        public static ConfigEntry<int> MeatDays;
        public static ConfigEntry<int> ToiletPaperDays;
        public static ConfigEntry<int> ClothesDays;
        public static ConfigEntry<int> TechDays;
        public static ConfigEntry<int> BabiesDays;
        public static ConfigEntry<int> CansDays;
        public static ConfigEntry<int> AgdDays;
        public static ConfigEntry<int> FrozenBakeryDays;
        public static ConfigEntry<int> BakeryDays;
        public static ConfigEntry<int> IceCreamDays;

        // Zmienna dla ręcznie dopisywanych wyjątków
        public static ConfigEntry<string> CustomShelfLifeList;

        public static ConfigEntry<bool> ShowDatesOnBoxes;

        public static ConfigEntry<bool> ShowWarningTriangles;

        // Koszyk na przeterminowane produkty
        public static ConfigEntry<UnityEngine.KeyCode> TrashBasketSpawnKey;

        // Ustawienia wydajności ładowania terminów
        public static ConfigEntry<int> LoadSyncSlotsPerFrame;
        public static ConfigEntry<bool> DetailedLoadLogs;

        public static void BindConfig(ConfigFile config)
        {
            EnableExpirySystem = config.Bind(
                "General",
                "EnableExpirySystem",
                true,
                "Enables or disables the entire expiration system. Set to false to keep Statistics & Analytics active while disabling expiration dates, spoilage, expiry labels, warning indicators, expiry-related Store Point effects, and the expired-products basket. GAME RESTART REQUIRED AFTER CHANGING THIS OPTION. Starting the game with this set to false deletes the existing SmartExpiration.txt expiration save data. If you later set it back to true and restart the game, fresh expiration dates are generated from the current in-game day of the loaded save."
            );

            DefaultShelfDays = config.Bind("Categories", "RegularShelf", 14, "Shelf life in days for regular shelf products.");
            FridgeDays = config.Bind("Categories", "Fridge", 9, "Shelf life in days for refrigerated products.");
            FreezerDays = config.Bind("Categories", "Freezer", 14, "Shelf life in days for freezer products.");
            FruitDays = config.Bind("Categories", "Fruits", 3, "Shelf life in days for fruits.");
            VegetableDays = config.Bind("Categories", "Vegetables", 5, "Shelf life in days for vegetables.");
            DrinkDays = config.Bind("Categories", "Drinks", 10, "Shelf life in days for drinks.");
            CleaningDays = config.Bind("Categories", "CleaningProducts", 21, "Shelf life in days for cleaning products.");
            BookDays = config.Bind("Categories", "Books", 60, "Shelf life in days for books.");
            AlcoholDays = config.Bind("Categories", "Alcohol", 60, "Shelf life in days for alcohol products.");
            MeatDays = config.Bind("Categories", "Meat", 6, "Shelf life in days for meat products.");
            ToiletPaperDays = config.Bind("Categories", "ToiletPaper", 90, "Shelf life in days for toilet paper products.");
            ClothesDays = config.Bind("Categories", "Clothes", 100, "Shelf life in days for clothes.");
            TechDays = config.Bind("Categories", "Electronics", 999, "Shelf life in days for electronics.");
            BabiesDays = config.Bind("Categories", "BabyProducts", 30, "Shelf life in days for baby products.");
            CansDays = config.Bind("Categories", "CannedFood", 60, "Shelf life in days for canned food.");
            AgdDays = config.Bind("Categories", "Appliances", 999, "Shelf life in days for home appliances.");
            FrozenBakeryDays = config.Bind("Categories", "FrozenBakery", 10, "Shelf life in days for frozen bakery products.");
            BakeryDays = config.Bind("Categories", "FreshBakery", 5, "Shelf life in days for fresh bakery products.");
            IceCreamDays = config.Bind("Categories", "IceCream", 14, "Shelf life in days for ice cream.");

            CustomShelfLifeList = config.Bind("Advanced", "CustomExceptions", "", "Custom shelf-life exceptions. Format: ID:DAYS separated by commas, for example 141:10,50:5.");
            ShowDatesOnBoxes = config.Bind(
                "Visual Settings",
                "ShowDatesOnBoxes",                     
                true,                                    
                "Show expiration dates on product boxes." 
            );

            ShowWarningTriangles = config.Bind(
                "Visual Settings",
                "ShowWarningTriangles",
                true,
                "Show warning triangles on shelves for products that are close to expiration."
            );

            TrashBasketSpawnKey = config.Bind(
                "Expired Products Basket",
                "SpawnKey",
                UnityEngine.KeyCode.U,
                "Key used to spawn the expired-products basket. Set to None to disable the keyboard shortcut."
            );

            LoadSyncSlotsPerFrame = config.Bind(
                "Performance",
                "LoadSyncSlotsPerFrame",
                4,
                "Number of shelf slots synchronized per frame after loading (1-32). Lower values may reduce frame-time spikes, but synchronization will take longer."
            );

            DetailedLoadLogs = config.Bind(
                "Performance",
                "DetailedLoadLogs",
                false,
                "Log every loaded shelf slot and box. Enable for diagnostics only, because large stores can generate hundreds of log entries."
            );
        }
    }
}