// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Groups;

namespace TrakHound.Instance.Pages.Configuration
{
    /// <summary>
    /// A Module Configuration (Api, App, Function or Worker) that can be in a Group
    /// </summary>
    public class GroupModule
    {
        public ConfigurationCategoryDefinition Category { get; init; }

        public ConfigurationModel Configuration { get; init; }

        public string Name => Category?.GetDisplayName(Configuration);

        public string Title => $"{Category?.SingularTitle} · {Name}";

        public string Link => ConfigurationCategories.GetLink(Category?.ConfigurationCategory, Configuration?.Id);
    }

    /// <summary>
    /// Reads the Groups and the Modules in them from the Configuration Profile
    /// </summary>
    public static class GroupModules
    {
        /// <summary>
        /// Every Group (ordered by Name)
        /// </summary>
        public static List<TrakHoundGroupConfiguration> GetGroups(ITrakHoundConfigurationProfile profile)
        {
            return profile?.Get<TrakHoundGroupConfiguration>(TrakHoundGroupConfiguration.ConfigurationCategory)?
                .Where(o => !string.IsNullOrEmpty(o.Id))
                .OrderBy(o => GetName(o), StringComparer.OrdinalIgnoreCase)
                .ToList() ?? new List<TrakHoundGroupConfiguration>();
        }

        public static TrakHoundGroupConfiguration GetGroup(ITrakHoundConfigurationProfile profile, string groupId)
        {
            return !string.IsNullOrEmpty(groupId) ? profile?.Get<TrakHoundGroupConfiguration>(TrakHoundGroupConfiguration.ConfigurationCategory, groupId) : null;
        }

        public static string GetName(TrakHoundGroupConfiguration group) => !string.IsNullOrEmpty(group?.Name) ? group.Name : group?.Id;

        public static string GetLink(string groupId) => $"_admin/configuration/groups/{Uri.EscapeDataString(groupId ?? "")}";

        /// <summary>
        /// Every Module that can be in a Group (ordered by type, then name)
        /// </summary>
        public static List<GroupModule> GetModules(ITrakHoundConfigurationProfile profile)
        {
            var modules = new List<GroupModule>();
            foreach (var category in ConfigurationCategories.All.Where(o => o.HasGroup && o.Load != null))
            {
                foreach (var configuration in category.Load(profile) ?? Enumerable.Empty<ConfigurationModel>())
                {
                    if (!string.IsNullOrEmpty(configuration?.Id)) modules.Add(new GroupModule { Category = category, Configuration = configuration });
                }
            }

            return modules.OrderBy(o => o.Category.Title).ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// The Modules in the Group
        /// </summary>
        public static List<GroupModule> GetMembers(ITrakHoundConfigurationProfile profile, string groupId)
        {
            return GetModules(profile).Where(o => IsMember(o.Configuration, groupId)).ToList();
        }

        public static bool IsMember(ConfigurationModel configuration, string groupId)
        {
            return !string.IsNullOrEmpty(groupId) && string.Equals(configuration?.GroupId?.Trim(), groupId, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Moves the Module into the Group (null removes it from its Group) and saves its Configuration
        /// </summary>
        public static void SetGroup(ConfigurationsService configurationsService, GroupModule module, string groupId)
        {
            if (module?.Configuration == null) return;

            var configuration = module.Configuration.Clone();
            configuration.GroupId = !string.IsNullOrWhiteSpace(groupId) ? groupId.Trim() : null;
            configurationsService.Save(module.Category, configuration);
        }
    }
}
