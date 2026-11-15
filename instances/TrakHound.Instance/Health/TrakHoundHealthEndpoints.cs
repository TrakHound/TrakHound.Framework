// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Instances;

namespace TrakHound.Instance.Health
{
    /// <summary>
    /// Public, unauthenticated Health endpoints (ex. for Load Balancers and Container health checks). They only return the Status,
    /// the details are available from the System API (/_instances/health).
    /// <list type="bullet">
    /// <item>/_health : 200 when Healthy or Degraded (still able to serve requests), 503 when Unhealthy</item>
    /// <item>/_health/live : 200 while the process is running and able to respond (use to restart a hung process, not for problems a restart won't fix)</item>
    /// </list>
    /// </summary>
    public static class TrakHoundHealthEndpoints
    {
        public const string HealthPath = "/_health";
        public const string LivePath = "/_health/live";


        /// <summary>
        /// Writes the response if the request is for a Health endpoint. Returns false otherwise.
        /// </summary>
        public static async Task<bool> Handle(HttpContext context, ITrakHoundInstance instance)
        {
            var path = context.Request.Path.Value?.TrimEnd('/');

            if (string.Equals(path, LivePath, StringComparison.OrdinalIgnoreCase))
            {
                await Write(context, StatusCodes.Status200OK, "Alive", null);
                return true;
            }

            if (string.Equals(path, HealthPath, StringComparison.OrdinalIgnoreCase))
            {
                var health = instance?.Health?.GetHealth();
                var status = health?.Status ?? TrakHoundHealthStatus.Unknown;
                var statusCode = status == TrakHoundHealthStatus.Unhealthy ? StatusCodes.Status503ServiceUnavailable : StatusCodes.Status200OK;

                await Write(context, statusCode, status.ToString(), health?.Checked);
                return true;
            }

            return false;
        }

        private static async Task Write(HttpContext context, int statusCode, string status, DateTime? checkedTimestamp)
        {
            context.Response.StatusCode = statusCode;
            context.Response.ContentType = "application/json";
            context.Response.Headers.CacheControl = "no-store";

            var body = checkedTimestamp != null
                ? JsonSerializer.Serialize(new { status, @checked = checkedTimestamp })
                : JsonSerializer.Serialize(new { status });

            if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.WriteAsync(body);
        }
    }
}
