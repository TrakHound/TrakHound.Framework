// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients;
using TrakHound.Instance.Api;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Security;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// Applies the Rate Limits to HTTP Requests to the Apis and the System API. Runs after the TrakHoundAuthenticationMiddleware, so that
    /// Requests are counted for the Identity that made them. The MCP Server's Tool calls are limited when they are called (see TrakHoundMcpServer)
    /// </summary>
    public class TrakHoundRateLimitMiddleware
    {
        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundRateLimiter _rateLimiter;
        private readonly RequestDelegate _next;
        private readonly ITrakHoundLogger _logger = new TrakHoundLogger<TrakHoundRateLimitMiddleware>();


        public TrakHoundRateLimitMiddleware(ITrakHoundInstance instance, TrakHoundRateLimiter rateLimiter, RequestDelegate next)
        {
            _instance = instance;
            _rateLimiter = rateLimiter;
            _next = next;
        }


        public async Task Invoke(HttpContext context)
        {
            // Requests made with a Module Token come from inside the Instance (ex. a worker process that an Api started)
            var area = GetArea(context);
            if (area == null || context.GetModuleToken() != null)
            {
                await _next(context);
                return;
            }

            var callerKey = TrakHoundRateLimiter.GetCallerKey(TrakHoundSecurityContext.Current?.Identity, context);

            using var lease = _rateLimiter.Acquire(area, callerKey, !IsStreamRequest(context.Request));
            if (!lease.IsAcquired)
            {
                _logger.LogDebug($"Rate Limit Exceeded : {area} : {callerKey} : {context.Request.Path} : {lease.Reason}");

                context.Response.StatusCode = StatusCodes.Status429TooManyRequests;
                context.Response.Headers.RetryAfter = lease.GetRetryAfterSeconds().ToString();
                context.Response.ContentType = "text/plain";
                await context.Response.WriteAsync($"Rate Limit Exceeded : {lease.Reason}. Retry after {lease.GetRetryAfterSeconds()} seconds");
                return;
            }

            await _next(context);
        }


        private string GetArea(HttpContext context)
        {
            var path = context.Request.Path;

            if (TrakHoundInstanceRoutes.IsMcpRoute(path)) return null; // Tool calls are limited by the MCP Server
            if (TrakHoundInstanceRoutes.IsSystemApiRoute(path)) return TrakHoundRateLimitAreas.SystemApi;

            if (_instance.ApiProvider.IsRouteValid(context) && _instance.ApiProvider.IsHttpEnabled(context)) return TrakHoundRateLimitAreas.Api;

            return null;
        }

        // WebSockets and Server-Sent Events stay open, so they aren't counted toward the Concurrency Limit
        private static bool IsStreamRequest(HttpRequest request)
        {
            return request.HttpContext.WebSockets.IsWebSocketRequest || request.Headers.Accept.Any(o => o != null && o.Contains("text/event-stream", StringComparison.OrdinalIgnoreCase));
        }
    }
}
