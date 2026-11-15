// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Boots;
using TrakHound.Instances;
using TrakHound.Logging;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// The Boot History keeps the recent Boots in a file that two processes can write at once, and the
    /// step timer and log collector record what happened during a Boot
    /// </summary>
    public class BootHistoryTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"trakhound-boots-tests-{Guid.NewGuid()}");
        private readonly string _path;


        public BootHistoryTests()
        {
            Directory.CreateDirectory(_root);
            _path = Path.Combine(_root, TrakHoundBootHistory.Filename);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }


        [Fact]
        public void Save_AddsThenReplacesById()
        {
            var history = new TrakHoundBootHistory(_path);

            var boot = CreateBoot(1, DateTime.UtcNow);
            history.Save(boot);

            boot.Status = TrakHoundInstanceBootStatus.Stopped;
            boot.Reason = "Console Termination Signal";
            history.Save(boot);

            var saved = Assert.Single(history.GetBoots());
            Assert.Equal(TrakHoundInstanceBootStatus.Stopped, saved.Status);
            Assert.Equal("Console Termination Signal", saved.Reason);
        }

        [Fact]
        public void GetBoots_NewestFirst_AndReadsChangesFromAnotherProcess()
        {
            var start = DateTime.UtcNow.AddHours(-1);
            var history = new TrakHoundBootHistory(_path);
            history.Save(CreateBoot(1, start));
            Assert.Single(history.GetBoots());

            // Another process (its own History object) adds a Boot
            new TrakHoundBootHistory(_path).Save(CreateBoot(2, start.AddMinutes(10)));

            var boots = history.GetBoots();
            Assert.Equal(new[] { 2, 1 }, boots.Select(o => o.ProcessId));
        }

        [Fact]
        public void Update_KeepsOnlyTheNewestBoots()
        {
            var history = new TrakHoundBootHistory(_path, maxBoots: 3);
            var start = DateTime.UtcNow.AddDays(-1);

            for (var i = 1; i <= 5; i++) history.Save(CreateBoot(i, start.AddMinutes(i)));

            Assert.Equal(new[] { 5, 4, 3 }, history.GetBoots().Select(o => o.ProcessId));
        }

        [Fact]
        public void Update_KeepsTheFirstRuntimeStartAndTheMostRecentRestarts()
        {
            var history = new TrakHoundBootHistory(_path);
            var start = DateTime.UtcNow.AddHours(-1);

            var boot = CreateBoot(1, start);
            boot.Starts = Enumerable.Range(0, TrakHoundBootHistory.MaxStarts + 10)
                .Select(i => new TrakHoundInstanceRuntimeStart { Started = start.AddSeconds(i), Success = true })
                .ToList();
            history.Save(boot);

            var starts = Assert.Single(history.GetBoots()).Starts;
            Assert.Equal(TrakHoundBootHistory.MaxStarts, starts.Count);
            Assert.Equal(start, starts[0].Started);
            Assert.Equal(start.AddSeconds(11), starts[1].Started);
            Assert.Equal(start.AddSeconds(TrakHoundBootHistory.MaxStarts + 9), starts[^1].Started);
        }

        [Fact]
        public void Update_UnreadableFile_StartsANewHistory()
        {
            File.WriteAllText(_path, "[{\"id\": \"cut short by a power loss");

            var history = new TrakHoundBootHistory(_path);
            Assert.Empty(history.GetBoots());

            history.Save(CreateBoot(1, DateTime.UtcNow));
            Assert.Single(history.GetBoots());
        }

        [Fact]
        public async Task Update_TwoProcessesWritingAtOnce_LoseNoBoots()
        {
            // Each History object stands in for a process (ex. the old and new Worker Process of an IIS Overlapped Recycle)
            var start = DateTime.UtcNow.AddHours(-1);
            var writers = Enumerable.Range(0, 2).Select(w => Task.Run(() =>
            {
                var history = new TrakHoundBootHistory(_path);
                for (var i = 0; i < 20; i++) history.Save(CreateBoot(w * 100 + i, start.AddSeconds(w * 100 + i)));
            }));
            await Task.WhenAll(writers);

            Assert.Equal(40, new TrakHoundBootHistory(_path).GetBoots().Count);
        }

        [Fact]
        public void Serialize_StatusIsWrittenAsText()
        {
            var history = new TrakHoundBootHistory(_path);
            history.Save(CreateBoot(1, DateTime.UtcNow));

            // Starting is the enum default, so it must still be written (and readable in the file)
            Assert.Contains("\"status\": \"Starting\"", File.ReadAllText(_path));
        }


        [Fact]
        public void StepTimer_RecordsEachStep_AndTheOneThatFailed()
        {
            var timer = new TrakHoundBootStepTimer();
            timer.Begin("Read Configuration");
            timer.Begin("Start HTTP Server");
            timer.End("Address already in use");
            timer.End(); // No step in progress

            Assert.Equal(new[] { "Read Configuration", "Start HTTP Server" }, timer.Steps.Select(o => o.Name));
            Assert.Null(timer.Steps[0].Error);
            Assert.Equal("Address already in use", timer.Steps[1].Error);
            Assert.All(timer.Steps, o => Assert.True(o.DurationMs >= 0));
        }

        [Fact]
        public void LogCollector_CountsEverything_KeepsTheFirstEntries_ErrorsFirst()
        {
            using var collector = new TrakHoundBootLogCollector(null);

            collector.Add(TrakHoundLogLevel.Information, "instance", "Loading Packages...");
            for (var i = 0; i < TrakHoundBootLogCollector.MaxEntries; i++) collector.Add(TrakHoundLogLevel.Warning, "workers/a", $"Warning {i}");
            collector.Add(TrakHoundLogLevel.Error, "functions/b", "Failed to Load\nwith a Stack Trace");
            collector.Add(TrakHoundLogLevel.Critical, "instance", "Critical");

            var log = collector.GetLog();
            Assert.Equal(TrakHoundBootLogCollector.MaxEntries, log.WarningCount);
            Assert.Equal(2, log.ErrorCount);
            Assert.Equal(TrakHoundBootLogCollector.MaxEntries, log.Entries.Count);

            // Both Errors are kept (in place of the last Warnings), with only the first line of the Message
            Assert.Contains(log.Entries, o => o.Logger == "functions/b" && o.Message == "Failed to Load");
            Assert.Contains(log.Entries, o => o.Level == "Critical");
            Assert.DoesNotContain(log.Entries, o => o.Level == "Information");
        }

        [Fact]
        public void LogCollector_IgnoresEntriesAfterDispose()
        {
            var collector = new TrakHoundBootLogCollector(null);
            collector.Add(TrakHoundLogLevel.Error, "instance", "During");
            collector.Dispose();
            collector.Add(TrakHoundLogLevel.Error, "instance", "After");

            Assert.Equal(1, collector.GetLog().ErrorCount);
        }


        private static TrakHoundInstanceBoot CreateBoot(int processId, DateTime started) => new TrakHoundInstanceBoot
        {
            Id = $"{started:yyyyMMddHHmmssfff}-{processId}",
            ProcessId = processId,
            Started = started
        };
    }
}
