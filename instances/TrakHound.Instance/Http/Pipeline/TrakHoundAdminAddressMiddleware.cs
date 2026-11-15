// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;
using TrakHound.Logging;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// Responds 404 to Admin Interface Requests (its Pages, Sign In and Downloads) from client addresses that aren't allowed (see
    /// TrakHoundAdminAddresses), the same as when the Admin Interface is disabled. Navigation inside a Blazor Circuit doesn't make a Request,
    /// so the Instance Router checks the address too
    /// </summary>
    public class TrakHoundAdminAddressMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly Func<TrakHoundInstanceConfiguration> _getConfiguration;
        private readonly ITrakHoundLogger _logger = new TrakHoundLogger<TrakHoundAdminAddressMiddleware>();


        public TrakHoundAdminAddressMiddleware(RequestDelegate next, Func<TrakHoundInstanceConfiguration> getConfiguration)
        {
            _next = next;
            _getConfiguration = getConfiguration;
        }


        public async Task Invoke(HttpContext context)
        {
            if (IsAdminRequest(context) && !TrakHoundAdminAddresses.IsAllowed(context, _getConfiguration?.Invoke()))
            {
                _logger.LogDebug($"Admin Interface Request Denied : {context.Connection.RemoteIpAddress} : {context.Request.Path}");

                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.CompleteAsync();
                return;
            }

            await _next(context);
        }

        private static bool IsAdminRequest(HttpContext context)
        {
            var path = context.Request.Path;
            return TrakHoundInstanceRoutes.IsAdminRoute(path) || TrakHoundInstanceRoutes.IsDownloadRoute(path) || TrakHoundAdminPort.IsAdminPage(context.GetEndpoint());
        }
    }
}
