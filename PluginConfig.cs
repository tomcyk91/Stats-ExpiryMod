using BepInEx.Configuration;

namespace SmartExpiration
{
    public static class PluginConfig
    {
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

        public static void BindConfig(ConfigFile config)
        {
            DefaultShelfDays = config.Bind("Categories", "RegularShelf", 14, "Ile dni ważności mają produkty na zwykłych półkach? / How many days of shelf life do products on regular shelves have?");
            FridgeDays = config.Bind("Categories", "Fridge", 9, "Ile dni ważności mają produkty w lodówce? / How many days of shelf life do fridge products have?");
            FreezerDays = config.Bind("Categories", "Freezer", 14, "Ile dni ważności mają mrożonki? / How many days of shelf life do freezer products have?");
            FruitDays = config.Bind("Categories", "Fruits", 3, "Ile dni ważności mają owoce? / How many days of shelf life do fruits have?");
            VegetableDays = config.Bind("Categories", "Vegetables", 5, "Ile dni ważności mają warzywa? / How many days of shelf life do vegetables have?");
            DrinkDays = config.Bind("Categories", "Drinks", 10, "Ile dni ważności mają napoje? / How many days of shelf life do drinks have?");
            CleaningDays = config.Bind("Categories", "CleaningProducts", 21, "Ile dni ważności ma chemia? / How many days of shelf life do cleaning products have?");
            BookDays = config.Bind("Categories", "Books", 60, "Ile dni ważności mają książki? / How many days of shelf life do books have?");
            AlcoholDays = config.Bind("Categories", "Alcohol", 60, "Domyślny termin dla alkoholu / Default expiration days for alcohol");
            MeatDays = config.Bind("Categories", "Meat", 6, "Domyślny termin dla mięsa / Default expiration days for meat");
            ToiletPaperDays = config.Bind("Categories", "ToiletPaper", 90, "Domyślny termin dla papieru toaletowego / Default expiration days for toilet paper");
            ClothesDays = config.Bind("Categories", "Clothes", 100, "Domyślny termin dla ubrań / Default expiration days for clothes");
            TechDays = config.Bind("Categories", "Electronics", 999, "Domyślny termin dla elektroniki / Default expiration days for electronics");
            BabiesDays = config.Bind("Categories", "BabyProducts", 30, "Domyślny termin dla artykułów dziecięcych / Default expiration days for baby products");
            CansDays = config.Bind("Categories", "CannedFood", 60, "Domyślny termin dla produktów w puszkach / Default expiration days for canned food");
            AgdDays = config.Bind("Categories", "Appliances", 999, "Domyślny termin dla urządzeń AGD / Default expiration days for home appliances");
            FrozenBakeryDays = config.Bind("Categories", "FrozenBakery", 10, "Domyślny termin dla mrożonego pieczywa / Default expiration days for frozen bakery");
            BakeryDays = config.Bind("Categories", "FreshBakery", 5, "Domyślny termin dla świeżego pieczywa / Default expiration days for fresh bakery");
            IceCreamDays = config.Bind("Categories", "IceCream", 14, "Domyślny termin dla lodów / Default expiration days for ice cream");

            CustomShelfLifeList = config.Bind("Advanced", "CustomExceptions", "", "Format ID:DNI oddzielone przecinkami (np. 141:10, 50:5) / Format ID:DAYS separated by commas (e.g., 141:10, 50:5)");
            ShowDatesOnBoxes = config.Bind(
                "Ustawienia Wizualne (Visual Settings)",
                "ShowDatesOnBoxes",                     
                true,                                    
                "Pokazuj daty ważności na kartonach./ Show expiration dates on boxes." 
            );

            ShowWarningTriangles = config.Bind(
                "Ustawienia Wizualne (Visual Settings)",
                "ShowWarningTriangles",
                true,
                "Pokazuj trójkąty ostrzegawcze na półkach przy kończącym się terminie. / Show warning triangles on shelves for expiring products."
            );
        }
    }
}