// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Health;
using TrakHound.Instances;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// The public Health endpoints (ex. for Load Balancers and Container health checks). First in the pipeline, so they are not affected
    /// by Authentication or the System API setting, and are not recorded in the HTTP Requests
    /// </summary>
    public class TrakHoundHealthMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITrakHoundInstance _instance;


        public TrakHoundHealthMiddleware(RequestDelegate next, ITrakHoundInstance instance)
        {
            _next = next;
            _instance = instance;
        }


        public async Task Invoke(HttpContext context)
        {
            if (!await TrakHoundHealthEndpoints.Handle(context, _instance)) await _next(context);
        }
    }

    /// <summary>
    /// The Initialization Page (shown while the Instance is not Started, see TrakHoundInitializationPage) and its Custom Page files, or a 404 when it is disabled
    /// </summary>
    public class TrakHoundInitializationPageMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITrakHoundInstance _instance;


        public TrakHoundInitializationPageMiddleware(RequestDelegate next, ITrakHoundInstance instance)
        {
            _next = next;
            _instance = instance;
        }


        public async Task Invoke(HttpContext context)
        {
            var configuration = _instance.Configuration;
            if (TrakHoundInitializationPage.IsRequest(context.Request.Path, configuration.BasePath))
            {
                if (configuration.InitializationPageEnabled)
                {
                    await TrakHoundInitializationPage.Handle(context, _instance);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.CompleteAsync();
                }
                return;
            }

            // The Custom Page's files (also served while the Instance is Started, ex. for the Settings preview)
            var assetPath = TrakHoundInitializationPage.GetAssetPath(context.Request.Path, configuration.BasePath);
            if (assetPath != null)
            {
                if (configuration.InitializationPageEnabled)
                {
                    await TrakHoundInitializationPage.HandleAsset(context, _instance, assetPath.Value);
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.CompleteAsync();
                }
                return;
            }

            await _next(context);
        }
    }

    /// <summary>
    /// When the Error Page is disabled, unhandled errors (re-executed by the Exception Handler) get an empty 500 and direct requests a 404
    /// </summary>
    public class TrakHoundErrorPageMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITrakHoundInstance _instance;


        public TrakHoundErrorPageMiddleware(RequestDelegate next, ITrakHoundInstance instance)
        {
            _next = next;
            _instance = instance;
        }


        public async Task Invoke(HttpContext context)
        {
            if (!_instance.Configuration.ErrorPageEnabled && TrakHoundInstanceRoutes.IsErrorRoute(context.Request.Path))
            {
                var exceptionFeature = context.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerFeature>();
                context.Response.StatusCode = exceptionFeature != null ? StatusCodes.Status500InternalServerError : StatusCodes.Status404NotFound;
                await context.Response.CompleteAsync();
                return;
            }

            await _next(context);
        }
    }

    /// <summary>
    /// Requests for anything the Instance doesn't serve get a 404 before routing (see TrakHoundInstanceRequests.IsValidRoute)
    /// </summary>
    public class TrakHoundRouteValidationMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITrakHoundInstance _instance;


        public TrakHoundRouteValidationMiddleware(RequestDelegate next, ITrakHoundInstance instance)
        {
            _next = next;
            _instance = instance;
        }


        public async Task Invoke(HttpContext context)
        {
            if (TrakHoundInstanceRequests.IsValidRoute(_instance, context))
            {
                await _next(context);
            }
            else if (_instance.Status != TrakHoundInstanceStatus.Started)
            {
                // App and API routes aren't registered until the Instance is Started (including while restarting or after an
                // error) : browsers are sent to the Initialization Page (if enabled), which reloads the route, and other clients get a 503
                var configuration = _instance.Configuration;
                await TrakHoundInitializationPage.HandleUnavailable(context, configuration.BasePath, configuration.InitializationPageEnabled);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.CompleteAsync();
            }
        }
    }

    /// <summary>
    /// The System API (and the MCP Server, which is part of it but also has its own setting) gets a 404 when it is disabled
    /// </summary>
    public class TrakHoundSystemApiSwitchMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITrakHoundInstance _instance;


        public TrakHoundSystemApiSwitchMiddleware(RequestDelegate next, ITrakHoundInstance instance)
        {
            _next = next;
            _instance = instance;
        }


        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path;
            if (TrakHoundInstanceRoutes.IsSystemApiRoute(path))
            {
                var configuration = _instance.Configuration;
                if (!configuration.SystemApiEnabled || (TrakHoundInstanceRoutes.IsMcpRoute(path) && !configuration.McpEnabled))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.CompleteAsync();
                    return;
                }
            }

            await _next(context);
        }
    }

    /// <summary>
    /// The Instance's own routes (ex. an unknown /_admin page) are never served by the App Host (which would render an empty page)
    /// </summary>
    public class TrakHoundReservedRouteMiddleware
    {
        private readonly RequestDelegate _next;


        public TrakHoundReservedRouteMiddleware(RequestDelegate next)
        {
            _next = next;
        }


        public async Task Invoke(HttpContext context)
        {
            if (TrakHoundInstanceRoutes.IsReservedRoute(context.Request.Path) && Pages.AppHost.IsEndpoint(context.GetEndpoint()))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.CompleteAsync();
                return;
            }

            await _next(context);
        }
    }

    /// <summary>
    /// Headers added to every response
    /// </summary>
    public class TrakHoundResponseHeadersMiddleware
    {
        private readonly RequestDelegate _next;


        public TrakHoundResponseHeadersMiddleware(RequestDelegate next)
        {
            _next = next;
        }


        public async Task Invoke(HttpContext context)
        {
            var headers = context.Response.Headers;

            // Browsers use the Content Type they are given (a file can't be run as a script because of what it contains)
            headers.XContentTypeOptions = "nosniff";

            // Other sites only see the origin (not the path or query, which can include IDs) of links followed from the Instance
            headers["Referrer-Policy"] = "strict-origin-when-cross-origin";

            if (!TrakHoundInstanceRequests.IsStaticRoute(context.Request.Path))
            {
                // Pages can only be framed by the Instance. With a separate Admin Port, the Admin Interface (a different origin)
                // can show Apps in its App Preview
                var adminOrigin = !TrakHoundAdminPort.IsAdminConnection(context) ? TrakHoundAdminPort.GetAdminOrigin(context) : null;
                headers.ContentSecurityPolicy = adminOrigin != null ? $"frame-ancestors 'self' {adminOrigin}" : "frame-ancestors 'self'";
            }

            await _next(context);
        }
    }
}
