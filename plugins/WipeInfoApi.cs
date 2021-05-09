using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using Oxide.Core;
using Rust;

namespace Oxide.Plugins
{
    [Info("Wipe Info Api", "MJSU", "1.1.0")]
    [Description("Api for when the server is wiping")]
    public class WipeInfoApi : RustPlugin
    {
        #region Class Fields

        private StoredData _storedData; //Plugin Data
        private PluginConfig _pluginConfig;

        private DateTime _currentDate;
        private DateTime _nextWipe;
        private int _daysTillNextWipe;
        private int _currentDaysBetweenWipes;
        private bool _isForcedWipeDay;
        private bool _newSaveVersion;

        #endregion

        #region Setup & Loading

        private void Init()
        {
            _storedData = Interface.Oxide.DataFileSystem.ReadObject<StoredData>(Name);

            if (_storedData.SaveVersion != Protocol.save)
            {
                _storedData.SaveVersion = Protocol.save;
                _newSaveVersion = true;
                Interface.Call("OnSaveVersionChanged");
                NextTick(SaveData);
            }

            CalculateWipe();
        }

        protected override void LoadDefaultConfig()
        {
            PrintWarning("Loading Default Config");
        }

        protected override void LoadConfig()
        {
            base.LoadConfig();
            Config.Settings.DefaultValueHandling = DefaultValueHandling.Populate;
            _pluginConfig = AdditionalConfig(Config.ReadObject<PluginConfig>());
            Config.WriteObject(_pluginConfig);
        }

        private PluginConfig AdditionalConfig(PluginConfig config)
        {
            config.ScheduleWeek4 = config.ScheduleWeek4 ?? new List<int> {0};
            config.ScheduleWeek5 = config.ScheduleWeek5 ?? new List<int> {0};
            return config;
        }

        private void OnServerSave()
        {
            if (DateTime.Now.Date != _currentDate)
            {
                CalculateWipe();
            }
        }

        private void OnNewSave(string name)
        {
            CalculateWipe();
        }

        private void OnServerInitialized()
        {
            CalculateWipe();
        }

        #endregion

        #region Wipe Calculations

        private void CalculateWipe()
        {
            _currentDate = DateTime.Now.Date;
            if (SaveRestore.SaveCreatedTime > _storedData.PreviousWipe)
            {
                _storedData.PreviousWipe = SaveRestore.SaveCreatedTime.Date;
                NextTick(SaveData);
            }

            DateTime lastForcedWipe = GetForcedWipe(_storedData.PreviousWipe);
            DateTime nextForcedWipe = GetForcedWipe(_storedData.PreviousWipe.AddMonths(1));
            DateTime nextNextForcedWipe = GetForcedWipe(_storedData.PreviousWipe.AddMonths(2));

            _isForcedWipeDay = nextForcedWipe.Date == DateTime.Now.Date && !_newSaveVersion;
            _currentDaysBetweenWipes = (int) (nextForcedWipe - lastForcedWipe).TotalDays;
            int nextDaysBetweenWipes = (int) (nextNextForcedWipe - nextForcedWipe).TotalDays;

            List<int> currentSchedule = _currentDaysBetweenWipes == 28 ? _pluginConfig.ScheduleWeek4 : _pluginConfig.ScheduleWeek5;
            List<int> nextSchedule = nextDaysBetweenWipes == 28 ? _pluginConfig.ScheduleWeek4 : _pluginConfig.ScheduleWeek5;

            List<DateTime> wipes = currentSchedule.Select(schedule => lastForcedWipe + TimeSpan.FromDays(schedule))
                .Concat(nextSchedule.Select(schedule => nextForcedWipe + TimeSpan.FromDays(schedule)))
                .ToList();

            _nextWipe = wipes.OrderBy(w => w).FirstOrDefault(w => w > _storedData.PreviousWipe);
            _daysTillNextWipe = (_nextWipe - DateTime.Today).Days;
            Puts($"Next Wipe: {_nextWipe} Days Until: {_daysTillNextWipe}");
            Interface.Call("OnWipeCalculated", _nextWipe, _daysTillNextWipe, _isForcedWipeDay);
        }

        private DateTime GetForcedWipe(DateTime date)
        {
            return new DateTime(date.Year, date.Month, FindWipeDay(date.Year, date.Month, DayOfWeek.Thursday));
        }

        private int FindWipeDay(int year, int month, DayOfWeek day)
        {
            int wipeDay = (int) day - (int) new DateTime(year, month, 1).DayOfWeek;
            if (wipeDay < 0) wipeDay += 7;
            return wipeDay + 1;
        }

        #endregion

        #region API Hooks

        private int GetDaysTillWipe()
        {
            return _daysTillNextWipe;
        }

        private DateTime GetNextWipe()
        {
            return _nextWipe;
        }

        private int GetDaysBetweenWipe()
        {
            return _currentDaysBetweenWipes;
        }

        private bool IsForcedWipeDay()
        {
            return _isForcedWipeDay;
        }

        private bool IsNewSaveVersion()
        {
            return _newSaveVersion;
        }

        #endregion

        #region Helper Methods

        private void SaveData() => Interface.Oxide.DataFileSystem.WriteObject(Name, _storedData);

        #endregion

        #region Classes

        private class PluginConfig
        {
            [JsonProperty("4 week schedule (Days since forced wipe)")]
            public List<int> ScheduleWeek4 { get; set; }

            [JsonProperty("5 week schedule (Days since forced wipe)")]
            public List<int> ScheduleWeek5 { get; set; }
        }

        private class StoredData
        {
            public int SaveVersion { get; set; }
            public DateTime PreviousWipe { get; set; } = DateTime.MinValue;
        }

        #endregion
    }
}