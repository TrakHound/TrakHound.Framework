// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Http;
using TrakHound.Instance.Api;
using TrakHound.Instances;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// What a Request is for: the fixed Instance routes (TrakHoundInstanceRoutes) plus the Api and App routes of the loaded Apis and Apps.
    /// Shared by the request pipeline's middleware and the HTTP Metrics
    /// </summary>
    public static class TrakHoundInstanceRequests
    {
        /// <summary>
        /// Whether the Request is for something the Instance serves (others get a 404, or a 503 while the Instance isn't Started)
        /// </summary>
        public static bool IsValidRoute(ITrakHoundInstance instance, HttpContext context)
        {
            var path = context.Request.Path;
            var configuration = instance.Configuration;

            if (TrakHoundInstanceRoutes.IsSystemApiRoute(path)) return true;
            if (IsStaticRoute(path)) return true;
            if (TrakHoundInstanceRoutes.IsFaviconRoute(path)) return true; // Requested by browsers for pages that don't link an icon (ex. Apps)
            if (TrakHoundInstanceRoutes.IsPackagesRoute(path)) return true;
            if (IsApiRouteValid(instance, context)) return true;
            if (TrakHoundInstanceRoutes.IsDeveloperRoute(path) && configuration.ApiHttpEnabled) return true; // The Api Catalog (only useful when the Apis are served over HTTP)
            if (IsAppRouteValid(instance, path)) return true;
            if (TrakHoundInstanceRoutes.IsInitializationRoute(path)) return true;
            if (TrakHoundInstanceRoutes.IsAdminRoute(path) && configuration.AdminInterfaceEnabled) return true; // Otherwise /_admin would fall through to an empty App page
            if (TrakHoundInstanceRoutes.IsErrorRoute(path)) return true;
            if (TrakHoundInstanceRoutes.IsIdentityRoute(path)) return true; // Sign In, Callback, and Sign Out

            return false;
        }

        /// <summary>
        /// Files needed to load pages, and Downloads (no Authentication)
        /// </summary>
        public static bool IsStaticRoute(PathString path) => TrakHoundInstanceRoutes.IsStaticRoute(path) || TrakHoundInstanceRoutes.IsDownloadRoute(path);

        /// <summary>
        /// An App route that is served over HTTP (Apps are still available within the Instance when it is disabled)
        /// </summary>
        public static bool IsAppRouteValid(ITrakHoundInstance instance, PathString path)
        {
            return instance.Configuration.AppsHttpEnabled && instance.AppProvider != null && instance.AppProvider.IsRouteValid(path);
        }

        /// <summary>
        /// An Api route that is served over HTTP. The Request's Api route is resolved once and reused by the other middleware (see TrakHoundApiRequestRoute)
        /// </summary>
        public static bool IsApiRouteValid(ITrakHoundInstance instance, HttpContext context)
        {
            return instance.Configuration.ApiHttpEnabled && instance.ApiProvider != null && instance.ApiProvider.IsRouteValid(context);
        }

        /// <summary>
        /// What an HTTP Request was for (for the HTTP Metrics)
        /// </summary>
        public static string GetHttpArea(ITrakHoundInstance instance, HttpContext context)
        {
            var path = context.Request.Path;

            if (TrakHoundInstanceRoutes.IsBlazorRoute(path)) return TrakHoundHttpAreas.Blazor;
            if (TrakHoundInstanceRoutes.IsMcpRoute(path)) return TrakHoundHttpAreas.Mcp;
            if (TrakHoundInstanceRoutes.IsSystemApiRoute(path) || TrakHoundInstanceRoutes.IsWorkersRoute(path)) return TrakHoundHttpAreas.SystemApi;
            if (IsStaticRoute(path) || TrakHoundInstanceRoutes.IsPackagesRoute(path) || TrakHoundInstanceRoutes.IsInitializationRoute(path) || TrakHoundInstanceRoutes.IsFaviconRoute(path)) return TrakHoundHttpAreas.StaticFiles;
            if (TrakHoundInstanceRoutes.IsAdminRoute(path)) return TrakHoundHttpAreas.Admin;
            if (TrakHoundInstanceRoutes.IsDeveloperRoute(path)) return TrakHoundHttpAreas.Api;

            // Instance pages outside of /_admin (ex. the Error Page) are Razor Component Endpoints (App pages use the App Host Endpoint)
            var endpoint = context.GetEndpoint();
            if (endpoint?.Metadata.GetMetadata<Microsoft.AspNetCore.Components.Endpoints.ComponentTypeMetadata>() != null && !Pages.AppHost.IsEndpoint(endpoint)) return TrakHoundHttpAreas.Admin;
            if (IsApiRouteValid(instance, context)) return TrakHoundHttpAreas.Api;
            if (IsAppRouteValid(instance, path)) return TrakHoundHttpAreas.Apps;

            return TrakHoundHttpAreas.Other;
        }
    }
}
