using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using UnityEngine;

namespace StatisticMod
{
    public static class BusinessAnalysisStore
    {
        private const int CurrentSchemaVersion = 2;

        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            IncludeFields = true,
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        private static string _activePath;
        private static bool _dirty;
        private static float _nextSaveAt;
        private static int _currentDay;

        public static BusinessAnalysisData Data { get; private set; } = new BusinessAnalysisData();
        public static string AbsoluteFilePath => ResolvePath();

        public static void Init()
        {
            _activePath = ResolvePath();
            _currentDay = 0;
            LoadInternal(_activePath);
        }

        public static void TickPath()
        {
            string resolved = ResolvePath();
            if (string.Equals(resolved, _activePath, StringComparison.OrdinalIgnoreCase)) return;

            SaveNow();
            _activePath = resolved;
            _currentDay = 0;
            LoadInternal(_activePath);
            DemandTrackingManager.ClearAllSessions();
        }

        public static void Load()
        {
            _activePath = ResolvePath();
            _currentDay = 0;
            LoadInternal(_activePath);
            DemandTrackingManager.ClearAllSessions();
        }

        /// <summary>
        /// Ustawia aktualny dzień gry i naprawia pliki schematu v1, w których
        /// bieżący dzień był błędnie umieszczany na liście zakończonych dni.
        /// </summary>
        public static void SetCurrentDay(int day)
        {
            if (day < 1) day = 1;
            if (_currentDay == day) return;

            _currentDay = day;
            bool changed = RepairDayPlacement(day);

            // Gdy Analiza została dodana do istniejącego zapisu, jej własny JSON
            // może nie zawierać wcześniejszych dni. Odtwarzamy wtedy zakończone dni
            // z głównego StatsStore. Sprzedaż staje się minimalnym przybliżeniem
            // popytu, a nowe dni nadal otrzymują pełne dane z hooków klientów.
            changed |= EnsureClosedDaysFromStatsStore(day);
            changed |= ReconcileAllClosedDays();

            if (changed) MarkDirty();
        }

        /// <summary>
        /// Zamyka wskazany dzień bez operacji dyskowej. Sprzedaż jest
        /// synchronizowana z głównym StatsStore, który pozostaje źródłem prawdy.
        /// </summary>
        public static void CloseDay(int day)
        {
            if (day < 1) return;
            EnsureData();

            BusinessDayData target = FindClosedDay(day);

            if (Data.OpenDay != null && Data.OpenDay.Day == day)
            {
                if (target == null)
                {
                    target = Data.OpenDay;
                    Data.Days.Add(target);
                }
                else
                {
                    MergeDayInto(target, Data.OpenDay);
                }

                Data.OpenDay = null;
            }

            if (target == null)
            {
                target = new BusinessDayData { Day = day };
                Data.Days.Add(target);
            }

            NormalizeDay(target, false);
            ReconcileClosedDaySales(target);
            RecomputeDayTotals(target);
            SortClosedDays();
            MarkDirty();
        }

        public static BusinessDayData TryGetDay(int day)
        {
            return FindClosedDay(day);
        }

        public static BusinessDayData GetOpenDay()
        {
            return Data?.OpenDay;
        }

        internal static void RecordCustomerResult(int day, List<DemandResultItem> items)
        {
            if (items == null || items.Count == 0) return;

            BusinessDayData dayData = GetWritableDay(day);
            dayData.CustomerVisits++;

            bool anyMissed = false;
            float customerLostRevenue = 0f;

            for (int i = 0; i < items.Count; i++)
            {
                DemandResultItem item = items[i];
                if (item == null || item.ProductId <= 0 || item.RequestedUnits <= 0) continue;

                BusinessProductLine line = GetProductLine(dayData, item.ProductId);
                line.RequestedUnits += item.RequestedUnits;
                line.PickedUnits += item.PickedUnits;
                line.FulfilledUnits = line.PickedUnits; // zgodność ze schematem v1
                line.MissedUnits += item.MissedUnits;
                line.WasDisplayed |= item.WasDisplayed || item.PickedUnits > 0;

                if (item.IsWeight && item.KgPerUnit > 0f)
                {
                    line.RequestedWeightKg += item.RequestedUnits * item.KgPerUnit;
                    line.PickedWeightKg += item.PickedUnits * item.KgPerUnit;
                    line.FulfilledWeightKg = line.PickedWeightKg;
                    line.MissedWeightKg += item.MissedUnits * item.KgPerUnit;
                }

                if (item.MissedUnits > 0)
                {
                    anyMissed = true;
                    bool isStockRelated = false;

                    switch (item.MissReason)
                    {
                        case MissReason.GlobalOutOfStock:
                            line.GlobalOutOfStockUnits += item.MissedUnits;
                            isStockRelated = true;
                            break;
                        case MissReason.ShelfEmpty:
                            line.ShelfEmptyUnits += item.MissedUnits;
                            isStockRelated = true;
                            break;
                        case MissReason.NotDisplayed:
                            line.NotDisplayedUnits += item.MissedUnits;
                            isStockRelated = true;
                            break;
                        default:
                            line.OtherUnfulfilledUnits += item.MissedUnits;
                            break;
                    }

                    // Tylko potwierdzony brak zapasu/niewystawienie oznacza
                    // utracony przychód. Zachowanie AI oznaczone jako Other nie.
                    if (isStockRelated && item.PriceAtRequest > 0f)
                    {
                        float missedValue = item.IsWeight
                            ? item.MissedUnits * item.KgPerUnit * item.PriceAtRequest
                            : item.MissedUnits * item.PriceAtRequest;

                        if (missedValue > 0f)
                        {
                            line.MissedRevenue += missedValue;
                            customerLostRevenue += missedValue;
                        }
                    }
                }
            }

            if (anyMissed) dayData.CustomersWithMissingProducts++;
            else dayData.FullySatisfiedCustomers++;

            dayData.PotentialRevenueLost += customerLostRevenue;
            MarkDirty();
        }

        /// <summary>
        /// Dopisuje sprzedaż w chwili potwierdzonej płatności. Przy zamknięciu
        /// dnia wartości i tak zostaną autorytatywnie nadpisane danymi StatsStore.
        /// </summary>
        public static void RecordConfirmedSale(
            int day,
            int productId,
            float physicalUnits,
            float weightKg,
            float revenue,
            bool isWeight)
        {
            if (day < 1) day = 1;
            if (productId <= 0) return;
            if (physicalUnits <= 0.0001f && weightKg <= 0.0001f) return;

            BusinessDayData dayData = GetWritableDay(day);
            BusinessProductLine line = GetProductLine(dayData, productId);

            if (isWeight)
            {
                // Dokładnie jak w StatsStore: produkt wagowy zapisuje kilogramy,
                // a SoldUnits pozostaje 0. Liczba fizycznych obiektów jest w PickedUnits.
                line.SoldWeightKg += Mathf.Max(0f, weightKg);
            }
            else
            {
                line.SoldUnits += Mathf.Max(0, Mathf.RoundToInt(physicalUnits));
            }

            line.SoldRevenue += Mathf.Max(0f, revenue);

            if (productId == 9999)
                ApplySpecialSaleAsDemand(line);

            MarkDirty();
        }

        public static void MarkDirty()
        {
            _dirty = true;
            if (_nextSaveAt <= 0f) _nextSaveAt = Time.realtimeSinceStartup + 8f;
        }

        public static void SaveIfDirty()
        {
            if (!_dirty) return;
            if (Time.realtimeSinceStartup < _nextSaveAt) return;
            SaveNow();
        }

        public static void SaveNow()
        {
            if (!_dirty || Data == null) return;

            string path = string.IsNullOrEmpty(_activePath) ? ResolvePath() : _activePath;
            try
            {
                string directory = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                string temp = path + ".tmp";
                string json = JsonSerializer.Serialize(Data, JsonOptions);
                File.WriteAllText(temp, json);

                if (File.Exists(path)) File.Delete(path);
                File.Move(temp, path);

                _dirty = false;
                _nextSaveAt = 0f;
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning("[BusinessAnalysisStore] Save failed: " + ex.Message);
                _nextSaveAt = Time.realtimeSinceStartup + 15f;
            }
        }

        private static BusinessDayData GetWritableDay(int day)
        {
            if (day < 1) day = 1;
            EnsureData();

            // Spóźniony hook klienta z poprzedniego dnia dopisujemy do historii,
            // zamiast tworzyć z niego bieżący dzień.
            if (_currentDay > 0 && day < _currentDay)
            {
                BusinessDayData closed = FindClosedDay(day);
                if (closed == null)
                {
                    closed = new BusinessDayData { Day = day };
                    Data.Days.Add(closed);
                    SortClosedDays();
                }
                return closed;
            }

            if (Data.OpenDay == null)
            {
                Data.OpenDay = new BusinessDayData { Day = day };
                return Data.OpenDay;
            }

            if (Data.OpenDay.Day == day) return Data.OpenDay;

            if (Data.OpenDay.Day < day)
            {
                CloseDay(Data.OpenDay.Day);
                Data.OpenDay = new BusinessDayData { Day = day };
                return Data.OpenDay;
            }

            // Dane starszego dnia przy istniejącym nowszym buforze.
            BusinessDayData fallback = FindClosedDay(day);
            if (fallback == null)
            {
                fallback = new BusinessDayData { Day = day };
                Data.Days.Add(fallback);
                SortClosedDays();
            }
            return fallback;
        }

        private static bool RepairDayPlacement(int currentDay)
        {
            EnsureData();
            bool changed = false;

            // Jeśli zapisano bufor poprzedniego dnia, zamykamy go po wczytaniu.
            if (Data.OpenDay != null && Data.OpenDay.Day < currentDay)
            {
                BusinessDayData oldOpen = Data.OpenDay;
                Data.OpenDay = null;
                BusinessDayData closed = FindClosedDay(oldOpen.Day);
                if (closed == null) Data.Days.Add(oldOpen);
                else MergeDayInto(closed, oldOpen);
                changed = true;
            }

            // Migracja v1: bieżący dzień znajdował się błędnie w Days.
            for (int i = Data.Days.Count - 1; i >= 0; i--)
            {
                BusinessDayData day = Data.Days[i];
                if (day == null || day.Day != currentDay) continue;

                if (Data.OpenDay == null) Data.OpenDay = day;
                else MergeDayInto(Data.OpenDay, day);

                Data.Days.RemoveAt(i);
                changed = true;
            }

            SortClosedDays();
            return changed;
        }

        private static bool EnsureClosedDaysFromStatsStore(int currentDay)
        {
            EnsureData();

            List<DayStats> statsDays = StatsStore.Data?.Days;
            if (statsDays == null || statsDays.Count == 0) return false;

            bool changed = false;
            int created = 0;

            for (int i = 0; i < statsDays.Count; i++)
            {
                DayStats source = statsDays[i];
                if (source == null || source.Day < 1 || source.Day >= currentDay)
                    continue;

                BusinessDayData target = FindClosedDay(source.Day);
                if (target == null)
                {
                    target = new BusinessDayData { Day = source.Day };
                    Data.Days.Add(target);
                    created++;
                    changed = true;
                }

                changed |= ReconcileClosedDaySales(target);
                RecomputeDayTotals(target);
            }

            if (changed)
            {
                SortClosedDays();

                if (created > 0)
                {
                    Plugin.Log?.LogInfo(
                        $"[BusinessAnalysis] Odtworzono {created} zakończonych dni z historii StatsStore.");
                }
            }

            return changed;
        }

        private static bool ReconcileAllClosedDays()
        {
            if (Data?.Days == null) return false;
            bool changed = false;

            for (int i = 0; i < Data.Days.Count; i++)
            {
                BusinessDayData day = Data.Days[i];
                if (day == null) continue;
                changed |= ReconcileClosedDaySales(day);
                RecomputeDayTotals(day);
            }

            return changed;
        }

        private static bool ReconcileClosedDaySales(BusinessDayData target)
        {
            if (target == null) return false;
            DayStats source = FindStatsDay(target.Day);
            if (source == null || source.Products == null) return false;

            bool changed = false;

            // Sprzedaż zamkniętego dnia zawsze pochodzi z głównego StatsStore.
            if (target.Products == null) target.Products = new List<BusinessProductLine>();
            for (int i = 0; i < target.Products.Count; i++)
            {
                BusinessProductLine line = target.Products[i];
                if (line == null) continue;
                if (line.SoldUnits != 0 || Mathf.Abs(line.SoldWeightKg) > 0.0001f || Mathf.Abs(line.SoldRevenue) > 0.0001f)
                    changed = true;
                line.SoldUnits = 0;
                line.SoldWeightKg = 0f;
                line.SoldRevenue = 0f;
            }

            for (int i = 0; i < source.Products.Count; i++)
            {
                ProductLine statsLine = source.Products[i];
                if (statsLine == null || statsLine.ProductId <= 0) continue;

                BusinessProductLine line = GetProductLine(target, statsLine.ProductId);
                bool isWeight = SalesUnifiedFinal.WeightPerUnit.TryGetValue(statsLine.ProductId, out float kgPerUnit);

                if (isWeight)
                {
                    line.SoldWeightKg = Mathf.Max(0f, statsLine.SoldWeightKg);
                    line.SoldUnits = Mathf.Max(0, statsLine.SoldUnits);
                }
                else
                {
                    line.SoldUnits = Mathf.Max(0, statsLine.SoldUnits);
                    line.SoldWeightKg = 0f;
                }

                line.SoldRevenue = Mathf.Max(0f, statsLine.SoldRevenue);

                if (line.ProductId == 9999)
                    ApplySpecialSaleAsDemand(line);

                changed = true;
            }

            return changed;
        }

        private static void ApplySpecialSaleAsDemand(BusinessProductLine line)
        {
            if (line == null || line.ProductId != 9999) return;

            line.RequestedUnits = line.SoldUnits;
            line.PickedUnits = line.SoldUnits;
            line.FulfilledUnits = line.PickedUnits;
            line.RequestedWeightKg = 0f;
            line.PickedWeightKg = 0f;
            line.FulfilledWeightKg = 0f;
            line.MissedUnits = 0;
            line.MissedWeightKg = 0f;
            line.MissedRevenue = 0f;
            line.GlobalOutOfStockUnits = 0;
            line.ShelfEmptyUnits = 0;
            line.NotDisplayedUnits = 0;
            line.OtherUnfulfilledUnits = 0;
            line.WasDisplayed = line.SoldUnits > 0;
        }

        private static DayStats FindStatsDay(int day)
        {
            List<DayStats> days = StatsStore.Data?.Days;
            if (days == null) return null;

            for (int i = 0; i < days.Count; i++)
            {
                DayStats candidate = days[i];
                if (candidate != null && candidate.Day == day) return candidate;
            }
            return null;
        }

        private static BusinessDayData FindClosedDay(int day)
        {
            if (Data?.Days == null) return null;
            for (int i = 0; i < Data.Days.Count; i++)
            {
                BusinessDayData existing = Data.Days[i];
                if (existing != null && existing.Day == day) return existing;
            }
            return null;
        }

        private static BusinessProductLine GetProductLine(BusinessDayData dayData, int productId)
        {
            if (dayData.Products == null) dayData.Products = new List<BusinessProductLine>();
            for (int i = 0; i < dayData.Products.Count; i++)
            {
                BusinessProductLine line = dayData.Products[i];
                if (line != null && line.ProductId == productId) return line;
            }

            var created = new BusinessProductLine { ProductId = productId };
            dayData.Products.Add(created);
            return created;
        }

        private static void LoadInternal(string path)
        {
            try
            {
                if (!File.Exists(path))
                {
                    Data = new BusinessAnalysisData();
                    _dirty = false;
                    _nextSaveAt = 0f;
                    return;
                }

                string json = File.ReadAllText(path);
                Data = JsonSerializer.Deserialize<BusinessAnalysisData>(json, JsonOptions) ?? new BusinessAnalysisData();
                bool changed = Normalize();
                _dirty = changed;
                _nextSaveAt = changed ? Time.realtimeSinceStartup + 2f : 0f;
            }
            catch (Exception ex)
            {
                Plugin.DebugWarning("[BusinessAnalysisStore] Load failed: " + ex.Message);
                Data = new BusinessAnalysisData();
                _dirty = false;
                _nextSaveAt = 0f;
            }
        }

        private static bool Normalize()
        {
            bool changed = false;
            if (Data == null)
            {
                Data = new BusinessAnalysisData();
                changed = true;
            }
            if (Data.Days == null)
            {
                Data.Days = new List<BusinessDayData>();
                changed = true;
            }

            int loadedSchema = Data.SchemaVersion;
            if (Data.SchemaVersion != CurrentSchemaVersion)
            {
                Data.SchemaVersion = CurrentSchemaVersion;
                changed = true;
            }

            // Usuwanie nulli i łączenie zduplikowanych dni.
            var unique = new Dictionary<int, BusinessDayData>();
            for (int i = 0; i < Data.Days.Count; i++)
            {
                BusinessDayData day = Data.Days[i];
                if (day == null || day.Day < 1)
                {
                    changed = true;
                    continue;
                }

                NormalizeDay(day, loadedSchema < 2);
                if (!unique.TryGetValue(day.Day, out BusinessDayData existing))
                    unique[day.Day] = day;
                else
                {
                    MergeDayInto(existing, day);
                    changed = true;
                }
            }

            Data.Days = new List<BusinessDayData>(unique.Values);
            SortClosedDays();

            if (Data.OpenDay != null)
            {
                if (Data.OpenDay.Day < 1)
                {
                    Data.OpenDay = null;
                    changed = true;
                }
                else
                {
                    NormalizeDay(Data.OpenDay, loadedSchema < 2);
                }
            }

            return changed || loadedSchema < 2;
        }

        private static void NormalizeDay(BusinessDayData day, bool migrateV1)
        {
            if (day == null) return;
            if (day.Products == null) day.Products = new List<BusinessProductLine>();

            for (int i = day.Products.Count - 1; i >= 0; i--)
            {
                BusinessProductLine line = day.Products[i];
                if (line == null || line.ProductId <= 0)
                {
                    day.Products.RemoveAt(i);
                    continue;
                }

                if (line.PickedUnits <= 0 && line.FulfilledUnits > 0)
                    line.PickedUnits = line.FulfilledUnits;
                if (line.PickedWeightKg <= 0f && line.FulfilledWeightKg > 0f)
                    line.PickedWeightKg = line.FulfilledWeightKg;

                line.FulfilledUnits = line.PickedUnits;
                line.FulfilledWeightKg = line.PickedWeightKg;

                if (!line.WasDisplayed && line.PickedUnits > 0)
                    line.WasDisplayed = true;

                // Schemat v1 naliczał przychód również dla OtherUnfulfilled.
                if (migrateV1 && line.MissedUnits > 0 && line.MissedRevenue > 0f)
                {
                    int stockMissed = line.GlobalOutOfStockUnits + line.ShelfEmptyUnits + line.NotDisplayedUnits;
                    if (stockMissed <= 0)
                        line.MissedRevenue = 0f;
                    else if (stockMissed < line.MissedUnits)
                        line.MissedRevenue *= stockMissed / (float)line.MissedUnits;
                }
            }

            RecomputeDayTotals(day);
        }

        private static void MergeDayInto(BusinessDayData target, BusinessDayData source)
        {
            if (target == null || source == null || ReferenceEquals(target, source)) return;

            target.CustomerVisits += source.CustomerVisits;
            target.FullySatisfiedCustomers += source.FullySatisfiedCustomers;
            target.CustomersWithMissingProducts += source.CustomersWithMissingProducts;

            if (source.Products != null)
            {
                for (int i = 0; i < source.Products.Count; i++)
                {
                    BusinessProductLine from = source.Products[i];
                    if (from == null || from.ProductId <= 0) continue;
                    BusinessProductLine to = GetProductLine(target, from.ProductId);

                    to.RequestedUnits += from.RequestedUnits;
                    to.RequestedWeightKg += from.RequestedWeightKg;
                    to.PickedUnits += from.PickedUnits;
                    to.PickedWeightKg += from.PickedWeightKg;
                    to.FulfilledUnits = to.PickedUnits;
                    to.FulfilledWeightKg = to.PickedWeightKg;
                    to.SoldUnits += from.SoldUnits;
                    to.SoldWeightKg += from.SoldWeightKg;
                    to.SoldRevenue += from.SoldRevenue;
                    to.MissedUnits += from.MissedUnits;
                    to.MissedWeightKg += from.MissedWeightKg;
                    to.MissedRevenue += from.MissedRevenue;
                    to.GlobalOutOfStockUnits += from.GlobalOutOfStockUnits;
                    to.ShelfEmptyUnits += from.ShelfEmptyUnits;
                    to.NotDisplayedUnits += from.NotDisplayedUnits;
                    to.OtherUnfulfilledUnits += from.OtherUnfulfilledUnits;
                    to.WasDisplayed |= from.WasDisplayed;
                }
            }

            RecomputeDayTotals(target);
        }

        private static void RecomputeDayTotals(BusinessDayData day)
        {
            if (day == null) return;
            float lost = 0f;
            if (day.Products != null)
            {
                for (int i = 0; i < day.Products.Count; i++)
                {
                    BusinessProductLine line = day.Products[i];
                    if (line != null) lost += Mathf.Max(0f, line.MissedRevenue);
                }
            }
            day.PotentialRevenueLost = lost;
        }

        private static void SortClosedDays()
        {
            if (Data?.Days == null) return;
            Data.Days.Sort((a, b) => (a?.Day ?? 0).CompareTo(b?.Day ?? 0));
        }

        private static void EnsureData()
        {
            if (Data == null) Data = new BusinessAnalysisData();
            if (Data.Days == null) Data.Days = new List<BusinessDayData>();
            if (Data.SchemaVersion != CurrentSchemaVersion) Data.SchemaVersion = CurrentSchemaVersion;
        }

        /// <summary>
        /// Czyści historię ANALIZY oraz otwarty dzień dla nowej gry.
        /// Nie wykonuje wcześniejszego SaveNow(), aby stary JSON nie wrócił.
        /// </summary>
        public static void ResetForNewGame()
        {
            _activePath = ResolvePath();
            Data = new BusinessAnalysisData();
            _dirty = false;
            _nextSaveAt = 0f;
            _currentDay = 1;

            DemandTrackingManager.ClearAllSessions();
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
                if (!string.IsNullOrWhiteSpace(statsPath)) return statsPath + ".analysis.json";
            }
            catch { }

            return Path.Combine(Application.persistentDataPath, "StatsAndExpiry.analysis.json");
        }
    }
}
