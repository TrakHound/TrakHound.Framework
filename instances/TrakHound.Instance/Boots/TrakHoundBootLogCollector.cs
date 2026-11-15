// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;
using TrakHound.Logging;

namespace TrakHound.Instance.Boots
{
    /// <summary>
    /// Collects the Warning and Error Log entries written while it is open (ex. during a Runtime Start)
    /// </summary>
    public class TrakHoundBootLogCollector : IDisposable
    {
        public const int MaxEntries = 20;
        private const int _maxMessageLength = 500;

        private readonly ITrakHoundLogProvider _logProvider;
        private readonly List<TrakHoundInstanceBootLogEntry> _errors = new List<TrakHoundInstanceBootLogEntry>();
        private readonly List<TrakHoundInstanceBootLogEntry> _warnings = new List<TrakHoundInstanceBootLogEntry>();
        private readonly object _lock = new object();
        private int _warningCount;
        private int _errorCount;
        private bool _disposed;


        public TrakHoundBootLogCollector(ITrakHoundLogProvider logProvider)
        {
            // Warnings and Errors are always raised by the Provider's Loggers, so no Receiver needs to be registered
            _logProvider = logProvider;
            if (_logProvider != null) _logProvider.LogEntryReceived += LogEntryReceived;
        }

        public void Dispose()
        {
            lock (_lock)
            {
                if (_disposed) return;
                _disposed = true;
            }

            if (_logProvider != null) _logProvider.LogEntryReceived -= LogEntryReceived;
        }


        /// <summary>
        /// The entries collected so far (Errors first when there are more than fit)
        /// </summary>
        public TrakHoundInstanceBootLog GetLog()
        {
            lock (_lock)
            {
                var entries = _errors.Concat(_warnings).Take(MaxEntries).OrderBy(o => o.Timestamp).ToList();

                return new TrakHoundInstanceBootLog
                {
                    WarningCount = _warningCount,
                    ErrorCount = _errorCount,
                    Entries = entries.Count > 0 ? entries : null
                };
            }
        }


        public void Add(TrakHoundLogLevel level, string logger, string message)
        {
            var isError = level == TrakHoundLogLevel.Error || level == TrakHoundLogLevel.Critical;
            if (!isError && level != TrakHoundLogLevel.Warning) return;

            lock (_lock)
            {
                if (_disposed) return;

                if (isError) _errorCount++;
                else _warningCount++;

                // Only the first entries are kept (the counts include the rest)
                var list = isError ? _errors : _warnings;
                if (list.Count < MaxEntries)
                {
                    list.Add(new TrakHoundInstanceBootLogEntry
                    {
                        Timestamp = DateTime.UtcNow,
                        Level = level.ToString(),
                        Logger = logger,
                        Message = GetFirstLine(message)
                    });
                }
            }
        }

        private void LogEntryReceived(object sender, TrakHoundLogItem item)
        {
            try
            {
                Add(item.LogLevel, (sender as ITrakHoundLogger)?.Name ?? item.Sender, item.Message);
            }
            catch { } // Never affect logging
        }

        private static string GetFirstLine(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;

            var line = message.Split('\n', 2)[0].TrimEnd('\r');
            return line.Length > _maxMessageLength ? line.Substring(0, _maxMessageLength) + "..." : line;
        }
    }
}
