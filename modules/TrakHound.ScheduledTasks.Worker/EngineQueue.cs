// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.Channels;

namespace TrakHound.ScheduledTasks
{
    /// <summary>
    /// Runs due Scheduled Tasks, up to MaxConcurrentRuns at a time (Run Slots).
    /// A Run only becomes Ready when its Task is below MaxConcurrentRunsPerTask, so a Run waiting for its own Task
    /// never holds a Run Slot. The Dispatcher starts Ready Runs in order as Run Slots free up
    /// </summary>
    internal class EngineQueue
    {
        // Log a Warning when a Run waited at least this long for a Run Slot
        internal static readonly TimeSpan SlowStartThreshold = TimeSpan.FromSeconds(5);

        // Log "All Run Slots busy" at most this often
        internal static readonly TimeSpan BusyWarningInterval = TimeSpan.FromMinutes(1);

        private class TaskState
        {
            public int Running;
            public int Ready;
            public readonly Queue<RunRequest> Waiting = new Queue<RunRequest>();

            public int Active => Running + Ready + Waiting.Count;
        }

        internal class RunRequest
        {
            public ScheduledTaskConfiguration Configuration { get; init; }

            /// <summary>
            /// The Run is dropped if it hasn't started by then (the Task's next occurrence)
            /// </summary>
            public DateTime? StaleAfter { get; init; }

            public DateTime Queued { get; init; }
        }

        private readonly IFunctionRunner _runner;
        private readonly IScheduleClock _clock;
        private readonly StatusTracker _tracker;
        private readonly int _maxConcurrentRuns;
        private readonly int _maxConcurrentRunsPerTask;
        private readonly OverlapPolicy _overlapPolicy;
        private readonly Channel<RunRequest> _ready = Channel.CreateUnbounded<RunRequest>(new UnboundedChannelOptions { SingleReader = true });
        private readonly Dictionary<string, TaskState> _tasks = new Dictionary<string, TaskState>(); // Task ID => State
        private readonly HashSet<Task> _runs = new HashSet<Task>();
        private readonly SemaphoreSlim _slots;
        private readonly object _lock = new object();
        private readonly CancellationTokenSource _cts = new CancellationTokenSource();
        private Task _dispatcher;
        private DateTime _lastBusyWarning = DateTime.MinValue;
        private bool _stopped;


        public event LogEventHandler LogReceived;

        public int MaxConcurrentRuns => _maxConcurrentRuns;

        public int MaxConcurrentRunsPerTask => _maxConcurrentRunsPerTask;

        public OverlapPolicy OverlapPolicy => _overlapPolicy;


        /// <param name="maxConcurrentRunsPerTask">Only used with the Concurrent Overlap Policy (Skip and Queue never overlap a Task's Runs)</param>
        /// <param name="tracker">Receives the Runs (for the Worker Status)</param>
        public EngineQueue(IFunctionRunner runner, int maxConcurrentRuns, OverlapPolicy overlapPolicy, int maxConcurrentRunsPerTask = 0, IScheduleClock clock = null, StatusTracker tracker = null)
        {
            _runner = runner;
            _tracker = tracker;
            _clock = clock ?? SystemScheduleClock.Instance;
            _maxConcurrentRuns = Math.Max(1, maxConcurrentRuns);
            _overlapPolicy = overlapPolicy;
            _maxConcurrentRunsPerTask = overlapPolicy == OverlapPolicy.Concurrent ? Math.Clamp(maxConcurrentRunsPerTask > 0 ? maxConcurrentRunsPerTask : GetDefaultMaxConcurrentRunsPerTask(overlapPolicy), 1, _maxConcurrentRuns) : 1;
            _slots = new SemaphoreSlim(_maxConcurrentRuns, _maxConcurrentRuns);
        }

        /// <summary>
        /// Run Slots for the machine (Runs execute in the Instance process)
        /// </summary>
        public static int GetDefaultMaxConcurrentRuns() => Math.Max(2, Environment.ProcessorCount / 2);

        public static int GetDefaultMaxConcurrentRunsPerTask(OverlapPolicy overlapPolicy) => overlapPolicy == OverlapPolicy.Concurrent ? 2 : 1;

        public void Start()
        {
            _dispatcher = Task.Run(() => Dispatch(_cts.Token));
        }

        /// <summary>
        /// Stops taking Runs, cancels the Runs in progress, and waits for them to end
        /// </summary>
        public async Task Stop()
        {
            Task[] runs;
            lock (_lock)
            {
                _stopped = true;
                runs = _runs.ToArray();
            }

            _ready.Writer.TryComplete();
            _cts.Cancel();

            if (_dispatcher != null) await _dispatcher;

            lock (_lock) runs = _runs.ToArray();
            await Task.WhenAll(runs);

            Log(TrakHoundLogLevel.Debug, "Run Queue Stopped");
        }

        /// <summary>
        /// Queues a Run of the Task (false if it was skipped because of the Overlap Policy, or the Queue is stopped)
        /// </summary>
        /// <param name="staleAfter">The Task's next occurrence : the Run is dropped if it hasn't started by then</param>
        public bool Enqueue(ScheduledTaskConfiguration configuration, DateTime? staleAfter = null)
        {
            if (configuration == null || string.IsNullOrEmpty(configuration.Id)) return false;

            lock (_lock)
            {
                if (_stopped) return false;

                var state = GetState(configuration.Id);
                var pending = state.Ready + state.Waiting.Count;

                var skip = _overlapPolicy switch
                {
                    OverlapPolicy.Skip => state.Active > 0,
                    OverlapPolicy.Queue => pending > 0, // One Run may wait behind the Run in progress
                    _ => pending >= _maxConcurrentRunsPerTask // Concurrent : at most a full set waiting
                };

                if (skip)
                {
                    Log(TrakHoundLogLevel.Warning, $"Scheduled Task Skipped : Previous Run still in progress : Task ID = {configuration.Id} : Function ID = {configuration.FunctionId}");
                    _tracker?.Skipped(configuration, "Previous Run still in progress");
                    return false;
                }

                var request = new RunRequest { Configuration = configuration, StaleAfter = staleAfter, Queued = _clock.UtcNow };
                _tracker?.Queued(configuration);

                if (state.Running + state.Ready < _maxConcurrentRunsPerTask)
                {
                    MakeReady(state, request);
                }
                else
                {
                    // Waits for its own Task (without holding a Run Slot)
                    state.Waiting.Enqueue(request);
                }

                return true;
            }
        }

        /// <summary>
        /// The number of Runs waiting or in progress for the Task
        /// </summary>
        public int GetActiveCount(string taskId)
        {
            lock (_lock)
            {
                return _tasks.TryGetValue(taskId, out var state) ? state.Active : 0;
            }
        }

        private TaskState GetState(string taskId)
        {
            if (!_tasks.TryGetValue(taskId, out var state))
            {
                state = new TaskState();
                _tasks.Add(taskId, state);
            }
            return state;
        }

        // Call with the Lock held
        private void MakeReady(TaskState state, RunRequest request)
        {
            state.Ready++;
            _ready.Writer.TryWrite(request);
        }

        // Call with the Lock held : the Task's Run ended (or was dropped), so its next waiting Run may become Ready
        private void ReleaseTask(string taskId, TaskState state)
        {
            if (state.Waiting.Count > 0 && state.Running + state.Ready < _maxConcurrentRunsPerTask)
            {
                MakeReady(state, state.Waiting.Dequeue());
            }

            if (state.Active == 0) _tasks.Remove(taskId);
        }

        private async Task Dispatch(CancellationToken cancellationToken)
        {
            try
            {
                await foreach (var request in _ready.Reader.ReadAllAsync(cancellationToken))
                {
                    // Wait for a free Run Slot (Ready Runs never wait on their own Task, so the order is fair)
                    if (!_slots.Wait(0))
                    {
                        WarnSlotsBusy();
                        await _slots.WaitAsync(cancellationToken);
                    }

                    var configuration = request.Configuration;
                    var now = _clock.UtcNow;

                    lock (_lock)
                    {
                        var state = GetState(configuration.Id);
                        state.Ready--;

                        // Started too late : the next occurrence will run instead (instead of running back to back)
                        if (request.StaleAfter != null && now >= request.StaleAfter.Value)
                        {
                            _slots.Release();
                            ReleaseTask(configuration.Id, state);
                            Log(TrakHoundLogLevel.Warning, $"Scheduled Task Run Dropped : Waited past its next occurrence ({request.StaleAfter.Value.ToISO8601String()}) for a free Run Slot : Task ID = {configuration.Id} : Function ID = {configuration.FunctionId}");
                            _tracker?.Dropped(configuration, $"Waited past its next occurrence ({request.StaleAfter.Value.ToISO8601String()}) for a free Run Slot");
                            continue;
                        }

                        state.Running++;
                    }

                    _tracker?.RunStarted(configuration, now - request.Queued);

                    var waited = now - request.Queued;
                    if (waited >= SlowStartThreshold)
                    {
                        Log(TrakHoundLogLevel.Warning, $"Scheduled Task Run waited {waited.ToFormattedString(true)} for a free Run Slot : Task ID = {configuration.Id} : Function ID = {configuration.FunctionId} : Max Concurrent Runs = {_maxConcurrentRuns}");
                    }

                    var run = RunTask(request, waited, cancellationToken);
                    lock (_lock)
                    {
                        if (!run.IsCompleted) _runs.Add(run);
                    }
                }
            }
            catch (OperationCanceledException) { }
        }

        private void WarnSlotsBusy()
        {
            int waiting;
            lock (_lock)
            {
                var now = _clock.UtcNow;
                if (now - _lastBusyWarning < BusyWarningInterval) return;
                _lastBusyWarning = now;

                waiting = _tasks.Values.Sum(o => o.Ready + o.Waiting.Count);
            }

            Log(TrakHoundLogLevel.Warning, $"All {_maxConcurrentRuns} Run Slots busy : {waiting} Runs waiting (increase the maxConcurrentRuns parameter if this continues)");
        }

        private async Task RunTask(RunRequest request, TimeSpan waited, CancellationToken cancellationToken)
        {
            // Continue on the Thread Pool (so the Dispatcher isn't held by a synchronous Function)
            await Task.Yield();

            var configuration = request.Configuration;
            var taskName = $"Task ID = {configuration.Id} : Function ID = {configuration.FunctionId}";
            var stpw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                Log(TrakHoundLogLevel.Debug, $"Run Started : {taskName} : Waited = {waited.ToFormattedString(true)}");

                var result = await _runner.Run(configuration, cancellationToken);

                stpw.Stop();
                if (result != null && result.Success)
                {
                    Log(TrakHoundLogLevel.Debug, $"Run Completed : {taskName} : Run ID = {result.RunId} : Status Code = {result.StatusCode} : in {stpw.Elapsed.ToFormattedString(true)}");
                    _tracker?.RunEnded(configuration, StatusEventTypes.Completed, result.StatusCode, stpw.Elapsed, result.Message);
                }
                else
                {
                    Log(TrakHoundLogLevel.Error, $"Scheduled Task Run Failed : {taskName} : Status Code = {result?.StatusCode} : {result?.Message}");
                    _tracker?.RunEnded(configuration, StatusEventTypes.Failed, result?.StatusCode, stpw.Elapsed, result?.Message ?? "No Response");
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                Log(TrakHoundLogLevel.Information, $"Scheduled Task Run Cancelled (Worker Stopping) : {taskName}");
                _tracker?.RunEnded(configuration, StatusEventTypes.Cancelled, null, stpw.Elapsed, "Worker Stopping");
            }
            catch (Exception ex)
            {
                // A failed Run never stops the Queue (or blocks the Task's later Runs)
                Log(TrakHoundLogLevel.Error, $"Scheduled Task Run Error : {taskName} : {ex.Message}");
                _tracker?.RunEnded(configuration, StatusEventTypes.Failed, null, stpw.Elapsed, ex.Message);
            }
            finally
            {
                lock (_lock)
                {
                    var state = GetState(configuration.Id);
                    state.Running--;
                    ReleaseTask(configuration.Id, state);

                    _runs.RemoveWhere(o => o.IsCompleted);
                }

                _slots.Release();
            }
        }

        private void Log(TrakHoundLogLevel level, string message, string code = null)
        {
            LogReceived?.Invoke(this, level, message, code);
        }
    }
}
