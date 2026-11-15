// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Logging;

namespace TrakHound.Workers
{
    /// <summary>
    /// Collects Starts, Restarts, Errors, Uptime and Logged Warnings / Errors for each Worker from its Status and Log events.
    /// Kept by Worker ID, so totals continue when a Worker's Engine is replaced (ex. its Configuration changed).
    /// The most recent Status changes are kept (see <see cref="MaxRecentEvents"/>).
    /// </summary>
    public class TrakHoundWorkerMetricsCollector
    {
        public const int MaxRecentEvents = 50;
        private const int _maxMessageLength = 500;

        // An Error Log Item this soon after an Error Status is used as that Status' message
        private static readonly TimeSpan _errorMessageWindow = TimeSpan.FromSeconds(5);

        private readonly Dictionary<string, WorkerState> _workers = new Dictionary<string, WorkerState>(); // Worker ID (lowercase) => State
        private readonly object _lock = new object();


        private class WorkerState
        {
            public string WorkerId;
            public string ConfigurationId;
            public string Name;
            public string PackageId;
            public string PackageVersion;
            public TrakHoundWorkerStatusType Status = TrakHoundWorkerStatusType.Stopped;
            public DateTime? StatusChanged;
            public long Starts;
            public long Restarts;
            public long Errors;
            public long WarningLogs;
            public long ErrorLogs;
            public DateTime? RunningSince;
            public double CompletedUptime;
            public DateTime? FirstStarted;
            public DateTime? LastStarted;
            public DateTime? LastStopped;
            public DateTime? LastError;
            public string LastErrorMessage;
            public readonly LinkedList<TrakHoundWorkerStatusEvent> Events = new LinkedList<TrakHoundWorkerStatusEvent>(); // Newest first
        }


        /// <summary>
        /// Set the Configuration details of a Worker (called when the Worker is loaded)
        /// </summary>
        public void Register(string workerId, string configurationId, string name, string packageId, string packageVersion)
        {
            if (string.IsNullOrEmpty(workerId)) return;

            lock (_lock)
            {
                var state = GetOrAdd(workerId);
                state.ConfigurationId = configurationId;
                state.Name = name;
                state.PackageId = packageId;
                state.PackageVersion = packageVersion;
            }
        }

        public void StatusReceived(string workerId, TrakHoundWorkerStatusType status)
        {
            if (string.IsNullOrEmpty(workerId)) return;

            var now = DateTime.UtcNow;

            lock (_lock)
            {
                var state = GetOrAdd(workerId);
                var previous = state.Status;

                // Workers may report the same Status more than once
                if (status == previous && state.StatusChanged != null) return;

                switch (status)
                {
                    case TrakHoundWorkerStatusType.Starting:
                        // The Engine only Starts again after an Error while the Worker was running (or failing to Start)
                        if (previous == TrakHoundWorkerStatusType.Error || previous == TrakHoundWorkerStatusType.Started) state.Restarts++;
                        break;

                    case TrakHoundWorkerStatusType.Started:
                        state.Starts++;
                        state.RunningSince = now;
                        state.FirstStarted ??= now;
                        state.LastStarted = now;
                        break;

                    case TrakHoundWorkerStatusType.Error:
                        state.Errors++;
                        state.LastError = now;
                        EndRun(state, now);
                        break;

                    case TrakHoundWorkerStatusType.Stopping:
                        EndRun(state, now);
                        break;

                    case TrakHoundWorkerStatusType.Stopped:
                        EndRun(state, now);
                        state.LastStopped = now;
                        break;
                }

                state.Status = status;
                state.StatusChanged = now;

                state.Events.AddFirst(new TrakHoundWorkerStatusEvent { Timestamp = now, Status = status });
                while (state.Events.Count > MaxRecentEvents) state.Events.RemoveLast();
            }
        }

        public void LogReceived(string workerId, TrakHoundLogItem item)
        {
            if (string.IsNullOrEmpty(workerId)) return;

            var isError = item.LogLevel == TrakHoundLogLevel.Critical || item.LogLevel == TrakHoundLogLevel.Error;
            var isWarning = item.LogLevel == TrakHoundLogLevel.Warning;
            if (!isError && !isWarning) return;

            var now = DateTime.UtcNow;

            lock (_lock)
            {
                var state = GetOrAdd(workerId);

                if (isWarning)
                {
                    state.WarningLogs++;
                    return;
                }

                state.ErrorLogs++;
                state.LastError = now;
                state.LastErrorMessage = GetFirstLine(item.Message);

                // Use as the message of the Error Status it accompanies
                var latest = state.Events.First?.Value;
                if (latest != null && latest.Status == TrakHoundWorkerStatusType.Error && latest.Message == null && now - latest.Timestamp <= _errorMessageWindow)
                {
                    latest.Message = state.LastErrorMessage;
                }
            }
        }


        /// <summary>
        /// Gets the Metrics for each Worker (Recent Events are not included). CPU values are added by the Worker Manager.
        /// </summary>
        public IEnumerable<TrakHoundWorkerMetrics> GetMetrics()
        {
            var now = DateTime.UtcNow;

            lock (_lock)
            {
                return _workers.Values.Select(o => CreateMetrics(o, now, false)).ToList();
            }
        }

        /// <summary>
        /// Gets the Metrics, including the Recent Events, for the specified Worker (null if it is unknown)
        /// </summary>
        public TrakHoundWorkerMetrics GetMetrics(string workerId)
        {
            if (string.IsNullOrEmpty(workerId)) return null;

            var now = DateTime.UtcNow;

            lock (_lock)
            {
                return _workers.TryGetValue(workerId.ToLower(), out var state) ? CreateMetrics(state, now, true) : null;
            }
        }


        private WorkerState GetOrAdd(string workerId)
        {
            var key = workerId.ToLower();
            if (!_workers.TryGetValue(key, out var state))
            {
                state = new WorkerState { WorkerId = workerId };
                _workers.Add(key, state);
            }

            return state;
        }

        private static void EndRun(WorkerState state, DateTime now)
        {
            if (state.RunningSince != null)
            {
                state.CompletedUptime += Math.Max(0, (now - state.RunningSince.Value).TotalMilliseconds);
                state.RunningSince = null;
            }
        }

        private static TrakHoundWorkerMetrics CreateMetrics(WorkerState state, DateTime now, bool includeEvents)
        {
            var currentUptime = state.RunningSince != null ? Math.Max(0, (now - state.RunningSince.Value).TotalMilliseconds) : 0;
            var totalUptime = state.CompletedUptime + currentUptime;
            var sinceFirstStarted = state.FirstStarted != null ? (now - state.FirstStarted.Value).TotalMilliseconds : 0;

            var metrics = new TrakHoundWorkerMetrics();
            metrics.WorkerId = state.WorkerId;
            metrics.ConfigurationId = state.ConfigurationId;
            metrics.Name = state.Name;
            metrics.PackageId = state.PackageId;
            metrics.PackageVersion = state.PackageVersion;
            metrics.Status = state.Status;
            metrics.StatusChanged = state.StatusChanged;
            metrics.TotalStartCount = state.Starts;
            metrics.TotalRestartCount = state.Restarts;
            metrics.TotalErrorCount = state.Errors;
            metrics.TotalWarningLogCount = state.WarningLogs;
            metrics.TotalErrorLogCount = state.ErrorLogs;
            metrics.CurrentUptime = Math.Round(currentUptime, 0);
            metrics.TotalUptime = Math.Round(totalUptime, 0);
            metrics.Availability = sinceFirstStarted > 0 ? Math.Round(Math.Min(100, totalUptime / sinceFirstStarted * 100), 2) : 0;
            metrics.FirstStarted = state.FirstStarted;
            metrics.LastStarted = state.LastStarted;
            metrics.LastStopped = state.LastStopped;
            metrics.LastError = state.LastError;
            metrics.LastErrorMessage = state.LastErrorMessage;

            if (includeEvents)
            {
                metrics.RecentEvents = state.Events
                    .Select(o => new TrakHoundWorkerStatusEvent { Timestamp = o.Timestamp, Status = o.Status, Message = o.Message })
                    .ToList();
            }

            return metrics;
        }

        private static string GetFirstLine(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;

            var line = message.Split('\n', 2)[0].TrimEnd('\r');
            return line.Length > _maxMessageLength ? line.Substring(0, _maxMessageLength) + "..." : line;
        }
    }
}
