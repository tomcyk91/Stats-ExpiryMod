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
            if (qty <= 0) return;
            var ds = GetDay(day);
            ds.SoldUnits += qty;
            ds.SoldRevenue += revenue;
            var p = GetProduct(ds, productId);
            p.SoldUnits += qty;
            p.SoldRevenue += revenue;
            _dirty = true;
        }

        public static void AddSaleF(int day, int productId, float qty, float revenue, bool isWeight)
        {
            if (qty <= 0f) return;
            var ds = GetDay(day);
            if (isWeight) ds.SoldWeightKg += qty;
            else ds.SoldUnits += Mathf.RoundToInt(qty);
            ds.SoldRevenue += revenue;
            var p = GetProduct(ds, productId);
            if (isWeight) p.SoldWeightKg += qty;
            else p.SoldUnits += Mathf.RoundToInt(qty);
            p.SoldRevenue += revenue;
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
            try
            {
                if (string.IsNullOrEmpty(_currentSlot)) return;
                Directory.CreateDirectory(SlotDir);

                var daysCopy = new List<DayStats>(Data.Days.Count);
                foreach (var d in Data.Days)
                {
                    var dCopy = new DayStats
                    {
                        Day = d.Day,
                        SoldUnits = d.SoldUnits,
                        SoldWeightKg = d.SoldWeightKg,
                        SoldRevenue = d.SoldRevenue,
                        ThrownUnits = d.ThrownUnits,
                        ThrownWeightKg = d.ThrownWeightKg,
                        ThrownValue = d.ThrownValue,
                        Products = new List<ProductLine>(d.Products.Count)
                    };

                    foreach (var p in d.Products)
                    {
                        dCopy.Products.Add(new ProductLine
                        {
                            ProductId = p.ProductId,
                            SoldUnits = p.SoldUnits,
                            SoldWeightKg = p.SoldWeightKg,
                            SoldRevenue = p.SoldRevenue,
                            ThrownUnits = p.ThrownUnits,
                            ThrownWeightKg = p.ThrownWeightKg,
                            ThrownValue = p.ThrownValue
                        });
                    }
                    daysCopy.Add(dCopy);
                }

                string currentPath = FilePath;
                string slotName = _currentSlot;

                ThreadPool.QueueUserWorkItem(state =>
                {
                    try
                    {
                        var ci = CultureInfo.InvariantCulture;

                        using (var sw = new StreamWriter(currentPath, false, Encoding.UTF8, 65536))
                        {
                            sw.WriteLine("# StatisticMod stats TSV");
                            sw.WriteLine("# DAY\t<day>\tSoldUnits\tSoldWeightKg\tSoldRevenue\tThrownUnits\tThrownWeightKg\tThrownValue");
                            sw.WriteLine("# PROD\t<day>\t<productId>\tSoldUnits\tSoldWeightKg\tSoldRevenue\tThrownUnits\tThrownWeightKg\tThrownValue");

                            foreach (var d in daysCopy)
                            {
                                sw.Write("DAY\t"); sw.Write(d.Day); sw.Write('\t');
                                sw.Write(d.SoldUnits); sw.Write('\t'); sw.Write(d.SoldWeightKg.ToString(ci)); sw.Write('\t');
                                sw.Write(d.SoldRevenue.ToString(ci)); sw.Write('\t');
                                sw.Write(d.ThrownUnits); sw.Write('\t'); sw.Write(d.ThrownWeightKg.ToString(ci)); sw.Write('\t');
                                sw.WriteLine(d.ThrownValue.ToString(ci));

                                foreach (var p in d.Products)
                                {
                                    sw.Write("PROD\t"); sw.Write(d.Day); sw.Write('\t'); sw.Write(p.ProductId); sw.Write('\t');
                                    sw.Write(p.SoldUnits); sw.Write('\t'); sw.Write(p.SoldWeightKg.ToString(ci)); sw.Write('\t');
                                    sw.Write(p.SoldRevenue.ToString(ci)); sw.Write('\t');
                                    sw.Write(p.ThrownUnits); sw.Write('\t'); sw.Write(p.ThrownWeightKg.ToString(ci)); sw.Write('\t');
                                    sw.WriteLine(p.ThrownValue.ToString(ci));
                                }
                            }
                        }
                    }
                    catch { }
                });

                _dirty = false;
            }
            catch (Exception e)
            {
                Plugin.Log.LogError($"[StatisticMod] Save setup failed: {e}");
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
                if (!File.Exists(FilePath)) { Data = newData; return; }
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
                            }
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
                            }
                        }
                    }
                }
                FixupWeightRevenueAndReturnFixCount(newData);
                Data = newData;
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
                float sR = 0f; float tV = 0f; int sU = 0; int tU = 0; float sK = 0f; float tK = 0f;
                foreach (var p in d.Products)
                {
                    if (p == null) continue;
                    sR += p.SoldRevenue; tV += p.ThrownValue;
                    sU += p.SoldUnits; tU += p.ThrownUnits;
                    sK += p.SoldWeightKg; tK += p.ThrownWeightKg;
                }
                d.SoldRevenue = sR; d.ThrownValue = tV; d.SoldUnits = sU; d.ThrownUnits = tU; d.SoldWeightKg = sK; d.ThrownWeightKg = tK;
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

        public static void SetSlotIndex(int index) { SetSlot($"slot_{index}"); }

        public static void SetSlot(string slotName)
        {
            if (SuspendReload) return;
            if (string.IsNullOrWhiteSpace(slotName)) slotName = "slot_0";
            slotName = slotName.ToLowerInvariant();
            if (_currentSlot == slotName) return;
            if (_dirty) SaveNow();
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