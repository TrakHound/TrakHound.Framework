// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Logging.History;
using TrakHound.Logging;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// A Log History Query's Sender (the Source shown with each line) : matched ignoring case, and against the Logger Name for entries without a Sender
    /// </summary>
    public class LogQuerySenderTests
    {
        private static TrakHoundLogRecord CreateRecord(string loggerName, string sender)
        {
            return new TrakHoundLogRecord { LoggerName = loggerName, Sender = sender, Level = TrakHoundLogLevel.Information, Timestamp = 100, Message = "Message" };
        }

        private static bool IsMatch(string sender, TrakHoundLogRecord record)
        {
            var query = new TrakHoundLogQuery { Sender = sender };
            return new TrakHoundLogFilter(query, 0, 1000).IsMatch(record);
        }


        [Fact]
        public void Sender_MatchesIgnoringCase()
        {
            var record = CreateRecord("workers/package/id/Engine", "workers/package/id/Engine/Main");

            Assert.True(IsMatch("workers/package/id/Engine/Main", record));
            Assert.True(IsMatch("WORKERS/PACKAGE/ID/ENGINE/MAIN", record));
            Assert.False(IsMatch("workers/package/id/Engine/Engine", record));
        }

        [Fact]
        public void EntryWithoutSender_MatchesItsLoggerName()
        {
            var record = CreateRecord("api/package/:/Request/jobs", null);

            Assert.True(IsMatch("api/package/:/Request/jobs", record));
            Assert.False(IsMatch("api/package/:/Request/runs", record));
        }

        [Fact]
        public void NoSender_MatchesEverySender()
        {
            Assert.True(IsMatch(null, CreateRecord("instance", "instance")));
        }
    }
}
