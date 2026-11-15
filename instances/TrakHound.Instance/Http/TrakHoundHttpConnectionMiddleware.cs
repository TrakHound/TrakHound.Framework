// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http.Features;
using System.Diagnostics;

namespace TrakHound.Http
{
    /// <summary>
    /// Records each HTTP Request (for the HTTP Requests Diagnostics and the HTTP Metrics)
    /// </summary>
    public class TrakHoundHttpConnectionMiddleware
    {
        private readonly TrakHoundHttpRequestHistory _requests;
        private readonly TrakHoundHttpMetricsCollector _metrics;
        private readonly Func<HttpContext, string> _getArea;
        private readonly RequestDelegate _next;


        /// <param name="getArea">Gets what the Request was for (System API, API, Apps, Admin, Blazor, Static Files, or null for Other)</param>
        public TrakHoundHttpConnectionMiddleware(TrakHoundHttpRequestHistory requests, TrakHoundHttpMetricsCollector metrics, Func<HttpContext, string> getArea, RequestDelegate next)
        {
            _requests = requests;
            _metrics = metrics;
            _getArea = getArea;
            _next = next;
        }


        public async Task Invoke(HttpContext context)
        {
            if (context.Response.HasStarted) return;

            // An unhandled error re-executed to the Error Page was already recorded (with its own Path and Status Code)
            if (context.Features.Get<IExceptionHandlerFeature>() != null)
            {
                await _next(context);
                return;
            }

            var requested = DateTime.UtcNow;
            var startTimestamp = Stopwatch.GetTimestamp();
            var started = _metrics?.RequestStarted() ?? false;

            // Count the Request and Response Body bytes (passed through to the original Body)
            CountingRequestStream requestStream = null;
            var bodyDetection = context.Features.Get<IHttpRequestBodyDetectionFeature>();
            if (bodyDetection == null || bodyDetection.CanHaveBody)
            {
                requestStream = new CountingRequestStream(context.Request.Body);
                context.Request.Body = requestStream;
            }

            CountingResponseBodyFeature responseFeature = null;
            var originalResponseFeature = context.Features.Get<IHttpResponseBodyFeature>();
            if (originalResponseFeature != null)
            {
                responseFeature = new CountingResponseBodyFeature(originalResponseFeature);
                context.Features.Set<IHttpResponseBodyFeature>(responseFeature);
            }

            var isUpgrade = context.WebSockets.IsWebSocketRequest;
            var statusCode = 0;

            try
            {
                await _next(context);
                statusCode = context.Response.StatusCode;
            }
            catch
            {
                // An unhandled exception results in a 500 Response
                statusCode = StatusCodes.Status500InternalServerError;
                throw;
            }
            finally
            {
                if (responseFeature != null) context.Features.Set(responseFeature.Inner);
                if (statusCode == StatusCodes.Status101SwitchingProtocols) isUpgrade = true;
                var isEventStream = !isUpgrade && IsEventStreamResponse(context);

                try
                {
                    var elapsed = Stopwatch.GetElapsedTime(startTimestamp);
                    var requestBytes = requestStream?.Count ?? 0;
                    var responseBytes = responseFeature?.Count ?? 0;
                    var area = GetArea(context, isUpgrade, isEventStream);
                    var route = GetRoute(context, area);
                    var clientAddress = GetClientAddress(context);

                    // Upgraded (WebSocket) and Event Stream Requests stay open for the life of the connection, so they aren't included in the Response Durations.
                    // MCP responses are Event Streams that end with their Request (the MCP Server is Stateless), so they are
                    var isLongLived = isUpgrade || (isEventStream && area != TrakHoundHttpAreas.Mcp);

                    if (_requests != null && _requests.Capacity > 0)
                    {
                        var request = new TrakHoundHttpRequestRecord();
                        request.Client = clientAddress;
                        request.Method = context.Request.Method;
                        request.Url = context.Request.Path + TrakHoundHttpQueryMask.Apply(context.Request.QueryString.Value);
                        request.Route = route;
                        request.Area = area;
                        if (isLongLived) request.Stream = isUpgrade ? TrakHoundHttpRequestStreams.WebSocket : TrakHoundHttpRequestStreams.EventStream;
                        request.RequestBytes = requestBytes;
                        request.StatusCode = statusCode;
                        request.ResponseBytes = responseBytes;
                        request.Requested = requested;
                        request.Completed = requested + elapsed;
                        _requests.Add(request);
                    }

                    _metrics?.RequestCompleted(area, context.Request.Method, route, clientAddress, statusCode, requestBytes, responseBytes,
                        isLongLived ? null : elapsed.TotalMilliseconds, requested, started);
                }
                catch { } // Recording must never affect the Response
            }
        }


        private string GetArea(HttpContext context, bool isUpgrade, bool isEventStream)
        {
            var area = _getArea?.Invoke(context);
            if (isUpgrade && area != TrakHoundHttpAreas.Blazor) return TrakHoundHttpAreas.WebSockets;
            if (isEventStream && area != TrakHoundHttpAreas.Mcp) return TrakHoundHttpAreas.EventStreams; // MCP responses can be Event Streams too

            return area ?? TrakHoundHttpAreas.Other;
        }

        private static bool IsEventStreamResponse(HttpContext context)
        {
            return context.Response.ContentType?.StartsWith(TrakHoundHttpEventStreamManager.ContentType, StringComparison.OrdinalIgnoreCase) == true;
        }

        /// <summary>
        /// The Route template when the Request matched an Endpoint with one (so Requests to the same Endpoint are grouped), otherwise the Path
        /// </summary>
        private static string GetRoute(HttpContext context, string area)
        {
            var path = context.Request.Path.Value ?? "/";

            // Group Static Files by extension (each file would be its own Endpoint)
            if (area == TrakHoundHttpAreas.StaticFiles)
            {
                var extension = Path.GetExtension(path);
                return !string.IsNullOrEmpty(extension) ? $"*{extension.ToLower()}" : "*";
            }

            // Blazor connections (negotiate, WebSocket) use generated IDs in the Query String only
            if (area == TrakHoundHttpAreas.Blazor) return "/_blazor";

            var routePattern = (context.GetEndpoint() as RouteEndpoint)?.RoutePattern?.RawText;
            if (!string.IsNullOrEmpty(routePattern) && !routePattern.Contains("{*") && !routePattern.Contains("{**"))
            {
                return "/" + routePattern.TrimStart('/');
            }

            return path;
        }

        private static string GetClientAddress(HttpContext context)
        {
            var address = context.Connection.RemoteIpAddress;
            if (address == null) return null;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return address.ToString();
        }
    }

    /// <summary>
    /// What an HTTP Request was for
    /// </summary>
    public static class TrakHoundHttpAreas
    {
        public const string SystemApi = "System API";
        public const string Api = "API";
        public const string Apps = "Apps";
        public const string Admin = "Admin";
        public const string Blazor = "Blazor";
        public const string StaticFiles = "Static Files";
        public const string WebSockets = "WebSockets";
        public const string EventStreams = "Event Streams";
        public const string Mcp = "MCP";
        public const string Other = "Other";
    }
}
