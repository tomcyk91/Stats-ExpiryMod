Stats & Expiry 2.4.8 — trójkąty bez skanowania w trakcie dnia

Zmiany względem 2.4.7:
- usunięto okresowy skan bezpieczeństwa slotów,
- usunięto odświeżanie cache półek co 2 sekundy,
- pełny przebieg półek odbywa się tylko po wczytaniu zapisu,
- po wczytaniu jest jeden dodatkowy przebieg po 1 sekundzie, aby złapać późno utworzone półki,
- pełny przebieg odbywa się przy zmianie dnia,
- w trakcie dnia odświeżane są tylko sloty zmienione przez AddProduct/TakeProductFromDisplay,
- operacje koszyka nadal odświeżają konkretny slot natychmiast,
- kolejka pełnego przebiegu nadal ma budżet 0,70 ms i maks. 12 slotów na klatkę.

Do podmiany:
- LabelExclamationOverlay.cs
- Plugin.cs (tylko numer wersji i istniejące patche zdarzeniowe)

TrashBoxManager.cs pozostaje bez zmian względem v4/v5.
