// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Zones;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// What an Admin is acting on, for a Role that is limited to Zones : a Module (its Zone Member ID, ex. "function:my-function"), or
    /// something Modules reach through the Instance (its Zone Resource ID, ex. "service:redis:key-values:get" or "volume:files:/a.txt")
    /// </summary>
    public sealed class TrakHoundAdminTarget
    {
        /// <summary>
        /// The Zone Member ID of the Module (null when the target isn't a Module)
        /// </summary>
        public string MemberId { get; }

        /// <summary>
        /// The Module's Group ("group:{groupId}") when it isn't the one the Zone Manager knows, ex. a Configuration that is being saved
        /// with a new Group. Null to use the Group the Module has now
        /// </summary>
        public string GroupReference { get; }

        /// <summary>
        /// Whether GroupReference is given (it can be given as null, for a Configuration that is being saved without a Group)
        /// </summary>
        public bool HasGroupReference { get; }

        public string ZoneResourceId { get; }

        public string ZonePermission { get; }


        private TrakHoundAdminTarget(string memberId, string groupReference, bool hasGroupReference, string zoneResourceId, string zonePermission)
        {
            MemberId = memberId;
            GroupReference = groupReference;
            HasGroupReference = hasGroupReference;
            ZoneResourceId = zoneResourceId;
            ZonePermission = zonePermission;
        }


        /// <summary>
        /// A Module (ex. "worker", "reader"), in the Group it has now
        /// </summary>
        public static TrakHoundAdminTarget Module(string type, string configurationId)
        {
            return new TrakHoundAdminTarget(TrakHoundZoneResources.GetMemberId(type, configurationId), null, false, null, null);
        }

        /// <summary>
        /// A Module as it is being saved, in the Group given (null for no Group)
        /// </summary>
        public static TrakHoundAdminTarget Module(string type, string configurationId, string groupId)
        {
            return new TrakHoundAdminTarget(TrakHoundZoneResources.GetMemberId(type, configurationId), TrakHoundZoneResources.GetGroupReference(groupId), true, null, null);
        }

        /// <summary>
        /// What a Client checks against Zones : the Module being called (if it is one) and the Zone Resource and Permission
        /// </summary>
        public static TrakHoundAdminTarget Call(string targetMemberId, string zoneResourceId, string zonePermission)
        {
            return new TrakHoundAdminTarget(targetMemberId, null, false, zoneResourceId, zonePermission);
        }
    }

    /// <summary>
    /// Limits an Admin Role to Zones. A Role an Account holds for certain Zones only applies to the Modules that belong to those Zones,
    /// and to the Service operations and Volume files those Zones' Rules allow : what the Zones' own Members can reach. It gives nothing
    /// on the areas that aren't about a Module (ex. Packages, Settings, Security)
    /// </summary>
    public static class TrakHoundAdminScope
    {
        // The Module types that are Zone Members (a Service is only something Members reach)
        private static readonly string[] _moduleTypes = new[] { TrakHoundZoneResources.ApiPrefix, TrakHoundZoneResources.AppPrefix, TrakHoundZoneResources.FunctionPrefix, TrakHoundZoneResources.WorkerPrefix };

        /// <summary>
        /// The Admin Resources a Role that is limited to Zones can give. Logs aren't one, since a Log page isn't limited to a Module
        /// </summary>
        public static readonly IReadOnlyList<string> Resources = new[]
        {
            TrakHoundAdminSecurity.Modules,
            TrakHoundAdminSecurity.Services,
            TrakHoundAdminSecurity.Volumes,
            TrakHoundAdminSecurity.AppConfigurations,
            TrakHoundAdminSecurity.ApiConfigurations,
            TrakHoundAdminSecurity.FunctionConfigurations,
            TrakHoundAdminSecurity.WorkerConfigurations
        };

        /// <summary>
        /// The running Instance's Zone Manager (set when the Instance starts). Without one, a Role that is limited to Zones gives nothing
        /// </summary>
        public static Func<TrakHoundZoneManager> ZoneManager { get; set; }


        public static bool IsScopeable(string resourceId) => resourceId != null && Resources.Contains(resourceId);

        public static bool IsModule(string memberId)
        {
            return memberId != null && _moduleTypes.Any(o => memberId.StartsWith(o + ":", StringComparison.Ordinal));
        }

        /// <summary>
        /// Whether the target is within the Zones : a Module that belongs to one of them, or something one of their Rules allows
        /// </summary>
        public static bool IsInZones(IEnumerable<string> zoneIds, TrakHoundAdminTarget target)
        {
            var zoneManager = ZoneManager?.Invoke();
            if (zoneManager == null) return false;

            return IsInZones(zoneManager.GetZones(), zoneIds, target, zoneManager.GetMemberGroup);
        }

        public static bool IsInZones(IEnumerable<TrakHoundZoneConfiguration> zones, IEnumerable<string> zoneIds, TrakHoundAdminTarget target, Func<string, string> getMemberGroup = null)
        {
            if (zones == null || zoneIds == null || target == null) return false;

            var ids = zoneIds.Where(o => !string.IsNullOrWhiteSpace(o)).ToList();
            if (ids.Count == 0) return false;

            bool IsScopeZone(string zoneId) => zoneId != null && ids.Contains(zoneId, StringComparer.OrdinalIgnoreCase);

            if (IsModule(target.MemberId))
            {
                var groupReference = target.HasGroupReference ? target.GroupReference : getMemberGroup?.Invoke(target.MemberId);
                return IsScopeZone(GetMemberZoneId(zones, target.MemberId, groupReference));
            }

            if (string.IsNullOrEmpty(target.ZoneResourceId)) return false;

            return zones.Any(o => o != null && IsScopeZone(o.Id) && o.Rules != null && o.Rules.Any(r => TrakHoundZoneResources.IsRuleMatch(r, target.ZoneResourceId, target.ZonePermission)));
        }

        /// <summary>
        /// The ID of the Zone a Module belongs to, the same way the Zone Manager decides it : the first Zone (by ID) that lists the Module
        /// itself (or a pattern that matches it), otherwise the first that lists its Group
        /// </summary>
        public static string GetMemberZoneId(IEnumerable<TrakHoundZoneConfiguration> zones, string memberId, string groupReference)
        {
            if (zones == null || string.IsNullOrEmpty(memberId)) return null;

            var ordered = zones.Where(o => o != null && !string.IsNullOrEmpty(o.Id)).OrderBy(o => o.Id, StringComparer.OrdinalIgnoreCase).ToList();

            var zone = ordered.FirstOrDefault(o => IsListed(o.Members, memberId));
            if (zone == null && !string.IsNullOrEmpty(groupReference)) zone = ordered.FirstOrDefault(o => IsListed(o.Members, groupReference));

            return zone?.Id;
        }

        /// <summary>
        /// Whether any Rule of the Zones could allow something of a Service (ex. to list the Service in the Explorer). The Service's
        /// operations are still checked one by one when they are used
        /// </summary>
        public static bool CouldReachService(IEnumerable<string> zoneIds, string serviceConfigurationId)
        {
            var zoneManager = ZoneManager?.Invoke();
            return zoneManager != null && CouldReachService(zoneManager.GetZones(), zoneIds, serviceConfigurationId);
        }

        public static bool CouldReachService(IEnumerable<TrakHoundZoneConfiguration> zones, IEnumerable<string> zoneIds, string serviceConfigurationId)
        {
            if (zones == null || zoneIds == null || string.IsNullOrEmpty(serviceConfigurationId)) return false;

            var prefix = $"{TrakHoundZoneResources.ServicePrefix}:{serviceConfigurationId}:";
            var ids = zoneIds.ToList();

            foreach (var rule in zones.Where(o => o != null && ids.Contains(o.Id, StringComparer.OrdinalIgnoreCase)).SelectMany(o => o.Rules ?? Enumerable.Empty<TrakHoundZoneRule>()))
            {
                var pattern = rule?.Resource;
                if (string.IsNullOrEmpty(pattern)) continue;
                if (pattern == TrakHoundZoneResources.Wildcard) return true;

                // "service:redis:*" and "service:*" cover the Service, as does one of its operations (ex. "service:redis:key-values:get")
                var start = pattern.TrimEnd('*');
                if (pattern.EndsWith(TrakHoundZoneResources.Wildcard) && prefix.StartsWith(start, StringComparison.OrdinalIgnoreCase)) return true;
                if (start.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }

        private static bool IsListed(IEnumerable<string> patterns, string id)
        {
            return patterns != null && patterns.Any(o => TrakHoundZoneResources.IsPatternMatch(o, id));
        }
    }
}
