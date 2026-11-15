// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog;

namespace TrakHound.Instance.Logging.History
{
    /// <summary>
    /// Applies Log History Retention, Archiving and Size limits on an interval.
    /// The Configuration File is re-read before each run so Retention changes apply without a restart.
    /// </summary>
    internal class TrakHoundLogMaintenanceService : BackgroundService
    {
        private static readonly TimeSpan _startDelay = TimeSpan.FromMinutes(1);

        private readonly ITrakHoundLogStore _store;
        private readonly string _configurationPath;
        private readonly Logger _logger;
        private readonly object _runLock = new object();
        private TrakHoundLogConfiguration _configuration;


        public TrakHoundLogMaintenanceResult LastResult { get; private set; }

        public DateTime? LastRunTime { get; private set; }

        public string LastError { get; private set; }


        public TrakHoundLogMaintenanceService(ITrakHoundLogStore store, TrakHoundLogConfiguration configuration, string configurationPath, Logger logger)
        {
            _store = store;
            _configuration = configuration;
            _configurationPath = configurationPath;
            _logger = logger;
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                // Don't add to Startup time
                await Task.Delay(_startDelay, stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Run(Run, stoppingToken);
                    await Task.Delay(_configuration.MaintenanceInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// Apply Retention and Archiving (waits for a run that is already in progress, then runs again with the latest Configuration)
        /// </summary>
        public TrakHoundLogMaintenanceResult Run()
        {
            lock (_runLock)
            {
                try
                {
                    var configuration = TrakHoundLogConfiguration.Read(_configurationPath);
                    if (configuration != null) _configuration = configuration;
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"Error Reading Log Configuration ({_configurationPath}) : {ex.Message}. Using previous settings");
                }

                try
                {
                    var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                    var result = _store.Maintain(_configuration);

                    LastResult = result;
                    LastRunTime = DateTime.Now;
                    LastError = null;

                    var message = $"Log History Maintenance : {result} : Retention = {(_configuration.RetentionDays > 0 ? $"{_configuration.RetentionDays} days" : "None")} : Archive Mode = {_configuration.ArchiveModeType} : Duration = {stopwatch.ElapsedMilliseconds}ms";
                    if (result.HasChanges) _logger?.Info(message);
                    else _logger?.Debug(message);

                    return result;
                }
                catch (Exception ex)
                {
                    LastRunTime = DateTime.Now;
                    LastError = ex.Message;
                    _logger?.Error(ex, "Log History Maintenance Error");
                    return null;
                }
            }
        }
    }
}
