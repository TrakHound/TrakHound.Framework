// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.ScheduledTasks.Tests
{
    /// <summary>
    /// A clock that only moves when Advance is called (Delays complete when the clock passes their due time)
    /// </summary>
    internal class FakeClock : IScheduleClock
    {
        private readonly List<(DateTime Due, TaskCompletionSource Completion)> _waiters = new();
        private readonly object _lock = new object();
        private DateTime _now;


        public FakeClock(DateTime utcNow)
        {
            _now = DateTime.SpecifyKind(utcNow, DateTimeKind.Utc);
        }

        public DateTime UtcNow
        {
            get { lock (_lock) return _now; }
        }

        public int WaiterCount
        {
            get { lock (_lock) return _waiters.Count(o => !o.Completion.Task.IsCompleted); }
        }

        public Task Delay(TimeSpan delay, CancellationToken cancellationToken)
        {
            var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                if (delay <= TimeSpan.Zero) return Task.CompletedTask;
                _waiters.Add((_now + delay, completion));
            }

            cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken));
            return completion.Task;
        }

        public void Advance(TimeSpan time)
        {
            List<TaskCompletionSource> due;
            lock (_lock)
            {
                _now += time;
                due = _waiters.Where(o => o.Due <= _now).Select(o => o.Completion).ToList();
                _waiters.RemoveAll(o => o.Due <= _now);
            }

            foreach (var completion in due) completion.TrySetResult();
        }
    }

    /// <summary>
    /// A Function Runner whose Runs finish when the test says so
    /// </summary>
    internal class FakeRunner : IFunctionRunner
    {
        private readonly object _lock = new object();
        private readonly List<(ScheduledTaskConfiguration Configuration, TaskCompletionSource<FunctionRunResult> Completion)> _runs = new();
        private int _running;
        private int _maxRunning;


        public Func<ScheduledTaskConfiguration, Exception> Throw { get; set; }

        public int StartedCount { get { lock (_lock) return _runs.Count; } }

        public int RunningCount { get { lock (_lock) return _running; } }

        public int MaxRunningCount { get { lock (_lock) return _maxRunning; } }


        public async Task<FunctionRunResult> Run(ScheduledTaskConfiguration configuration, CancellationToken cancellationToken)
        {
            var exception = Throw?.Invoke(configuration);
            if (exception != null) throw exception;

            var completion = new TaskCompletionSource<FunctionRunResult>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_lock)
            {
                _runs.Add((configuration, completion));
                _running++;
                _maxRunning = Math.Max(_maxRunning, _running);
            }

            try
            {
                using (cancellationToken.Register(() => completion.TrySetCanceled(cancellationToken)))
                {
                    return await completion.Task;
                }
            }
            finally
            {
                lock (_lock) _running--;
            }
        }

        /// <summary>
        /// Completes the oldest Run in progress
        /// </summary>
        public void CompleteNext(bool success = true)
        {
            TaskCompletionSource<FunctionRunResult> completion;
            lock (_lock)
            {
                completion = _runs.Select(o => o.Completion).First(o => !o.Task.IsCompleted);
            }

            completion.TrySetResult(new FunctionRunResult { Success = success, StatusCode = success ? 200 : 500 });
        }
    }

    internal static class Wait
    {
        /// <summary>
        /// Waits for the condition (the Engines and Run Workers run on the Thread Pool)
        /// </summary>
        public static async Task Until(Func<bool> condition, int timeoutMilliseconds = 5000)
        {
            var stpw = System.Diagnostics.Stopwatch.StartNew();
            while (!condition())
            {
                if (stpw.ElapsedMilliseconds > timeoutMilliseconds) throw new TimeoutException("Condition not met");
                await Task.Delay(10);
            }
        }

        /// <summary>
        /// Gives background work a chance to run (for asserting that something did NOT happen)
        /// </summary>
        public static Task Settle() => Task.Delay(150);
    }

    internal static class Tasks
    {
        public static ScheduledTaskConfiguration Create(string id = "task-1", string schedule = "*/10 * * * * *", string timeZone = "UTC")
        {
            return new ScheduledTaskConfiguration
            {
                Id = id,
                Enabled = true,
                FunctionId = $"function-{id}",
                Schedule = schedule,
                TimeZone = timeZone
            };
        }
    }
}
