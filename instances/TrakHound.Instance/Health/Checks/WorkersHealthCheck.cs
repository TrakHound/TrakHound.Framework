// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;
using TrakHound.Workers;

namespace TrakHound.Instance.Health.Checks
{
    /// <summary>
    /// Workers that are in Error, or that keep failing (and being Restarted). Stopped Workers are not a problem (they may be Stopped on purpose).
    /// </summary>
    public class WorkersHealthCheck : ITrakHoundHealthCheck
    {
        private const int _repeatedErrors = 3;
        private static readonly TimeSpan _repeatedErrorsWindow = TimeSpan.FromMinutes(10);


        public string Id => "workers";

        public string Title => "Workers";


        public Task<TrakHoundHealthCheckResult> Evaluate(TrakHoundHealthContext context)
        {
            var workerManager = context.Instance.WorkerManager;
            var workers = workerManager?.GetInformation()?.Where(o => o != null && !string.IsNullOrEmpty(o.Id)).ToList();
            if (workers.IsNullOrEmpty())
            {
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Healthy, "No Workers are configured", "workers"));
            }

            var metricsEnabled = context.Instance.Metrics?.WorkersEnabled ?? true;
            var items = new List<TrakHoundHealthCheckItem>();

            foreach (var worker in workers)
            {
                var metrics = metricsEnabled ? workerManager.GetMetrics(worker.Id) : null;
                var name = !string.IsNullOrEmpty(metrics?.Name) ? metrics.Name : worker.Name ?? worker.Id;

                // Errors recently (also catches a Worker that recovers between evaluations but keeps failing)
                var recentErrors = metrics?.RecentEvents?.Count(o => o.Status == TrakHoundWorkerStatusType.Error && context.Now - o.Timestamp <= _repeatedErrorsWindow) ?? 0;

                string description = null;
                if (worker.Status == TrakHoundWorkerStatusType.Error)
                {
                    description = recentErrors > 1 ? $"Error · failed {recentErrors} times in the last {_repeatedErrorsWindow.TotalMinutes:0} minutes" : "Error";
                }
                else if (recentErrors >= _repeatedErrors)
                {
                    description = $"{worker.Status} · failed {recentErrors} times in the last {_repeatedErrorsWindow.TotalMinutes:0} minutes";
                }

                if (description != null)
                {
                    if (!string.IsNullOrEmpty(metrics?.LastErrorMessage)) description += $" : {metrics.LastErrorMessage}";

                    items.Add(new TrakHoundHealthCheckItem
                    {
                        Id = worker.Id,
                        Name = name,
                        Status = TrakHoundHealthStatus.Degraded,
                        Description = description,
                        Link = $"diagnostics/workers?WorkerId={worker.Id}"
                    });
                }
            }

            var started = workers.Count(o => o.Status == TrakHoundWorkerStatusType.Started);
            var stopped = workers.Count(o => o.Status == TrakHoundWorkerStatusType.Stopped);

            if (items.Count > 0)
            {
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Degraded, $"{items.Count} of {workers.Count} Workers failing", "diagnostics/workers", items));
            }

            var summary = $"{started} of {workers.Count} Workers Started";
            if (stopped > 0) summary += $" · {stopped} Stopped";
            return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Healthy, summary, "diagnostics/workers"));
        }
    }
}
