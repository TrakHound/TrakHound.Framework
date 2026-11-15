// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Security;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// A System API Resource (ex. "system:logs") that Profiles can be assigned to
    /// </summary>
    public class TrakHoundSystemResource
    {
        public string Id { get; }

        public string Title { get; }

        public string Description { get; }

        /// <summary>
        /// The System API routes the Resource covers (for display)
        /// </summary>
        public string Routes { get; }


        public TrakHoundSystemResource(string id, string title, string description, string routes)
        {
            Id = id;
            Title = title;
            Description = description;
            Routes = routes;
        }
    }


    /// <summary>
    /// Security Resource IDs and Permissions for the System API (the Instance management API used by Clients and other Instances).
    /// Each area is a Resource with the ID "system:{area}" (ex. "system:logs"). Requests that only read require the "read" Permission
    /// and requests that change something (ex. starting a Worker or uploading a Volume file) require the "write" Permission.
    /// Service operations (/_services/blobs, /_services/key-values, ...) are authorized per Service operation instead (see TrakHoundServiceSecurity).
    /// </summary>
    public static class TrakHoundSystemSecurity
    {
        public const string ResourcePrefix = "system";
        public const string ReadPermission = "read";
        public const string WritePermission = "write";

        public const string ApiInformation = "system:api-information";
        public const string ApiCatalog = "system:api-catalog";
        public const string Apis = "system:apis";
        public const string Apps = "system:apps";
        public const string Functions = "system:functions";
        public const string Workers = "system:workers";
        public const string Services = "system:services";
        public const string Logs = "system:logs";
        public const string Volumes = "system:volumes";
        public const string Buffers = "system:buffers";
        public const string Modules = "system:modules";
        public const string Instance = "system:instance";
        public const string Metrics = "system:metrics";
        public const string Mcp = "system:mcp";
        public const string Audit = "system:audit";

        /// <summary>
        /// The Resource ID used before the System API Resources were added (Profiles using it are migrated to ApiInformation)
        /// </summary>
        public const string LegacyApiInformation = "System.Api.Information";

        private static readonly string[] _permissions = new[] { ReadPermission, WritePermission };

        public static readonly IReadOnlyList<TrakHoundSystemResource> Resources = new[]
        {
            new TrakHoundSystemResource(ApiInformation, "Api Information", "Describes the loaded Apis and their Endpoints", "/_api/information, /_api/openapi"),
            new TrakHoundSystemResource(ApiCatalog, "Api Catalog", "Describes the Apis to developers (only the Endpoints the caller can use). Served on the HTTP Port", "/_developer/catalog, /_developer/openapi, /llms.txt"),
            new TrakHoundSystemResource(Apis, "Apis", "Api Metrics, Recent Requests and Streams, and reloading Api Configurations and closing Streams (write)", "/_api/metrics, /_api/requests, /_api/streams, /_api/reload"),
            new TrakHoundSystemResource(Apps, "Apps", "App Information and Metrics, and reloading / refreshing Apps (write)", "/_apps"),
            new TrakHoundSystemResource(Functions, "Functions", "Function Information, Metrics, and Logs, and running Functions (write)", "/_functions"),
            new TrakHoundSystemResource(Workers, "Workers", "Worker Information, Metrics, Status, and Logs, and starting / stopping Workers (write)", "/_workers"),
            new TrakHoundSystemResource(Services, "Services", "Service Information, Metrics, Routers, and Configurations, and changing Configurations (write)", "/_services, /_services/routers, /_services/configurations"),
            new TrakHoundSystemResource(Logs, "Logs", "Log files and live Log streams", "/_logs"),
            new TrakHoundSystemResource(Volumes, "Volumes", "Volume files, and creating, uploading, and deleting files (write)", "/_volumes"),
            new TrakHoundSystemResource(Buffers, "Buffers", "Buffer Metrics", "/_buffers"),
            new TrakHoundSystemResource(Modules, "Modules", "Loaded Modules", "/_modules"),
            new TrakHoundSystemResource(Instance, "Instance", "Host Information, the detailed Health report and CPU usage by Module", "/_instances"),
            new TrakHoundSystemResource(Metrics, "Metrics", "HTTP, Stream and MCP Metrics", "/_http/metrics, /_streams/metrics, /_mcp/metrics"),
            new TrakHoundSystemResource(Mcp, "MCP Server", "Connecting AI clients and using the read-only Tools. Tools that change something (ex. running a Function or a SQL query) also require write. Each Tool also requires the Permission for what it uses (ex. system:functions)", "/_mcp"),
            new TrakHoundSystemResource(Audit, "Audit Log", "The record of changes to Packages and Configurations, and checking its Hash Chain", "/_instances/audit")
        };


        /// <summary>
        /// Registers the System API Resources with the Security Manager
        /// </summary>
        public static void RegisterResources(ITrakHoundSecurityManager securityManager)
        {
            if (securityManager == null) return;

            foreach (var resource in Resources)
            {
                securityManager.AddResource(TrakHoundIdentityResourceType.System, resource.Id, _permissions);
            }
        }

        public static TrakHoundSystemResource GetResource(string resourceId)
        {
            return Resources.FirstOrDefault(o => o.Id == resourceId);
        }

        /// <summary>
        /// Gets the Permission required for a System API action
        /// </summary>
        public static string GetPermission(bool write) => write ? WritePermission : ReadPermission;

        /// <summary>
        /// Profiles saved before the System API Resources were added use "System.Api.Information"
        /// </summary>
        public static string MigrateResourceId(string resourceId)
        {
            return resourceId == LegacyApiInformation ? ApiInformation : resourceId;
        }
    }


    /// <summary>
    /// The System API Resource that a Controller (or Action) belongs to. Requests are authorized by the TrakHoundAuthenticationMiddleware
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
    public class TrakHoundSystemResourceAttribute : Attribute
    {
        public string ResourceId { get; }


        public TrakHoundSystemResourceAttribute(string resourceId)
        {
            ResourceId = resourceId;
        }
    }

    /// <summary>
    /// The System API Action changes something (ex. starts a Worker) so it requires the "write" Permission. Other Actions require "read"
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class TrakHoundSystemWriteAttribute : Attribute
    {
    }

    /// <summary>
    /// The Controller is authorized per Service operation (see TrakHoundServiceSecurity) instead of by a System API Resource
    /// </summary>
    [AttributeUsage(AttributeTargets.Class)]
    public class TrakHoundServiceOperationsAttribute : Attribute
    {
    }
}
