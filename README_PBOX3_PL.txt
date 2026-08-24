Stats & Expiration - PBOX3
================================

CEL
---
PBOX3 usuwa Box.Data.UID z roli trwałego identyfikatora kartonu.

Test wykazał, że te same fizyczne kartony mogą zmienić UID po restarcie gry
(np. 21/22 -> 1/2). Dlatego PBOX2 nie może bezpiecznie wiązać danych z kartonem.

NOWY FORMAT
-----------
PBOX3|StatsExpiryGuid|BoxId|ProductId|px|py|pz|qx|qy|qz|qw|ExpirationDays|DeliveryDays

Przykład:
PBOX3|5a12...|3|66|12.500|0.150|-4.200|0|0.707|0|0.707|10,10,14,14|1,1,5,5

WAŻNE:
- game UID NIE występuje w PBOX3,
- własny GUID moda jest trwałą tożsamością rekordu,
- po restarcie GUID jest przypinany do fizycznego kartonu przez fingerprint:
  BoxID + ProductID + liczba sztuk + pozycja + rotacja,
- UID gry pozostaje tylko tymczasowym cache kompatybilności w bieżącej sesji.

DELIVERY DAY NA POZIOMIE PRODUKTU
--------------------------------
ProductExpirationComponent przechowuje teraz:
- ExpirationDay
- DeliveryDay
- ProductID

DeliveryDay należy do konkretnej sztuki produktu, NIE do kartonu.

Przykład:
- dzień gry 5,
- pusty karton wygenerowany Box Spawnerem,
- wkładamy mleko z dostawy dnia 1, ExpirationDay=10.

Wynik:
Dostawa: 1
Termin: 5 dni

Pusty karton nie posiada własnej historii dostawy.

MIESZANE PARTIE
---------------
Karton może zawierać:
ExpirationDays: 10,10,14,14
DeliveryDays:   1,1,5,5

Etykieta pokazuje parę należącą do produktu z najbliższym terminem.
Po zejściu starej partii automatycznie przejdzie na kolejną partię.

PÓŁKI
-----
Stary rekord półki zostaje dla zgodności:
DisplaySlotPath|ExpirationDays

Dodany jest równoległy rekord:
SDEL|DisplaySlotPath|DeliveryDays

Dzięki temu produkt zdjęty z półki dnia 5 nadal może pamiętać,
że pochodził z dostawy dnia 1.

FEFO
----
FEFO przenosi teraz CAŁĄ parę:
ExpirationDay + DeliveryDay

Nie zamienia tylko samego terminu ważności.

MIGRACJA PBOX2 -> PBOX3
-----------------------
PBOX2 jest czytany wyłącznie jako stary format migracyjny.

Stary PBOX2 nie ma pozycji ani fingerprintu, więc dla kilku identycznych
kartonów produktu nie da się matematycznie odzyskać ich dokładnej tożsamości.
Migracja robi best-effort:
- grupuje po ProductID i liczbie sztuk,
- używa starego UID i nowego UID jedynie jako słabego porządku migracyjnego.

Po pierwszym poprawnym zapisie dopasowane kartony są już PBOX3 i dalsze
restarty nie zależą od UID gry.

Przed pierwszym nadpisaniem starego sidecara tworzony jest:
SmartExpiration.pre_PBOX3.bak

Jeśli backupu nie uda się stworzyć, PBOX3 NIE nadpisze SmartExpiration.txt.

BEZPIECZEŃSTWO
--------------
- SaveData nadal jest zablokowane podczas Main Menu/startup sync.
- Niezmatchowane rekordy PBOX3 są zachowywane zamiast bezgłośnie znikać.
- Niezmatchowane stare PBOX2/PBOX są zachowywane do kolejnej próby migracji.
- ExpiryRescueV3 rozpoznaje PBOX3.
- Jeżeli ExpiryRescueV3 przeładuje sidecar, finalizer ponownie aplikuje PBOX3
  i SDEL do fizycznych produktów przed odblokowaniem zapisów.

PODMIANA PLIKÓW
---------------
Root projektu:
- ExpirationSaveManager.cs
- ExpirationManager.cs
- ProductExpirationComponent.cs
- ExpirationLoadFinalizer.cs
- ExpirationSafetyMigration.cs

Patches:
- BoxPatches.cs
- BoxExpirationLabel.cs

DisplaySlotPatches.cs nie wymaga zmiany: istniejący transfer zachowuje
ProductExpirationComponent, a logika FEFO została zaktualizowana w
ExpirationManager.cs.

UWAGA
-----
Paczka została sprawdzona strukturalnie i pod kątem spójności zależności,
ale w tym środowisku nie ma kompilatora/projektowych DLL gry, więc pełnej
kompilacji Stats&ExpiryMod tutaj nie wykonano.
