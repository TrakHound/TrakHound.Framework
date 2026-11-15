// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Logging;

namespace TrakHound.Instance.Health
{
    /// <summary>
    /// Counts the Error and Critical Log entries for each Logger over a recent window (one bucket per minute)
    /// </summary>
    public class TrakHoundLogErrorTracker : IDisposable
    {
        public static readonly TimeSpan Window = TimeSpan.FromMinutes(5);
        private const int _maxMessageLength = 300;

        private readonly ITrakHoundLogProvider _logProvider;
        private readonly Dictionary<string, LoggerState> _loggers = new Dictionary<string, LoggerState>(StringComparer.OrdinalIgnoreCase); // Logger Name => State
        private readonly object _lock = new object();


        private class LoggerState
        {
            public readonly Dictionary<long, int> Minutes = new Dictionary<long, int>(); // Unix Minute => Error Count
            public string LastMessage;
            public DateTime LastError;
        }

        public class LoggerErrors
        {
            public string Logger { get; set; }
            public int Count { get; set; }
            public string LastMessage { get; set; }
            public DateTime LastError { get; set; }
        }


        public TrakHoundLogErrorTracker(ITrakHoundLogProvider logProvider)
        {
            _logProvider = logProvider;
            if (_logProvider != null) _logProvider.LogEntryReceived += LogEntryReceived;
        }

        public void Dispose()
        {
            if (_logProvider != null) _logProvider.LogEntryReceived -= LogEntryReceived;
        }


        /// <summary>
        /// The Loggers with Errors within the Window (most Errors first)
        /// </summary>
        public IEnumerable<LoggerErrors> GetErrors(DateTime now)
        {
            var oldest = ToMinute(now - Window);

            lock (_lock)
            {
                var results = new List<LoggerErrors>();
                foreach (var logger in _loggers.ToList())
                {
                    foreach (var expired in logger.Value.Minutes.Keys.Where(o => o <= oldest).ToList()) logger.Value.Minutes.Remove(expired);

                    if (logger.Value.Minutes.Count == 0)
                    {
                        _loggers.Remove(logger.Key);
                        continue;
                    }

                    results.Add(new LoggerErrors
                    {
                        Logger = logger.Key,
                        Count = logger.Value.Minutes.Values.Sum(),
                        LastMessage = logger.Value.LastMessage,
                        LastError = logger.Value.LastError
                    });
                }

                return results.OrderByDescending(o => o.Count).ToList();
            }
        }


        private void LogEntryReceived(object sender, TrakHoundLogItem item)
        {
            try
            {
                if (item.LogLevel != TrakHoundLogLevel.Error && item.LogLevel != TrakHoundLogLevel.Critical) return;

                var loggerName = (sender as ITrakHoundLogger)?.Name ?? item.Sender ?? "(unknown)";
                var now = DateTime.UtcNow;
                var minute = ToMinute(now);

                lock (_lock)
                {
                    if (!_loggers.TryGetValue(loggerName, out var state))
                    {
                        state = new LoggerState();
                        _loggers.Add(loggerName, state);
                    }

                    state.Minutes[minute] = state.Minutes.GetValueOrDefault(minute) + 1;
                    state.LastMessage = GetFirstLine(item.Message);
                    state.LastError = now;
                }
            }
            catch { } // Never affect logging
        }

        private static long ToMinute(DateTime timestamp) => timestamp.ToUnixTimeSeconds() / 60;

        private static string GetFirstLine(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;

            var line = message.Split('\n', 2)[0].TrimEnd('\r');
            return line.Length > _maxMessageLength ? line.Substring(0, _maxMessageLength) + "..." : line;
        }
    }
}
