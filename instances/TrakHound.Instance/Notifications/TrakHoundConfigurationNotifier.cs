// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Instance.Pages.Configuration;
using TrakHound.Services;

namespace TrakHound.Instance.Notifications
{
    /// <summary>
    /// Notifies when a Configuration is Added to (or Removed from) the Configuration Profile after the Instance was Initialized
    /// (ex. a Configuration file copied into the Configuration directory, or a default Configuration added by a Package install)
    /// </summary>
    internal class TrakHoundConfigurationNotifier : IDisposable
    {
        private readonly ITrakHoundConfigurationProfile _configurationProfile;
        private readonly TrakHoundInstanceNotificationService _notificationService;


        public TrakHoundConfigurationNotifier(ITrakHoundConfigurationProfile configurationProfile, TrakHoundInstanceNotificationService notificationService)
        {
            _configurationProfile = configurationProfile;
            _notificationService = notificationService;

            if (_configurationProfile != null)
            {
                _configurationProfile.ConfigurationChanged += ConfigurationChanged;
            }
        }

        public void Dispose()
        {
            if (_configurationProfile != null)
            {
                _configurationProfile.ConfigurationChanged -= ConfigurationChanged;
            }
        }


        private void ConfigurationChanged(object sender, TrakHoundConfigurationChangedArgs args)
        {
            if (string.IsNullOrEmpty(args?.Configuration?.Id)) return;

            // Loaded (ex. a Category Loaded later by a page) and Updated Configurations aren't new
            switch (args.ChangeType)
            {
                case TrakHoundConfigurationChangeType.Added: ConfigurationAdded(args.Configuration); break;
                case TrakHoundConfigurationChangeType.Removed: ConfigurationRemoved(args.Configuration); break;
            }
        }

        private void ConfigurationAdded(ITrakHoundConfiguration configuration)
        {
            _notificationService.Notify(
                TrakHoundInstanceNotificationType.Information,
                TrakHoundInstanceNotificationCategories.Configuration,
                $"{GetCategoryTitle(configuration.Category)} Configuration Added",
                GetDescription(configuration),
                GetLink(configuration.Category, configuration.Id),
                "View Configuration"
                );
        }

        private void ConfigurationRemoved(ITrakHoundConfiguration configuration)
        {
            _notificationService.Notify(
                TrakHoundInstanceNotificationType.Information,
                TrakHoundInstanceNotificationCategories.Configuration,
                $"{GetCategoryTitle(configuration.Category)} Configuration Removed",
                GetDescription(configuration),
                GetLink(configuration.Category, null),
                "View Configurations"
                );
        }


        private static string GetCategoryTitle(string configurationCategory)
        {
            if (configurationCategory == TrakHoundServiceRouterConfiguration.ConfigurationCategory) return "Service Router";
            if (configurationCategory == TrakHound.Zones.TrakHoundZoneConfiguration.ConfigurationCategory) return "Zone";
            if (configurationCategory == TrakHound.Groups.TrakHoundGroupConfiguration.ConfigurationCategory) return "Group";
            return ConfigurationCategories.Get(configurationCategory)?.SingularTitle ?? configurationCategory;
        }

        private static string GetLink(string configurationCategory, string configurationId)
        {
            // Service Routers are edited on the Routing page
            if (configurationCategory == TrakHoundServiceRouterConfiguration.ConfigurationCategory) return "_admin/configuration/routing";

            // Zones are edited on the Security pages
            if (configurationCategory == TrakHound.Zones.TrakHoundZoneConfiguration.ConfigurationCategory) return !string.IsNullOrEmpty(configurationId) ? $"_admin/configuration/zones/{Uri.EscapeDataString(configurationId)}" : "_admin/configuration/zones";

            // Groups have their own pages in Configuration
            if (configurationCategory == TrakHound.Groups.TrakHoundGroupConfiguration.ConfigurationCategory) return !string.IsNullOrEmpty(configurationId) ? GroupModules.GetLink(configurationId) : "_admin/configuration/groups";
            return ConfigurationCategories.GetLink(configurationCategory, configurationId);
        }

        private static string GetDescription(ITrakHoundConfiguration configuration)
        {
            // Most Configuration types have a Name (not part of ITrakHoundConfiguration)
            var name = configuration.GetType().GetProperty("Name")?.GetValue(configuration) as string;
            return !string.IsNullOrEmpty(name) && name != configuration.Id ? $"{name} ({configuration.Id})" : configuration.Id;
        }
    }
}
