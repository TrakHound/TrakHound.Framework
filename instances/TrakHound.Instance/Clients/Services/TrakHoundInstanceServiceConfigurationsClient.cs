// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using TrakHound.Configurations;
using TrakHound.Instances;
using TrakHound.Services;
using TrakHound.Zones;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// Service and Router Configurations from the Instance's Configuration Profile. Saved Configurations are written to the
    /// Profile (and its files), which (re)loads the Services and Routers.
    /// </summary>
    public class TrakHoundInstanceServiceConfigurationsClient : ITrakHoundServiceConfigurationsClient
    {
        // Configuration IDs are used as the file names in the Profile, so they can't contain path characters
        private static readonly Regex _idRegex = new Regex(@"^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);

        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundInstanceClient _baseClient;

        // Read when used (the Profile may not be loaded yet when the Client is created)
        private ITrakHoundConfigurationProfile _configurationProfile => _instance?.ConfigurationProfile;


        public TrakHoundInstanceServiceConfigurationsClient(ITrakHoundInstance instance, TrakHoundInstanceClient baseClient = null)
        {
            _instance = instance;
            _baseClient = baseClient;
        }


        public Task<IEnumerable<TrakHoundServiceConfiguration>> GetServices()
        {
            if (!IsZoneAllowed(TrakHoundServiceConfiguration.ConfigurationCategory, null, TrakHoundZoneResources.ReadPermission)) return Task.FromResult<IEnumerable<TrakHoundServiceConfiguration>>(null);

            var configurations = _configurationProfile?.Get<TrakHoundServiceConfiguration>(TrakHoundServiceConfiguration.ConfigurationCategory);
            return Task.FromResult<IEnumerable<TrakHoundServiceConfiguration>>(configurations?.Select(CopyService).ToList());
        }

        public Task<TrakHoundServiceConfiguration> GetService(string configurationId)
        {
            if (!IsValidId(configurationId)) return Task.FromResult<TrakHoundServiceConfiguration>(null);
            if (!IsZoneAllowed(TrakHoundServiceConfiguration.ConfigurationCategory, configurationId, TrakHoundZoneResources.ReadPermission)) return Task.FromResult<TrakHoundServiceConfiguration>(null);

            var configuration = _configurationProfile?.Get<TrakHoundServiceConfiguration>(TrakHoundServiceConfiguration.ConfigurationCategory, configurationId);
            return Task.FromResult(CopyService(configuration));
        }

        public Task<bool> SaveService(TrakHoundServiceConfiguration configuration)
        {
            if (configuration == null || string.IsNullOrEmpty(configuration.PackageId)) return Task.FromResult(false);
            if (!IsZoneAllowed(TrakHoundServiceConfiguration.ConfigurationCategory, configuration.Id, TrakHoundZoneResources.WritePermission)) return Task.FromResult(false);

            return Task.FromResult(Save(CopyService(configuration)));
        }

        public Task<bool> RemoveService(string configurationId)
        {
            if (!IsZoneAllowed(TrakHoundServiceConfiguration.ConfigurationCategory, configurationId, TrakHoundZoneResources.WritePermission)) return Task.FromResult(false);

            return Task.FromResult(Remove(TrakHoundServiceConfiguration.ConfigurationCategory, configurationId));
        }


        public Task<IEnumerable<TrakHoundServiceRouterConfiguration>> GetRouters()
        {
            if (!IsZoneAllowed(TrakHoundServiceRouterConfiguration.ConfigurationCategory, null, TrakHoundZoneResources.ReadPermission)) return Task.FromResult<IEnumerable<TrakHoundServiceRouterConfiguration>>(null);

            return Task.FromResult(GetAll<TrakHoundServiceRouterConfiguration>(TrakHoundServiceRouterConfiguration.ConfigurationCategory));
        }

        public Task<TrakHoundServiceRouterConfiguration> GetRouter(string configurationId)
        {
            if (!IsZoneAllowed(TrakHoundServiceRouterConfiguration.ConfigurationCategory, configurationId, TrakHoundZoneResources.ReadPermission)) return Task.FromResult<TrakHoundServiceRouterConfiguration>(null);

            return Task.FromResult(Get<TrakHoundServiceRouterConfiguration>(TrakHoundServiceRouterConfiguration.ConfigurationCategory, configurationId));
        }

        public Task<bool> SaveRouter(TrakHoundServiceRouterConfiguration configuration)
        {
            if (configuration == null) return Task.FromResult(false);
            if (!IsZoneAllowed(TrakHoundServiceRouterConfiguration.ConfigurationCategory, configuration.Id, TrakHoundZoneResources.WritePermission)) return Task.FromResult(false);

            return Task.FromResult(Save(Copy(configuration)));
        }

        public Task<bool> RemoveRouter(string configurationId)
        {
            if (!IsZoneAllowed(TrakHoundServiceRouterConfiguration.ConfigurationCategory, configurationId, TrakHoundZoneResources.WritePermission)) return Task.FromResult(false);

            return Task.FromResult(Remove(TrakHoundServiceRouterConfiguration.ConfigurationCategory, configurationId));
        }


        // Configurations can contain secrets (ex. connection strings), so reading them is checked as well as changing them
        private bool IsZoneAllowed(string category, string configurationId, string permission)
        {
            if (_baseClient == null) return true;

            var id = !string.IsNullOrEmpty(configurationId) ? $"{category}:{configurationId}" : category;
            return _baseClient.AuthorizeZone(null, TrakHoundZoneResources.GetManageResourceId("configuration", id), permission).IsAllowed;
        }


        public static bool IsValidId(string configurationId)
        {
            return !string.IsNullOrEmpty(configurationId) && !configurationId.Contains("..") && _idRegex.IsMatch(configurationId);
        }

        private IEnumerable<TConfiguration> GetAll<TConfiguration>(string category) where TConfiguration : ITrakHoundConfiguration
        {
            // Copies, so the Profile's Configurations can't be changed without Saving them
            return _configurationProfile?.Get<TConfiguration>(category)?.Select(Copy).ToList();
        }

        private TConfiguration Get<TConfiguration>(string category, string configurationId) where TConfiguration : ITrakHoundConfiguration
        {
            if (!IsValidId(configurationId)) return default;

            var configuration = _configurationProfile != null ? _configurationProfile.Get<TConfiguration>(category, configurationId) : default;
            return configuration != null ? Copy(configuration) : default;
        }

        private bool Save<TConfiguration>(TConfiguration configuration) where TConfiguration : ITrakHoundConfiguration
        {
            if (_configurationProfile == null || configuration == null || !IsValidId(configuration.Id)) return false;

            // The Profile determines the file (by Category and ID)
            configuration.Path = null;

            _configurationProfile.Add(configuration, true);
            return true;
        }

        private bool Remove(string category, string configurationId)
        {
            if (_configurationProfile == null || !IsValidId(configurationId)) return false;
            if (_configurationProfile.Get(category, configurationId) == null) return false;

            _configurationProfile.Remove(category, configurationId);
            return true;
        }

        private static TConfiguration Copy<TConfiguration>(TConfiguration configuration)
        {
            return configuration != null ? Json.Convert<TConfiguration>(configuration.ToJson()) : default;
        }

        /// <summary>
        /// Service Configurations are copied directly (instead of with Json) so that the Parameter values stay strings and
        /// primitives. Values deserialized from Json (ex. a Configuration sent over HTTP) are JsonElements, which can't be saved
        /// </summary>
        private static TrakHoundServiceConfiguration CopyService(TrakHoundServiceConfiguration configuration)
        {
            if (configuration == null) return null;

            var copy = new TrakHoundServiceConfiguration();
            copy.Id = configuration.Id;
            copy.Name = configuration.Name;
            copy.Description = configuration.Description;
            copy.VolumeId = configuration.VolumeId;
            copy.PackageId = configuration.PackageId;
            copy.PackageVersion = configuration.PackageVersion;

            if (configuration.Parameters != null)
            {
                copy.Parameters = new Dictionary<string, object>();
                foreach (var parameter in configuration.Parameters)
                {
                    var value = NormalizeValue(parameter.Value);
                    if (value != null) copy.Parameters[parameter.Key] = value;
                }
            }

            return copy;
        }

        private static object NormalizeValue(object value)
        {
            if (value is JsonElement element)
            {
                switch (element.ValueKind)
                {
                    case JsonValueKind.String: return element.GetString();
                    case JsonValueKind.True: return "true";
                    case JsonValueKind.False: return "false";
                    case JsonValueKind.Null:
                    case JsonValueKind.Undefined: return null;
                    default: return element.GetRawText();
                }
            }

            return value;
        }
    }
}
