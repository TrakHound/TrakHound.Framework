// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Configurations;
using TrakHound.Functions;
using TrakHound.Packages;
using TrakHound.Security;
using TrakHound.Services;
using TrakHound.Workers;

namespace TrakHound.Instance.Pages.Configuration
{
    /// <summary>
    /// A Configuration being viewed or edited. The same model is used for every Category (fields a Category doesn't have are ignored).
    /// </summary>
    public class ConfigurationModel
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Route { get; set; }
        public string FunctionId { get; set; }
        public string VolumeId { get; set; }
        public string RouterId { get; set; }
        public string GroupId { get; set; }
        public string PackageId { get; set; }

        /// <summary>
        /// The Package Version as stored (ex. "*" for the latest installed version)
        /// </summary>
        public string PackageVersion { get; set; }

        /// <summary>
        /// Whether the Configuration is served over HTTP (Api only)
        /// </summary>
        public bool HttpEnabled { get; set; } = true;

        /// <summary>
        /// Which Endpoints are served as MCP Tools (Api only)
        /// </summary>
        public TrakHoundApiMcpToolsMode McpTools { get; set; } = TrakHoundApiMcpToolsMode.Marked;

        /// <summary>
        /// MCP Tool Route patterns to include (one per line, Api only)
        /// </summary>
        public string McpInclude { get; set; }

        /// <summary>
        /// MCP Tool Route patterns to exclude (one per line, Api only)
        /// </summary>
        public string McpExclude { get; set; }

        /// <summary>
        /// Only serve the read only MCP Tools (Api only)
        /// </summary>
        public bool McpReadOnly { get; set; } = true;

        public Dictionary<string, object> Parameters { get; set; } = new Dictionary<string, object>();


        /// <summary>
        /// True if the Configuration uses the latest installed version of its Package
        /// </summary>
        public bool UseLatest => string.IsNullOrEmpty(PackageVersion) || PackageVersion.Contains(TrakHoundPackage.WilcardVersion);

        /// <summary>
        /// True if the latest version includes Prereleases (ex. "*-*" : 0.4.5-dev.20261114153000 is newer than 0.4.4)
        /// </summary>
        public bool IncludePrereleases => UseLatest && !string.IsNullOrEmpty(PackageVersion) && PackageVersion.EndsWith(TrakHoundPackage.PrereleaseWildcardSuffix);

        /// <summary>
        /// The Package Version for display : "(latest)", "(latest, including prereleases)", or the specific version (ex. "v1.2.3")
        /// </summary>
        public string VersionText => UseLatest ? (IncludePrereleases ? "(latest, including prereleases)" : "(latest)") : $"v{PackageVersion}";

        public ConfigurationModel Clone()
        {
            var clone = (ConfigurationModel)MemberwiseClone();
            clone.Parameters = Parameters != null ? new Dictionary<string, object>(Parameters) : new Dictionary<string, object>();
            return clone;
        }

        /// <summary>
        /// Lines for a text box (one value per line)
        /// </summary>
        public static string JoinLines(IEnumerable<string> values) => values != null ? string.Join('\n', values) : null;

        /// <summary>
        /// One value per line (trimmed, blank lines and duplicates removed). Null when there are none
        /// </summary>
        public static List<string> SplitLines(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            var values = value.Split('\n').Select(o => o.Trim()).Where(o => o.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return values.Count > 0 ? values : null;
        }

        /// <summary>
        /// A value that changes when any field changes (used to detect unsaved changes)
        /// </summary>
        public string GetSnapshot()
        {
            var parameters = Parameters != null ? string.Join('\u001e', Parameters.OrderBy(o => o.Key).Select(o => $"{o.Key}={o.Value}")) : "";
            return string.Join('\u001f', Id, Name, Description, Route, FunctionId, VolumeId, RouterId, GroupId, PackageId, PackageVersion, HttpEnabled, McpTools, JoinLines(SplitLines(McpInclude)), JoinLines(SplitLines(McpExclude)), McpReadOnly, parameters);
        }
    }


    /// <summary>
    /// A Configuration Category (Apps, Api, Functions, Workers, Services, Identity) : its labels, the fields it has, and how to read and write its Configurations
    /// </summary>
    public class ConfigurationCategoryDefinition
    {
        /// <summary>
        /// The URL segment (ex. "apps")
        /// </summary>
        public string Key { get; init; }

        /// <summary>
        /// The Configuration Category (ex. "app")
        /// </summary>
        public string ConfigurationCategory { get; init; }

        /// <summary>
        /// The Package Category (ex. "app")
        /// </summary>
        public string PackageCategory { get; init; }

        public string Title { get; init; }

        public string SingularTitle { get; init; }

        public string Description { get; init; }

        public string Icon { get; init; }

        public string Group { get; init; }

        public bool HasName { get; init; } = true;

        public bool HasRoute { get; init; }

        public bool HasFunctionId { get; init; }

        public bool HasRouter { get; init; } = true;

        /// <summary>
        /// Whether its Configurations can be in a Group (Apis, Apps, Functions and Workers)
        /// </summary>
        public bool HasGroup { get; init; }

        public bool HasHttpEnabled { get; init; }

        public bool HasMcpTools { get; init; }

        public Func<ITrakHoundConfigurationProfile, IEnumerable<ConfigurationModel>> Load { get; init; }

        public Func<ConfigurationModel, ITrakHoundConfiguration> Build { get; init; }


        /// <summary>
        /// The name shown for a Configuration (falls back to the Function ID, Route or Package ID when it has no Name)
        /// </summary>
        public string GetDisplayName(ConfigurationModel configuration)
        {
            if (configuration == null) return null;
            if (!string.IsNullOrWhiteSpace(configuration.Name)) return configuration.Name;
            if (HasFunctionId && !string.IsNullOrWhiteSpace(configuration.FunctionId)) return configuration.FunctionId;
            if (HasRoute && !string.IsNullOrWhiteSpace(configuration.Route)) return NormalizeRoute(configuration.Route);
            if (!string.IsNullOrWhiteSpace(configuration.PackageId)) return configuration.PackageId;
            return configuration.Id;
        }

        public ConfigurationModel Get(ITrakHoundConfigurationProfile profile, string configurationId)
        {
            return Load(profile)?.FirstOrDefault(o => o.Id == configurationId);
        }

        public static string NormalizeRoute(string route)
        {
            var value = string.IsNullOrWhiteSpace(route) ? "" : route.Trim().Trim('/');
            return "/" + value;
        }
    }


    public static class ConfigurationCategories
    {
        public static readonly IReadOnlyList<ConfigurationCategoryDefinition> All = new[]
        {
            new ConfigurationCategoryDefinition
            {
                Key = "apps",
                ConfigurationCategory = TrakHoundAppConfiguration.ConfigurationCategory,
                PackageCategory = "app",
                Title = "Apps",
                SingularTitle = "App",
                Description = "User Interfaces to view and interact with information",
                Icon = "fa-solid fa-sharp fa-browsers",
                Group = "Applications",
                HasGroup = true,
                HasRoute = true,
                Load = profile => Map(profile.Get<TrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory), o => new ConfigurationModel
                {
                    Id = o.Id, Name = o.Name, Description = o.Description, Route = o.Route, VolumeId = o.VolumeId, RouterId = o.RouterId, GroupId = o.GroupId,
                    PackageId = o.PackageId, PackageVersion = o.PackageVersion, Parameters = CopyParameters(o.Parameters)
                }),
                Build = m => new TrakHoundAppConfiguration
                {
                    Id = m.Id, Name = Clean(m.Name), Description = Clean(m.Description), Route = Clean(m.Route), VolumeId = Clean(m.VolumeId), RouterId = Clean(m.RouterId), GroupId = Clean(m.GroupId),
                    PackageId = m.PackageId, PackageVersion = m.PackageVersion, Parameters = CopyParameters(m.Parameters)
                }
            },
            new ConfigurationCategoryDefinition
            {
                Key = "api",
                ConfigurationCategory = TrakHoundApiConfiguration.ConfigurationCategory,
                PackageCategory = "api",
                Title = "Apis",
                SingularTitle = "Api",
                Description = "Endpoints to Query, Subscribe, Publish, and Delete",
                Icon = "fa-solid fa-sharp fa-circle-location-arrow",
                Group = "Applications",
                HasGroup = true,
                HasRoute = true,
                HasHttpEnabled = true,
                HasMcpTools = true,
                Load = profile => Map(profile.Get<TrakHoundApiConfiguration>(TrakHoundApiConfiguration.ConfigurationCategory), o => new ConfigurationModel
                {
                    Id = o.Id, Name = o.Name, Description = o.Description, Route = o.Route, VolumeId = o.VolumeId, RouterId = o.RouterId, GroupId = o.GroupId,
                    PackageId = o.PackageId, PackageVersion = o.PackageVersion, HttpEnabled = o.HttpEnabled, McpTools = o.McpTools, McpInclude = ConfigurationModel.JoinLines(o.McpInclude), McpExclude = ConfigurationModel.JoinLines(o.McpExclude), McpReadOnly = o.McpReadOnly, Parameters = CopyParameters(o.Parameters)
                }),
                Build = m => new TrakHoundApiConfiguration
                {
                    Id = m.Id, Name = Clean(m.Name), Description = Clean(m.Description), Route = Clean(m.Route), VolumeId = Clean(m.VolumeId), RouterId = Clean(m.RouterId), GroupId = Clean(m.GroupId),
                    PackageId = m.PackageId, PackageVersion = m.PackageVersion, HttpEnabled = m.HttpEnabled, McpTools = m.McpTools, McpInclude = ConfigurationModel.SplitLines(m.McpInclude), McpExclude = ConfigurationModel.SplitLines(m.McpExclude), McpReadOnly = m.McpReadOnly, Parameters = CopyParameters(m.Parameters)
                }
            },
            new ConfigurationCategoryDefinition
            {
                Key = "functions",
                ConfigurationCategory = TrakHoundFunctionConfiguration.ConfigurationCategory,
                PackageCategory = "function",
                Title = "Functions",
                SingularTitle = "Function",
                Description = "Run a short lived process to import, export, and/or transform",
                Icon = "fa-solid fa-sharp fa-code",
                Group = "Applications",
                HasGroup = true,
                HasFunctionId = true,
                Load = profile => Map(profile.Get<TrakHoundFunctionConfiguration>(TrakHoundFunctionConfiguration.ConfigurationCategory), o => new ConfigurationModel
                {
                    Id = o.Id, Name = o.Name, Description = o.Description, FunctionId = o.FunctionId, VolumeId = o.VolumeId, RouterId = o.RouterId, GroupId = o.GroupId,
                    PackageId = o.PackageId, PackageVersion = o.PackageVersion, Parameters = CopyParameters(o.Parameters)
                }),
                Build = m => new TrakHoundFunctionConfiguration
                {
                    Id = m.Id, Name = Clean(m.Name), Description = Clean(m.Description), FunctionId = Clean(m.FunctionId), VolumeId = Clean(m.VolumeId), RouterId = Clean(m.RouterId), GroupId = Clean(m.GroupId),
                    PackageId = m.PackageId, PackageVersion = m.PackageVersion, Parameters = CopyParameters(m.Parameters)
                }
            },
            new ConfigurationCategoryDefinition
            {
                Key = "workers",
                ConfigurationCategory = TrakHoundWorkerConfiguration.ConfigurationCategory,
                PackageCategory = "worker",
                Title = "Workers",
                SingularTitle = "Worker",
                Description = "Connect and read data from external sources with long lived processes",
                Icon = "fa-solid fa-sharp fa-gears",
                Group = "Applications",
                HasGroup = true,
                Load = profile => Map(profile.Get<TrakHoundWorkerConfiguration>(TrakHoundWorkerConfiguration.ConfigurationCategory), o => new ConfigurationModel
                {
                    Id = o.Id, Name = o.Name, Description = o.Description, VolumeId = o.VolumeId, RouterId = o.RouterId, GroupId = o.GroupId,
                    PackageId = o.PackageId, PackageVersion = o.PackageVersion, Parameters = CopyParameters(o.Parameters)
                }),
                Build = m => new TrakHoundWorkerConfiguration
                {
                    Id = m.Id, Name = Clean(m.Name), Description = Clean(m.Description), VolumeId = Clean(m.VolumeId), RouterId = Clean(m.RouterId), GroupId = Clean(m.GroupId),
                    PackageId = m.PackageId, PackageVersion = m.PackageVersion, Parameters = CopyParameters(m.Parameters)
                }
            },
            new ConfigurationCategoryDefinition
            {
                Key = "services",
                ConfigurationCategory = TrakHoundServiceConfiguration.ConfigurationCategory,
                PackageCategory = "service",
                Title = "Services",
                SingularTitle = "Service",
                Description = "Handle routable requests to external systems",
                Icon = "fa-solid fa-sharp fa-right-left-large",
                Group = "Services",
                HasRouter = false,
                Load = profile => Map(profile.Get<TrakHoundServiceConfiguration>(TrakHoundServiceConfiguration.ConfigurationCategory), o => new ConfigurationModel
                {
                    Id = o.Id, Name = o.Name, Description = o.Description, VolumeId = o.VolumeId,
                    PackageId = o.PackageId, PackageVersion = o.PackageVersion, Parameters = CopyParameters(o.Parameters)
                }),
                Build = m => new TrakHoundServiceConfiguration
                {
                    Id = m.Id, Name = Clean(m.Name), Description = Clean(m.Description), VolumeId = Clean(m.VolumeId),
                    PackageId = m.PackageId, PackageVersion = m.PackageVersion, Parameters = CopyParameters(m.Parameters)
                }
            },
            new ConfigurationCategoryDefinition
            {
                Key = "identity",
                ConfigurationCategory = TrakHoundIdentityProviderConfiguration.ConfigurationCategory,
                PackageCategory = "identity",
                Title = "Identity",
                SingularTitle = "Identity Provider",
                Description = "Authentication & Authorization using an external provider",
                Icon = "fa-solid fa-sharp fa-lock-keyhole",
                Group = "Access",
                HasName = false,
                Load = profile => Map(profile.Get<TrakHoundIdentityProviderConfiguration>(TrakHoundIdentityProviderConfiguration.ConfigurationCategory), o => new ConfigurationModel
                {
                    Id = o.Id, Description = o.Description, VolumeId = o.VolumeId, RouterId = o.RouterId,
                    PackageId = o.PackageId, PackageVersion = o.PackageVersion, Parameters = CopyParameters(o.Parameters)
                }),
                Build = m => new TrakHoundIdentityProviderConfiguration
                {
                    Id = m.Id, Description = Clean(m.Description), VolumeId = Clean(m.VolumeId), RouterId = Clean(m.RouterId),
                    PackageId = m.PackageId, PackageVersion = m.PackageVersion, Parameters = CopyParameters(m.Parameters)
                }
            }
        };


        public static ConfigurationCategoryDefinition Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return null;

            // Accept the URL segment (ex. "apps") or the Configuration Category (ex. "app")
            return All.FirstOrDefault(o => string.Equals(o.Key, key, StringComparison.OrdinalIgnoreCase)) ??
                   All.FirstOrDefault(o => string.Equals(o.ConfigurationCategory, key, StringComparison.OrdinalIgnoreCase));
        }

        public static string GetLink(string configurationCategory, string configurationId = null)
        {
            var category = Get(configurationCategory);
            if (category == null) return "_admin/configuration";

            var link = $"_admin/configuration/{category.Key}";
            if (!string.IsNullOrEmpty(configurationId)) link += $"/{Uri.EscapeDataString(configurationId)}";
            return link;
        }


        private static IEnumerable<ConfigurationModel> Map<T>(IEnumerable<T> configurations, Func<T, ConfigurationModel> map)
        {
            return configurations?.Select(map).ToList() ?? new List<ConfigurationModel>();
        }

        private static Dictionary<string, object> CopyParameters(Dictionary<string, object> parameters)
        {
            return parameters != null ? new Dictionary<string, object>(parameters) : new Dictionary<string, object>();
        }

        private static string Clean(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }
}
