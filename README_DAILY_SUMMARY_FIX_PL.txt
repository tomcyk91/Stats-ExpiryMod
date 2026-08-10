DOPASOWANA POPRAWKA: PODSUMOWANIE + ANALIZA
============================================

Ta wersja została przygotowana bezpośrednio na bazie przesłanego projektu
"Stats-ExpiryMod-main (2).zip".

CO DODANO
---------
1. Nowa pozycja PODSUMOWANIE w dropdownie aplikacji:
   STATYSTYKI -> TERMINY -> PRODUKTY -> PODSUMOWANIE -> ANALIZA -> WYKRESY

2. Trwały zapis finalnego podsumowania dnia do pliku:
   StatisticMod.stats.tsv.daily.tsv

3. Widok kafelków PODSUMOWANIE z klientami, przychodami, kosztami,
   zyskiem dnia i saldem.

4. Obsługa strzałek dni w PODSUMOWANIU. Pokazywane są tylko dni,
   dla których faktycznie zapisano podsumowanie.

5. Naprawa pustej ANALIZY dla istniejących zapisów:
   zakończone dni są odtwarzane z historii StatsStore. Dla starych dni
   sprzedaż jest używana jako minimalne przybliżenie popytu.

WAŻNE O ANALIZIE
----------------
- Stare dni: można odtworzyć sprzedaż i podstawowy popyt ze StatsStore.
- Pełne przyczyny braków, utracona sprzedaż oraz zachowanie klientów
  będą dostępne dopiero dla dni rozegranych z aktywnymi hookami Analizy.

INSTALACJA
----------
Najbezpieczniej podmienić cały projekt zawartością tej paczki.
Jeżeli podmieniasz tylko zmienione pliki, użyj paczki
"Stats-ExpiryMod_DailySummary_ChangedFiles.zip".

Następnie:
1. Wyczyść rozwiązanie.
2. Przebuduj rozwiązanie.
3. Skopiuj nową DLL do BepInEx/plugins.
4. Uruchom grę i otwórz aplikację moda.

SPODZIEWANE LOGI
----------------
Po uruchomieniu istniejącego zapisu może pojawić się:
[BusinessAnalysis] Odtworzono X zakończonych dni z historii StatsStore.

Po zakończeniu dnia:
[DailySummary] Captured day=..., customers=..., income=..., profit=...
