// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients;
using TrakHound.Logging;

namespace TrakHound.Workers
{
    public class TrakHoundWorkerEngine : IAsyncDisposable
    {
        private const string _logName = "Engine";
        private const int _startDelay = 250;
        private static readonly TimeSpan _minimumRestartInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan _maximumRestartInterval = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan _stableRunDuration = TimeSpan.FromMinutes(1);
        private static readonly TimeSpan _stopTimeout = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan _cpuSampleInterval = TimeSpan.FromSeconds(1);

        /// <summary>
        /// How long CPU Usage history is kept for
        /// </summary>
        public static readonly TimeSpan CpuHistoryDuration = TimeSpan.FromMinutes(10);

        private readonly string _engineId;
        private readonly string _packageId;
        private readonly string _packageVersion;
        private readonly ITrakHoundWorker _worker;
        private readonly ITrakHoundClient _client;
        private readonly TrakHoundCpuMonitor _cpuMonitor; // Null when CPU Monitoring is disabled
        private readonly Queue<TrakHoundWorkerCpuSample> _cpuHistory = new Queue<TrakHoundWorkerCpuSample>();
        private readonly object _lock = new object();
        private CancellationTokenSource _stop;
        private CancellationTokenSource _restart;
        private Task _runTask;
        private volatile TrakHoundWorkerStatusType _status;
        private bool _disposed;


        public string EngineId => _engineId;

        public string PackageId => _packageId;

        public string PackageVersion => _packageVersion;

        public ITrakHoundWorker Worker => _worker;

        public ITrakHoundClient Client => _client;

        public TrakHoundWorkerStatusType Status => _status;

        /// <summary>
        /// The CPU usage of the Worker (percent of total machine capacity, 0 - 100)
        /// </summary>
        public double CpuUsage => _cpuMonitor?.Usage ?? 0;

        /// <summary>
        /// The total CPU time consumed by the Worker
        /// </summary>
        public TimeSpan TotalProcessorTime => _cpuMonitor?.TotalProcessorTime ?? TimeSpan.Zero;

        /// <summary>
        /// Whether the Worker's CPU usage is measured
        /// </summary>
        public bool CpuMonitoringEnabled => _cpuMonitor != null;


        public event TrakHoundWorkerStatusHandler StatusReceived;

        public event TrakHoundWorkerLogHandler LogReceived;


        /// <param name="parentCpuMonitor">The Worker's Monitor in TrakHoundModuleCpuMonitors (so its time is included in the Instance's CPU by Module)</param>
        public TrakHoundWorkerEngine(ITrakHoundWorker worker, string packageId, string packageVersion, bool monitorCpu = true, TrakHoundCpuMonitor parentCpuMonitor = null)
        {
            if (monitorCpu) _cpuMonitor = new TrakHoundCpuMonitor(parentCpuMonitor);

            _worker = worker;

            if (worker != null)
            {
                _engineId = worker.Id;
                _packageId = packageId;
                _packageVersion = packageVersion;
                _client = worker.Client;

                worker.StatusReceived += WorkerStatusReceived;
                worker.LogReceived += WorkerLogReceived;
            }
        }

        /// <summary>
        /// Stop the Engine and Dispose of the Worker. The Engine cannot be restarted after this.
        /// </summary>
        public async ValueTask DisposeAsync()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            var stopped = await Stop();

            if (_worker != null)
            {
                _worker.StatusReceived -= WorkerStatusReceived;
                _worker.LogReceived -= WorkerLogReceived;

                if (stopped)
                {
                    await DisposeWorker();
                }
                else
                {
                    // The Worker is still stuck in Start or Stop : Dispose once it returns instead of blocking the caller
                    var runTask = _runTask;
                    _ = runTask.ContinueWith(async o => await DisposeWorker(), TaskScheduler.Default).Unwrap();
                }
            }
        }


        public void Start()
        {
            if (_worker == null) return;

            CancellationToken token;

            lock (_lock)
            {
                if (_disposed) return;

                // Only start from a fully stopped state (a previous Run must have completed)
                if (_runTask != null && !_runTask.IsCompleted) return;
            }

            if (_client == null)
            {
                SetStatus(TrakHoundWorkerStatusType.Error);
                Log(TrakHoundLogLevel.Error, "Worker Start ERROR : No Client is available");
                return;
            }

            lock (_lock)
            {
                if (_disposed || (_runTask != null && !_runTask.IsCompleted)) return;

                _stop?.Dispose();
                _stop = new CancellationTokenSource();

                // Each Run keeps its own Token so a later Start cannot affect it
                token = _stop.Token;
                _status = TrakHoundWorkerStatusType.Starting;
                _runTask = Task.Run(() => Run(token));
            }

            // Raise events outside of the lock
            SetStatus(TrakHoundWorkerStatusType.Starting, "Worker Starting..");
        }

        /// <summary>
        /// Stop the Worker. Returns once the Engine has fully Stopped, or false if the Worker did not Stop within the timeout.
        /// </summary>
        public async Task<bool> Stop()
        {
            Task runTask;
            CancellationTokenSource stop;

            lock (_lock)
            {
                if (_runTask == null || _runTask.IsCompleted) return true;

                runTask = _runTask;
                stop = _stop;
            }

            if (!stop.IsCancellationRequested)
            {
                SetStatus(TrakHoundWorkerStatusType.Stopping, "Worker Stopping..");

                // The Run loop Stops the Worker (so that a Stop can never overlap a Start)
                stop.Cancel();
            }

            var completed = await Task.WhenAny(runTask, Task.Delay(_stopTimeout)) == runTask;
            if (!completed)
            {
                Log(TrakHoundLogLevel.Warning, $"Worker Stop TIMEOUT : The Worker did not Stop within {_stopTimeout.TotalSeconds}s. It will finish Stopping in the background.");
            }

            return completed;
        }

        private async Task Run(CancellationToken token)
        {
            var restartInterval = _minimumRestartInterval;

            try
            {
                await Task.Delay(_startDelay, token);

                while (!token.IsCancellationRequested)
                {
                    using var restart = CancellationTokenSource.CreateLinkedTokenSource(token);
                    lock (_lock) _restart = restart;

                    var started = false;
                    var startedTimestamp = DateTime.UtcNow;

                    try
                    {
                        _cpuMonitor?.ResetUsage();

                        // Start the Worker (a Stop requested during this waits for it to return)
                        await StartWorker();
                        started = true;
                        startedTimestamp = DateTime.UtcNow;

                        if (!token.IsCancellationRequested)
                        {
                            SetStatus(TrakHoundWorkerStatusType.Started, "Worker Started");
                        }

                        // Run until Stopped, or until the Worker reports an Error
                        using var timer = new PeriodicTimer(_cpuSampleInterval);
                        while (await timer.WaitForNextTickAsync(restart.Token))
                        {
                            // The Timer sets the interval, so always recalculate using the actual elapsed time
                            if (_cpuMonitor != null && _cpuMonitor.Sample(0)) AddCpuSample(_cpuMonitor.Usage);
                        }
                    }
                    catch (OperationCanceledException) when (restart.IsCancellationRequested) { }
                    catch (Exception ex)
                    {
                        SetStatus(TrakHoundWorkerStatusType.Error);
                        LogException(ex);
                    }
                    finally
                    {
                        lock (_lock) _restart = null;
                    }

                    // Always Stop what was Started (or partially Started) before Restarting or exiting
                    await StopWorkerSafe(started);

                    if (!token.IsCancellationRequested)
                    {
                        // Only reset the Back off once the Worker has run successfully for a while
                        if (started && DateTime.UtcNow - startedTimestamp >= _stableRunDuration) restartInterval = _minimumRestartInterval;

                        Log(TrakHoundLogLevel.Information, $"Worker Restarting in {restartInterval.TotalSeconds}s");
                        await Task.Delay(restartInterval, token);

                        // Back off when the Worker keeps failing (reset after a successful Start)
                        restartInterval = TimeSpan.FromTicks(Math.Min(restartInterval.Ticks * 2, _maximumRestartInterval.Ticks));

                        SetStatus(TrakHoundWorkerStatusType.Starting, "Worker Starting..");
                    }
                }
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { }
            catch (Exception ex)
            {
                LogException(ex);
            }
            finally
            {
                // Always report Stopped, however the loop ends
                if (_cpuMonitor != null)
                {
                    _cpuMonitor.ResetUsage();
                    AddCpuSample(0); // Show the drop to zero when Stopped
                }

                SetStatus(TrakHoundWorkerStatusType.Stopped, "Worker Stopped");
            }
        }

        private async Task StopWorkerSafe(bool started)
        {
            try
            {
                await StopWorker();
            }
            catch (Exception ex)
            {
                var action = started ? "Stop" : "Cleanup after failed Start";
                Log(TrakHoundLogLevel.Error, $"Worker {action} ERROR : {TrakHoundLogger.GetExceptionStackTrace(ex)}");
            }
        }

        private async Task DisposeWorker()
        {
            try
            {
                if (_worker is IAsyncDisposable asyncDisposable) await asyncDisposable.DisposeAsync();
                else _worker.Dispose();
            }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Error, $"Worker Dispose ERROR : {TrakHoundLogger.GetExceptionStackTrace(ex)}");
            }

            lock (_lock) _stop?.Dispose();
        }


        /// <summary>
        /// Get the CPU Usage history (oldest first). If 'from' is specified, only samples after that time (UTC) are returned.
        /// </summary>
        public IEnumerable<TrakHoundWorkerCpuSample> GetCpuHistory(DateTime? from = null)
        {
            lock (_lock)
            {
                if (from.HasValue) return _cpuHistory.Where(o => o.Timestamp > from.Value).ToList();
                return _cpuHistory.ToList();
            }
        }

        private void AddCpuSample(double usage)
        {
            var now = DateTime.UtcNow;

            lock (_lock)
            {
                _cpuHistory.Enqueue(new TrakHoundWorkerCpuSample(now, usage));
                while (_cpuHistory.Count > 0 && now - _cpuHistory.Peek().Timestamp > CpuHistoryDuration) _cpuHistory.Dequeue();
            }
        }

        // The Worker's code (and anything it starts) is attributed to the CPU Monitor, the Engine's is not
        private Task StartWorker() => _cpuMonitor != null ? _cpuMonitor.Run(() => _worker.Start()) : _worker.Start();

        private Task StopWorker() => _cpuMonitor != null ? _cpuMonitor.Run(() => _worker.Stop()) : _worker.Stop();


        private void SetStatus(TrakHoundWorkerStatusType status, string message = null)
        {
            _status = status;
            RaiseStatus(status);

            if (message != null) Log(TrakHoundLogLevel.Information, message);
        }

        private void Log(TrakHoundLogLevel level, string message, string code = null)
        {
            var item = code != null
                ? new TrakHoundLogItem(_logName, level, message, code)
                : new TrakHoundLogItem(_logName, level, message);

            RaiseLog(item);
        }

        private void LogException(Exception ex)
        {
            Log(TrakHoundLogLevel.Critical, TrakHoundLogger.GetExceptionStackTrace(ex), ex.HResult.ToString());
        }

        // Subscriber exceptions must not affect the Worker (ex. be treated as a Worker failure and cause a Restart)
        private void RaiseStatus(TrakHoundWorkerStatusType status)
        {
            try
            {
                StatusReceived?.Invoke(_engineId, status);
            }
            catch { }
        }

        private void RaiseLog(TrakHoundLogItem item)
        {
            try
            {
                LogReceived?.Invoke(_engineId, item);
            }
            catch { }
        }

        private void WorkerStatusReceived(object sender, TrakHoundWorkerStatusType status)
        {
            _status = status;
            RaiseStatus(status);

            if (status == TrakHoundWorkerStatusType.Error)
            {
                // Restart the Worker (if it is currently running)
                CancellationTokenSource restart;
                lock (_lock) restart = _restart;

                try
                {
                    restart?.Cancel();
                }
                catch (ObjectDisposedException) { }
            }
        }

        private void WorkerLogReceived(object sender, TrakHoundLogItem item)
        {
            RaiseLog(item);
        }
    }
}
