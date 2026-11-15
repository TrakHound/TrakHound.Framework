// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Logging.History
{
    /// <summary>
    /// Log History for the Admin Interface : the running Configuration, the Configuration File, the Store and Maintenance
    /// </summary>
    public class TrakHoundLogHistoryService
    {
        private readonly TrakHoundLogMaintenanceService _maintenance;


        /// <summary>
        /// The Configuration the Log History was started with (Provider, Path and Levels changes require a restart)
        /// </summary>
        public TrakHoundLogConfiguration RunningConfiguration { get; }

        /// <summary>
        /// The path of the Configuration File (logs.config.yaml)
        /// </summary>
        public string ConfigurationPath { get; }

        /// <summary>
        /// The Store (null if Log History couldn't be created)
        /// </summary>
        public ITrakHoundLogStore Store { get; }

        public TrakHoundLogMaintenanceResult LastMaintenanceResult => _maintenance?.LastResult;

        public DateTime? LastMaintenanceTime => _maintenance?.LastRunTime;

        public string LastMaintenanceError => _maintenance?.LastError;


        internal TrakHoundLogHistoryService(TrakHoundLogConfiguration runningConfiguration, string configurationPath, ITrakHoundLogStore store, TrakHoundLogMaintenanceService maintenance)
        {
            RunningConfiguration = runningConfiguration;
            ConfigurationPath = configurationPath;
            Store = store;
            _maintenance = maintenance;
        }


        /// <summary>
        /// Read the Configuration File (or the defaults if it doesn't exist)
        /// </summary>
        public TrakHoundLogConfiguration ReadConfiguration()
        {
            return TrakHoundLogConfiguration.Read(ConfigurationPath) ?? new TrakHoundLogConfiguration();
        }

        public void SaveConfiguration(TrakHoundLogConfiguration configuration)
        {
            configuration?.Save(ConfigurationPath);
        }

        /// <summary>
        /// Returns true if the Configuration differs from the running Configuration in a setting that requires a restart
        /// </summary>
        public bool RequiresRestart(TrakHoundLogConfiguration configuration)
        {
            if (configuration == null || RunningConfiguration == null) return false;

            return configuration.ProviderType != RunningConfiguration.ProviderType ||
                   !string.Equals(configuration.GetDirectory(), RunningConfiguration.GetDirectory(), StringComparison.OrdinalIgnoreCase) ||
                   configuration.MinimumLogLevel != RunningConfiguration.MinimumLogLevel ||
                   configuration.FrameworkMinimumLogLevel != RunningConfiguration.FrameworkMinimumLogLevel;
        }

        /// <summary>
        /// Apply Retention and Archiving now (using the saved Configuration File)
        /// </summary>
        public Task<TrakHoundLogMaintenanceResult> RunMaintenance()
        {
            if (_maintenance == null) return Task.FromResult<TrakHoundLogMaintenanceResult>(null);
            return Task.Run(() => _maintenance.Run());
        }
    }
}
