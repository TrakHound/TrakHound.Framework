// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.ScheduledTasks
{
    /// <summary>
    /// Collects what the Engines and the Run Queue do (Tasks, Runs, skips, ...) for the Worker Status
    /// </summary>
    internal class StatusTracker
    {
        internal const int MaxEvents = 200;
        internal const int DefaultUpcomingCount = 20;

        private class TrackedTask
        {
            public ScheduledTaskStatus Status { get; } = new ScheduledTaskStatus();
            public Schedule Schedule { get; set; }
            public readonly Queue<DateTime> RunStarts = new Queue<DateTime>();
            public readonly Queue<DateTime> WaitStarts = new Queue<DateTime>();
        }

        private readonly IScheduleClock _clock;
        private readonly Dictionary<string, TrackedTask> _tasks = new Dictionary<string, TrackedTask>(); // Task ID => Task
        private readonly LinkedList<StatusEvent> _events = new LinkedList<StatusEvent>(); // Newest first
        private readonly object _lock = new object();
        private long _version;


        public DateTime Started { get; }

        /// <summary>
        /// Incremented on every change (so a snapshot is only sent when something changed)
        /// </summary>
        public long Version => Interlocked.Read(ref _version);


        public StatusTracker(IScheduleClock clock = null)
        {
            _clock = clock ?? SystemScheduleClock.Instance;
            Started = _clock.UtcNow;
        }


        #region "Tasks"

        public void TaskStarted(ScheduledTaskConfiguration configuration, Schedule schedule, DateTime? nextRun)
        {
            if (configuration?.Id == null) return;

            lock (_lock)
            {
                var task = GetTask(configuration);
                task.Schedule = schedule;
                task.Status.NextRun = nextRun;
                task.Status.Error = null;
                task.Status.TimeZone = schedule?.TimeZone?.Id;
                UpdateState(task);
            }
            Changed();
        }

        public void TaskInvalid(ScheduledTaskConfiguration configuration, string error)
        {
            if (configuration?.Id == null) return;

            lock (_lock)
            {
                var task = GetTask(configuration);
                task.Schedule = null;
                task.Status.NextRun = null;
                task.Status.Error = error;
                task.Status.State = TaskStates.Invalid;
            }
            Changed();
        }

        /// <summary>
        /// The Task was disabled or deleted (its counts are forgotten)
        /// </summary>
        public void TaskRemoved(string taskId)
        {
            if (taskId == null) return;

            lock (_lock) _tasks.Remove(taskId);
            Changed();
        }

        public void NextRun(string taskId, DateTime? nextRun)
        {
            lock (_lock)
            {
                if (!_tasks.TryGetValue(taskId, out var task)) return;
                task.Status.NextRun = nextRun;
                UpdateState(task);
            }
            Changed();
        }

        #endregion

        #region "Runs"

        public void Due(ScheduledTaskConfiguration configuration, DateTime occurrence)
        {
            lock (_lock)
            {
                var task = GetTask(configuration);
                task.Status.DueCount++;
                task.Status.LastDue = occurrence;
            }
            Changed();
        }

        public void MissedRun(ScheduledTaskConfiguration configuration, DateTime occurrence)
        {
            lock (_lock)
            {
                var task = GetTask(configuration);
                task.Status.MissedRunCount++;
                AddEvent(StatusEventTypes.Missed, configuration, $"Running missed occurrence {occurrence.ToISO8601String()}");
            }
            Changed();
        }

        public void Queued(ScheduledTaskConfiguration configuration)
        {
            lock (_lock)
            {
                var task = GetTask(configuration);
                task.WaitStarts.Enqueue(_clock.UtcNow);
                UpdateState(task);
                AddEvent(StatusEventTypes.Due, configuration, null);
            }
            Changed();
        }

        public void Skipped(ScheduledTaskConfiguration configuration, string reason)
        {
            lock (_lock)
            {
                var task = GetTask(configuration);
                task.Status.SkippedCount++;
                AddEvent(StatusEventTypes.Skipped, configuration, reason);
            }
            Changed();
        }

        public void Dropped(ScheduledTaskConfiguration configuration, string reason)
        {
            lock (_lock)
            {
                var task = GetTask(configuration);
                if (task.WaitStarts.Count > 0) task.WaitStarts.Dequeue();
                task.Status.DroppedCount++;
                UpdateState(task);
                AddEvent(StatusEventTypes.Dropped, configuration, reason);
            }
            Changed();
        }

        public void RunStarted(ScheduledTaskConfiguration configuration, TimeSpan waited)
        {
            lock (_lock)
            {
                var task = GetTask(configuration);
                if (task.WaitStarts.Count > 0) task.WaitStarts.Dequeue();
                task.RunStarts.Enqueue(_clock.UtcNow);
                task.Status.StartedCount++;
                UpdateState(task);
                AddEvent(StatusEventTypes.Started, configuration, waited >= TimeSpan.FromSeconds(1) ? $"Waited {waited.ToFormattedString(true)} for a Run Slot" : null);
            }
            Changed();
        }

        /// <summary>
        /// A Run ended (completed, failed, or cancelled)
        /// </summary>
        public void RunEnded(ScheduledTaskConfiguration configuration, string type, int? statusCode, TimeSpan duration, string message)
        {
            lock (_lock)
            {
                var task = GetTask(configuration);
                if (task.RunStarts.Count > 0) task.RunStarts.Dequeue();

                if (type == StatusEventTypes.Completed) task.Status.CompletedCount++;
                else if (type == StatusEventTypes.Failed) task.Status.FailedCount++;

                task.Status.LastFinished = _clock.UtcNow;
                task.Status.LastStatusCode = statusCode;
                task.Status.LastSuccess = type == StatusEventTypes.Completed;
                task.Status.LastDurationMs = duration.TotalMilliseconds;
                task.Status.LastMessage = message;

                UpdateState(task);
                AddEvent(type, configuration, type == StatusEventTypes.Completed ? $"Status Code {statusCode} in {duration.ToFormattedString(true)}" : message);
            }
            Changed();
        }

        #endregion

        #region "Snapshot"

        /// <summary>
        /// The Worker Status (Run Slot details are added by the Worker)
        /// </summary>
        public WorkerStatus GetSnapshot(int upcomingCount = DefaultUpcomingCount)
        {
            var now = _clock.UtcNow;
            var snapshot = new WorkerStatus();
            snapshot.Started = Started;
            snapshot.Updated = now;

            List<UpcomingTask> upcomingTasks;
            lock (_lock)
            {
                foreach (var task in _tasks.Values.OrderBy(o => o.Status.FunctionId).ThenBy(o => o.Status.Description ?? o.Status.Id))
                {
                    snapshot.Tasks.Add(task.Status.Clone());
                }

                snapshot.RunningCount = _tasks.Values.Sum(o => o.Status.Running);
                snapshot.WaitingCount = _tasks.Values.Sum(o => o.Status.Waiting);
                snapshot.Events = _events.ToList();

                upcomingTasks = GetUpcomingTasks(upcomingCount);
            }

            // Occurrences are computed outside the Lock (Schedules don't change once parsed)
            snapshot.Upcoming = GetUpcoming(upcomingTasks, upcomingCount);

            return snapshot;
        }

        private class UpcomingTask
        {
            public int Index; // The Task's position in the Tasks (ties keep this order)
            public Schedule Schedule;
            public DateTime NextRun;
            public string TaskId;
            public string FunctionId;
            public string Description;
        }

        private readonly struct UpcomingCandidate
        {
            public readonly DateTime Time;
            public readonly UpcomingTask Task;
            public readonly int Occurrence; // The occurrence's position in its Task

            public UpcomingCandidate(DateTime time, UpcomingTask task, int occurrence)
            {
                Time = time;
                Task = task;
                Occurrence = occurrence;
            }
        }

        // The Tasks with a next occurrence (call with the Lock held)
        private List<UpcomingTask> GetUpcomingTasks(int count)
        {
            var tasks = new List<UpcomingTask>();
            if (count <= 0) return tasks;

            var index = 0;
            foreach (var task in _tasks.Values)
            {
                if (task.Schedule != null && task.Status.NextRun != null)
                {
                    tasks.Add(new UpcomingTask { Index = index, Schedule = task.Schedule, NextRun = task.Status.NextRun.Value, TaskId = task.Status.Id, FunctionId = task.Status.FunctionId, Description = task.Status.Description });
                }
                index++;
            }
            return tasks;
        }

        // Upcoming order : Time, then Function ID, then Task order, then occurrence order
        private static int CompareUpcoming(UpcomingCandidate a, UpcomingCandidate b)
        {
            var result = a.Time.CompareTo(b.Time);
            if (result == 0) result = Comparer<string>.Default.Compare(a.Task.FunctionId, b.Task.FunctionId);
            if (result == 0) result = a.Task.Index.CompareTo(b.Task.Index);
            if (result == 0) result = a.Occurrence.CompareTo(b.Occurrence);
            return result;
        }

        private static readonly IComparer<UpcomingCandidate> _latestFirst = Comparer<UpcomingCandidate>.Create((a, b) => CompareUpcoming(b, a));

        // The next (count) occurrences across the Tasks
        private static List<UpcomingRun> GetUpcoming(List<UpcomingTask> tasks, int count)
        {
            var upcoming = new List<UpcomingRun>();
            if (count <= 0 || tasks.Count < 1) return upcoming;

            // The (count) earliest candidates so far, with the latest (the (count)th) at the top
            var kept = new PriorityQueue<UpcomingCandidate, UpcomingCandidate>(count + 1, _latestFirst);

            // Earliest Tasks first : a Task stops expanding once its occurrences sort after the (count)th candidate
            foreach (var task in tasks.OrderBy(o => o.NextRun))
            {
                // Later Tasks start later still, so none of their occurrences can be kept
                if (kept.Count >= count && task.NextRun > kept.Peek().Time) break;

                DateTime? occurrence = task.NextRun;
                for (var i = 0; i < count && occurrence != null; i++)
                {
                    var candidate = new UpcomingCandidate(occurrence.Value, task, i);
                    if (kept.Count < count) kept.Enqueue(candidate, candidate);
                    else if (CompareUpcoming(candidate, kept.Peek()) < 0) kept.EnqueueDequeue(candidate, candidate);
                    else break; // The Task's later occurrences sort after this one

                    occurrence = task.Schedule.GetNext(occurrence.Value);
                }
            }

            var candidates = new List<UpcomingCandidate>(kept.Count);
            while (kept.Count > 0) candidates.Add(kept.Dequeue());
            candidates.Sort(CompareUpcoming);

            foreach (var candidate in candidates)
            {
                upcoming.Add(new UpcomingRun { Time = candidate.Time, TaskId = candidate.Task.TaskId, FunctionId = candidate.Task.FunctionId, Description = candidate.Task.Description });
            }
            return upcoming;
        }

        #endregion

        // Call with the Lock held
        private TrackedTask GetTask(ScheduledTaskConfiguration configuration)
        {
            if (!_tasks.TryGetValue(configuration.Id, out var task))
            {
                task = new TrackedTask();
                task.Status.Id = configuration.Id;
                task.Status.State = TaskStates.Scheduled;
                _tasks.Add(configuration.Id, task);
            }

            task.Status.FunctionId = configuration.FunctionId;
            task.Status.JobId = configuration.JobId;
            task.Status.Description = configuration.Description;
            task.Status.Schedule = configuration.Schedule;
            return task;
        }

        // Call with the Lock held
        private static void UpdateState(TrackedTask task)
        {
            task.Status.Running = task.RunStarts.Count;
            task.Status.Waiting = task.WaitStarts.Count;
            task.Status.RunStarted = task.RunStarts.Count > 0 ? task.RunStarts.Peek() : null;
            task.Status.WaitingSince = task.WaitStarts.Count > 0 ? task.WaitStarts.Peek() : null;

            if (task.Status.State == TaskStates.Invalid && task.Schedule == null) return;

            if (task.Status.Running > 0) task.Status.State = TaskStates.Running;
            else if (task.Status.Waiting > 0) task.Status.State = TaskStates.Waiting;
            else if (task.Schedule != null && task.Status.NextRun == null) task.Status.State = TaskStates.Ended;
            else task.Status.State = TaskStates.Scheduled;
        }

        // Call with the Lock held
        private void AddEvent(string type, ScheduledTaskConfiguration configuration, string message)
        {
            _events.AddFirst(new StatusEvent { Time = _clock.UtcNow, Type = type, TaskId = configuration?.Id, FunctionId = configuration?.FunctionId, Message = message });
            while (_events.Count > MaxEvents) _events.RemoveLast();
        }

        private void Changed() => Interlocked.Increment(ref _version);

    }
}
