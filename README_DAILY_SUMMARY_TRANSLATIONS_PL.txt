TŁUMACZENIA PODSUMOWANIA I WYKRESÓW SKLEPU
===========================================

Zmiana dodaje tłumaczenia nowych elementów moda:
- pozycja menu PODSUMOWANIE,
- wszystkie kafelki podsumowania dnia,
- opisy klientów, kosztów, wpływów, zysku i salda,
- tryb SKLEP w zakładce WYKRESY,
- nazwy metryk i legendę wykresu.

Obsługiwane języki:
- polski,
- angielski,
- francuski,
- włoski,
- niemiecki,
- hiszpański,
- chiński uproszczony,
- portugalski brazylijski,
- niderlandzki,
- japoński,
- koreański,
- portugalski europejski,
- rosyjski,
- turecki,
- duński,
- fiński,
- węgierski,
- rumuński,
- czeski,
- litewski.

PODMIANA RĘCZNA
---------------
Podmień:
- ModLocalization.Packs1.cs
- StatsAppChartsStoreSummary.cs

Dodaj:
- ModLocalization.DailySummary.cs

Projekt SDK automatycznie dołączy nowy plik .cs.
Po podmianie wykonaj:
Kompilacja -> Wyczyść rozwiązanie
Kompilacja -> Przebuduj rozwiązanie

UWAGA
-----
Język jest wykrywany z ustawień lokalizacji gry. Zmiana języka podczas działania
gry powinna odświeżyć aplikację moda tak jak pozostałe przetłumaczone widoki.
