// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Functions
{
    /// <summary>
    /// Collects Run Counts, Results and Durations for each Function since the Instance started.
    /// The most recent Runs of each Function are kept (see <see cref="MaxRecentRuns"/>).
    /// </summary>
    public class TrakHoundFunctionMetricsCollector
    {
        public const int MaxRecentRuns = 50;
        public const int CancelledStatusCode = 499;

        private readonly Dictionary<string, FunctionState> _functions = new Dictionary<string, FunctionState>(); // Function ID (lowercase) => State
        private readonly object _lock = new object();


        private class RunState
        {
            public string RunId;
            public DateTime Started;
            public DateTime? Completed;
            public int StatusCode;
            public double Duration;
            public double CpuTime;
            public double CpuUsage;
            public string Message;
        }

        private class FunctionState
        {
            public string FunctionId;
            public string Name;
            public string PackageId;
            public string PackageVersion;
            public long Runs;
            public long Successes;
            public long Errors;
            public long Cancelled;
            public double TotalDuration;
            public double MinDuration;
            public double MaxDuration;
            public double TotalCpuTime;
            public double MaxCpuTime;
            public RunState Last;
            public DateTime? LastStarted;
            public DateTime? LastSuccess;
            public DateTime? LastError;
            public string LastErrorMessage;
            public readonly Dictionary<string, RunState> Active = new Dictionary<string, RunState>(); // Run ID => State
            public readonly LinkedList<RunState> Recent = new LinkedList<RunState>(); // Newest first
        }


        /// <summary>
        /// Record that a Function Run started
        /// </summary>
        public void RunStarted(string functionId, string name, string packageId, string packageVersion, string runId, DateTime started)
        {
            if (string.IsNullOrEmpty(functionId) || string.IsNullOrEmpty(runId)) return;

            lock (_lock)
            {
                var function = GetOrAddFunction(functionId);
                function.Name = name;
                function.PackageId = packageId;
                function.PackageVersion = packageVersion;
                function.LastStarted = started;
                function.Active[runId] = new RunState { RunId = runId, Started = started };
            }
        }

        /// <summary>
        /// Record the Result of a Function Run that was started with <see cref="RunStarted"/>
        /// </summary>
        public void RunCompleted(string functionId, string runId, int statusCode, string message, DateTime completed, TimeSpan cpuTime = default)
        {
            if (string.IsNullOrEmpty(functionId) || string.IsNullOrEmpty(runId)) return;

            lock (_lock)
            {
                if (!_functions.TryGetValue(functionId.ToLower(), out var function)) return;
                if (!function.Active.Remove(runId, out var run)) return;

                run.Completed = completed;
                run.StatusCode = statusCode;
                run.Duration = Math.Max(0, (completed - run.Started).TotalMilliseconds);
                run.Message = message;
                run.CpuTime = Math.Max(0, cpuTime.TotalMilliseconds);
                run.CpuUsage = GetCpuUsage(run.CpuTime, run.Duration);

                function.MinDuration = function.Runs > 0 ? Math.Min(function.MinDuration, run.Duration) : run.Duration;
                function.MaxDuration = Math.Max(function.MaxDuration, run.Duration);
                function.TotalDuration += run.Duration;
                function.TotalCpuTime += run.CpuTime;
                function.MaxCpuTime = Math.Max(function.MaxCpuTime, run.CpuTime);
                function.Runs++;

                if (statusCode >= 200 && statusCode < 300)
                {
                    function.Successes++;
                    function.LastSuccess = completed;
                }
                else if (statusCode == CancelledStatusCode)
                {
                    function.Cancelled++;
                }
                else
                {
                    function.Errors++;
                    function.LastError = completed;
                    function.LastErrorMessage = message;
                }

                function.Last = run;

                function.Recent.AddFirst(run);
                while (function.Recent.Count > MaxRecentRuns) function.Recent.RemoveLast();
            }
        }


        /// <summary>
        /// Gets the Metrics for each Function that has been Run (Recent Runs are not included)
        /// </summary>
        public IEnumerable<TrakHoundFunctionMetrics> GetMetrics()
        {
            var now = DateTime.UtcNow;

            lock (_lock)
            {
                return _functions.Values.Select(o => CreateMetrics(o, now, false)).ToList();
            }
        }

        /// <summary>
        /// Gets the Metrics, including the Recent Runs, for the specified Function (null if it has not been Run)
        /// </summary>
        public TrakHoundFunctionMetrics GetMetrics(string functionId)
        {
            if (string.IsNullOrEmpty(functionId)) return null;

            var now = DateTime.UtcNow;

            lock (_lock)
            {
                return _functions.TryGetValue(functionId.ToLower(), out var function) ? CreateMetrics(function, now, true) : null;
            }
        }


        private FunctionState GetOrAddFunction(string functionId)
        {
            var key = functionId.ToLower();
            if (!_functions.TryGetValue(key, out var function))
            {
                function = new FunctionState { FunctionId = functionId };
                _functions.Add(key, function);
            }

            return function;
        }

        private static TrakHoundFunctionMetrics CreateMetrics(FunctionState function, DateTime now, bool includeRuns)
        {
            var metrics = new TrakHoundFunctionMetrics();
            metrics.FunctionId = function.FunctionId;
            metrics.Name = function.Name;
            metrics.PackageId = function.PackageId;
            metrics.PackageVersion = function.PackageVersion;
            metrics.TotalRunCount = function.Runs;
            metrics.TotalSuccessCount = function.Successes;
            metrics.TotalErrorCount = function.Errors;
            metrics.TotalCancelledCount = function.Cancelled;
            metrics.ActiveRunCount = function.Active.Count;
            metrics.TotalDuration = Math.Round(function.TotalDuration, 4);
            metrics.AverageDuration = function.Runs > 0 ? Math.Round(function.TotalDuration / function.Runs, 4) : 0;
            metrics.MinDuration = Math.Round(function.MinDuration, 4);
            metrics.MaxDuration = Math.Round(function.MaxDuration, 4);
            metrics.TotalCpuTime = Math.Round(function.TotalCpuTime, 4);
            metrics.AverageCpuTime = function.Runs > 0 ? Math.Round(function.TotalCpuTime / function.Runs, 4) : 0;
            metrics.MaxCpuTime = Math.Round(function.MaxCpuTime, 4);
            metrics.AverageCpuUsage = Math.Round(GetCpuUsage(function.TotalCpuTime, function.TotalDuration), 4);
            metrics.LastStarted = function.LastStarted;
            metrics.LastCompleted = function.Last?.Completed;
            metrics.LastStatusCode = function.Last?.StatusCode ?? 0;
            metrics.LastDuration = Math.Round(function.Last?.Duration ?? 0, 4);
            metrics.LastSuccess = function.LastSuccess;
            metrics.LastError = function.LastError;
            metrics.LastErrorMessage = function.LastErrorMessage;

            if (includeRuns)
            {
                // Running first (newest first), then the Completed Runs
                var running = function.Active.Values
                    .OrderByDescending(o => o.Started)
                    .Select(o => CreateRunMetrics(o, Math.Max(0, (now - o.Started).TotalMilliseconds)));

                var completed = function.Recent.Select(o => CreateRunMetrics(o, o.Duration));

                metrics.RecentRuns = running.Concat(completed).ToList();
            }

            return metrics;
        }

        /// <summary>
        /// Percent of total machine capacity (0 - 100) used over the duration
        /// </summary>
        private static double GetCpuUsage(double cpuTime, double duration)
        {
            if (duration <= 0) return 0;
            return Math.Clamp(cpuTime / (duration * Environment.ProcessorCount) * 100, 0, 100);
        }

        private static TrakHoundFunctionRunMetrics CreateRunMetrics(RunState run, double duration)
        {
            return new TrakHoundFunctionRunMetrics
            {
                RunId = run.RunId,
                Started = run.Started,
                Completed = run.Completed,
                StatusCode = run.StatusCode,
                Duration = Math.Round(duration, 4),
                CpuTime = Math.Round(run.CpuTime, 4),
                CpuUsage = Math.Round(run.CpuUsage, 4),
                Message = run.Message
            };
        }
    }
}
