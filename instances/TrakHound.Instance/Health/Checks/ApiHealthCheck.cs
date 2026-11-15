// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;

namespace TrakHound.Instance.Health.Checks
{
    /// <summary>
    /// APIs with a high rate of Server Errors (Status Code >= 500) over the recent Metrics window.
    /// Client Errors (4xx, ex. Not Found) are a normal part of an API and are not counted.
    /// </summary>
    public class ApiHealthCheck : ITrakHoundHealthCheck
    {
        private const int _minimumRequests = 5;
        private const double _serverErrorRatio = 0.1;


        public string Id => "api";

        public string Title => "APIs";


        public Task<TrakHoundHealthCheckResult> Evaluate(TrakHoundHealthContext context)
        {
            if (!(context.Instance.Metrics?.ApiEnabled ?? true))
            {
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Unknown, "API Metrics are disabled", "diagnostics/api"));
            }

            var apiProvider = context.Instance.ApiProvider;
            var metrics = apiProvider?.GetMetrics()?.ToList() ?? new List<TrakHound.Api.TrakHoundApiMetrics>();
            var items = new List<TrakHoundHealthCheckItem>();
            var window = metrics.FirstOrDefault()?.Window ?? 10;
            var totalRequests = 0d;

            foreach (var api in metrics)
            {
                // Rates are per second over the window
                var requests = api.RequestRate * api.Window;
                var serverErrors = api.ServerErrorRate * api.Window;
                totalRequests += requests;

                if (requests >= _minimumRequests && serverErrors >= requests * _serverErrorRatio)
                {
                    var route = apiProvider.GetInformation(api.ApiId)?.Route;

                    items.Add(new TrakHoundHealthCheckItem
                    {
                        Id = api.ApiId,
                        Name = !string.IsNullOrEmpty(route) ? route : api.PackageId ?? api.ApiId,
                        Status = TrakHoundHealthStatus.Degraded,
                        Description = $"{serverErrors / requests:0%} of {requests:0} requests failed with a Server Error (5xx) in the last {api.Window} seconds",
                        Link = $"diagnostics/api?ApiId={api.ApiId}"
                    });
                }
            }

            if (items.Count > 0)
            {
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Degraded, $"{items.Count} APIs with Server Errors", "diagnostics/api", items));
            }

            var healthy = totalRequests > 0 ? $"{totalRequests / window:0.#} requests/s, no Server Errors" : "No recent requests";
            return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Healthy, healthy, "diagnostics/api"));
        }
    }
}
