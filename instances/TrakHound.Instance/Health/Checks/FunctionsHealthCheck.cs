// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Functions;
using TrakHound.Instances;

namespace TrakHound.Instance.Health.Checks
{
    /// <summary>
    /// Functions whose recent Runs are mostly failing (Cancelled Runs are not failures)
    /// </summary>
    public class FunctionsHealthCheck : ITrakHoundHealthCheck
    {
        private const int _recentRuns = 10;
        private const int _minimumFailures = 2;
        private const double _failureRatio = 0.5;
        private static readonly TimeSpan _recentWindow = TimeSpan.FromHours(1);


        public string Id => "functions";

        public string Title => "Functions";


        public Task<TrakHoundHealthCheckResult> Evaluate(TrakHoundHealthContext context)
        {
            if (!(context.Instance.Metrics?.FunctionsEnabled ?? true))
            {
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Unknown, "Function Metrics are disabled", "diagnostics/functions"));
            }

            var functionManager = context.Instance.FunctionManager;
            var functionCount = functionManager?.GetInformation()?.Count() ?? 0;
            var items = new List<TrakHoundHealthCheckItem>();
            var recentRunCount = 0;

            foreach (var summary in functionManager?.GetMetrics() ?? Enumerable.Empty<TrakHoundFunctionMetrics>())
            {
                var metrics = functionManager.GetMetrics(summary.FunctionId);
                var runs = metrics?.RecentRuns?
                    .Where(o => o.Completed != null && context.Now - o.Completed.Value <= _recentWindow)
                    .Take(_recentRuns)
                    .ToList();

                if (runs.IsNullOrEmpty()) continue;
                recentRunCount += runs.Count;

                var failures = runs.Count(o => !IsSuccess(o.StatusCode) && o.StatusCode != TrakHoundFunctionMetricsCollector.CancelledStatusCode);
                if (failures >= _minimumFailures && failures >= runs.Count * _failureRatio)
                {
                    var description = $"{failures} of the last {runs.Count} Runs failed";
                    if (!string.IsNullOrEmpty(metrics.LastErrorMessage)) description += $" : {metrics.LastErrorMessage}";

                    items.Add(new TrakHoundHealthCheckItem
                    {
                        Id = metrics.FunctionId,
                        Name = metrics.FunctionId,
                        Status = TrakHoundHealthStatus.Degraded,
                        Description = description,
                        Link = $"diagnostics/functions?FunctionId={metrics.FunctionId}"
                    });
                }
            }

            if (items.Count > 0)
            {
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Degraded, $"{items.Count} of {functionCount} Functions failing", "diagnostics/functions", items));
            }

            var healthy = recentRunCount > 0 ? $"{recentRunCount} Runs in the last hour, none failing" : $"{functionCount} Functions · no Runs in the last hour";
            return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Healthy, healthy, "diagnostics/functions"));
        }

        private static bool IsSuccess(int statusCode) => statusCode >= 200 && statusCode < 300;
    }
}
