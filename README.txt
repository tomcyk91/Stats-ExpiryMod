Stats & Expiration - First Run Expiry Rescue V1
=================================================

Cel:
Jednorazowo zabezpieczyć użytkowników aktualizujących mod po wcześniejszym
błędzie z nieprawidłowymi terminami.

Warunek:
daysLeft = ExpirationDay - CurrentDay

Jeżeli:
daysLeft <= 0

to migracja ustawia:
ExpirationDay = CurrentDay + 1

Czyli produkt dostaje dokładnie 1 dzień pozostałej ważności.

Migracja skanuje:
- fizyczne produkty na półkach,
- slotDates,
- pending PBOX2,
- legacy PBOX,
- runtimeBoxDates,
- boxDates,
- fizyczne ProductExpirationComponent w kartonach.

Uruchomienie:
- po ExpirationSaveManager.LoadData(),
- po normalnym DelayedSync półek,
- raz na każdy slot zapisu.

Marker:
<persistentDataPath>/<slot>/StatsExpiry_Migration_ExpiryRescueV1.done

WAŻNE:
Marker NIE jest związany automatycznie z wersją Pluginu.
Dzięki temu każda kolejna aktualizacja moda NIE będzie ponownie ratowała
normalnie przeterminowanych produktów.

Jeżeli kiedyś będzie potrzebna kolejna jednorazowa migracja, utwórz V2
z nowym MigrationId / MarkerFileName.

Pliki:
1. PODMIEŃ ExpirationSaveManager.cs
2. PODMIEŃ ExpirationLoadFinalizer.cs
3. DODAJ ExpirationSafetyMigration.cs

Nie trzeba zmieniać Plugin.cs.
Nie trzeba zmieniać FEFO, BoxPatches, DisplaySlotPatches ani BoxExpirationLabel.
