# MIGRATION PLAN: Stats&Expiry Mod

> Plan migracji wygenerowany na podstawie Raportu Audytu względem `IL2CPP_GAME_API_FIELD_GUIDE.md`.
> Status: oczekuje na realizację. Każdy punkt odznaczany dopiero po udanym `dotnet build`.

## FAZA 1: Krytyczne tarcze pamięci (Kategoria A — Naruszenia twarde)

- [ ] **A1-1. StatsHook.cs** (Linie 39, 48–50) -> Naprawa buga 807810400
    - Cel: Zamiana `GetValue` na bezpieczny odczyt natywny.
    - Szczegóły: refleksyjny fallback dla `m_ID`/`m_ProductId`/`ProductID` czyta wartościowe pole gry przez `System.Reflection.GetValue` (zwraca śmieci `807810400`). Rozwiązać typ przez `AccessTools.TypeByName(il2cppType.FullName)` + ctor `IntPtr`, albo `Marshal.ReadInt32(Pointer + offset)`, albo preferować właściwość-wrapper. Ścieżka szybka `m_ProductSO.ID` pozostaje bez zmian.

- [ ] **A1-2. ExpirationSaveManager.cs** (Linie 88–156, 274–283) -> Naprawa buga 807810400
    - Cel: Zastąpienie refleksji przez `ProductKey.GetId()`.
    - Szczegóły: `GetProductIdFromProduct` i odczyt UID `Box` używają `prop.GetValue`/`field.GetValue`/`m.Invoke` na obiektach IL2CPP. Zastąpić istniejącym `ProductKey.GetId(p)` (czyta `p.ProductSO.ID` bezpośrednio) oraz typowanym odczytem `Box.Data`.

- [ ] **A2. ExpirationSaveManager.cs** (Linia 60) -> Usunięcie LINQ z Il2CppArray
    - Cel: Zamiana `.ToList()` na ręczną iterację po indeksie.
    - Szczegóły: `GetComponentsInChildren<Product>(true)` zwraca `Il2CppArrayBase<T>`; iterować po `.Count`/indekserze do managed `List<Product>`, dopiero potem `.Sort(...)`.

- [ ] **A3. SalesUnifiedFinal.cs & ProductVisualCache.cs** -> Rzutowanie wrappera
    - Cel: Zamiana `as` na `.TryCast<T>()`.
    - Szczegóły: `SalesUnifiedFinal.cs:67` (`__0 as global::Product`), `ProductVisualCache.cs:69` (`dictObj as Il2CppObjectBase`), `ProductVisualCache.cs:183` (`item is ProductSO`). Po `TryCast` dodać guard `== null`.

- [ ] **A4. Cały projekt (7 plików)** -> Wyplenienie operatorów `??` oraz `?.` na obiektach Unity
    - Cel: Zamiana na jawną weryfikację (`obj == null`).
    - Szczegóły: `LabelExclamationOverlay.cs:74` (`Shader.Find ?? Shader.Find`), `StatsAppCharts.cs:70, 619, 698, 88, 91, 121, 758`, `TrashBoxManager.cs:177, 180`. Pozostawić `action?.Invoke()` na delegatach (dozwolone przez przewodnik).

- [ ] **A5. EmbeddedIconLoader.cs & LabelExclamationOverlay.cs** -> Asset Lifetime
    - Cel: Nadanie `hideFlags` `DontUnloadUnusedAsset` / `HideAndDontSave` przed wejściem do cache.
    - Szczegóły: `EmbeddedIconLoader.cs:35,40` (`new Texture2D`, `Sprite.Create`), `LabelExclamationOverlay.cs:42,44` (`new Texture2D`, `Sprite.Create`). Ustawić flagi na teksturze i sprite zanim trafią do `_cache`/`_iconSprite`.

- [ ] **A6. EmbeddedIconLoader.cs & LabelExclamationOverlay.cs** -> Marshalling tekstur
    - Cel: Jawne rzutowanie `(Il2CppStructArray<byte>)` przy `LoadImage`.
    - Szczegóły: `EmbeddedIconLoader.cs:38` (`tex.LoadImage(data)`), `LabelExclamationOverlay.cs:43` (`ImageConversion.LoadImage(tex, ba)`). Rzutować managed `byte[]` na `Il2CppStructArray<byte>`.

## FAZA 2: Stabilność silnika i architektury (Kategoria B — Naruszenia średnie)

- [ ] **B1. ExpirationCalculator.cs** -> Pozbycie się MyBox
    - Cel: Zastąpić `Singleton<IDManager>.Instance` (linie 3, 61) natywnym `IDManager.Instance` (z gate `HasInstance`); usunąć `using MyBox;`.

- [ ] **B2. RestockerIntegrationPatch.cs** -> Przepięcie loggera na BepInEx
    - Cel: Zamienić `UnityEngine.Debug.Log` (linia 87) na `StatisticMod.Plugin.Log.LogError(...)` / `DebugLog`.

- [ ] **B3. RestockerIntegrationPatch.cs & GameSavePatches.cs** -> Fail-Soft Prepare()
    - Cel: Dodać `public static bool Prepare()` z bezpieczną refleksją celu do: `OvernightWorkersIntegration` (`RestockerIntegrationPatch.cs:96`) oraz `SaveManager_Save_SaveInfo_Patch`/`_String_Patch`/`_NoArgs_Patch`/`SaveManager_ApplySaveData_Patch` (`GameSavePatches.cs:8, 28, 48, 99`).

- [ ] **B4. StatsAppManager.cs** (Linia 2996) -> Optymalizacja skanera pętli klawisza
    - Cel: `OnSearchValueChanged` przestaje robić pełny `FindObjectsOfType<DisplaySlot>()` + `<Box>()` na każdy znak. Cache'ować słowniki stanu, rebuild tylko przy zmianie dnia/inwentarza.

## FAZA 3: Higiena kodu (Kategoria C — Borderline)

- [ ] **C1. BoxPatches.cs** (Linie 83–87) -> Odczyt `Box.Data.ProductID` przez wrapper-property zamiast refleksji `GetValue` (ryzyko 807810400, jeśli pole wartościowe).
- [ ] **C2. DisplaySlotPatches.cs** (Linia 30) -> `products.Last()` na `products[products.Count-1]`; usunięcie zależności `System.Linq` (źródło to managed `List`, więc niski priorytet).
- [ ] **C3. GameSavePatches.cs** (Linia 129) -> Weryfikacja, że `StartCoroutine(DelayedSyncCoroutine())` rozwiązuje extension `BepInEx.Unity.IL2CPP.Utils` (owija managed `IEnumerator`), nie surowy IL2CPP `StartCoroutine`.
- [ ] **C4. ProductVisualCache.cs** (Linie 63, 138–187) -> Zastąpienie `AccessTools.Field/Property(...).GetValue` bezpośrednim `idManager.m_Products` / `m_ProductSODictionary` (poprawić też nazwę pola).
- [ ] **C5. Niebramkowane `.Instance` (bez `HasInstance`)** -> Gate `X.HasInstance ? X.Instance : null`. Priorytet: `SalesUnifiedFinal.cs:26, 132` (bez try/catch). Niżej: `StatsRunner.cs:69`, `TrashBoxManager.cs:116, 245, 344, 356, 379`, `SalesUnifiedFinal.cs:46, 163`, `StatsStore.cs:65`.
- [ ] **C6. StatsHook.cs** (Linia 18) -> Weryfikacja `productObj is global::Product p`; jeśli bywa żywym wrapperem Il2Cpp, użyć `.TryCast`.
- [ ] **C7. Plugin.cs** (Linia 2) -> Usunięcie legacy `using BepInEx.IL2CPP;` (nowy `BepInEx.Unity.IL2CPP` już w linii 4).
- [ ] **C8. ExpirationLoadFinalizer.cs** (Linie 32, 43) -> Rozważenie jednorazowego skanu `FindObjectsOfType<DisplaySlot>()` po `SaveLoaded` zamiast powtarzania w pętli korutyny.

---
### PROTOKÓŁ OPERACYJNY DLA CLAUDE CLI:
1. Pracujesz WYŁĄCZNIE w reżimie "Jednego Pacjenta" — bierz na warsztat tylko jeden plik .cs naraz.
2. Po każdej edycji pliku wywołujesz `dotnet build`. Jeśli build padnie, naprawiasz błąd przed kontynuacją.
3. Po udanym buildzie odznaczasz checkbox `[x]` w tym pliku i ZATRZYMUJESZ SIĘ, czekając na komendę użytkownika: "Następny".
