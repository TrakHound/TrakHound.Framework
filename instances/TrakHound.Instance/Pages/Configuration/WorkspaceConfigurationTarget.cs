// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Configurations;
using TrakHound.Functions;
using TrakHound.Groups;
using TrakHound.Instance.Logging;
using TrakHound.Instance.Pages.Settings;
using TrakHound.Instances;
using TrakHound.Packages;
using TrakHound.Security;
using TrakHound.Services;
using TrakHound.Workers;
using TrakHound.Zones;

namespace TrakHound.Instance.Pages.Configuration
{
    /// <summary>
    /// A Deployment Workspace : a Profile-shaped directory that is edited with the Admin pages but isn't run by the Instance
    /// </summary>
    public class WorkspaceConfigurationTarget : IConfigurationTarget, IDisposable
    {
        private readonly TrakHoundConfigurationProfile _profile;
        private readonly TrakHoundZoneManager _zoneManager;
        private TrakHoundInstanceConfiguration _instanceConfiguration;


        /// <summary>
        /// The ID of the Workspace (its directory name, used in its URLs)
        /// </summary>
        public string Id { get; }

        public string Name => Id;

        public bool IsLive => false;

        /// <summary>
        /// The Workspace directory (laid out like a Configuration Profile)
        /// </summary>
        public string Path { get; }

        public ITrakHoundConfigurationProfile Profile => _profile;

        public TrakHoundZoneManager ZoneManager => _zoneManager;

        public TrakHoundInstanceConfiguration InstanceConfiguration
        {
            get
            {
                if (_instanceConfiguration == null)
                {
                    // References (ex. "${port}") are shown with the Workspace's Variables (each device resolves them with its own)
                    var path = System.IO.Path.Combine(Path, TrakHoundInstanceConfiguration.Filename);
                    var variables = TrakHoundConfigurationVariables.Read(Path);
                    _instanceConfiguration = TrakHoundInstanceConfiguration.Read(path, value => TrakHoundConfigurationVariables.Resolve(value, variables)) ?? new TrakHoundInstanceConfiguration { Path = path };
                }
                return _instanceConfiguration;
            }
        }

        private string LogConfigurationPath => System.IO.Path.Combine(Path, TrakHoundLogConfiguration.Filename);


        public WorkspaceConfigurationTarget(string id, string path, TrakHoundPackageManager packageManager)
        {
            Id = id;
            Path = path;

            // The secrets key is global : keep the running Profile's key (the Workspace's parent directory has none)
            _profile = new TrakHoundConfigurationProfile(id, null, path, initializeSecrets: false);

            // The same Categories the Instance loads
            _profile.Load<TrakHoundFunctionConfiguration>(TrakHoundFunctionConfiguration.ConfigurationCategory);
            _profile.Load<TrakHoundWorkerConfiguration>(TrakHoundWorkerConfiguration.ConfigurationCategory);
            _profile.Load<TrakHoundServiceConfiguration>(TrakHoundServiceConfiguration.ConfigurationCategory);
            _profile.Load<TrakHoundServiceRouterConfiguration>(TrakHoundServiceRouterConfiguration.ConfigurationCategory);
            _profile.Load<TrakHoundApiConfiguration>(TrakHoundApiConfiguration.ConfigurationCategory);
            _profile.Load<TrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory);
            _profile.Load<TrakHoundIdentityProviderConfiguration>(TrakHoundIdentityProviderConfiguration.ConfigurationCategory);
            _profile.Load<TrakHoundZoneConfiguration>(TrakHoundZoneConfiguration.ConfigurationCategory);
            _profile.Load<TrakHoundGroupConfiguration>(TrakHoundGroupConfiguration.ConfigurationCategory);

            // No calls go through a Workspace, so its Zones never record activity
            // A Workspace's Zones are part of Deployments
            _zoneManager = new TrakHoundZoneManager(_profile, null, packageManager) { AdminResource = TrakHound.Instance.Security.TrakHoundAdminSecurity.Deployments };
        }

        public void Dispose()
        {
            _zoneManager.Dispose();
            _profile.Dispose();
        }


        public TrakHoundLogConfiguration ReadLogConfiguration(out string error)
        {
            error = null;

            try
            {
                return TrakHoundLogConfiguration.Read(LogConfigurationPath) ?? new TrakHoundLogConfiguration();
            }
            catch (Exception ex)
            {
                error = $"Error reading {TrakHoundLogConfiguration.Filename} : {ex.Message}";
                return new TrakHoundLogConfiguration();
            }
        }

        public void SaveSettings(InstanceSettingsModel instanceSettings, TrakHoundLogConfiguration logConfiguration)
        {
            TrakHound.Instance.Security.TrakHoundAdminContext.DemandWrite(TrakHound.Instance.Security.TrakHoundAdminSecurity.Deployments);

            var configuration = InstanceConfiguration;
            instanceSettings.ApplyTo(configuration);
            configuration.Save(configuration.Path, createBackup: false);

            logConfiguration?.Save(LogConfigurationPath);
        }

        // Workspace files aren't part of the running Instance, so they aren't in its Audit Log
        public void RecordFile(string path) { }
    }
}
