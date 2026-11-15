// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Configurations;
using TrakHound.Functions;
using TrakHound.Groups;
using TrakHound.Services;
using TrakHound.Volumes;
using TrakHound.Workers;
using TrakHound.Zones;

namespace TrakHound.Instance.Pages.Security
{
    /// <summary>
    /// A Module Configuration that can be a Zone Member (or the target of a Rule)
    /// </summary>
    public class ZoneModule
    {
        public string MemberId { get; set; }

        public string Type { get; set; }

        public string ConfigurationId { get; set; }

        public string Name { get; set; }

        public string PackageId { get; set; }

        public string PackageVersion { get; set; }

        public string VolumeId { get; set; }

        /// <summary>
        /// The Group Reference ("group:{groupId}") when the Module is in a Group
        /// </summary>
        public string GroupReference { get; set; }

        public string Icon => ZoneModules.GetTypeIcon(Type);

        public string TypeTitle => ZoneModules.GetTypeTitle(Type);

        public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : !string.IsNullOrEmpty(PackageId) ? PackageId : ConfigurationId;
    }

    /// <summary>
    /// A suggested Resource pattern for a Zone Rule
    /// </summary>
    public class ZoneResourceSuggestion
    {
        public string Pattern { get; set; }

        public string Text { get; set; }
    }

    /// <summary>
    /// The Module Configurations of the Instance (Apis, Apps, Functions, Workers and Services), for choosing Zone Members and Rules
    /// </summary>
    public class ZoneModules
    {
        private readonly List<ZoneModule> _modules = new List<ZoneModule>();


        /// <summary>
        /// Modules that make calls through a Client (Services are called, but don't make calls)
        /// </summary>
        public IEnumerable<ZoneModule> Callers => _modules.Where(o => o.Type != TrakHoundZoneResources.ServicePrefix);

        /// <summary>
        /// Every Module that can be a Member (a Service Member can limit its callers with AllowedCallers)
        /// </summary>
        public IReadOnlyList<ZoneModule> All => _modules;

        /// <summary>
        /// The Groups of the Instance (ordered by Name), which can be Members ("group:{groupId}")
        /// </summary>
        public IReadOnlyList<TrakHoundGroupConfiguration> Groups { get; } = new List<TrakHoundGroupConfiguration>();


        public ZoneModules(ITrakHoundConfigurationProfile profile)
        {
            if (profile == null) return;

            foreach (var o in profile.Get<TrakHoundApiConfiguration>(TrakHoundApiConfiguration.ConfigurationCategory) ?? Enumerable.Empty<TrakHoundApiConfiguration>())
                Add(TrakHoundZoneResources.ApiPrefix, o.Id, o.Name, o.PackageId, o.PackageVersion, o.VolumeId, o.GroupId);

            foreach (var o in profile.Get<TrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory) ?? Enumerable.Empty<TrakHoundAppConfiguration>())
                Add(TrakHoundZoneResources.AppPrefix, o.Id, o.Name, o.PackageId, o.PackageVersion, o.VolumeId, o.GroupId);

            foreach (var o in profile.Get<TrakHoundFunctionConfiguration>(TrakHoundFunctionConfiguration.ConfigurationCategory) ?? Enumerable.Empty<TrakHoundFunctionConfiguration>())
                Add(TrakHoundZoneResources.FunctionPrefix, o.Id, o.Name, o.PackageId, o.PackageVersion, o.VolumeId, o.GroupId);

            foreach (var o in profile.Get<TrakHoundWorkerConfiguration>(TrakHoundWorkerConfiguration.ConfigurationCategory) ?? Enumerable.Empty<TrakHoundWorkerConfiguration>())
                Add(TrakHoundZoneResources.WorkerPrefix, o.Id, o.Name, o.PackageId, o.PackageVersion, o.VolumeId, o.GroupId);

            foreach (var o in profile.Get<TrakHoundServiceConfiguration>(TrakHoundServiceConfiguration.ConfigurationCategory) ?? Enumerable.Empty<TrakHoundServiceConfiguration>())
                Add(TrakHoundZoneResources.ServicePrefix, o.Id, o.Name, o.PackageId, o.PackageVersion, o.VolumeId, null);

            Groups = profile.Get<TrakHoundGroupConfiguration>(TrakHoundGroupConfiguration.ConfigurationCategory)?
                .Where(o => !string.IsNullOrEmpty(o.Id)).OrderBy(o => GetGroupName(o), StringComparer.OrdinalIgnoreCase).ToList() ?? new List<TrakHoundGroupConfiguration>();
        }

        private void Add(string type, string id, string name, string packageId, string packageVersion, string volumeId, string groupId)
        {
            if (string.IsNullOrEmpty(id)) return;

            _modules.Add(new ZoneModule
            {
                MemberId = TrakHoundZoneResources.GetMemberId(type, id),
                Type = type,
                ConfigurationId = id,
                Name = name,
                PackageId = packageId,
                PackageVersion = packageVersion,
                VolumeId = !string.IsNullOrEmpty(volumeId) ? volumeId : id,
                GroupReference = TrakHoundZoneResources.GetGroupReference(groupId)
            });
        }

        public static string GetGroupName(TrakHoundGroupConfiguration group) => !string.IsNullOrEmpty(group?.Name) ? group.Name : group?.Id;


        public ZoneModule Get(string memberId)
        {
            if (string.IsNullOrEmpty(memberId)) return null;
            return _modules.FirstOrDefault(o => string.Equals(o.MemberId, memberId, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Gets the Modules a Member pattern matches (ex. "worker:dev-*", or "group:line-1" for the Modules in a Group)
        /// </summary>
        public IEnumerable<ZoneModule> Match(string pattern)
        {
            return _modules.Where(o => TrakHoundZoneResources.IsPatternMatch(pattern, o.MemberId) || TrakHoundZoneResources.IsPatternMatch(pattern, o.GroupReference)).ToList();
        }

        /// <summary>
        /// Describes a Member ID or pattern (ex. "function:my-function" => "My Function")
        /// </summary>
        public string Describe(string memberPattern)
        {
            if (string.IsNullOrEmpty(memberPattern)) return null;
            if (memberPattern == TrakHoundZoneResources.Wildcard) return "Every Module";

            var module = Get(memberPattern);
            if (module != null) return $"{module.TypeTitle} · {module.DisplayName}";

            var parts = memberPattern.Split(':', 2);
            if (parts[0] == TrakHoundZoneResources.ZonePrefix && parts.Length > 1) return $"Every Member of the Zone \"{parts[1]}\"";
            if (parts[0] == TrakHoundZoneResources.GroupPrefix && parts.Length > 1 && !parts[1].EndsWith(TrakHoundZoneResources.Wildcard))
            {
                var group = Groups.FirstOrDefault(o => string.Equals(o.Id, parts[1], StringComparison.OrdinalIgnoreCase));
                return $"Every Module in the Group \"{(group != null ? GetGroupName(group) : parts[1])}\"";
            }
            if (memberPattern.EndsWith(TrakHoundZoneResources.Wildcard)) return $"{GetTypeTitle(parts[0])} matching \"{memberPattern}\"";

            return null;
        }


        /// <summary>
        /// Suggested Resource patterns for Rules (ex. every Endpoint of each Api)
        /// </summary>
        public IEnumerable<ZoneResourceSuggestion> GetResourceSuggestions()
        {
            var suggestions = new List<ZoneResourceSuggestion>();

            foreach (var module in _modules.OrderBy(o => o.Type).ThenBy(o => o.DisplayName))
            {
                switch (module.Type)
                {
                    case TrakHoundZoneResources.ApiPrefix:
                        suggestions.Add(new ZoneResourceSuggestion { Pattern = $"api:{module.ConfigurationId}:*", Text = $"Api · {module.DisplayName} · All Endpoints" });
                        break;

                    case TrakHoundZoneResources.FunctionPrefix:
                        suggestions.Add(new ZoneResourceSuggestion { Pattern = TrakHoundZoneResources.GetFunctionResourceId(module.ConfigurationId), Text = $"Function · {module.DisplayName} · Run" });
                        break;

                    case TrakHoundZoneResources.WorkerPrefix:
                        suggestions.Add(new ZoneResourceSuggestion { Pattern = TrakHoundZoneResources.GetWorkerResourceId(module.ConfigurationId), Text = $"Worker · {module.DisplayName} · Start / Stop" });
                        break;

                    case TrakHoundZoneResources.ServicePrefix:
                        suggestions.Add(new ZoneResourceSuggestion { Pattern = $"service:{module.ConfigurationId}:*", Text = $"Service · {module.DisplayName} · All Operations" });
                        break;
                }
            }

            // Volumes are shared by ID (a Module's own Volume is not accessed through its Client)
            foreach (var volumeId in _modules.Select(o => o.VolumeId).Where(o => o != null).Distinct().OrderBy(o => o))
            {
                suggestions.Add(new ZoneResourceSuggestion { Pattern = $"volume:{volumeId}:*", Text = $"Volume · {volumeId} · All Files" });
            }

            suggestions.Add(new ZoneResourceSuggestion { Pattern = "api:*", Text = "Every Api" });
            suggestions.Add(new ZoneResourceSuggestion { Pattern = "service:*", Text = "Every Service" });
            suggestions.Add(new ZoneResourceSuggestion { Pattern = "manage:*", Text = "Management (Reload, Start / Stop the Instance, Service Configurations)" });

            return suggestions;
        }

        /// <summary>
        /// Describes a Resource ID or pattern
        /// </summary>
        public string DescribeResource(string resource)
        {
            if (string.IsNullOrEmpty(resource)) return null;
            if (resource == TrakHoundZoneResources.Wildcard) return "Everything";

            var parts = resource.Split(':', 3);
            var target = parts.Length > 1 && parts[1] != TrakHoundZoneResources.Wildcard ? Get(TrakHoundZoneResources.GetMemberId(parts[0], parts[1])) : null;
            var targetName = target?.DisplayName ?? (parts.Length > 1 ? parts[1] : null);
            var rest = parts.Length > 2 ? parts[2] : null;

            switch (parts[0])
            {
                case TrakHoundZoneResources.ApiPrefix:
                    if (targetName == null || targetName == TrakHoundZoneResources.Wildcard) return "Every Api";
                    return rest == null || rest == TrakHoundZoneResources.Wildcard ? $"Api · {targetName} · All Endpoints" : $"Api · {targetName} · {rest}";

                case TrakHoundZoneResources.FunctionPrefix:
                    return targetName == null || targetName == TrakHoundZoneResources.Wildcard ? "Every Function" : $"Function · {targetName}";

                case TrakHoundZoneResources.WorkerPrefix:
                    return targetName == null || targetName == TrakHoundZoneResources.Wildcard ? "Every Worker" : $"Worker · {targetName}";

                case TrakHoundZoneResources.ServicePrefix:
                    if (targetName == null || targetName == TrakHoundZoneResources.Wildcard) return "Every Service";
                    return rest == null || rest == TrakHoundZoneResources.Wildcard ? $"Service · {targetName} · All Operations" : $"Service · {targetName} · {rest.Replace(":", " · ")}";

                case TrakHoundZoneResources.VolumePrefix:
                    if (targetName == null || targetName == TrakHoundZoneResources.Wildcard) return "Every Volume";
                    return rest == null || rest == TrakHoundZoneResources.Wildcard ? $"Volume · {targetName} · All Files" : $"Volume · {targetName} · {rest}";

                case TrakHoundZoneResources.ManagePrefix:
                    return parts.Length < 2 || parts[1] == TrakHoundZoneResources.Wildcard ? "All Management" : $"Manage · {resource.Substring(TrakHoundZoneResources.ManagePrefix.Length + 1)}";
            }

            return null;
        }

        /// <summary>
        /// A broader pattern that covers the Resource (ex. every Endpoint of the same Api). Null if there isn't one
        /// </summary>
        public static string GetBroaderPattern(string resourceId)
        {
            if (string.IsNullOrEmpty(resourceId)) return null;

            var parts = resourceId.Split(':', 3);
            if (parts.Length < 3) return null;

            switch (parts[0])
            {
                case TrakHoundZoneResources.ApiPrefix:
                case TrakHoundZoneResources.ServicePrefix:
                case TrakHoundZoneResources.VolumePrefix:
                    return $"{parts[0]}:{parts[1]}:*";
            }

            return null;
        }


        public static string GetTypeTitle(string type)
        {
            switch (type)
            {
                case TrakHoundZoneResources.ApiPrefix: return "Api";
                case TrakHoundZoneResources.AppPrefix: return "App";
                case TrakHoundZoneResources.FunctionPrefix: return "Function";
                case TrakHoundZoneResources.WorkerPrefix: return "Worker";
                case TrakHoundZoneResources.ServicePrefix: return "Service";
                case TrakHoundZoneResources.ZonePrefix: return "Zone";
                case TrakHoundZoneResources.GroupPrefix: return "Group";
                default: return type;
            }
        }

        public static string GetTypeIcon(string type)
        {
            switch (type)
            {
                case TrakHoundZoneResources.ApiPrefix: return "fa-solid fa-sharp fa-plug";
                case TrakHoundZoneResources.AppPrefix: return "fa-solid fa-sharp fa-browser";
                case TrakHoundZoneResources.FunctionPrefix: return "fa-solid fa-sharp fa-function";
                case TrakHoundZoneResources.WorkerPrefix: return "fa-solid fa-sharp fa-gears";
                case TrakHoundZoneResources.ServicePrefix: return "fa-solid fa-sharp fa-server";
                case TrakHoundZoneResources.VolumePrefix: return "fa-solid fa-sharp fa-hard-drive";
                case TrakHoundZoneResources.ManagePrefix: return "fa-solid fa-sharp fa-screwdriver-wrench";
                case TrakHoundZoneResources.GroupPrefix: return "fa-solid fa-sharp fa-layer-group";
                default: return "fa-solid fa-sharp fa-cube";
            }
        }
    }
}
