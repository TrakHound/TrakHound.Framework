// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using TrakHound.Api;

namespace TrakHound.Zones
{
    /// <summary>
    /// Member IDs, Resource IDs and Permissions used by Zones.
    /// </summary>
    /// <remarks>
    /// Member IDs identify a Module by its Configuration: "{type}:{configurationId}" (ex. "function:my-function").
    /// "zone:{zoneId}" refers to every Member of a Zone (in AllowedCallers), and "group:{groupId}" to every Module in a Group (in Members or AllowedCallers).
    /// Resource IDs identify what is being accessed:
    /// <list type="bullet">
    /// <item>"api:{apiId}:{route}" (ex. "api:entities:/objects/query") - "read" (GET, HEAD, OPTIONS) or "write"</item>
    /// <item>"function:{functionConfigurationId}" - "run"</item>
    /// <item>"worker:{workerConfigurationId}" - "run" (Start / Stop)</item>
    /// <item>"service:{serviceId}:{family}:{operation}" (ex. "service:redis:key-values:publish") - "read" or "write"</item>
    /// <item>"volume:{volumeId}:{path}" - "read" or "write"</item>
    /// <item>"manage:{category}:{id}" (ex. "manage:api:entities", "manage:instance:{id}", "manage:configuration:service") - "read" or "write"</item>
    /// </list>
    /// </remarks>
    public static class TrakHoundZoneResources
    {
        public const string Wildcard = "*";

        public const string ReadPermission = "read";
        public const string WritePermission = "write";
        public const string RunPermission = "run";

        public const string ApiPrefix = "api";
        public const string AppPrefix = "app";
        public const string FunctionPrefix = "function";
        public const string WorkerPrefix = "worker";
        public const string ServicePrefix = "service";
        public const string VolumePrefix = "volume";
        public const string ManagePrefix = "manage";
        public const string ZonePrefix = "zone";
        public const string GroupPrefix = "group";

        /// <summary>
        /// The Permissions a Rule can allow
        /// </summary>
        public static readonly IReadOnlyList<string> Permissions = new string[] { ReadPermission, WritePermission, RunPermission };


        #region "Members"

        /// <summary>
        /// Gets the Member ID of a Request Source (null for Sources that are not Modules, ex. the Instance or an HTTP Request)
        /// </summary>
        public static string GetMemberId(TrakHoundApiRequestSource source)
        {
            if (source != null && !string.IsNullOrEmpty(source.Id))
            {
                switch (source.Type)
                {
                    case TrakHoundApiRequestSourceType.Api: return GetMemberId(ApiPrefix, source.Id);
                    case TrakHoundApiRequestSourceType.App: return GetMemberId(AppPrefix, source.Id);
                    case TrakHoundApiRequestSourceType.Function: return GetMemberId(FunctionPrefix, source.Id);
                    case TrakHoundApiRequestSourceType.Worker: return GetMemberId(WorkerPrefix, source.Id);
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the Member ID of a Module Configuration (ex. "function:my-function")
        /// </summary>
        public static string GetMemberId(string type, string configurationId)
        {
            if (!string.IsNullOrEmpty(type) && !string.IsNullOrEmpty(configurationId))
            {
                return $"{type}:{configurationId}";
            }

            return null;
        }

        /// <summary>
        /// Gets the ID used to refer to every Member of a Zone (ex. in AllowedCallers)
        /// </summary>
        public static string GetZoneReference(string zoneId) => !string.IsNullOrEmpty(zoneId) ? $"{ZonePrefix}:{zoneId}" : null;

        /// <summary>
        /// Gets the ID used to refer to every Module in a Group (in a Zone's Members or AllowedCallers)
        /// </summary>
        public static string GetGroupReference(string groupId) => !string.IsNullOrWhiteSpace(groupId) ? $"{GroupPrefix}:{groupId.Trim()}" : null;

        #endregion

        #region "Resources"

        public static string GetApiResourceId(string apiId, string route)
        {
            var path = !string.IsNullOrEmpty(route) ? route : "/";
            if (!path.StartsWith('/')) path = '/' + path;
            return $"{ApiPrefix}:{apiId}:{path}";
        }

        /// <summary>
        /// Gets the Permission required for an API Method ("read" for GET, HEAD and OPTIONS, otherwise "write")
        /// </summary>
        public static string GetApiPermission(TrakHoundApiMethod method)
        {
            switch (method)
            {
                case TrakHoundApiMethod.GET:
                case TrakHoundApiMethod.HEAD:
                case TrakHoundApiMethod.OPTIONS:
                    return ReadPermission;

                default:
                    return WritePermission;
            }
        }

        public static string GetFunctionResourceId(string functionConfigurationId) => $"{FunctionPrefix}:{functionConfigurationId}";

        public static string GetWorkerResourceId(string workerConfigurationId) => $"{WorkerPrefix}:{workerConfigurationId}";

        public static string GetVolumeResourceId(string volumeId, string path)
        {
            var filePath = path?.Replace('\\', '/').TrimStart('/') ?? string.Empty;
            return $"{VolumePrefix}:{volumeId}:/{filePath}";
        }

        public static string GetManageResourceId(string category, string id = null)
        {
            return !string.IsNullOrEmpty(id) ? $"{ManagePrefix}:{category}:{id}" : $"{ManagePrefix}:{category}";
        }

        #endregion

        #region "Matching"

        /// <summary>
        /// Patterns match exactly, or by prefix when they end with a Wildcard (ex. "api:my-api:*"). Matching ignores case
        /// </summary>
        public static bool IsPatternMatch(string pattern, string value)
        {
            if (!string.IsNullOrEmpty(pattern) && !string.IsNullOrEmpty(value))
            {
                if (pattern == Wildcard) return true;

                if (pattern.EndsWith(Wildcard))
                {
                    return value.StartsWith(pattern.TrimEnd('*'), StringComparison.OrdinalIgnoreCase);
                }

                return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// True if the Rule allows the Permission on the Resource
        /// </summary>
        public static bool IsRuleMatch(TrakHoundZoneRule rule, string resourceId, string permission)
        {
            if (rule != null && IsPatternMatch(rule.Resource, resourceId))
            {
                if (rule.Permissions.IsNullOrEmpty() || string.IsNullOrEmpty(permission)) return true;

                foreach (var rulePermission in rule.Permissions)
                {
                    if (rulePermission == Wildcard || string.Equals(rulePermission, permission, StringComparison.OrdinalIgnoreCase)) return true;
                }
            }

            return false;
        }

        #endregion
    }
}
