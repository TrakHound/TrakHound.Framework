// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.ScheduledTasks
{
    internal delegate void LogEventHandler(object sender, TrakHoundLogLevel loglevel, string message, string code = null);

    /// <summary>
    /// Waits for each occurrence of a Scheduled Task's Schedule and passes the Task to the Run Queue.
    /// The Worker replaces the Engine when the Task's configuration changes (so the file isn't re-read here)
    /// </summary>
    internal class Engine
    {
        // Wake at least this often while waiting (so system clock changes are picked up)
        internal static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(1);

        // Occurrences more recent than this when the Worker starts are left to the normal schedule (not run as Missed)
        internal static readonly TimeSpan MissedGracePeriod = TimeSpan.FromMinutes(1);

        private readonly ScheduledTaskConfiguration _configuration;
        private readonly Schedule _schedule;
        private readonly IScheduleClock _clock;
        private readonly Func<ScheduledTaskConfiguration, DateTime?, bool> _onDue;
        private readonly Func<CancellationToken, Task<DateTime?>> _getLastRun;
        private readonly StatusTracker _tracker;
        private CancellationTokenSource _stop;
        private Task _task;


        public event LogEventHandler LogReceived;

        public ScheduledTaskConfiguration Configuration => _configuration;


        /// <param name="onDue">Called for each occurrence with the next occurrence (a Run that hasn't started by then is stale). Returns false if the Run was skipped</param>
        /// <param name="getLastRun">When set, gets the Task's last Run so a missed occurrence runs once at start (RunMissed)</param>
        /// <param name="tracker">Receives the occurrences of the Task (for the Worker Status)</param>
        public Engine(ScheduledTaskConfiguration configuration, Schedule schedule, IScheduleClock clock, Func<ScheduledTaskConfiguration, DateTime?, bool> onDue, Func<CancellationToken, Task<DateTime?>> getLastRun = null, StatusTracker tracker = null)
        {
            _tracker = tracker;
            _configuration = configuration;
            _schedule = schedule;
            _clock = clock;
            _onDue = onDue;
            _getLastRun = getLastRun;
        }

        public void Start()
        {
            _stop = new CancellationTokenSource();
            _task = Task.Run(() => Worker(_stop.Token));
        }

        /// <summary>
        /// Stops the Engine and waits for it to finish
        /// </summary>
        public async Task Stop()
        {
            if (_stop != null) _stop.Cancel();
            if (_task != null)
            {
                try { await _task; }
                catch { }
            }
        }

        private async Task Worker(CancellationToken cancellationToken)
        {
            var taskName = $"Task ID = {_configuration.Id} : Function ID = {_configuration.FunctionId}";

            try
            {
                Log(TrakHoundLogLevel.Information, $"Scheduled Task Engine Started : {taskName} : Schedule = {_schedule.Expression} : Time Zone = {_schedule.TimeZone.Id}");
                _tracker?.TaskStarted(_configuration, _schedule, _schedule.GetNext(_clock.UtcNow));

                if (_getLastRun != null) await RunMissed(taskName, cancellationToken);

                var next = _schedule.GetNext(_clock.UtcNow);
                if (next != null) Log(TrakHoundLogLevel.Debug, $"Scheduled Task Next Run : {taskName} : {next.Value.ToISO8601String()}");
                _tracker?.NextRun(_configuration.Id, next);

                while (next != null && !cancellationToken.IsCancellationRequested)
                {
                    var now = _clock.UtcNow;
                    if (now >= next.Value)
                    {
                        Log(TrakHoundLogLevel.Debug, $"Scheduled Task Due : {taskName} : Occurrence = {next.Value.ToISO8601String()}");
                        _tracker?.Due(_configuration, next.Value);

                        // From now, so a late wake skips the occurrences that already passed (instead of running them back to back)
                        next = _schedule.GetNext(now);
                        _tracker?.NextRun(_configuration.Id, next);
                        _onDue(_configuration, next);
                    }
                    else
                    {
                        var wait = next.Value - now;
                        await _clock.Delay(wait < MaxWait ? wait : MaxWait, cancellationToken);
                    }
                }

                if (next == null) Log(TrakHoundLogLevel.Information, $"Scheduled Task has no more occurrences : {taskName} : Schedule = {_schedule.Expression}");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Error, $"Scheduled Task Engine Error : {taskName} : {ex.Message}");
            }
            finally
            {
                Log(TrakHoundLogLevel.Information, $"Scheduled Task Engine Stopped : {taskName}");
            }
        }

        /// <summary>
        /// Runs the Task once if an occurrence was missed since its last Run (ex. while the Instance was stopped)
        /// </summary>
        private async Task RunMissed(string taskName, CancellationToken cancellationToken)
        {
            DateTime? lastRun = null;
            try { lastRun = await _getLastRun(cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log(TrakHoundLogLevel.Warning, $"Scheduled Task Missed Run Check Failed : {taskName} : {ex.Message}");
            }

            if (lastRun != null)
            {
                var missed = _schedule.GetMissedOccurrence(lastRun.Value, _clock.UtcNow, MissedGracePeriod);
                if (missed != null)
                {
                    Log(TrakHoundLogLevel.Information, $"Scheduled Task Running Missed Occurrence : {taskName} : Occurrence = {missed.Value.ToISO8601String()} : Last Run = {lastRun.Value.ToISO8601String()}");
                    _tracker?.MissedRun(_configuration, missed.Value);
                    _onDue(_configuration, _schedule.GetNext(_clock.UtcNow));
                }
            }
        }

        private void Log(TrakHoundLogLevel level, string message, string code = null)
        {
            LogReceived?.Invoke(this, level, message, code);
        }
    }
}
