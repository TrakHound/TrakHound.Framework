// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;

namespace TrakHound.Instance.Health.Checks
{
    /// <summary>
    /// The rate of Error and Critical Log entries. A steady stream of errors usually means something is failing on every attempt
    /// (ex. a Worker loop or a scheduled task), even when no other Check covers it.
    /// </summary>
    public class LogsHealthCheck : ITrakHoundHealthCheck
    {
        private const double _errorsPerMinute = 10;
        private const int _maxItems = 5;

        private readonly TrakHoundLogErrorTracker _tracker;


        public string Id => "logs";

        public string Title => "Logs";


        public LogsHealthCheck(TrakHoundLogErrorTracker tracker)
        {
            _tracker = tracker;
        }


        public Task<TrakHoundHealthCheckResult> Evaluate(TrakHoundHealthContext context)
        {
            var loggers = _tracker.GetErrors(context.Now).ToList();
            var total = loggers.Sum(o => o.Count);
            var minutes = TrakHoundLogErrorTracker.Window.TotalMinutes;
            var rate = total / minutes;
            var windowText = $"the last {minutes:0} minutes";

            if (total == 0)
            {
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Healthy, $"No errors logged in {windowText}", "diagnostics/logs/view"));
            }

            // The Loggers with the most errors (each is a Log in the Explorer)
            var items = loggers.Take(_maxItems).Select(o => new TrakHoundHealthCheckItem
            {
                Id = o.Logger,
                Name = o.Logger,
                Status = rate >= _errorsPerMinute ? TrakHoundHealthStatus.Degraded : TrakHoundHealthStatus.Healthy,
                Description = $"{o.Count:N0} errors in {windowText}, last {o.LastError.ToLocalTime():HH:mm:ss} : {o.LastMessage}",
                Link = "diagnostics/logs/view"
            }).ToList();

            var description = $"{total:N0} errors in {windowText} ({rate:0.#} per minute) from {loggers.Count} Logs";

            if (rate >= _errorsPerMinute)
            {
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Degraded, description, "diagnostics/logs/view", items));
            }

            return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Healthy, description, "diagnostics/logs/view", items));
        }
    }
}
