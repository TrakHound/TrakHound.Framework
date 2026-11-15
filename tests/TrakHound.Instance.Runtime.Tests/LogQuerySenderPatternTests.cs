// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Logging.History;
using TrakHound.Logging;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// Log History Query Patterns match Entry Senders as well as Logger Names (as Live Log Subscriptions do),
    /// so a Pattern can select part of a Logger's entries (ex. one Device's entries from a Worker)
    /// </summary>
    public class LogQuerySenderPatternTests : IDisposable
    {
        private const string WorkerId = "worker-1";
        private const string WorkerName = "workers/TrakHound.Package/0e1b/Engine";
        private const string ApiId = "api-1";
        private const string ApiName = "api/TrakHound.Api";
        private const long Second = 1000000000; // Timestamps are Unix time in nanoseconds

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"trakhound-log-sender-tests-{Guid.NewGuid()}");
        private readonly List<IDisposable> _stores = new List<IDisposable>();


        public void Dispose()
        {
            foreach (var store in _stores) store.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }

        public static TheoryData<string> Stores => new TheoryData<string> { "file", "sqlite" };

        private TrakHoundLogStoreBase CreateStore(string type)
        {
            var directory = Path.Combine(_root, type);
            TrakHoundLogStoreBase store = type == "sqlite" ? new TrakHoundSqliteLogStore(directory) : new TrakHoundFileLogStore(directory);
            _stores.Add(store);

            store.RegisterLogger(WorkerId, WorkerName);
            store.RegisterLogger(ApiId, ApiName);

            var timestamp = DateTime.UtcNow.ToUnixTime();
            store.Write(new[]
            {
                CreateRecord(WorkerId, WorkerName, WorkerName, timestamp, "Worker Started"),
                CreateRecord(WorkerId, WorkerName, $"{WorkerName}/Devices/d-1", timestamp + 1 * Second, "d-1 Connected"),
                CreateRecord(WorkerId, WorkerName, $"{WorkerName}/Devices/d-2", timestamp + 2 * Second, "d-2 Connected"),
                CreateRecord(WorkerId, WorkerName, $"{WorkerName}/Devices/d-1", timestamp + 3 * Second, "d-1 Disconnected"),
                CreateRecord(ApiId, ApiName, ApiName, timestamp + 4 * Second, "Request")
            });

            return store;
        }

        private static TrakHoundLogRecord CreateRecord(string loggerId, string loggerName, string sender, long timestamp, string message)
        {
            return new TrakHoundLogRecord { LoggerId = loggerId, LoggerName = loggerName, Sender = sender, Level = TrakHoundLogLevel.Information, Timestamp = timestamp, Message = message };
        }

        private static string[] Query(TrakHoundLogStoreBase store, params string[] patterns)
        {
            var result = store.Query(new TrakHoundLogQuery { Patterns = patterns, SortOrder = TrakHoundSortOrder.Ascending });
            return result.Items.Select(o => o.Message).ToArray();
        }


        [Theory]
        [MemberData(nameof(Stores))]
        public void SenderPattern_ReturnsOnlyThatSendersEntries(string type)
        {
            var store = CreateStore(type);

            Assert.Equal(new[] { "d-1 Connected", "d-1 Disconnected" }, Query(store, "workers/**/Devices/d-1/**"));
            Assert.Equal(new[] { "d-2 Connected" }, Query(store, "workers/TrakHound.Package/**/devices/D-2"));
        }

        [Theory]
        [MemberData(nameof(Stores))]
        public void LoggerPattern_StillReturnsAllOfTheLoggersEntries(string type)
        {
            var store = CreateStore(type);

            Assert.Equal(4, Query(store, "workers/TrakHound.Package/**").Length);
            Assert.Equal(new[] { "Request" }, Query(store, "api/**"));
        }

        [Theory]
        [MemberData(nameof(Stores))]
        public void LoggerAndSenderPatterns_ReturnBoth(string type)
        {
            var store = CreateStore(type);

            Assert.Equal(new[] { "d-1 Connected", "d-1 Disconnected", "Request" }, Query(store, "workers/**/Devices/d-1", "api/**"));
        }

        [Theory]
        [MemberData(nameof(Stores))]
        public void SenderPattern_PagesAndCounts(string type)
        {
            var store = CreateStore(type);

            var first = store.Query(new TrakHoundLogQuery { Patterns = new[] { "workers/**/Devices/d-1" }, Take = 1, SortOrder = TrakHoundSortOrder.Descending });
            Assert.Equal(new[] { "d-1 Disconnected" }, first.Items.Select(o => o.Message));
            Assert.True(first.HasMore);

            var second = store.Query(new TrakHoundLogQuery { Patterns = new[] { "workers/**/Devices/d-1" }, Take = 1, SortOrder = TrakHoundSortOrder.Descending, Cursor = first.NextCursor });
            Assert.Equal(new[] { "d-1 Connected" }, second.Items.Select(o => o.Message));
            Assert.False(second.HasMore);

            var counts = store.QueryCounts(new TrakHoundLogQuery { Patterns = new[] { "workers/**/Devices/d-1" } });
            Assert.Equal(2, counts.Sum(o => o.Counts.Values.Sum()));
        }
    }
}
