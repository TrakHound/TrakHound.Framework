// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Groups;
using TrakHound.Zones;

namespace TrakHound.Instance.Pages.Configuration.Routing
{
    /// <summary>
    /// A Module (Api, App, Function, Worker or Identity Provider) and the Router its Service calls go through
    /// </summary>
    public class RoutingModule
    {
        public ConfigurationCategoryDefinition Category { get; init; }

        public string ConfigurationId { get; init; }

        public string Name { get; init; }

        /// <summary>
        /// The Router the Module's Configuration names (null uses the default Router)
        /// </summary>
        public string RouterId { get; init; }

        /// <summary>
        /// The Zone the Module is a Member of (null if it isn't in one)
        /// </summary>
        public string ZoneId { get; init; }

        /// <summary>
        /// The Router the Module's Zone sends every Service call through, whatever Router the Module names (null if the Zone doesn't set one)
        /// </summary>
        public string ZoneRouterId { get; init; }

        /// <summary>
        /// The Router the Module's Group sets, used when the Module doesn't name one (null if it isn't in a Group, or the Group doesn't set one)
        /// </summary>
        /// <summary>
        /// The Group the Module is in (null if it isn't in one)
        /// </summary>
        public string GroupId { get; init; }

        public string GroupRouterId { get; init; }

        /// <summary>
        /// The Router the Module's Service calls go through (null for the default Router)
        /// </summary>
        public string RouterKey => !string.IsNullOrEmpty(ZoneRouterId) ? ZoneRouterId : (!string.IsNullOrWhiteSpace(RouterId) ? RouterId.Trim() : GroupRouterId);

        public bool IsZoneRouter => !string.IsNullOrEmpty(ZoneRouterId);

        /// <summary>
        /// The Module uses its Group's Router (it doesn't name one, and its Zone doesn't set one)
        /// </summary>
        public bool IsGroupRouter => !IsZoneRouter && string.IsNullOrWhiteSpace(RouterId) && !string.IsNullOrEmpty(GroupRouterId);

        public string Title => $"{Category?.SingularTitle} · {Name}";

        public string Link => $"_admin/configuration/{Category?.Key}/{Uri.EscapeDataString(ConfigurationId ?? "")}";
    }

    public static class RoutingModules
    {
        private static readonly HashSet<string> _zoneCategories = new HashSet<string>
        {
            TrakHoundZoneResources.ApiPrefix,
            TrakHoundZoneResources.AppPrefix,
            TrakHoundZoneResources.FunctionPrefix,
            TrakHoundZoneResources.WorkerPrefix
        };

        /// <summary>
        /// Reads every Module that makes Service calls through a Router, from the Configuration Profile
        /// </summary>
        public static List<RoutingModule> Load(ITrakHoundConfigurationProfile profile)
        {
            var modules = new List<RoutingModule>();
            if (profile == null) return modules;

            // A Member listed in more than one Zone belongs to the first (by ID), as in the Zone Manager
            var zones = profile.Get<TrakHoundZoneConfiguration>(TrakHoundZoneConfiguration.ConfigurationCategory)?
                .Where(o => !string.IsNullOrEmpty(o?.Id))
                .OrderBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<TrakHoundZoneConfiguration>();

            foreach (var category in ConfigurationCategories.All.Where(o => o.HasRouter && o.Load != null))
            {
                foreach (var configuration in category.Load(profile) ?? Enumerable.Empty<ConfigurationModel>())
                {
                    if (string.IsNullOrEmpty(configuration?.Id)) continue;

                    // Identity Providers aren't Zone Members (or in Groups). A Zone that lists the Module comes before one that lists its Group
                    var memberId = TrakHoundZoneResources.GetMemberId(category.PackageCategory, configuration.Id);
                    var groupReference = TrakHoundZoneResources.GetGroupReference(configuration.GroupId);
                    var zone = _zoneCategories.Contains(category.PackageCategory)
                        ? zones.FirstOrDefault(o => IsListed(o.Members, memberId)) ?? (groupReference != null ? zones.FirstOrDefault(o => IsListed(o.Members, groupReference)) : null)
                        : null;

                    modules.Add(new RoutingModule
                    {
                        Category = category,
                        ConfigurationId = configuration.Id,
                        Name = category.GetDisplayName(configuration),
                        RouterId = configuration.RouterId,
                        GroupId = _zoneCategories.Contains(category.PackageCategory) && !string.IsNullOrWhiteSpace(configuration.GroupId) ? configuration.GroupId.Trim() : null,
                        GroupRouterId = _zoneCategories.Contains(category.PackageCategory) ? TrakHoundGroups.GetRouterId(profile, null, configuration.GroupId) : null,
                        ZoneId = zone?.Id,
                        ZoneRouterId = !string.IsNullOrEmpty(zone?.RouterId) ? zone.RouterId : null
                    });
                }
            }

            return modules;
        }

        private static bool IsListed(IEnumerable<string> patterns, string id) => !patterns.IsNullOrEmpty() && patterns.Any(o => TrakHoundZoneResources.IsPatternMatch(o, id));

        /// <summary>
        /// Names a few Modules (ex. "Worker · Collector, Api · Status and 3 more")
        /// </summary>
        public static string Describe(IEnumerable<RoutingModule> modules, int max = 3)
        {
            var list = modules?.ToList() ?? new List<RoutingModule>();
            var names = string.Join(", ", list.Take(max).Select(o => o.Title));
            return list.Count > max ? $"{names} and {list.Count - max} more" : names;
        }
    }
}
