// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using System.Text.Json.Serialization;
using TrakHound.Clients;
using TrakHound.Volumes;
using TrakHound.Workers;

namespace TrakHound.ScheduledTasks
{
    /// <summary>
    /// Runs Functions on CRON Schedules : one Engine per enabled Scheduled Task ({id}.json in the Volume),
    /// with the Runs on a shared Run Queue.
    /// Parameters :
    ///   maxConcurrentRuns : Runs at a time across all Tasks (default half the processor count, at least 2)
    ///   overlapPolicy : skip | queue | concurrent, when a Task is due while its previous Run is in progress (default skip)
    ///   maxConcurrentRunsPerTask : Runs at a time for one Task with the concurrent Overlap Policy (default 2)
    /// </summary>
    public class Worker : TrakHoundWorker, IDisposable
    {
        private const string _functionsRoute = "functions";

        // The Worker Status is posted to the TrakHound.ScheduledTasks.Api package (every interval when something changed, and at least every heartbeat)
        private const string _statusRoute = "scheduled-tasks/api/status";
        private static readonly TimeSpan _statusInterval = TimeSpan.FromSeconds(2);
        private static readonly TimeSpan _statusHeartbeat = TimeSpan.FromSeconds(10);

        // How far back to look for a Task's last Run (for RunMissed)
        private static readonly TimeSpan _lastRunLookback = TimeSpan.FromDays(7);
        private const int _lastRunAttempts = 3;
        private static readonly TimeSpan _lastRunRetryDelay = TimeSpan.FromSeconds(10);

        private readonly Dictionary<string, Engine> _engines = new Dictionary<string, Engine>(); // Configuration File ID => Engine
        private readonly Dictionary<string, string> _engineConfigurations = new Dictionary<string, string>(); // Configuration File ID => Configuration JSON
        private readonly SemaphoreSlim _engineLock = new SemaphoreSlim(1, 1);
        private ITrakHoundVolumeListener _configurationListener;
        private EngineQueue _queue;
        private StatusTracker _tracker;
        private CancellationTokenSource _reportStop;
        private Task _reporter;
        private bool _started;


        internal EngineQueue Queue => _queue;

        internal StatusTracker Tracker => _tracker;

        internal IScheduleClock Clock { get; set; } = SystemScheduleClock.Instance;


        public Worker(ITrakHoundWorkerConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume) : base(configuration, client, volume) { }


        protected async override Task OnStartAsync()
        {
            var maxConcurrentRuns = GetIntParameter("maxConcurrentRuns", EngineQueue.GetDefaultMaxConcurrentRuns());
            var overlapPolicy = GetEnumParameter("overlapPolicy", OverlapPolicy.Skip);
            var maxConcurrentRunsPerTask = GetIntParameter("maxConcurrentRunsPerTask", EngineQueue.GetDefaultMaxConcurrentRunsPerTask(overlapPolicy));

            _tracker = new StatusTracker(Clock);
            _queue = new EngineQueue(new ApiFunctionRunner(Client), maxConcurrentRuns, overlapPolicy, maxConcurrentRunsPerTask, Clock, _tracker);
            _queue.LogReceived += EngineLogReceived;
            _queue.Start();

            Log(TrakHoundLogLevel.Information, $"Scheduled Tasks Worker Started : Max Concurrent Runs = {_queue.MaxConcurrentRuns} : Overlap Policy = {_queue.OverlapPolicy} : Max Concurrent Runs per Task = {_queue.MaxConcurrentRunsPerTask}");

            // Missed occurrences are only run when the Worker starts (not when a Task is changed later)
            await LoadConfigurations(runMissed: true);

            _configurationListener = Volume.CreateListener("/");
            _configurationListener.Changed += ConfigurationChanged;
            _configurationListener.Start();

            _started = true;

            _reportStop = new CancellationTokenSource();
            _reporter = Task.Run(() => ReportStatus(_reportStop.Token));
        }

        protected override async Task OnStopAsync()
        {
            _started = false;

            if (_reportStop != null)
            {
                _reportStop.Cancel();
                if (_reporter != null)
                {
                    try { await _reporter; }
                    catch { }
                }
            }

            if (_configurationListener != null)
            {
                _configurationListener.Changed -= ConfigurationChanged;
                _configurationListener.Dispose();
            }

            await StopEngines();

            if (_queue != null)
            {
                await _queue.Stop();
                _queue.LogReceived -= EngineLogReceived;
            }
        }


        #region "Configuration"

        private async void ConfigurationChanged(string path, TrakHoundVolumeOnChangeType changeType)
        {
            // async void : nothing may escape
            try
            {
                Log(TrakHoundLogLevel.Debug, $"Configuration File Changed : {path} => {changeType}");
                if (_started) await LoadConfiguration(path, runMissed: false);
            }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Error, $"Error Loading Configuration : {path} : {ex.Message}");
            }
        }

        private async Task LoadConfigurations(bool runMissed)
        {
            var configurationFilePaths = await Volume.ListFiles();
            if (!configurationFilePaths.IsNullOrEmpty())
            {
                foreach (var configurationFilePath in configurationFilePaths)
                {
                    try
                    {
                        await LoadConfiguration(configurationFilePath, runMissed);
                    }
                    catch (Exception ex)
                    {
                        Log(TrakHoundLogLevel.Error, $"Error Loading Configuration : {configurationFilePath} : {ex.Message}");
                    }
                }
            }
        }

        /// <summary>
        /// Starts, replaces, or stops the Engine for a Configuration File (one at a time, so overlapping change events can't start two Engines)
        /// </summary>
        private async Task LoadConfiguration(string configurationPath, bool runMissed)
        {
            // Engines are keyed by the File ID (deletes only know the File)
            var fileId = Path.GetFileNameWithoutExtension(configurationPath);
            if (string.IsNullOrEmpty(fileId)) return;

            await _engineLock.WaitAsync();
            try
            {
                ScheduledTaskConfiguration configuration = null;
                try { configuration = await ReadConfiguration(fileId); }
                catch (Exception ex) { Log(TrakHoundLogLevel.Warning, $"Error Reading Configuration : {fileId} : {ex.Message}"); }

                if (configuration != null && configuration.Enabled && !string.IsNullOrEmpty(configuration.FunctionId) && !string.IsNullOrEmpty(configuration.Schedule))
                {
                    if (string.IsNullOrEmpty(configuration.Id)) configuration.Id = fileId;
                    if (configuration.Id != fileId)
                    {
                        Log(TrakHoundLogLevel.Warning, $"Scheduled Task ID ({configuration.Id}) doesn't match its File ({fileId}.json) : the File ID is used to stop it when the File changes");
                    }

                    if (configuration.Parameters?.Count > 0)
                    {
                        Log(TrakHoundLogLevel.Warning, $"Scheduled Task Parameters are ignored (Parameters come from a Job) : Task ID = {configuration.Id} : Function ID = {configuration.FunctionId} : Create a Job with these Parameters and schedule the Job instead");
                    }

                    // A save often raises more than one change event : keep the Engine if the Task didn't change
                    var json = JsonSerializer.Serialize(configuration);
                    if (_engines.ContainsKey(fileId) && _engineConfigurations.GetValueOrDefault(fileId) == json) return;

                    Schedule schedule;
                    try
                    {
                        schedule = Schedule.Parse(configuration.Schedule, configuration.TimeZone);
                    }
                    catch (Exception ex)
                    {
                        Log(TrakHoundLogLevel.Error, $"Invalid Scheduled Task : Task ID = {configuration.Id} : Function ID = {configuration.FunctionId} : Schedule = {configuration.Schedule} : Time Zone = {configuration.TimeZone} : {ex.Message}");
                        await RemoveEngine(fileId);
                        _tracker?.TaskInvalid(configuration, ex.Message);
                        return;
                    }

                    Func<CancellationToken, Task<DateTime?>> getLastRun = null;
                    if (runMissed && configuration.RunMissed)
                    {
                        var taskConfiguration = configuration;
                        getLastRun = (cancellationToken) => GetLastRun(taskConfiguration, cancellationToken);
                    }

                    var engine = new Engine(configuration, schedule, Clock, _queue.Enqueue, getLastRun, _tracker);
                    engine.LogReceived += EngineLogReceived;

                    await RemoveEngine(fileId);

                    _engines[fileId] = engine;
                    _engineConfigurations[fileId] = json;
                    engine.Start();
                }
                else
                {
                    // Disabled, Deleted, or Incomplete : Stop the Engine now (instead of at its next occurrence, which may be hours away)
                    var taskId = _engines.GetValueOrDefault(fileId)?.Configuration?.Id ?? fileId;
                    await RemoveEngine(fileId);
                    _tracker?.TaskRemoved(taskId);
                }
            }
            finally
            {
                _engineLock.Release();
            }
        }

        /// <summary>
        /// Stops and removes an Engine (call with the Engine Lock held)
        /// </summary>
        private async Task RemoveEngine(string fileId)
        {
            if (_engines.Remove(fileId, out var engine))
            {
                _engineConfigurations.Remove(fileId);
                await engine.Stop();
                engine.LogReceived -= EngineLogReceived;
            }
        }

        public async Task<ScheduledTaskConfiguration> ReadConfiguration(string configurationId)
        {
            var configurationPath = Path.ChangeExtension(configurationId, "json");
            return await Volume.ReadJson<ScheduledTaskConfiguration>(configurationPath);
        }

        #endregion

        #region "Engines"

        private async Task StopEngines()
        {
            await _engineLock.WaitAsync();
            try
            {
                foreach (var fileId in _engines.Keys.ToList())
                {
                    await RemoveEngine(fileId);
                }
            }
            finally
            {
                _engineLock.Release();
            }
        }

        #endregion

        #region "Status"

        /// <summary>
        /// The Worker Status : the Tasks, Runs, upcoming occurrences, and recent events
        /// </summary>
        internal WorkerStatus GetStatus()
        {
            var status = _tracker.GetSnapshot();
            status.WorkerId = !string.IsNullOrEmpty(Id) ? Id : Configuration?.Id;
            status.InstanceId = InstanceId;
            status.OverlapPolicy = _queue?.OverlapPolicy.ToString().ToLower();
            status.MaxConcurrentRuns = _queue?.MaxConcurrentRuns ?? 0;
            status.MaxConcurrentRunsPerTask = _queue?.MaxConcurrentRunsPerTask ?? 0;
            return status;
        }

        /// <summary>
        /// Posts the Worker Status to the TrakHound.ScheduledTasks.Api package when something changed (and at least every heartbeat)
        /// </summary>
        private async Task ReportStatus(CancellationToken cancellationToken)
        {
            long reportedVersion = -1;
            var reported = DateTime.MinValue;
            var failing = false;

            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    var version = _tracker.Version;
                    var now = Clock.UtcNow;

                    if (version != reportedVersion || now - reported >= _statusHeartbeat)
                    {
                        var response = await Client.Api.Post(_statusRoute, (object)GetStatus(), "application/json", null, cancellationToken);
                        if (response.Success)
                        {
                            reportedVersion = version;
                            reported = now;

                            if (failing) Log(TrakHoundLogLevel.Information, "Worker Status reporting resumed");
                            failing = false;
                        }
                        else if (!failing)
                        {
                            Log(TrakHoundLogLevel.Warning, $"Worker Status not accepted by {_statusRoute} : Status Code {response.StatusCode} (is the TrakHound.ScheduledTasks.Api package installed and up to date?)");
                            failing = true;
                        }
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (!failing) Log(TrakHoundLogLevel.Warning, $"Error Reporting Worker Status : {ex.Message}");
                    failing = true;
                }

                try { await Clock.Delay(_statusInterval, cancellationToken); }
                catch (OperationCanceledException) { break; }
            }
        }

        #endregion

        #region "Missed Runs"

        private class TaskRunSummary
        {
            [JsonPropertyName("id")]
            public string Id { get; set; }

            [JsonPropertyName("lastRun")]
            public DateTime? LastRun { get; set; }
        }

        private class FunctionRunSummary
        {
            [JsonPropertyName("tasks")]
            public List<TaskRunSummary> Tasks { get; set; }
        }

        private class TaskLastRun
        {
            [JsonPropertyName("taskId")]
            public string TaskId { get; set; }

            [JsonPropertyName("lastRun")]
            public DateTime? LastRun { get; set; }
        }

        /// <summary>
        /// The Task's last Run from the TrakHound.Functions.Api package (retried, since the Api may still be starting with the Instance)
        /// </summary>
        private async Task<DateTime?> GetLastRun(ScheduledTaskConfiguration configuration, CancellationToken cancellationToken)
        {
            for (var attempt = 1; attempt <= _lastRunAttempts; attempt++)
            {
                var now = Clock.UtcNow;

                // The last-run route reads only this Task's latest Run (the summary reads every Run and Trigger in the lookback)
                var response = await Client.Api.Get(Url.Combine(_functionsRoute, "tasks", Uri.EscapeDataString(configuration.Id), "last-run"), cancellationToken: cancellationToken);
                if (response.Success)
                {
                    var lastRun = Json.Convert<TaskLastRun>(response.GetContentUtf8String());
                    if (lastRun != null)
                    {
                        // Runs older than the lookback are ignored (as the summary did)
                        var lastRunUtc = lastRun.LastRun?.ToUniversalTime();
                        return lastRunUtc >= now.Subtract(_lastRunLookback) ? lastRunUtc : null;
                    }
                }
                else if (response.StatusCode == 404)
                {
                    // An older TrakHound.Functions.Api without the last-run route (or the Api isn't loaded yet)
                    var summaryLastRun = await GetSummaryLastRun(configuration, now, cancellationToken);
                    if (summaryLastRun.Found) return summaryLastRun.LastRun;
                }

                if (attempt < _lastRunAttempts) await Clock.Delay(_lastRunRetryDelay, cancellationToken);
            }

            throw new Exception("Functions Last Run not available (is the TrakHound.Functions.Api package installed?)");
        }

        /// <summary>
        /// The Task's last Run from the Functions Summary (for a TrakHound.Functions.Api without the last-run route)
        /// </summary>
        private async Task<(bool Found, DateTime? LastRun)> GetSummaryLastRun(ScheduledTaskConfiguration configuration, DateTime now, CancellationToken cancellationToken)
        {
            var queryParameters = new Dictionary<string, string>
            {
                { "functionId", configuration.FunctionId },
                { "start", now.Subtract(_lastRunLookback).ToUnixTime().ToString() },
                { "stop", now.ToUnixTime().ToString() },
                { "bucketCount", "1" }
            };

            var summaries = await Client.Api.GetJson<IEnumerable<FunctionRunSummary>>(Url.Combine(_functionsRoute, "summary"), queryParameters: queryParameters, cancellationToken: cancellationToken);
            if (summaries != null)
            {
                var task = summaries.SelectMany(o => o?.Tasks ?? Enumerable.Empty<TaskRunSummary>()).FirstOrDefault(o => o.Id == configuration.Id);
                return (true, task?.LastRun?.ToUniversalTime());
            }

            return (false, null);
        }

        #endregion

        #region "Parameters"

        private int GetIntParameter(string name, int defaultValue)
        {
            var value = Configuration?.GetParameter(name);
            return int.TryParse(value, out var result) && result > 0 ? result : defaultValue;
        }

        private TEnum GetEnumParameter<TEnum>(string name, TEnum defaultValue) where TEnum : struct, Enum
        {
            var value = Configuration?.GetParameter(name);
            return Enum.TryParse<TEnum>(value, true, out var result) ? result : defaultValue;
        }

        #endregion

        #region "Logging"

        private void EngineLogReceived(object sender, TrakHoundLogLevel level, string message, string code = null)
        {
            Log(level, message, code);
        }

        #endregion
    }
}
