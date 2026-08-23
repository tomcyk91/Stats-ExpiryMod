Stats & Expiration - FIX "Random expiration dates on newly purchased products"
===============================================================================

Podmień:
- Patches/BoxPatches.cs
- Patches/DisplaySlotPatches.cs
- ExpirationManager.cs
- BoxExpirationLabel.cs

Ta paczka jest zgodna z wcześniejszą poprawką PBOX2.

Co zostało naprawione:
1. DisplaySlot.TakeProductFromDisplay NIE zapisuje już terminu do globalnego ClipboardDate.
2. Klient zdejmujący produkt z półki nie może już "zatruć" daty następnej dostawy.
3. Shelf -> Box:
   Box.AddProduct odczytuje termin bezpośrednio z ProductExpirationComponent konkretnego Product.
4. Box -> Shelf:
   Box.GetProductFromBox przypina dokładną datę bezpośrednio do zwróconego Product.
5. DisplaySlot.AddProduct używa dokładnego parametru Product, zamiast globalnego schowka.
6. Nowo utworzony produkt bez wcześniejszej daty dostaje zawsze:
      CurrentDay + ExpirationCalculator/GetConfigOverride
7. BoxExpirationLabel nie używa już ClipboardDate przy uzupełnianiu brakujących pozycji.

Nie trzeba zmieniać Plugin.cs.
Nie trzeba zmieniać ExpirationSaveManager.cs z wcześniejszego PBOX2 fix.

Test:
- ustaw ShowDatesOnBoxes=true;
- kup kilka nowych kartonów;
- ich termin powinien być identyczny dla tego samego produktu kupionego tego samego dnia;
- klienci mogą w tym czasie zdejmować stare/przeterminowane produkty z półek i nie powinno to
  wpływać na datę nowej dostawy;
- sprawdź shelf -> box -> shelf, czy termin konkretnego produktu pozostaje ten sam.
