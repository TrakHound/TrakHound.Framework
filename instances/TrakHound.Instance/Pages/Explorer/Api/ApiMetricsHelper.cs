// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;

namespace TrakHound.Instance.Pages.Explorer.Api
{
    /// <summary>
    /// Matches Api Endpoints to their Request Metrics, and formats Metric values for display
    /// </summary>
    public static class ApiMetricsHelper
    {
        /// <summary>
        /// The Route of an Endpoint within its Api ("/" + Controller Route + Endpoint Route), as recorded in the Metrics
        /// </summary>
        public static string GetEndpointRoute(TrakHoundApiControllerInformation controller, TrakHoundApiEndpointInformation endpoint)
        {
            if (endpoint == null) return null;

            var route = Url.Combine(controller?.Route, endpoint.Route);
            return $"/{route?.TrimStart('/')}";
        }

        /// <summary>
        /// Gets the Metrics for an Endpoint (null if it has not received any Requests)
        /// </summary>
        public static TrakHoundApiEndpointMetrics GetEndpointMetrics(TrakHoundApiMetrics metrics, TrakHoundApiControllerInformation controller, TrakHoundApiEndpointInformation endpoint)
        {
            if (metrics?.Endpoints == null || endpoint == null) return null;

            var key = GetKey(controller, endpoint);
            return metrics.Endpoints.FirstOrDefault(o => GetKey(o.Type, o.Method, o.Route) == key);
        }

        /// <summary>
        /// Gets a lookup of Endpoint Metrics by Key (see GetKey)
        /// </summary>
        public static Dictionary<string, TrakHoundApiEndpointMetrics> GetEndpointLookup(TrakHoundApiMetrics metrics)
        {
            return metrics?.Endpoints?
                .GroupBy(o => GetKey(o.Type, o.Method, o.Route))
                .ToDictionary(o => o.Key, o => o.First()) ?? new Dictionary<string, TrakHoundApiEndpointMetrics>();
        }

        public static string GetKey(TrakHoundApiControllerInformation controller, TrakHoundApiEndpointInformation endpoint)
        {
            // Stream Requests are recorded with the GET Method
            var method = endpoint.Method?.ToString() ?? (endpoint.Type == TrakHoundApiEndpointType.Stream ? TrakHoundApiMethod.GET.ToString() : null);
            return GetKey(endpoint.Type.ToString(), method, GetEndpointRoute(controller, endpoint));
        }

        public static string GetKey(string type, string method, string route)
        {
            var normalizedRoute = "/" + (route ?? "").Trim('/');
            return $"{type}:{method}:{normalizedRoute}".ToLower();
        }


        public static string FormatRate(double value)
        {
            if (value >= 1000) return $"{value / 1000:0.#}k";
            if (value >= 100) return value.ToString("0");
            if (value >= 10) return value.ToString("0.#");
            return value.ToString("0.##");
        }

        public static string FormatDuration(double milliseconds)
        {
            if (milliseconds >= 1000) return $"{milliseconds / 1000:0.##} s";
            if (milliseconds >= 100) return $"{milliseconds:0} ms";
            if (milliseconds >= 1) return $"{milliseconds:0.#} ms";
            return $"{milliseconds:0.##} ms";
        }

        public static string FormatLastRequested(DateTime? timestamp)
        {
            if (timestamp == null) return "-";

            var local = timestamp.Value.ToLocalTime();
            return local.Date == DateTime.Now.Date ? local.ToString("HH:mm:ss") : local.ToString("yyyy-MM-dd HH:mm:ss");
        }

        public static string FormatErrorPercent(long errors, long total)
        {
            if (total < 1) return null;
            return $"({(double)errors / total:0.#%})";
        }
    }
}
