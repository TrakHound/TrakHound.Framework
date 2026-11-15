// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.ScheduledTasks.Tests
{
    public class EngineQueueTests
    {
        private static readonly DateTime Start = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc);


        private class Fixture
        {
            public EngineQueue Queue { get; init; }
            public FakeRunner Runner { get; init; }
            public FakeClock Clock { get; init; }
            public List<string> Logs { get; } = new List<string>();

            public bool Logged(string text)
            {
                lock (Logs) return Logs.Any(o => o.Contains(text));
            }
        }

        private static Fixture Create(OverlapPolicy policy, int maxConcurrentRuns = 5, int maxConcurrentRunsPerTask = 0)
        {
            var clock = new FakeClock(Start);
            var runner = new FakeRunner();
            var fixture = new Fixture
            {
                Runner = runner,
                Clock = clock,
                Queue = new EngineQueue(runner, maxConcurrentRuns, policy, maxConcurrentRunsPerTask, clock)
            };

            fixture.Queue.LogReceived += (sender, level, message, code) => { lock (fixture.Logs) fixture.Logs.Add($"{level} : {message}"); };
            fixture.Queue.Start();
            return fixture;
        }


        [Fact]
        public async Task Skip_SkipsWhileTheRunIsInProgress()
        {
            var f = Create(OverlapPolicy.Skip);
            var task = Tasks.Create();

            Assert.True(f.Queue.Enqueue(task));
            await Wait.Until(() => f.Runner.RunningCount == 1);

            Assert.False(f.Queue.Enqueue(task));
            Assert.True(f.Logged("Skipped"));

            f.Runner.CompleteNext();
            await Wait.Until(() => f.Queue.GetActiveCount(task.Id) == 0);

            Assert.True(f.Queue.Enqueue(task));
            await Wait.Until(() => f.Runner.StartedCount == 2);

            await f.Queue.Stop();
        }

        [Fact]
        public async Task Queue_RunsOneMoreAfterTheRunInProgress()
        {
            var f = Create(OverlapPolicy.Queue);
            var task = Tasks.Create();

            Assert.True(f.Queue.Enqueue(task));
            await Wait.Until(() => f.Runner.RunningCount == 1);

            Assert.True(f.Queue.Enqueue(task));  // Waits behind the Run in progress
            Assert.False(f.Queue.Enqueue(task)); // Only one may wait

            await Wait.Settle();
            Assert.Equal(1, f.Runner.RunningCount); // Not alongside

            f.Runner.CompleteNext();
            await Wait.Until(() => f.Runner.StartedCount == 2);
            Assert.Equal(1, f.Runner.MaxRunningCount);

            f.Runner.CompleteNext();
            await Wait.Until(() => f.Queue.GetActiveCount(task.Id) == 0);
            await f.Queue.Stop();
        }

        [Fact]
        public async Task Queue_WaitingRun_DoesNotHoldARunSlot()
        {
            // Two Run Slots : Task A running with a Run waiting behind it must leave the second Slot for Task B
            var f = Create(OverlapPolicy.Queue, maxConcurrentRuns: 2);
            var taskA = Tasks.Create("task-a");
            var taskB = Tasks.Create("task-b");

            Assert.True(f.Queue.Enqueue(taskA));
            await Wait.Until(() => f.Runner.RunningCount == 1);
            Assert.True(f.Queue.Enqueue(taskA)); // Waits for Task A (not for a Slot)

            Assert.True(f.Queue.Enqueue(taskB));
            await Wait.Until(() => f.Runner.RunningCount == 2);

            await f.Queue.Stop();
        }

        [Fact]
        public async Task Concurrent_RunsAlongside_UpToThePerTaskCap()
        {
            var f = Create(OverlapPolicy.Concurrent, maxConcurrentRuns: 5, maxConcurrentRunsPerTask: 2);
            var task = Tasks.Create();

            Assert.True(f.Queue.Enqueue(task));
            await Wait.Until(() => f.Runner.RunningCount == 1);
            Assert.True(f.Queue.Enqueue(task));
            await Wait.Until(() => f.Runner.RunningCount == 2);

            // At the cap : the third waits (Slots are free, but one Task can't take them all)
            Assert.True(f.Queue.Enqueue(task));
            await Wait.Settle();
            Assert.Equal(2, f.Runner.RunningCount);

            f.Runner.CompleteNext();
            await Wait.Until(() => f.Runner.StartedCount == 3);
            Assert.Equal(2, f.Runner.MaxRunningCount);

            await f.Queue.Stop();
        }

        [Fact]
        public async Task Concurrent_PerTaskCap_LeavesSlotsForOtherTasks()
        {
            var f = Create(OverlapPolicy.Concurrent, maxConcurrentRuns: 3, maxConcurrentRunsPerTask: 2);
            var busy = Tasks.Create("busy");
            var other = Tasks.Create("other");

            for (var i = 0; i < 2; i++)
            {
                Assert.True(f.Queue.Enqueue(busy));
                await Wait.Until(() => f.Runner.RunningCount == i + 1);
            }
            Assert.True(f.Queue.Enqueue(busy)); // Waits at the cap

            Assert.True(f.Queue.Enqueue(other));
            await Wait.Until(() => f.Runner.RunningCount == 3);

            await f.Queue.Stop();
        }

        [Fact]
        public async Task MaxConcurrentRuns_IsRespected()
        {
            var f = Create(OverlapPolicy.Skip, maxConcurrentRuns: 2);

            for (var i = 1; i <= 4; i++) Assert.True(f.Queue.Enqueue(Tasks.Create($"task-{i}")));

            await Wait.Until(() => f.Runner.RunningCount == 2);
            await Wait.Settle();
            Assert.Equal(2, f.Runner.RunningCount);

            f.Runner.CompleteNext();
            f.Runner.CompleteNext();
            await Wait.Until(() => f.Runner.StartedCount == 4);
            Assert.Equal(2, f.Runner.MaxRunningCount);

            await f.Queue.Stop();
        }

        [Fact]
        public async Task StaleRun_IsDropped_WhenItWaitedPastTheNextOccurrence()
        {
            var f = Create(OverlapPolicy.Skip, maxConcurrentRuns: 1);
            var blocking = Tasks.Create("blocking");
            var task = Tasks.Create();

            Assert.True(f.Queue.Enqueue(blocking));
            await Wait.Until(() => f.Runner.RunningCount == 1);

            // Next occurrence in 10 seconds, but the only Run Slot is busy for 11
            Assert.True(f.Queue.Enqueue(task, staleAfter: Start.AddSeconds(10)));
            f.Clock.Advance(TimeSpan.FromSeconds(11));
            f.Runner.CompleteNext();

            await Wait.Until(() => f.Logged("Run Dropped"));
            await Wait.Until(() => f.Queue.GetActiveCount(task.Id) == 0);
            Assert.Equal(1, f.Runner.StartedCount);

            // The Task isn't stuck : its next occurrence runs
            Assert.True(f.Queue.Enqueue(task, staleAfter: Start.AddSeconds(20)));
            await Wait.Until(() => f.Runner.StartedCount == 2);

            await f.Queue.Stop();
        }

        [Fact]
        public async Task SlowStart_IsLogged_WithTheWaitTime()
        {
            var f = Create(OverlapPolicy.Skip, maxConcurrentRuns: 1);

            Assert.True(f.Queue.Enqueue(Tasks.Create("blocking")));
            await Wait.Until(() => f.Runner.RunningCount == 1);

            Assert.True(f.Queue.Enqueue(Tasks.Create()));
            await Wait.Until(() => f.Logged("Run Slots busy"));

            f.Clock.Advance(EngineQueue.SlowStartThreshold + TimeSpan.FromSeconds(1));
            f.Runner.CompleteNext();

            await Wait.Until(() => f.Runner.StartedCount == 2);
            await Wait.Until(() => f.Logged("for a free Run Slot"));

            await f.Queue.Stop();
        }

        [Fact]
        public async Task FailedRun_DoesNotStopTheQueue_OrBlockTheTask()
        {
            // One Run Slot : if a Run's exception leaked the Slot, nothing would run after it
            var f = Create(OverlapPolicy.Skip, maxConcurrentRuns: 1);
            var task = Tasks.Create();

            var throwOnce = true;
            f.Runner.Throw = _ =>
            {
                if (!throwOnce) return null;
                throwOnce = false;
                return new InvalidOperationException("Response was not JSON");
            };

            Assert.True(f.Queue.Enqueue(task));
            await Wait.Until(() => f.Queue.GetActiveCount(task.Id) == 0);
            Assert.True(f.Logged("Run Error") && f.Logged("Response was not JSON"));

            Assert.True(f.Queue.Enqueue(task));
            await Wait.Until(() => f.Runner.RunningCount == 1);

            await f.Queue.Stop();
        }

        [Fact]
        public async Task UnsuccessfulRun_IsLoggedAsFailed()
        {
            var f = Create(OverlapPolicy.Skip);

            f.Queue.Enqueue(Tasks.Create());
            await Wait.Until(() => f.Runner.RunningCount == 1);
            f.Runner.CompleteNext(success: false);

            await Wait.Until(() => f.Logged("Run Failed"));
            await f.Queue.Stop();
        }

        [Fact]
        public async Task Stop_CancelsRunsInProgress()
        {
            var f = Create(OverlapPolicy.Skip);

            f.Queue.Enqueue(Tasks.Create());
            await Wait.Until(() => f.Runner.RunningCount == 1);

            var stop = f.Queue.Stop();
            Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(5000)));
            Assert.Equal(0, f.Runner.RunningCount);
        }

        [Fact]
        public async Task Enqueue_AfterStop_IsRejected()
        {
            var f = Create(OverlapPolicy.Skip);
            await f.Queue.Stop();

            Assert.False(f.Queue.Enqueue(Tasks.Create()));
        }

        [Fact]
        public void Defaults()
        {
            Assert.True(EngineQueue.GetDefaultMaxConcurrentRuns() >= 2);
            Assert.Equal(1, EngineQueue.GetDefaultMaxConcurrentRunsPerTask(OverlapPolicy.Skip));
            Assert.Equal(2, EngineQueue.GetDefaultMaxConcurrentRunsPerTask(OverlapPolicy.Concurrent));

            // Skip and Queue never overlap a Task's Runs (the per Task setting only applies to Concurrent)
            var queue = new EngineQueue(new FakeRunner(), 4, OverlapPolicy.Queue, maxConcurrentRunsPerTask: 3);
            Assert.Equal(1, queue.MaxConcurrentRunsPerTask);

            // The per Task cap can't exceed the total
            var concurrent = new EngineQueue(new FakeRunner(), 2, OverlapPolicy.Concurrent, maxConcurrentRunsPerTask: 5);
            Assert.Equal(2, concurrent.MaxConcurrentRunsPerTask);
        }
    }
}
