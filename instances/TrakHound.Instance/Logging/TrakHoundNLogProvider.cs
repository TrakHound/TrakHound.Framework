// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog;
using TrakHound.Instance.Logging.History;
using TrakHound.Logging;

namespace TrakHound.Instance.Logging
{
    /// <summary>
    /// Writes TrakHound Logger entries to NLog (Console, Log History and any targets in nlog.config)
    /// and queries Log History from the <see cref="ITrakHoundLogStore"/>
    /// </summary>
    public class TrakHoundNLogProvider : TrakHoundLogProviderBase
    {
        private readonly Logger _logger;
        private readonly string _instanceId;
        private readonly ITrakHoundLogStore _store;


        /// <summary>
        /// The Log Event Property containing the readable Logger Name (ex. "api/TrakHound.Packages.Api") used by the Console layout
        /// </summary>
        public const string LoggerNameProperty = "name";


        public ITrakHoundLogStore Store => _store;


        public TrakHoundNLogProvider(string instanceId, Logger logger, ITrakHoundLogStore store)
        {
            _logger = logger;
            _instanceId = instanceId;
            _store = store;

            // Stream entries from NLog Loggers (ex. "system/TrakHound.Instance.InstanceApplication") like TrakHound Logger entries
            TrakHoundLogHistoryTarget.SystemRecordsWritten += SystemRecordsWritten;

            // Skip the Levels that no NLog Rule writes (re-read when nlog.config is reloaded). Subscribed after InstanceApplication's
            // handlers, so the Log History Rule has already been added to the new configuration
            if (_logger != null) _logger.Factory.ConfigurationChanged += (s, args) => UpdateLoggerMinimumLevel();
            UpdateLoggerMinimumLevel();
        }

        private void SystemRecordsWritten(IReadOnlyList<TrakHoundLogRecord> records)
        {
            foreach (var record in records)
            {
                RaiseLogEntryReceived(this, record.ToItem());
            }
        }

        private string GetLogNameBase()
        {
            return $"trakhound.instance.{_instanceId}";
        }


        public override string GetLoggerId()
        {
            return $"trakhound.instance.{_instanceId}";
        }

        public override string GetLoggerId(string name)
        {
            return TrakHoundPath.Combine(GetLogNameBase(), name).ToMD5Hash();
        }


        protected override void OnLoggerAdded(ITrakHoundLogger logger)
        {
            // Index the Logger (by Name) so its history can be found, including after a restart
            _store?.RegisterLogger(logger.Id, logger.Name);
        }

        protected override TrakHoundLogLevel GetOutputMinimumLevel()
        {
            // Entries are all written through _logger, so its Rules (nlog.config and the Log History Rule) decide which Levels reach a Target
            if (_logger == null) return TrakHoundLogLevel.Critical;
            if (_logger.IsTraceEnabled) return TrakHoundLogLevel.Trace;
            if (_logger.IsDebugEnabled) return TrakHoundLogLevel.Debug;
            if (_logger.IsInfoEnabled) return TrakHoundLogLevel.Information;
            if (_logger.IsWarnEnabled) return TrakHoundLogLevel.Warning;
            if (_logger.IsErrorEnabled) return TrakHoundLogLevel.Error;
            return TrakHoundLogLevel.Critical;
        }

        protected override void HandleEntryReceived(ITrakHoundLogger logger, TrakHoundLogItem entry)
        {
            var level = TrakHoundLogHistoryTarget.ToNLogLevel(entry.LogLevel);
            if (_logger == null || !_logger.IsEnabled(level)) return;

            // The Logger ID is the NLog Logger Name (used by the Log History Target) and the readable
            // Logger Name, Sender and Code are included as properties
            var logEvent = new LogEventInfo();
            logEvent.LoggerName = logger.Id;
            logEvent.Message = entry.Message;
            logEvent.Level = level;
            if (entry.Timestamp > 0) logEvent.TimeStamp = entry.Timestamp.ToLocalDateTime();

            logEvent.Properties[TrakHoundLogHistoryTarget.EntryProperty] = true;
            logEvent.Properties[LoggerNameProperty] = logger.Name;
            if (!string.IsNullOrEmpty(entry.Sender) && entry.Sender != logger.Name) logEvent.Properties[TrakHoundLogHistoryTarget.SenderProperty] = entry.Sender;
            if (!string.IsNullOrEmpty(entry.Code)) logEvent.Properties[TrakHoundLogHistoryTarget.CodeProperty] = entry.Code;

            _logger.Log(logEvent);
        }


        public override TrakHoundLogQueryResult Query(TrakHoundLogQuery query)
        {
            return _store?.Query(query);
        }

        public override TrakHoundLoggerLogCount[] QueryCounts(TrakHoundLogQuery query)
        {
            return _store?.QueryCounts(query);
        }

        public override TrakHoundLoggerInformation[] QueryLoggers(string pattern)
        {
            var loggers = _store?.GetLoggers();
            if (loggers == null) return null;

            return loggers
                .Where(o => TrakHoundLoggerPattern.IsMatch(pattern, o.Name) || string.Equals(pattern, o.Id, StringComparison.OrdinalIgnoreCase))
                .OrderBy(o => o.Name)
                .ToArray();
        }

        public override string[] QueryLoggerIds(string pattern)
        {
            return QueryLoggers(pattern)?.Select(o => o.Id).ToArray();
        }

        public override string[] QueryLoggerIds(IEnumerable<string> patterns)
        {
            var loggers = _store?.GetLoggers();
            if (loggers == null) return null;

            return loggers.Where(o => TrakHoundLoggerPattern.IsMatch(patterns, o.Name)).Select(o => o.Id).ToArray();
        }

        public override string[] QueryDirectories(string pattern)
        {
            var loggers = _store?.GetLoggers();
            if (loggers == null) return null;

            var directories = new HashSet<string>();
            foreach (var logger in loggers)
            {
                var directory = TrakHoundPath.GetParentPath(logger.Name);
                if (!string.IsNullOrEmpty(directory))
                {
                    foreach (var path in TrakHoundPath.GetPaths(directory)) directories.Add(path.ToLower());
                }
            }

            return directories.Where(o => MatchDirectory(pattern, o)).OrderBy(o => o).ToArray();
        }


        public override TrakHoundLogItem[] QueryLogs(string filter, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            return QueryLogsByMinimumLevel(filter, TrakHoundLogLevel.Trace, from, to, skip, take, sortOrder);
        }

        public override TrakHoundLogItem[] QueryLogsByMinimumLevel(string filter, TrakHoundLogLevel minimumLogLevel, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            var query = new TrakHoundLogQuery(filter, from, to) { MinimumLevel = minimumLogLevel, Skip = skip, Take = take, SortOrder = sortOrder };
            return Query(query)?.Items?.ToArray();
        }

        public override TrakHoundLogItem[] QueryLogsByLevel(string filter, TrakHoundLogLevel logLevel, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            var query = new TrakHoundLogQuery(filter, from, to) { Levels = new[] { logLevel }, Skip = skip, Take = take, SortOrder = sortOrder };
            return Query(query)?.Items?.ToArray();
        }


        private static bool MatchDirectory(string pattern, string directory)
        {
            if (TrakHoundLoggerPattern.IsMatchAll(pattern)) return true;
            if (pattern == "/") return TrakHoundPath.GetRoot(directory)?.ToLower() == directory.ToLower();

            return TrakHoundLoggerPattern.IsMatch(pattern, directory);
        }
    }
}
