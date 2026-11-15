// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// With a separate Admin Port, the Admin Interface and System API are only served on the Admin Port, and Apps and Apis only on the
    /// HTTP Port. Requests on the wrong Port get a 404 (only added to the pipeline when the Admin Port is enabled)
    /// </summary>
    public class TrakHoundAdminPortMiddleware
    {
        private readonly RequestDelegate _next;


        public TrakHoundAdminPortMiddleware(RequestDelegate next)
        {
            _next = next;
        }


        public async Task Invoke(HttpContext context)
        {
            if (await Handle(context)) return;
            await _next(context);
        }

        /// <summary>
        /// Responds to Requests on the wrong Port. Returns true when the Request was handled
        /// </summary>
        private static async Task<bool> Handle(HttpContext context)
        {
            var path = context.Request.Path;

            // Files needed to load pages (and the Blazor connection) are served on both Ports. Health is handled before this
            if (TrakHoundInstanceRoutes.IsStaticRoute(path) || TrakHoundInstanceRoutes.IsHealthRoute(path) || TrakHoundInstanceRoutes.IsFaviconRoute(path)) return false;

            // The Api Catalog describes the Apis (served on the HTTP Port) and is also available to the Admin Interface
            if (TrakHoundInstanceRoutes.IsDeveloperRoute(path)) return false;

            // MCP clients connect on either Port (authorized by Identity Profiles like the rest of the System API)
            if (TrakHoundInstanceRoutes.IsMcpRoute(path)) return false;

            var adminOnly = TrakHoundAdminPort.IsAdminPage(context.GetEndpoint()) || TrakHoundInstanceRoutes.IsAdminRoute(path) || TrakHoundInstanceRoutes.IsSystemApiRoute(path) || TrakHoundInstanceRoutes.IsDownloadRoute(path);

            if (TrakHoundAdminPort.IsAdminConnection(context))
            {
                if (adminOnly || TrakHoundInstanceRoutes.IsErrorRoute(path)) return false;

                // The root of the Admin Port opens the Admin Interface
                if (!path.HasValue || path == "/")
                {
                    context.Response.Redirect($"{context.Request.PathBase}{TrakHoundInstanceRoutes.AdminPrefix}");
                    return true;
                }
            }
            else if (!adminOnly)
            {
                return false;
            }

            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await context.Response.CompleteAsync();
            return true;
        }
    }
}
