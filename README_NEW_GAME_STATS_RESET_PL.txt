RESET STATYSTYK PRZY NOWEJ GRZE
================================

Zmiana usuwa dane moda wyłącznie dla slotu, w którym gracz potwierdził
rozpoczęcie nowej gry. Pozostałe sloty nie są dotykane.

Usuwane pliki:
- StatisticMod.stats.tsv
- StatisticMod.stats.tsv.daily.tsv
- StatisticMod.stats.tsv.analysis.json
- pliki tymczasowe tych zapisów

Czyszczona pamięć:
- sprzedaż i straty produktów
- podsumowania dni
- ANALIZA i otwarty dzień
- sesje klientów
- bufory kas i zamówień online
- cache stanów magazynowych
- stan wykrywania dnia w StatsRunner

Hooki:
- SaveManager.CreateLoadNewSave(SaveInfo)
- SaveManager.CreateLoadNewSave_MP(SaveInfo)
- DailyStatisticsScreen.StartNewGame()
- BankruptcyCanvas.StartNewGame()

TEST:
1. W slocie zakończ kilka dni i sprawdź dane w aplikacji.
2. Wróć do menu i rozpocznij nową grę w tym samym slocie.
3. W logu powinno pojawić się:
   [NewGameReset] Wyczyszczono statystyki slotu slot_X...
4. STATYSTYKI, PODSUMOWANIE, ANALIZA i WYKRESY powinny być puste.
5. Dane innych slotów powinny pozostać bez zmian.

Uwaga: poprawka nie usuwa SmartExpiration.txt, ponieważ jest to zapis terminów
ważności, a nie historia statystyk.
