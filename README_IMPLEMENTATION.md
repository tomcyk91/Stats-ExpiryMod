# Business Analysis – integracja z projektem Stats & Expiration

Pakiet jest przygotowany jako pierwsza działająca wersja funkcji zaproponowanych na Nexusie.

## Pliki nowe – dodaj do projektu

- `BusinessAnalysisModels.cs`
- `BusinessAnalysisStore.cs`
- `StockSnapshotService.cs`
- `DemandTrackingHooks.cs`
- `BusinessAnalysisService.cs`
- `StatsAppAnalysis.cs`

Wszystkie używają namespace `StatisticMod`.

## Pliki istniejące – zastąp wersjami z tego pakietu

- `Plugin.cs`
- `StatsRunner.cs`
- `StatsAppManager.cs`

Pozostałych plików projektu nie trzeba zmieniać.

## Co zawiera wersja

1. Rejestrowanie pełnej listy zakupów klienta.
2. Porównanie listy z faktycznie zebranym koszykiem.
3. Liczenie popytu, realizacji i utraconego przychodu.
4. Rozpoznawanie:
   - braku produktu w całym sklepie,
   - pustej półki przy zapasie w magazynie,
   - produktu niewystawionego,
   - innych niezrealizowanych pozycji.
5. Prognoza popytu z ostatnich 3 i 14 dni.
6. Rekomendacja przeniesienia towaru magazyn → półka.
7. Rekomendacja zamówienia w sztukach i kartonach.
8. Ograniczenie nadmiernego zamawiania przez orientacyjny termin ważności.
9. Sugestia cenowa na podstawie popytu, zapasu i `Pricing.MarketPrice`.
10. Nowy tryb aplikacji `ANALIZA`.

## Obsługa UI

Klikanie tytułu aplikacji przełącza teraz:

`STATYSTYKI → TERMINY → PRODUKTY → ANALIZA → WYKRESY`

W trybie `ANALIZA` przycisk sortowania przełącza podwidoki:

`POPYT → UTRACONA SPRZ. → UZUPEŁNIANIE → CENY`

Strzałka zmienia kolejność malejąco/rosnąco. Wyszukiwarka działa również w analizie.
Przycisk `7 DNI / 14 DNI / 30 DNI` zmienia zakres analizowanych danych.

## Plik zapisu

Analiza jest zapisywana obok obecnego pliku statystyk:

`<plik_statystyk>.analysis.json`

Rozwiązanie nie zmienia struktury starego pliku statystyk i jest zgodne ze starszymi zapisami moda.

## Wymaganie projektu

Kod używa `System.Text.Json`. Projekt powinien nadal celować w `net6.0`, zgodnie z aktualnym środowiskiem BepInEx IL2CPP.

## Pierwszy test w grze

1. Uruchom zapis z kilkoma produktami na półkach.
2. Pozwól kilku klientom zrobić zakupy.
3. Opróżnij półkę produktu, pozostawiając karton w magazynie.
4. Pozwól kolejnym klientom wejść.
5. Otwórz aplikację i przejdź do `ANALIZA`.
6. W `UTRACONA SPRZ.` produkt powinien otrzymać wpis `Pusta półka`.
7. W `UZUPEŁNIANIE` powinna pojawić się rekomendacja przeniesienia produktu na półkę.

## Logi diagnostyczne

Po ustawieniu `Plugin.EnableLogs = true` pojawiają się wpisy:

`[Demand] Finalized customer=...`

Brak tych wpisów oznacza, że sygnatura jednej z metod `Customer` zmieniła się w aktualnej wersji gry. Każdy hook jest instalowany oddzielnie, więc pozostałe funkcje moda nadal powinny działać.

## Ważne

Sugestie cenowe w tej wersji są tylko informacyjne. Kod nie zmienia automatycznie ceny produktu.
