Stats & Expiry Mod - COMPLETE v2.5.5
========================================

Jest to pełna paczka kumulacyjna. Zastępuje wszystkie wcześniejsze paczki
wydane podczas prac nad wersjami 2.5.0-2.5.4.

INSTALACJA W PROJEKCIE
----------------------
1. Zamień wszystkie pliki .cs z tej paczki w projekcie moda.
2. Dodaj pliki, których jeszcze nie ma w projekcie:
   - BusinessAnalysisModels.cs
   - BusinessAnalysisStore.cs
   - BusinessAnalysisService.cs
   - DemandTrackingHooks.cs
   - StockSnapshotService.cs
   - StatsAppAnalysis.cs
   - StatsAppDropdowns.cs
3. Przebuduj projekt.
4. Nie usuwaj istniejących plików StatisticMod.stats.tsv ani
   StatisticMod.stats.tsv.analysis.json. Migracja danych jest automatyczna.

NAJNOWSZA POPRAWKA v2.5.5
-------------------------
- tekst w kafelkach ANALIZY automatycznie dopasowuje rozmiar,
- długie nazwy produktów kończą się wielokropkiem,
- opisy kart mają krótsze etykiety i nie zawijają się poza kafelek,
- każda karta ANALIZY ma RectMask2D jako zabezpieczenie,
- listy rozwijane są ograniczane do prostokąta aplikacji,
- lista przy prawej krawędzi przesuwa się automatycznie w lewo,
- przy braku miejsca na dole lista próbuje otworzyć się nad przyciskiem,
- opcje list również używają automatycznego rozmiaru i wielokropka.

WAŻNE
-----
Pełna kompilacja wymaga lokalnych bibliotek gry, BepInEx i IL2CPP.
