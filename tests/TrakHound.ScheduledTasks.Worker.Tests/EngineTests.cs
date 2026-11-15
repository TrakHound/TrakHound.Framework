// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.ScheduledTasks.Tests
{
    public class EngineTests
    {
        private static readonly DateTime Start = new DateTime(2026, 1, 15, 0, 0, 5, DateTimeKind.Utc);


        private static (Engine Engine, FakeClock Clock, List<DateTime> Fired) Create(string schedule = "*/10 * * * * *", Func<CancellationToken, Task<DateTime?>> getLastRun = null, List<DateTime?> staleAfters = null)
        {
            var clock = new FakeClock(Start);
            var fired = new List<DateTime>();
            var configuration = Tasks.Create(schedule: schedule);

            var engine = new Engine(configuration, Schedule.Parse(configuration.Schedule, configuration.TimeZone), clock, (o, staleAfter) =>
            {
                lock (fired)
                {
                    fired.Add(clock.UtcNow);
                    staleAfters?.Add(staleAfter);
                }
                return true;
            }, getLastRun);

            return (engine, clock, fired);
        }

        private static int Count(List<DateTime> fired) { lock (fired) return fired.Count; }


        [Fact]
        public async Task Fires_OnEachOccurrence_AndNotBefore()
        {
            var (engine, clock, fired) = Create();
            engine.Start();
            await Wait.Until(() => clock.WaiterCount > 0);

            clock.Advance(TimeSpan.FromSeconds(4)); // 00:00:09
            await Wait.Settle();
            Assert.Equal(0, Count(fired));

            clock.Advance(TimeSpan.FromSeconds(1)); // 00:00:10
            await Wait.Until(() => Count(fired) == 1);

            clock.Advance(TimeSpan.FromSeconds(10)); // 00:00:20
            await Wait.Until(() => Count(fired) == 2);

            await engine.Stop();
        }

        [Fact]
        public async Task Due_PassesTheNextOccurrence_AsStaleAfter()
        {
            var staleAfters = new List<DateTime?>();
            var (engine, clock, fired) = Create(staleAfters: staleAfters);
            engine.Start();
            await Wait.Until(() => clock.WaiterCount > 0);

            clock.Advance(TimeSpan.FromSeconds(5)); // 00:00:10
            await Wait.Until(() => Count(fired) == 1);

            lock (fired) Assert.Equal(Start.AddSeconds(15), staleAfters[0]); // 00:00:20

            await engine.Stop();
        }

        [Fact]
        public async Task LateWake_FiresOnce_InsteadOfCatchingUpEveryOccurrence()
        {
            var (engine, clock, fired) = Create();
            engine.Start();
            await Wait.Until(() => clock.WaiterCount > 0);

            // Wakes 35 seconds late (00:00:10, 00:00:20, 00:00:30 all passed)
            clock.Advance(TimeSpan.FromSeconds(35));
            await Wait.Until(() => Count(fired) == 1);
            await Wait.Settle();
            Assert.Equal(1, Count(fired));

            await engine.Stop();
        }

        [Fact]
        public async Task LongWaits_WakeAtLeastEveryMaxWait()
        {
            // Daily : the Engine still wakes every MaxWait (so system clock changes are picked up)
            var (engine, clock, fired) = Create("0 0 * * *");
            engine.Start();
            await Wait.Until(() => clock.WaiterCount > 0);

            clock.Advance(Engine.MaxWait);
            await Wait.Until(() => clock.WaiterCount > 0); // Waiting again
            Assert.Equal(0, Count(fired));

            await engine.Stop();
        }

        [Fact]
        public async Task RunMissed_FiresOnceAtStart_WhenAnOccurrenceWasMissed()
        {
            // Every 10 seconds, last Run 5 minutes ago : the occurrences since then were missed (runs once, not once per occurrence)
            var (engine, clock, fired) = Create(getLastRun: _ => Task.FromResult<DateTime?>(Start.AddMinutes(-5)));
            engine.Start();

            await Wait.Until(() => Count(fired) == 1);
            await Wait.Settle();
            Assert.Equal(1, Count(fired));

            await engine.Stop();
        }

        [Fact]
        public async Task RunMissed_DoesNotFire_WhenUpToDate()
        {
            var (engine, clock, fired) = Create("0 * * * *", _ => Task.FromResult<DateTime?>(Start.AddSeconds(-5)));
            engine.Start();
            await Wait.Until(() => clock.WaiterCount > 0);
            await Wait.Settle();

            Assert.Equal(0, Count(fired));
            await engine.Stop();
        }

        [Fact]
        public async Task RunMissed_DoesNotFire_WhenTheTaskHasNeverRun()
        {
            var (engine, clock, fired) = Create(getLastRun: _ => Task.FromResult<DateTime?>(null));
            engine.Start();
            await Wait.Until(() => clock.WaiterCount > 0);
            await Wait.Settle();

            Assert.Equal(0, Count(fired));
            await engine.Stop();
        }

        [Fact]
        public async Task RunMissed_CheckFailure_StillStartsTheSchedule()
        {
            var (engine, clock, fired) = Create(getLastRun: _ => throw new Exception("Functions Api not available"));
            engine.Start();
            await Wait.Until(() => clock.WaiterCount > 0);

            clock.Advance(TimeSpan.FromSeconds(5)); // 00:00:10
            await Wait.Until(() => Count(fired) == 1);

            await engine.Stop();
        }

        [Fact]
        public async Task Stop_EndsTheEngine()
        {
            var (engine, clock, fired) = Create("0 0 1 1 *"); // Yearly
            engine.Start();
            await Wait.Until(() => clock.WaiterCount > 0);

            var stop = engine.Stop();
            Assert.Same(stop, await Task.WhenAny(stop, Task.Delay(5000)));
        }
    }
}
