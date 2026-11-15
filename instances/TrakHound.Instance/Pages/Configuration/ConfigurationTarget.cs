// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Instance.Configurations;
using TrakHound.Instance.Logging;
using TrakHound.Instance.Logging.History;
using TrakHound.Instance.Pages.Settings;
using TrakHound.Instances;
using TrakHound.Zones;

namespace TrakHound.Instance.Pages.Configuration
{
    /// <summary>
    /// The Configurations the Admin configuration pages edit : the running Instance's Profile, or a set that isn't running (ex. a Deployment Workspace)
    /// </summary>
    public interface IConfigurationTarget
    {
        /// <summary>
        /// The display Name of the target
        /// </summary>
        string Name { get; }

        /// <summary>
        /// Whether changes apply to the running Instance. Runtime state (Module status, Zone activity, restarting) is only shown when true
        /// </summary>
        bool IsLive { get; }

        /// <summary>
        /// The Configuration Profile that Configurations are read from and saved to
        /// </summary>
        ITrakHoundConfigurationProfile Profile { get; }

        /// <summary>
        /// The Zones of the Profile (saved to its 'zone' Category)
        /// </summary>
        TrakHoundZoneManager ZoneManager { get; }

        /// <summary>
        /// The Instance Settings (instance.config.yaml). Edit a copy (<see cref="InstanceSettingsModel"/>) and save it with <see cref="SaveSettings"/>
        /// </summary>
        TrakHoundInstanceConfiguration InstanceConfiguration { get; }


        /// <summary>
        /// Reads the Log History settings (logs.config.yaml). Returns the defaults (and the error) if the file can't be read
        /// </summary>
        TrakHoundLogConfiguration ReadLogConfiguration(out string error);

        /// <summary>
        /// Saves the Instance Settings and the Log History settings
        /// </summary>
        void SaveSettings(InstanceSettingsModel instanceSettings, TrakHoundLogConfiguration logConfiguration);

        /// <summary>
        /// Records a change to a file in the Profile directory that wasn't saved through the Profile (in the Audit Log, for the running Instance)
        /// </summary>
        void RecordFile(string path);
    }

    /// <summary>
    /// The running Instance's Configuration Profile and Settings
    /// </summary>
    public class LiveConfigurationTarget : IConfigurationTarget
    {
        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundInstanceConfiguration _instanceConfiguration;
        private readonly RemotesConfiguration _remotesConfiguration;
        private readonly TrakHoundLogHistoryService _logHistoryService;


        public string Name => _instance.Information?.Name ?? _instance.Id;

        public bool IsLive => true;

        public ITrakHoundConfigurationProfile Profile => _instance.ConfigurationProfile;

        public TrakHoundZoneManager ZoneManager => _instance.ZoneManager;

        public TrakHoundInstanceConfiguration InstanceConfiguration => _instanceConfiguration;


        public LiveConfigurationTarget(ITrakHoundInstance instance, TrakHoundInstanceConfiguration instanceConfiguration, RemotesConfiguration remotesConfiguration, TrakHoundLogHistoryService logHistoryService)
        {
            _instance = instance;
            _instanceConfiguration = instanceConfiguration;
            _remotesConfiguration = remotesConfiguration;
            _logHistoryService = logHistoryService;
        }


        public TrakHoundLogConfiguration ReadLogConfiguration(out string error)
        {
            error = null;

            try
            {
                // Read from the file (the running Log History settings may differ if the file was changed after startup)
                return _logHistoryService.ReadConfiguration();
            }
            catch (Exception ex)
            {
                error = $"Error reading {TrakHoundLogConfiguration.Filename} : {ex.Message}";
                return _logHistoryService.RunningConfiguration?.Clone() ?? new TrakHoundLogConfiguration();
            }
        }

        public void SaveSettings(InstanceSettingsModel instanceSettings, TrakHoundLogConfiguration logConfiguration)
        {
            TrakHound.Instance.Security.TrakHoundAdminContext.DemandWrite(TrakHound.Instance.Security.TrakHoundAdminSecurity.Settings);

            // Save to the Configuration directory the Instance was started with (not ConfigurationProfile.Id, which is always 'default')
            var instanceConfigurationPath = _instanceConfiguration.Path;
            if (string.IsNullOrEmpty(instanceConfigurationPath)) throw new InvalidOperationException("The Instance Configuration file path is not known");
            var configurationDirectory = Path.GetDirectoryName(instanceConfigurationPath);
            var remotesPath = Path.Combine(configurationDirectory, RemotesConfiguration.Filename);

            // Apply to the running Configuration (ex. System API Enabled is checked for each request)
            instanceSettings.ApplyTo(_instanceConfiguration);
            _instanceConfiguration.Save(instanceConfigurationPath);
            _remotesConfiguration.Save(remotesPath);
            RecordFile(instanceConfigurationPath);
            RecordFile(remotesPath);

            _instance.Information.Id = _instanceConfiguration.InstanceId;
            _instance.Information.Name = _instanceConfiguration.Name;
            _instance.Information.Description = _instanceConfiguration.Description;

            _logHistoryService.SaveConfiguration(logConfiguration);
        }

        public void RecordFile(string path) => _instance.AuditMonitor?.RecordFile(path);
    }
}
