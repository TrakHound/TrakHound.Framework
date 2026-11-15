// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using TrakHound.Logging;

namespace TrakHound.Clients
{
    /// <summary>
    /// Subscribes a Stream to the Log Provider's entries from the Loggers that match a Filter (ex. a Worker's or an App's Loggers)
    /// </summary>
    internal static class TrakHoundInstanceLogSubscription
    {
        public static ITrakHoundStream<IEnumerable<TrakHoundLogItem>> Subscribe(ITrakHoundLogProvider logProvider, string[] logFilter, TrakHoundLogLevel minimumLevel)
        {
            if (logProvider == null) return null;

            var consumer = new TrakHoundStream<IEnumerable<TrakHoundLogItem>>();

            EventHandler<TrakHoundLogItem> handler = (sender, item) =>
            {
                // Match the Logger Name (and the Sender) the same way as Log Queries and the Logs Client
                if (item.LogLevel <= minimumLevel && TrakHoundLoggerPattern.IsMatch(logFilter, (sender as ITrakHoundLogger)?.Name ?? item.Sender, item.Sender))
                {
                    _ = consumer.Push(new TrakHoundLogItem[] { item });
                }
            };

            logProvider.LogEntryReceived += handler;

            // So the Log Provider's Loggers raise the Levels this Consumer receives
            var receiver = logProvider.RegisterReceiver(minimumLevel);

            consumer.OnDisposed = () =>
            {
                logProvider.LogEntryReceived -= handler;
                receiver?.Dispose();
            };

            return consumer;
        }
    }
}
