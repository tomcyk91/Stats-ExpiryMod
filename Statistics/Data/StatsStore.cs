#nullable disable
using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using System.Collections.Generic;
using System.Threading;

namespace StatisticMod
{
    public static class StatsStore
    {
        public static StatsData Data { get; private set; } = new StatsData();

        private static bool _dirty;
        private static float _nextAutoSaveTime;

        // Chroni plik przed wyścigiem: stary zapis z wątku roboczego nie może
        // odtworzyć danych po rozpoczęciu nowej gry.
        private static readonly object _fileIoLock = new object();
        private static int _writeGeneration;

        private static string _currentSlot = "slot_0";
        public static string CurrentSlot => _currentSlot;

        public static int CurrentDay { get; private set; } = 1;
        public static bool SuspendReload { get; set; }

        public static void SetCurrentDay(int day)
        {
            if (day <= 0) day = 1;
            CurrentDay = day;
        }

        private static float _nextSlotPoll;

        private static string SlotDir => Path.Combine(Application.persistentDataPath, _currentSlot);
        private static string FilePath => Path.Combine(SlotDir, "StatisticMod.stats.tsv");

        public static void Init()
        {
            TryUpdateSlotFromSaveManager(force: true);
            Load();
            GetDay(CurrentDay);

            _dirty = false;
            _nextAutoSaveTime = Time.realtimeSinceStartup + 20f;
        }

        public static void TickSlotDetectFromGame()
        {
            if (SuspendReload) return;
            if (Time.realtimeSinceStartup < _nextSlotPoll) return;
            _nextSlotPoll = Time.realtimeSinceStartup + 1.0f;
            TryUpdateSlotFromSaveManager(force: false);
        }

        // Wymusza natychmiastowe wykrycie slotu (wolane przy zaladowaniu save'a).
        public static void RedetectSlotForce()
        {
            TryUpdateSlotFromSaveManager(force: true);
        }

        private static void TryUpdateSlotFromSaveManager(bool force)
        {
            if (SuspendReload) return;
            try
            {
                var sm = SaveManager.Instance;
                if (sm == null) return;

                string savePath = sm.m_CurrentSaveFilePath; // FIX: pole, ktore realnie trzyma zaladowany slot (jak ExpirationSaveManager)
                if (string.IsNullOrEmpty(savePath)) return;

                string saveName = Path.GetFileNameWithoutExtension(savePath);
                if (string.IsNullOrEmpty(saveName) || !saveName.StartsWith("slot_", StringComparison.OrdinalIgnoreCase)) return;

                string slot = saveName.ToLowerInvariant();
                if (!force && _currentSlot == slot) return;

                SetSlot(slot);
                Plugin.DebugLog($"[StatisticMod] Active slot detected: {_currentSlot}");
            }
            catch (Exception e) { Plugin.Log.LogWarning($"[StatisticMod] Slot detect failed: {e}"); }
        }

        public static DayStats TryGetDay(int day)
        {
            for (int i = 0; i < Data.Days.Count; i++)
                if (Data.Days[i].Day == day) return Data.Days[i];
            return null;
        }

        public static DayStats GetDay(int day)
        {
            var ds = FindDay(day);
            if (ds != null) return ds;

            ds = new DayStats { Day = day };
            Data.Days.Add(ds);
            _dirty = true;
            return ds;
        }

        public static void AddSale(int day, int productId, int qty, float revenue)
        {
            AddSale(day, productId, qty, revenue, -1f);
        }

        /// <summary>
        /// Records a unit-based sale. unitCost &lt; 0 means the cost basis was unavailable
        /// and profitability for that portion of the sale must remain unknown.
        /// </summary>
        public static void AddSale(int day, int productId, int qty, float revenue, float unitCost)
        {
            if (qty <= 0) return;
            var ds = GetDay(day);
            ds.SoldUnits += qty;
            ds.SoldRevenue += revenue;
            var p = GetProduct(ds, productId);
            p.SoldUnits += qty;
            p.SoldRevenue += revenue;

            if (unitCost >= 0f)
            {
                float soldCost = unitCost * qty;
                ds.SoldCost += soldCost;
                ds.CostedUnits += qty;
                p.SoldCost += soldCost;
                p.CostedUnits += qty;
            }

            _dirty = true;
        }

        public static void AddSaleF(int day, int productId, float qty, float revenue, bool isWeight)
        {
            AddSaleF(day, productId, qty, revenue, isWeight, -1f);
        }

        /// <summary>
        /// Records a sale and captures the wholesale cost basis at the exact moment of sale.
        /// For weighted products qty and unitCost are both expressed per kilogram.
        /// </summary>
        public static void AddSaleF(int day, int productId, float qty, float revenue, bool isWeight, float unitCost)
        {
            if (qty <= 0f) return;
            var ds = GetDay(day);
            int roundedQty = Mathf.RoundToInt(qty);

            if (isWeight) ds.SoldWeightKg += qty;
            else ds.SoldUnits += roundedQty;
            ds.SoldRevenue += revenue;

            var p = GetProduct(ds, productId);
            if (isWeight) p.SoldWeightKg += qty;
            else p.SoldUnits += roundedQty;
            p.SoldRevenue += revenue;

            if (unitCost >= 0f)
            {
                float soldCost = unitCost * qty;
                ds.SoldCost += soldCost;
                p.SoldCost += soldCost;

                if (isWeight)
                {
                    ds.CostedWeightKg += qty;
                    p.CostedWeightKg += qty;
                }
                else
                {
                    ds.CostedUnits += roundedQty;
                    p.CostedUnits += roundedQty;
                }
            }

            _dirty = true;
        }

        public static void RecordIceCreamSale(
            int day,
            int coneProductId,
            int toppingIndex,
            int scoopCount,
            string recipe,
            float revenue,
            float totalCost,
            bool costKnown)
        {
            var ds = GetDay(day);
            if (ds.IceCreamSales == null) ds.IceCreamSales = new List<IceCreamSaleLine>();

            recipe ??= string.Empty;

            IceCreamSaleLine line = null;
            for (int i = 0; i < ds.IceCreamSales.Count; i++)
            {
                var existing = ds.IceCreamSales[i];
                if (existing == null) continue;

                if (existing.ConeProductId == coneProductId &&
                    existing.ToppingIndex == toppingIndex &&
                    existing.ScoopCount == scoopCount &&
                    string.Equals(existing.Recipe ?? string.Empty, recipe, StringComparison.Ordinal))
                {
                    line = existing;
                    break;
                }
            }

            if (line == null)
            {
                line = new IceCreamSaleLine
                {
                    ConeProductId = coneProductId,
                    ToppingIndex = toppingIndex,
                    ScoopCount = scoopCount,
                    Recipe = recipe
                };
                ds.IceCreamSales.Add(line);
            }

            line.SoldCount += 1;
            line.SoldRevenue += Mathf.Max(0f, revenue);

            if (costKnown && totalCost >= 0f)
            {
                line.SoldCost += totalCost;
                line.CostedCount += 1;
            }

            _dirty = true;
        }

        public static void AddThrown(int day, int productId, int qty, float value)
        {
            if (qty <= 0) return;
            var ds = GetDay(day);
            ds.ThrownUnits += qty;
            ds.ThrownValue += value;
            var p = GetProduct(ds, productId);
            p.ThrownUnits += qty;
            p.ThrownValue += value;
            _dirty = true;
        }

        public static void AddThrownF(int day, int productId, float qty, float value, bool isWeight)
        {
            if (qty <= 0f) return;
            var ds = GetDay(day);
            if (isWeight) ds.ThrownWeightKg += qty;
            else ds.ThrownUnits += Mathf.RoundToInt(qty);
            ds.ThrownValue += value;
            var p = GetProduct(ds, productId);
            if (isWeight) p.ThrownWeightKg += qty;
            else p.ThrownUnits += Mathf.RoundToInt(qty);
            p.ThrownValue += value;
            _dirty = true;
        }

        public static void TickAutoSave()
        {
            // POPRAWKA: Usunięto zapisywanie co 30 sekund.
            // Zapis odbywa się teraz tylko przy zmianie dnia lub po kliknięciu "Zapisz".
        }

        public static void SaveNow()
        {
            string tempPath = null;
            try
            {
                if (string.IsNullOrEmpty(_currentSlot) || Data == null) return;
                Directory.CreateDirectory(SlotDir);

                // SaveNow is now intentionally synchronous. Statistics are only
                // written at rare commit points (real game save / completed day),
                // so finishing the sidecar write before returning keeps it aligned
                // with the game save and avoids losing a queued background write on
                // an immediate quit/reload.
                var daysCopy = new List<DayStats>(Data.Days.Count);
                foreach (var d in Data.Days)
                {
                    if (d == null) continue;

                    var products = d.Products ?? new List<ProductLine>();
                    var iceCreamSales = d.IceCreamSales ?? new List<IceCreamSaleLine>();
                    var dCopy = new DayStats
                    {
                        Day = d.Day,
                        SoldUnits = d.SoldUnits,
                        SoldWeightKg = d.SoldWeightKg,
                        SoldRevenue = d.SoldRevenue,
                        SoldCost = d.SoldCost,
                        CostedUnits = d.CostedUnits,
                        CostedWeightKg = d.CostedWeightKg,
                        ThrownUnits = d.ThrownUnits,
                        ThrownWeightKg = d.ThrownWeightKg,
                        ThrownValue = d.ThrownValue,
                        Products = new List<ProductLine>(products.Count),
                        IceCreamSales = new List<IceCreamSaleLine>(iceCreamSales.Count)
                    };

                    foreach (var product in products)
                    {
                        if (product == null) continue;
                        dCopy.Products.Add(new ProductLine
                        {
                            ProductId = product.ProductId,
                            SoldUnits = product.SoldUnits,
                            SoldWeightKg = product.SoldWeightKg,
                            SoldRevenue = product.SoldRevenue,
                            SoldCost = product.SoldCost,
                            CostedUnits = product.CostedUnits,
                            CostedWeightKg = product.CostedWeightKg,
                            ThrownUnits = product.ThrownUnits,
                            ThrownWeightKg = product.ThrownWeightKg,
                            ThrownValue = product.ThrownValue
                        });
                    }

                    foreach (var ice in iceCreamSales)
                    {
                        if (ice == null) continue;
                        dCopy.IceCreamSales.Add(new IceCreamSaleLine
                        {
                            ConeProductId = ice.ConeProductId,
                            ToppingIndex = ice.ToppingIndex,
                            ScoopCount = ice.ScoopCount,
                            Recipe = ice.Recipe ?? string.Empty,
                            SoldCount = ice.SoldCount,
                            SoldRevenue = ice.SoldRevenue,
                            SoldCost = ice.SoldCost,
                            CostedCount = ice.CostedCount
                        });
                    }

                    daysCopy.Add(dCopy);
                }

                string currentPath = FilePath;
                int generation = Volatile.Read(ref _writeGeneration);
                tempPath = currentPath + ".tmp." + generation + "." + Guid.NewGuid().ToString("N");

                if (generation != Volatile.Read(ref _writeGeneration)) return;

                var ci = CultureInfo.InvariantCulture;
                using (var sw = new StreamWriter(tempPath, false, Encoding.UTF8, 65536))
                {
                    sw.WriteLine("# StatisticMod stats TSV");
                    sw.WriteLine("# DAY\t<day>\tSoldUnits\tSoldWeightKg\tSoldRevenue\tThrownUnits\tThrownWeightKg\tThrownValue\tSoldCost\tCostedUnits\tCostedWeightKg");
                    sw.WriteLine("# PROD\t<day>\t<productId>\tSoldUnits\tSoldWeightKg\tSoldRevenue\tThrownUnits\tThrownWeightKg\tThrownValue\tSoldCost\tCostedUnits\tCostedWeightKg");
                    sw.WriteLine("# ICE\t<day>\t<coneProductId>\t<toppingIndex>\t<scoopCount>\t<soldCount>\t<soldRevenue>\t<soldCost>\t<costedCount>\t<recipe>");

                    foreach (var d in daysCopy)
                    {
                        sw.Write("DAY\t"); sw.Write(d.Day); sw.Write('\t');
                        sw.Write(d.SoldUnits); sw.Write('\t'); sw.Write(d.SoldWeightKg.ToString(ci)); sw.Write('\t');
                        sw.Write(d.SoldRevenue.ToString(ci)); sw.Write('\t');
                        sw.Write(d.ThrownUnits); sw.Write('\t'); sw.Write(d.ThrownWeightKg.ToString(ci)); sw.Write('\t');
                        sw.Write(d.ThrownValue.ToString(ci)); sw.Write('\t');
                        sw.Write(d.SoldCost.ToString(ci)); sw.Write('\t'); sw.Write(d.CostedUnits); sw.Write('\t');
                        sw.WriteLine(d.CostedWeightKg.ToString(ci));

                        foreach (var product in d.Products)
                        {
                            sw.Write("PROD\t"); sw.Write(d.Day); sw.Write('\t'); sw.Write(product.ProductId); sw.Write('\t');
                            sw.Write(product.SoldUnits); sw.Write('\t'); sw.Write(product.SoldWeightKg.ToString(ci)); sw.Write('\t');
                            sw.Write(product.SoldRevenue.ToString(ci)); sw.Write('\t');
                            sw.Write(product.ThrownUnits); sw.Write('\t'); sw.Write(product.ThrownWeightKg.ToString(ci)); sw.Write('\t');
                            sw.Write(product.ThrownValue.ToString(ci)); sw.Write('\t');
                            sw.Write(product.SoldCost.ToString(ci)); sw.Write('\t'); sw.Write(product.CostedUnits); sw.Write('\t');
                            sw.WriteLine(product.CostedWeightKg.ToString(ci));
                        }

                        foreach (var ice in d.IceCreamSales)
                        {
                            if (ice == null) continue;
                            sw.Write("ICE\t"); sw.Write(d.Day); sw.Write('\t');
                            sw.Write(ice.ConeProductId); sw.Write('\t');
                            sw.Write(ice.ToppingIndex); sw.Write('\t');
                            sw.Write(ice.ScoopCount); sw.Write('\t');
                            sw.Write(ice.SoldCount); sw.Write('\t');
                            sw.Write(ice.SoldRevenue.ToString(ci)); sw.Write('\t');
                            sw.Write(ice.SoldCost.ToString(ci)); sw.Write('\t');
                            sw.Write(ice.CostedCount); sw.Write('\t');
                            sw.WriteLine(ice.Recipe ?? string.Empty);
                        }
                    }
                }

                lock (_fileIoLock)
                {
                    if (generation != Volatile.Read(ref _writeGeneration)) return;
                    if (File.Exists(currentPath)) File.Delete(currentPath);
                    File.Move(tempPath, currentPath);
                    tempPath = null;
                }

                _dirty = false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[StatisticMod] Save failed: {e}");
            }
            finally
            {
                if (!string.IsNullOrEmpty(tempPath)) DeleteFileSafe(tempPath);
            }
        }

        // Publiczna pelna sciezka aktualnie uzywanego pliku statystyk - do diagnostyki.
        public static string AbsoluteFilePath => FilePath;

        public static void Load()
        {
            var newData = new StatsData();
            Plugin.Log.LogInfo($"[StatisticMod] Wczytuje statystyki z: {FilePath} (istnieje={File.Exists(FilePath)})");
            try
            {
                if (!File.Exists(FilePath))
                {
                    Data = newData;
                    _dirty = false;
                    return;
                }
                var ci = CultureInfo.InvariantCulture;

                var dayDict = new Dictionary<int, DayStats>();
                var prodDict = new Dictionary<int, Dictionary<int, ProductLine>>();

                DayStats GetDayLocal(int day)
                {
                    if (dayDict.TryGetValue(day, out var ds)) return ds;
                    ds = new DayStats { Day = day };
                    newData.Days.Add(ds);
                    dayDict[day] = ds;
                    prodDict[day] = new Dictionary<int, ProductLine>();
                    return ds;
                }

                ProductLine GetProductLocal(DayStats ds, int pid)
                {
                    var pDict = prodDict[ds.Day];
                    if (pDict.TryGetValue(pid, out var pl)) return pl;
                    pl = new ProductLine { ProductId = pid };
                    ds.Products.Add(pl);
                    pDict[pid] = pl;
                    return pl;
                }

                // ⚡ OPTYMALIZACJA PAMIĘCI: Czytamy linijka po linijce, a nie cały plik na raz
                using (var reader = new StreamReader(FilePath, Encoding.UTF8))
                {
                    string raw;
                    while ((raw = reader.ReadLine()) != null)
                    {
                        if (string.IsNullOrWhiteSpace(raw) || raw.StartsWith("#")) continue;
                        var parts = raw.Split('\t');
                        if (parts.Length < 2) continue;

                        if (parts[0] == "DAY")
                        {
                            int dNum = int.Parse(parts[1]);
                            var ds = GetDayLocal(dNum);
                            if (parts.Length >= 8)
                            {
                                ds.SoldUnits = int.Parse(parts[2]);
                                ds.SoldWeightKg = float.Parse(parts[3], ci);
                                ds.SoldRevenue = float.Parse(parts[4], ci);
                                ds.ThrownUnits = int.Parse(parts[5]);
                                ds.ThrownWeightKg = float.Parse(parts[6], ci);
                                ds.ThrownValue = float.Parse(parts[7], ci);
                                if (parts.Length >= 11)
                                {
                                    ds.SoldCost = float.Parse(parts[8], ci);
                                    ds.CostedUnits = int.Parse(parts[9]);
                                    ds.CostedWeightKg = float.Parse(parts[10], ci);
                                }
                            }
                        }
                        else if (parts[0] == "ICE")
                        {
                            if (parts.Length < 9) continue;

                            int dNum = int.Parse(parts[1]);
                            var ds = GetDayLocal(dNum);
                            if (ds.IceCreamSales == null) ds.IceCreamSales = new List<IceCreamSaleLine>();

                            var ice = new IceCreamSaleLine
                            {
                                ConeProductId = int.Parse(parts[2]),
                                ToppingIndex = int.Parse(parts[3]),
                                ScoopCount = int.Parse(parts[4]),
                                SoldCount = int.Parse(parts[5]),
                                SoldRevenue = float.Parse(parts[6], ci),
                                SoldCost = float.Parse(parts[7], ci),
                                CostedCount = int.Parse(parts[8]),
                                Recipe = parts.Length >= 10 ? parts[9] : string.Empty
                            };
                            ds.IceCreamSales.Add(ice);
                        }
                        else if (parts[0] == "PROD")
                        {
                            int dNum = int.Parse(parts[1]);
                            int pid = int.Parse(parts[2]);
                            var ds = GetDayLocal(dNum);
                            var pl = GetProductLocal(ds, pid);
                            if (parts.Length >= 9)
                            {
                                pl.SoldUnits = int.Parse(parts[3]);
                                pl.SoldWeightKg = float.Parse(parts[4], ci);
                                pl.SoldRevenue = float.Parse(parts[5], ci);
                                pl.ThrownUnits = int.Parse(parts[6]);
                                pl.ThrownWeightKg = float.Parse(parts[7], ci);
                                pl.ThrownValue = float.Parse(parts[8], ci);
                                if (parts.Length >= 12)
                                {
                                    pl.SoldCost = float.Parse(parts[9], ci);
                                    pl.CostedUnits = int.Parse(parts[10]);
                                    pl.CostedWeightKg = float.Parse(parts[11], ci);
                                }
                            }
                        }
                    }
                }
                FixupWeightRevenueAndReturnFixCount(newData);
                Data = newData;
                _dirty = false;
            }
            catch (Exception e) { Plugin.Log.LogError($"[StatisticMod] Load Error: {e.Message}"); }
        }

        private static int FixupWeightRevenueAndReturnFixCount(StatsData data)
        {
            if (data == null || data.Days == null) return 0;
            int fixedLines = 0;
            static bool FixMoneyPerKg(ref float money, float kg, ref int fixedLinesLocal)
            {
                if (kg <= 0.0001f || money <= 0.0001f) return false;
                float unitGuess = money / kg;
                if (unitGuess > 500f) { money /= 1000f; fixedLinesLocal++; return true; }
                if (unitGuess > 200f) { money /= 100f; fixedLinesLocal++; return true; }
                if (unitGuess > 80f) { money /= 10f; fixedLinesLocal++; return true; }
                return false;
            }

            foreach (var d in data.Days)
            {
                if (d?.Products == null) continue;
                foreach (var p in d.Products)
                {
                    if (p == null) continue;
                    float rev = p.SoldRevenue;
                    if (FixMoneyPerKg(ref rev, p.SoldWeightKg, ref fixedLines)) p.SoldRevenue = rev;
                    float thr = p.ThrownValue;
                    if (FixMoneyPerKg(ref thr, p.ThrownWeightKg, ref fixedLines)) p.ThrownValue = thr;
                }
            }

            foreach (var d in data.Days)
            {
                if (d?.Products == null) continue;
                float sR = 0f; float tV = 0f; float soldCost = 0f; int sU = 0; int tU = 0;
                int costedUnits = 0; float sK = 0f; float tK = 0f; float costedWeightKg = 0f;
                foreach (var p in d.Products)
                {
                    if (p == null) continue;
                    sR += p.SoldRevenue; tV += p.ThrownValue; soldCost += p.SoldCost;
                    sU += p.SoldUnits; tU += p.ThrownUnits; costedUnits += p.CostedUnits;
                    sK += p.SoldWeightKg; tK += p.ThrownWeightKg; costedWeightKg += p.CostedWeightKg;
                }
                d.SoldRevenue = sR; d.ThrownValue = tV; d.SoldCost = soldCost;
                d.SoldUnits = sU; d.ThrownUnits = tU; d.CostedUnits = costedUnits;
                d.SoldWeightKg = sK; d.ThrownWeightKg = tK; d.CostedWeightKg = costedWeightKg;
            }
            return fixedLines;
        }

        private static DayStats FindDay(int day)
        {
            for (int i = 0; i < Data.Days.Count; i++)
                if (Data.Days[i].Day == day) return Data.Days[i];
            return null;
        }

        private static ProductLine GetProduct(DayStats ds, int productId)
        {
            for (int i = 0; i < ds.Products.Count; i++)
                if (ds.Products[i].ProductId == productId) return ds.Products[i];
            var p = new ProductLine { ProductId = productId };
            ds.Products.Add(p);
            return p;
        }

        /// <summary>
        /// Czyści historię sprzedaży/strat dla nowej gry w podanym slocie.
        /// Nie wywołuje SaveNow(), więc stary stan nie zostanie ponownie zapisany.
        /// </summary>
        public static void ResetForNewGame(string slotName)
        {
            slotName = NormalizeSlotName(slotName);

            // Unieważnij wszystkie wcześniej zakolejkowane zapisy.
            Interlocked.Increment(ref _writeGeneration);

            _currentSlot = slotName;
            CurrentDay = 1;
            SuspendReload = false;
            _nextSlotPoll = 0f;
            _nextAutoSaveTime = Time.realtimeSinceStartup + 20f;

            Data = new StatsData();
            Data.Days.Add(new DayStats { Day = 1 });
            _dirty = false;

            lock (_fileIoLock)
            {
                DeleteFileSafe(FilePath);
                DeleteFileSafe(FilePath + ".tmp");
                DeleteTempFilesSafe(FilePath);
            }
        }

        private static string NormalizeSlotName(string slotName)
        {
            if (string.IsNullOrWhiteSpace(slotName)) return "slot_0";
            string normalized = Path.GetFileNameWithoutExtension(slotName.Trim()).ToLowerInvariant();
            return normalized.StartsWith("slot_", StringComparison.OrdinalIgnoreCase)
                ? normalized
                : "slot_0";
        }

        private static void DeleteTempFilesSafe(string basePath)
        {
            try
            {
                string directory = Path.GetDirectoryName(basePath);
                string fileName = Path.GetFileName(basePath);
                if (string.IsNullOrEmpty(directory) || !Directory.Exists(directory)) return;

                string[] files = Directory.GetFiles(directory, fileName + ".tmp*");
                for (int i = 0; i < files.Length; i++) DeleteFileSafe(files[i]);
            }
            catch { }
        }

        private static void DeleteFileSafe(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        public static void SetSlotIndex(int index) { SetSlot($"slot_{index}"); }

        public static void SetSlot(string slotName)
        {
            if (SuspendReload) return;
            if (string.IsNullOrWhiteSpace(slotName)) slotName = "slot_0";
            slotName = slotName.ToLowerInvariant();
            if (_currentSlot == slotName) return;

            // Slot/load changes are not statistics commit points. If the player
            // abandons unsaved game progress, matching runtime stats must be
            // abandoned too and restored from the last committed sidecar file.
            _currentSlot = slotName;
            Load();
            _dirty = false;
            _nextAutoSaveTime = Time.realtimeSinceStartup + 20f;
        }
    }
}

namespace System.Runtime.CompilerServices
{
    [AttributeUsage(AttributeTargets.All)]
    internal sealed class NullableAttribute : Attribute
    {
        public readonly byte[] NullableFlags;
        public NullableAttribute(byte flag) { NullableFlags = new[] { flag }; }
        public NullableAttribute(byte[] flags) { NullableFlags = flags; }
    }

    [AttributeUsage(AttributeTargets.All)]
    internal sealed class NullableContextAttribute : Attribute
    {
        public readonly byte Flag;
        public NullableContextAttribute(byte flag) { Flag = flag; }
    }
}