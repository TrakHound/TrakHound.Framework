// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.ScheduledTasks.Tests
{
    public class StatusTrackerTests
    {
        private static readonly DateTime Start = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);


        private static ScheduledTaskStatus GetTask(StatusTracker tracker, string id = "task-1") => tracker.GetSnapshot().Tasks.Single(o => o.Id == id);


        [Fact]
        public void Run_MovesThroughWaiting_Running_Scheduled()
        {
            var clock = new FakeClock(Start);
            var tracker = new StatusTracker(clock);
            var task = Tasks.Create();
            tracker.TaskStarted(task, Schedule.Parse(task.Schedule, task.TimeZone), Start.AddSeconds(10));

            Assert.Equal(TaskStates.Scheduled, GetTask(tracker).State);

            tracker.Queued(task);
            Assert.Equal(TaskStates.Waiting, GetTask(tracker).State);
            Assert.Equal(1, tracker.GetSnapshot().WaitingCount);

            clock.Advance(TimeSpan.FromSeconds(2));
            tracker.RunStarted(task, TimeSpan.FromSeconds(2));
            var running = GetTask(tracker);
            Assert.Equal(TaskStates.Running, running.State);
            Assert.Equal(clock.UtcNow, running.RunStarted);
            Assert.Equal(1, tracker.GetSnapshot().RunningCount);

            clock.Advance(TimeSpan.FromSeconds(5));
            tracker.RunEnded(task, StatusEventTypes.Completed, 200, TimeSpan.FromSeconds(5), null);
            var ended = GetTask(tracker);
            Assert.Equal(TaskStates.Scheduled, ended.State);
            Assert.Equal(1, ended.StartedCount);
            Assert.Equal(1, ended.CompletedCount);
            Assert.Equal(200, ended.LastStatusCode);
            Assert.True(ended.LastSuccess);
            Assert.Equal(5000, ended.LastDurationMs);
            Assert.Equal(0, tracker.GetSnapshot().RunningCount);
        }

        [Fact]
        public void SkippedAndDropped_AreCounted_WithEvents()
        {
            var tracker = new StatusTracker(new FakeClock(Start));
            var task = Tasks.Create();

            tracker.Skipped(task, "Previous Run still in progress");
            tracker.Queued(task);
            tracker.Dropped(task, "Waited past its next occurrence");

            var status = GetTask(tracker);
            Assert.Equal(1, status.SkippedCount);
            Assert.Equal(1, status.DroppedCount);
            Assert.Equal(0, status.Waiting);

            var events = tracker.GetSnapshot().Events;
            Assert.Equal(StatusEventTypes.Dropped, events[0].Type); // Newest first
            Assert.Contains(events, o => o.Type == StatusEventTypes.Skipped && o.Message.Contains("in progress"));
        }

        [Fact]
        public void FailedRun_IsCounted()
        {
            var tracker = new StatusTracker(new FakeClock(Start));
            var task = Tasks.Create();

            tracker.Queued(task);
            tracker.RunStarted(task, TimeSpan.Zero);
            tracker.RunEnded(task, StatusEventTypes.Failed, 500, TimeSpan.FromSeconds(1), "Boom");

            var status = GetTask(tracker);
            Assert.Equal(1, status.FailedCount);
            Assert.False(status.LastSuccess);
            Assert.Equal("Boom", status.LastMessage);
        }

        [Fact]
        public void Events_AreCapped()
        {
            var tracker = new StatusTracker(new FakeClock(Start));
            var task = Tasks.Create();

            for (var i = 0; i < StatusTracker.MaxEvents + 50; i++) tracker.Skipped(task, $"skip {i}");

            var events = tracker.GetSnapshot().Events;
            Assert.Equal(StatusTracker.MaxEvents, events.Count);
            Assert.Equal($"skip {StatusTracker.MaxEvents + 49}", events[0].Message);
        }

        [Fact]
        public void Upcoming_MergesTasks_InTimeOrder()
        {
            var tracker = new StatusTracker(new FakeClock(Start));
            var every10 = Tasks.Create("every-10", "*/10 * * * * *");
            var every15 = Tasks.Create("every-15", "*/15 * * * * *");

            tracker.TaskStarted(every10, Schedule.Parse(every10.Schedule, "UTC"), Start.AddSeconds(10));
            tracker.TaskStarted(every15, Schedule.Parse(every15.Schedule, "UTC"), Start.AddSeconds(15));

            var upcoming = tracker.GetSnapshot(upcomingCount: 5).Upcoming;

            Assert.Equal(5, upcoming.Count);
            Assert.Equal(new[] { 10, 15, 20, 30, 30 }, upcoming.Select(o => (int)(o.Time - Start).TotalSeconds));
            Assert.Equal("every-10", upcoming[0].TaskId);
            Assert.Equal("every-15", upcoming[1].TaskId);
        }

        [Fact]
        public void InvalidAndRemovedTasks()
        {
            var tracker = new StatusTracker(new FakeClock(Start));
            var task = Tasks.Create();

            tracker.TaskInvalid(task, "Time Zone Not Found");
            var invalid = GetTask(tracker);
            Assert.Equal(TaskStates.Invalid, invalid.State);
            Assert.Equal("Time Zone Not Found", invalid.Error);

            tracker.TaskRemoved(task.Id);
            Assert.Empty(tracker.GetSnapshot().Tasks);
        }

        [Fact]
        public void NoMoreOccurrences_IsEnded()
        {
            var tracker = new StatusTracker(new FakeClock(Start));
            var task = Tasks.Create();
            tracker.TaskStarted(task, Schedule.Parse(task.Schedule, "UTC"), Start.AddSeconds(10));

            tracker.NextRun(task.Id, null);

            Assert.Equal(TaskStates.Ended, GetTask(tracker).State);
        }

        [Fact]
        public void Version_ChangesOnEveryUpdate()
        {
            var tracker = new StatusTracker(new FakeClock(Start));
            var task = Tasks.Create();

            var version = tracker.Version;
            tracker.Queued(task);
            Assert.True(tracker.Version > version);
        }

        [Fact]
        public async Task Queue_ReportsRunsAndSkips()
        {
            var clock = new FakeClock(Start);
            var tracker = new StatusTracker(clock);
            var runner = new FakeRunner();
            var queue = new EngineQueue(runner, 2, OverlapPolicy.Skip, clock: clock, tracker: tracker);
            queue.Start();
            var task = Tasks.Create();

            Assert.True(queue.Enqueue(task));
            await Wait.Until(() => runner.RunningCount == 1);
            Assert.Equal(TaskStates.Running, GetTask(tracker).State);

            Assert.False(queue.Enqueue(task)); // Skipped
            Assert.Equal(1, GetTask(tracker).SkippedCount);

            runner.CompleteNext();
            await Wait.Until(() => GetTask(tracker).CompletedCount == 1);
            Assert.Equal(TaskStates.Scheduled, GetTask(tracker).State);

            await queue.Stop();
        }

        [Fact]
        public async Task Engine_ReportsDueAndNextRun()
        {
            var clock = new FakeClock(Start.AddSeconds(5));
            var tracker = new StatusTracker(clock);
            var task = Tasks.Create();
            var engine = new Engine(task, Schedule.Parse(task.Schedule, task.TimeZone), clock, (o, next) => true, tracker: tracker);
            engine.Start();

            await Wait.Until(() => clock.WaiterCount > 0);
            Assert.Equal(Start.AddSeconds(10), GetTask(tracker).NextRun);

            clock.Advance(TimeSpan.FromSeconds(5)); // 00:00:10
            await Wait.Until(() => GetTask(tracker).DueCount == 1);
            Assert.Equal(Start.AddSeconds(20), GetTask(tracker).NextRun);

            await engine.Stop();
        }
    }
}
