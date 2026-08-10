using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace StatisticMod
{
    /// <summary>
    /// Osobny, slot-aware zapis podsumowań dnia.
    /// Plik jest tworzony obok głównego pliku StatsStore:
    /// StatisticMod.stats.tsv.daily.tsv
    /// </summary>
    public static class DailySummaryStore
    {
        private static readonly Dictionary<int, DailySummaryStats> _days =
            new Dictionary<int, DailySummaryStats>();

        private static string _activePath;
        private static bool _dirty;

        public static string AbsoluteFilePath => ResolvePath();

        public static void Init()
        {
            _activePath = ResolvePath();
            LoadInternal(_activePath);
        }

        /// <summary>
        /// Wywołuj po StatsStore.TickSlotDetectFromGame().
        /// Dzięki temu podsumowania przełączają się razem ze slotem zapisu gry.
        /// </summary>
        public static void TickPath()
        {
            string resolved = ResolvePath();
            if (string.Equals(resolved, _activePath, StringComparison.OrdinalIgnoreCase))
                return;

            SaveNow();
            _activePath = resolved;
            LoadInternal(_activePath);
        }

        public static void Load()
        {
            SaveNow();
            _activePath = ResolvePath();
            LoadInternal(_activePath);
        }

        public static DailySummaryStats TryGetDay(int day)
        {
            if (day < 1) return null;
            _days.TryGetValue(day, out DailySummaryStats result);
            return result;
        }

        public static List<DailySummaryStats> GetDaysSnapshot()
        {
            var result = new List<DailySummaryStats>(_days.Values);
            result.Sort((a, b) => a.Day.CompareTo(b.Day));
            return result;
        }

        public static void SetDay(DailySummaryStats summary)
        {
            if (summary == null || summary.Day < 1) return;

            summary.Captured = true;
            _days[summary.Day] = summary;
            _dirty = true;

            // Podsumowanie pojawia się raz na zakończenie dnia, więc bezpiecznie
            // zapisujemy je od razu. Nie jest to operacja wykonywana co klatkę.
            SaveNow();
        }

        public static void SaveNow()
        {
            if (!_dirty) return;

            string path = string.IsNullOrEmpty(_activePath)
                ? ResolvePath()
                : _activePath;

            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory))
                    Directory.CreateDirectory(directory);

                string temp = path + ".tmp";
                var lines = new List<string>();

                lines.Add(
                    "Day\tCaptured\tSatisfiedCustomerCount\tCouldntFindProduct\t" +
                    "ExpensiveProducts\tShortChangeAmount\tHarmedCustomerCount\t" +
                    "TotalCustomerCount\tStorePoint\tCheckoutIncome\tSupplyCosts\t" +
                    "UpgradeCosts\tCustomizationCosts\tBillCosts\tVendingIncome\t" +
                    "RentCosts\tLoanIncome\tLoanPayment\tStaffPayment\tPaintCosts\t" +
                    "FloorBoxCosts\tDailyProfit\tBalance");

                List<DailySummaryStats> sorted = GetDaysSnapshot();
                for (int i = 0; i < sorted.Count; i++)
                {
                    DailySummaryStats d = sorted[i];
                    if (d == null || d.Day < 1) continue;

                    lines.Add(string.Join("\t", new[]
                    {
                        d.Day.ToString(CultureInfo.InvariantCulture),
                        d.Captured ? "1" : "0",
                        d.SatisfiedCustomerCount.ToString(CultureInfo.InvariantCulture),
                        d.CouldntFindProduct.ToString(CultureInfo.InvariantCulture),
                        d.ExpensiveProducts.ToString(CultureInfo.InvariantCulture),
                        d.ShortChangeAmount.ToString(CultureInfo.InvariantCulture),
                        d.HarmedCustomerCount.ToString(CultureInfo.InvariantCulture),
                        d.TotalCustomerCount.ToString(CultureInfo.InvariantCulture),
                        d.StorePoint.ToString(CultureInfo.InvariantCulture),
                        F(d.CheckoutIncome),
                        F(d.SupplyCosts),
                        F(d.UpgradeCosts),
                        F(d.CustomizationCosts),
                        F(d.BillCosts),
                        F(d.VendingIncome),
                        F(d.RentCosts),
                        F(d.LoanIncome),
                        F(d.LoanPayment),
                        F(d.StaffPayment),
                        F(d.PaintCosts),
                        F(d.FloorBoxCosts),
                        F(d.DailyProfit),
                        F(d.Balance)
                    }));
                }

                File.WriteAllLines(temp, lines);

                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);

                _dirty = false;
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("[DailySummaryStore] Save failed: " + ex.Message);
            }
        }

        private static void LoadInternal(string path)
        {
            _days.Clear();
            _dirty = false;

            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;

            try
            {
                foreach (string line in File.ReadLines(path))
                {
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (line.StartsWith("Day\t", StringComparison.OrdinalIgnoreCase)) continue;

                    string[] p = line.Split('\t');
                    if (p.Length < 23) continue;
                    if (!int.TryParse(p[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int day)) continue;
                    if (day < 1) continue;

                    var d = new DailySummaryStats
                    {
                        Day = day,
                        Captured = p[1] == "1" || string.Equals(p[1], "true", StringComparison.OrdinalIgnoreCase),
                        SatisfiedCustomerCount = PI(p, 2),
                        CouldntFindProduct = PI(p, 3),
                        ExpensiveProducts = PI(p, 4),
                        ShortChangeAmount = PI(p, 5),
                        HarmedCustomerCount = PI(p, 6),
                        TotalCustomerCount = PI(p, 7),
                        StorePoint = PI(p, 8),
                        CheckoutIncome = PF(p, 9),
                        SupplyCosts = PF(p, 10),
                        UpgradeCosts = PF(p, 11),
                        CustomizationCosts = PF(p, 12),
                        BillCosts = PF(p, 13),
                        VendingIncome = PF(p, 14),
                        RentCosts = PF(p, 15),
                        LoanIncome = PF(p, 16),
                        LoanPayment = PF(p, 17),
                        StaffPayment = PF(p, 18),
                        PaintCosts = PF(p, 19),
                        FloorBoxCosts = PF(p, 20),
                        DailyProfit = PF(p, 21),
                        Balance = PF(p, 22)
                    };

                    _days[day] = d;
                }
            }
            catch (Exception ex)
            {
                Plugin.Log?.LogWarning("[DailySummaryStore] Load failed: " + ex.Message);
                _days.Clear();
            }
        }

        /// <summary>
        /// Czyści podsumowania dni bieżącego slotu bez zapisywania starej pamięci.
        /// Wywoływane wyłącznie przy potwierdzonym rozpoczęciu nowej gry.
        /// </summary>
        public static void ResetForNewGame()
        {
            _activePath = ResolvePath();
            _days.Clear();
            _dirty = false;

            DeleteFileSafe(_activePath);
            DeleteFileSafe(_activePath + ".tmp");
        }

        private static void DeleteFileSafe(string path)
        {
            try
            {
                if (!string.IsNullOrEmpty(path) && File.Exists(path)) File.Delete(path);
            }
            catch { }
        }

        private static string ResolvePath()
        {
            try
            {
                string statsPath = StatsStore.AbsoluteFilePath;
                if (!string.IsNullOrEmpty(statsPath))
                    return statsPath + ".daily.tsv";
            }
            catch { }

            return Path.Combine(
                UnityEngine.Application.persistentDataPath,
                "StatisticMod.stats.daily.tsv");
        }

        private static string F(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static int PI(string[] p, int index)
        {
            if (p == null || index < 0 || index >= p.Length) return 0;
            int.TryParse(p[index], NumberStyles.Integer, CultureInfo.InvariantCulture, out int value);
            return value;
        }

        private static float PF(string[] p, int index)
        {
            if (p == null || index < 0 || index >= p.Length) return 0f;
            float.TryParse(p[index], NumberStyles.Float, CultureInfo.InvariantCulture, out float value);
            return value;
        }
    }
}
