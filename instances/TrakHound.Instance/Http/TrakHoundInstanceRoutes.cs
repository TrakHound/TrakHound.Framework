// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Health;
using TrakHound.Instance.Security;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// A fixed route prefix served by the Instance (other than the Api and App routes, which come from the loaded Apis and Apps)
    /// </summary>
    public class TrakHoundInstanceRoute
    {
        public string Prefix { get; }

        public string Title { get; }

        public string Description { get; }


        public TrakHoundInstanceRoute(string prefix, string title, string description)
        {
            Prefix = prefix;
            Title = title;
            Description = description;
        }
    }


    /// <summary>
    /// The fixed route prefixes served by the Instance. Used by the request pipeline (InstanceApplication), the TrakHoundAuthenticationMiddleware,
    /// and the Security 'What is Public?' page so that they always agree on which routes exist and how they are handled
    /// </summary>
    public static class TrakHoundInstanceRoutes
    {
        /// <summary>
        /// Files needed to load pages (no Authentication)
        /// </summary>
        public static readonly IReadOnlyList<TrakHoundInstanceRoute> StaticRoutes = new[]
        {
            new TrakHoundInstanceRoute("/_blazor", "Blazor", "The Blazor connection used by the Admin Interface and Apps"),
            new TrakHoundInstanceRoute("/_framework", "Blazor Framework", "Blazor framework scripts"),
            new TrakHoundInstanceRoute("/_content", "Static Content", "Scripts and stylesheets from Razor Class Libraries"),
            new TrakHoundInstanceRoute("/_packages", "Package Files", "Static files (css, js, images) from installed Packages"),
            new TrakHoundInstanceRoute("/_images", "Explorer Images", "Images used by the Explorer"),
            new TrakHoundInstanceRoute("/img", "Images", "Static image files"),
            new TrakHoundInstanceRoute("/css", "Stylesheets", "Static CSS files"),
            new TrakHoundInstanceRoute("/js", "Scripts", "Static script files used by the Admin Interface"),
            new TrakHoundInstanceRoute("/TrakHound.Instance.styles.css", "Instance Stylesheet", "The Admin Interface's scoped CSS bundle")
        };

        /// <summary>
        /// Downloads created by the Admin Interface. Each download is only available at a random, temporary key
        /// </summary>
        public static readonly TrakHoundInstanceRoute DownloadRoute = new TrakHoundInstanceRoute("/_download", "Downloads", "Files prepared for download by the Admin Interface, each at a random, temporary key");

        /// <summary>
        /// The System API (the Instance management API). Each route is a System Resource (see TrakHoundSystemSecurity) or a Service operation.
        /// /_workers is not included, so the Workers controller is not served (requests return 404)
        /// </summary>
        public static readonly IReadOnlyList<string> SystemApiPrefixes = new[]
        {
            "/_instances",
            "/_api",
            "/_apps",
            "/_entities",
            "/_functions",
            "/_services",
            "/_volumes",
            "/_logs",
            "/_buffers",
            "/_modules",
            "/_streams",
            "/_http",
            McpPrefix
        };

        /// <summary>
        /// The MCP Server (Model Context Protocol). Part of the System API, but also served on the HTTP Port when there is an Admin Port
        /// </summary>
        public const string McpPrefix = "/_mcp";

        /// <summary>
        /// Describes the Apis to developers building their own applications (the Api Catalog and OpenAPI Documents). Unlike the System API,
        /// it is served on the HTTP Port (with the Apis) and only includes the Endpoints the caller can use
        /// </summary>
        public const string DeveloperPrefix = "/_developer";

        /// <summary>
        /// A Markdown index of the Instance and its Apis for AI agents (https://llmstxt.org). Served like the Api Catalog
        /// </summary>
        public const string LlmsTextPath = "/llms.txt";

        public const string AdminPrefix = "/_admin";
        public const string ErrorPrefix = "/_error";
        public const string PackagesPrefix = "/_packages";

        /// <summary>
        /// The Blazor connection (SignalR) used by the Admin Interface and Apps
        /// </summary>
        public const string BlazorPrefix = "/_blazor";

        /// <summary>
        /// The Workers Controller, which is not served (see SystemApiPrefixes)
        /// </summary>
        public const string WorkersPrefix = "/_workers";

        public const string FaviconPath = "/favicon.ico";


        public static bool IsStaticRoute(PathString path) => StartsWithAny(path, _staticPrefixes);

        public static bool IsDownloadRoute(PathString path) => StartsWith(path, DownloadRoute.Prefix);

        public static bool IsPackagesRoute(PathString path) => StartsWith(path, PackagesPrefix);

        public static bool IsSystemApiRoute(PathString path) => StartsWithAny(path, SystemApiPrefixes);

        public static bool IsMcpRoute(PathString path) => path.StartsWithSegments(McpPrefix, StringComparison.OrdinalIgnoreCase);

        public static bool IsBlazorRoute(PathString path) => StartsWith(path, BlazorPrefix);

        public static bool IsWorkersRoute(PathString path) => StartsWith(path, WorkersPrefix);

        public static bool IsFaviconRoute(PathString path) => path.Equals(FaviconPath, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The Api Catalog, its OpenAPI Documents, and llms.txt
        /// </summary>
        public static bool IsDeveloperRoute(PathString path) => path.StartsWithSegments(DeveloperPrefix, StringComparison.OrdinalIgnoreCase) || path.Equals(LlmsTextPath, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The Admin Interface's Pages are all under /_admin (the Error Page, /_error, is also shown to App users)
        /// </summary>
        public static bool IsAdminRoute(PathString path) => path.StartsWithSegments(AdminPrefix, StringComparison.OrdinalIgnoreCase);

        public static bool IsErrorRoute(PathString path) => StartsWith(path, ErrorPrefix);

        public static bool IsInitializationRoute(PathString path) => StartsWith(path, _initializationPrefix);

        public static bool IsIdentityRoute(PathString path) => path.StartsWithSegments(TrakHoundIdentityHttp.IdentityPrefix);

        public static bool IsHealthRoute(PathString path) => path.StartsWithSegments(TrakHoundHealthEndpoints.HealthPath, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Whether the path belongs to the Instance (the Admin Interface, System API, Sign In, Instance pages, and static files), so
        /// App routes never match it (ex. an App at "/{id}" would otherwise match "/_admin"). Matched by whole path segment
        /// </summary>
        public static bool IsReservedRoute(PathString path)
        {
            foreach (var prefix in _reservedPrefixes)
            {
                if (path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }


        // These are checked several times for every Request, so the prefix lists are built once (declared last so the lists above are already set)
        private static readonly string _initializationPrefix = "/" + TrakHoundInitializationPage.Route;
        private static readonly string[] _staticPrefixes = StaticRoutes.Select(o => o.Prefix).ToArray();
        private static readonly string[] _reservedPrefixes = new[]
        {
            AdminPrefix,
            ErrorPrefix,
            _initializationPrefix,
            TrakHoundIdentityHttp.IdentityPrefix,
            TrakHoundHealthEndpoints.HealthPath,
            DownloadRoute.Prefix,
            DeveloperPrefix,
            LlmsTextPath,
            WorkersPrefix // The Workers controller (not served, see SystemApiPrefixes)
        }.Concat(SystemApiPrefixes).Concat(_staticPrefixes).ToArray();


        // Matched by whole path segment and ignoring case, like endpoint routing (which would otherwise serve "/_LOGS" or "/_logsx" to a
        // Controller that these checks didn't recognize). OrdinalIgnoreCase, since a culture-sensitive comparison is much slower and these run for every Request
        private static bool StartsWith(PathString path, string prefix)
        {
            return path.StartsWithSegments(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static bool StartsWithAny(PathString path, IReadOnlyList<string> prefixes)
        {
            for (var i = 0; i < prefixes.Count; i++)
            {
                if (path.StartsWithSegments(prefixes[i], StringComparison.OrdinalIgnoreCase)) return true;
            }
            return false;
        }
    }
}
