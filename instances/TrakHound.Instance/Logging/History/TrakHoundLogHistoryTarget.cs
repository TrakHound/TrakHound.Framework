// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog;
using NLog.Common;
using NLog.Config;
using NLog.Layouts;
using NLog.Targets;
using NLog.Targets.Wrappers;
using TrakHound.Logging;
using LogLevel = NLog.LogLevel;

namespace TrakHound.Instance.Logging.History
{
    /// <summary>
    /// NLog Target that writes to the Log History Store. Registered by the Instance (not nlog.config)
    /// so that Log History always matches what the Logs API and Explorer query.
    /// </summary>
    [Target("TrakHoundLogHistory")]
    public sealed class TrakHoundLogHistoryTarget : Target
    {
        public const string TargetName = "trakhound-log-history";

        /// <summary>
        /// Log Event Properties set by <see cref="TrakHoundNLogProvider"/> for TrakHound Logger entries
        /// </summary>
        public const string EntryProperty = "trakhound";
        public const string SenderProperty = "sender";
        public const string CodeProperty = "code";

        // NLog Loggers that aren't TrakHound Loggers (ex. "TrakHound.Instance.InstanceApplication") are listed under this directory
        public const string SystemLoggerDirectory = "system";

        // Legacy default File Target (replaced by Log History)
        private static readonly string[] _legacyFileNames = new[]
        {
            @"logs\${logger}\${shortdate}.${lowercase:${level}}.log",
            @"logs/${logger}/${shortdate}.${lowercase:${level}}.log"
        };

        private readonly ITrakHoundLogStore _store;
        private readonly LogLevel _frameworkMinimumLevel;


        /// <summary>
        /// Raised with the records from NLog Loggers (listed under "system") after they are written, so they can be streamed
        /// like TrakHound Logger entries (which are raised by the Log Provider). Static since the Target is created before the
        /// Log Provider and again whenever nlog.config is reloaded
        /// </summary>
        public static event Action<IReadOnlyList<TrakHoundLogRecord>> SystemRecordsWritten;


        public TrakHoundLogHistoryTarget(ITrakHoundLogStore store, TrakHoundLogLevel frameworkMinimumLevel)
        {
            _store = store;
            _frameworkMinimumLevel = ToNLogLevel(frameworkMinimumLevel);
            Name = TargetName + "-store";
        }


        protected override void Write(LogEventInfo logEvent)
        {
            var record = ToRecord(logEvent, out var isSystem);
            if (record != null)
            {
                WriteRecords(new[] { record });
                if (isSystem) RaiseSystemRecordsWritten(new[] { record });
            }
        }

        protected override void Write(IList<AsyncLogEventInfo> logEvents)
        {
            var records = new List<TrakHoundLogRecord>(logEvents.Count);
            List<TrakHoundLogRecord> systemRecords = null;
            foreach (var logEvent in logEvents)
            {
                var record = ToRecord(logEvent.LogEvent, out var isSystem);
                if (record != null)
                {
                    records.Add(record);
                    if (isSystem) (systemRecords ??= new List<TrakHoundLogRecord>()).Add(record);
                }
            }

            Exception exception = null;
            try
            {
                WriteRecords(records);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            foreach (var logEvent in logEvents)
            {
                logEvent.Continuation(exception);
            }

            if (systemRecords != null) RaiseSystemRecordsWritten(systemRecords);
        }

        private static void RaiseSystemRecordsWritten(IReadOnlyList<TrakHoundLogRecord> records)
        {
            try
            {
                SystemRecordsWritten?.Invoke(records);
            }
            catch (Exception ex)
            {
                // Never let a subscriber break Log History
                InternalLogger.Warn(ex, "TrakHound Log History : Error Raising System Records");
            }
        }

        private void WriteRecords(IReadOnlyList<TrakHoundLogRecord> records)
        {
            if (records.Count > 0) _store.Write(records);
        }

        private TrakHoundLogRecord ToRecord(LogEventInfo logEvent, out bool isSystem)
        {
            isSystem = false;
            if (logEvent == null) return null;

            var record = new TrakHoundLogRecord();
            record.Timestamp = logEvent.TimeStamp.ToUnixTime();
            record.Level = ToTrakHoundLevel(logEvent.Level);
            record.Message = logEvent.FormattedMessage;
            record.Exception = logEvent.Exception?.ToString();

            if (logEvent.HasProperties && logEvent.Properties.ContainsKey(EntryProperty))
            {
                // TrakHound Logger : the NLog Logger Name is the TrakHound Logger ID
                record.LoggerId = logEvent.LoggerName;
                record.LoggerName = logEvent.Properties.TryGetValue(TrakHoundNLogProvider.LoggerNameProperty, out var name) ? name?.ToString() : null;
                record.Sender = logEvent.Properties.TryGetValue(SenderProperty, out var sender) ? sender?.ToString() : null;
                record.Code = logEvent.Properties.TryGetValue(CodeProperty, out var code) ? code?.ToString() : null;
            }
            else
            {
                // NLog Logger (ex. "TrakHound.Instance.InstanceApplication", "Microsoft.Hosting.Lifetime")
                var loggerName = logEvent.LoggerName ?? "unknown";
                if (IsFrameworkLogger(loggerName) && logEvent.Level < _frameworkMinimumLevel) return null;

                // The ID matches the legacy NLog directory name so that earlier logs remain with the Logger
                record.LoggerId = GetSystemLoggerId(loggerName);
                record.LoggerName = $"{SystemLoggerDirectory}/{loggerName}";
                isSystem = true;
            }

            record.LoggerName ??= record.LoggerId;
            record.Sender ??= record.LoggerName;
            return record;
        }

        private static bool IsFrameworkLogger(string loggerName)
        {
            return loggerName.StartsWith("Microsoft.", StringComparison.Ordinal) || loggerName.StartsWith("System.", StringComparison.Ordinal);
        }

        public static string GetSystemLoggerId(string loggerName)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var id = new string(loggerName.Select(c => invalid.Contains(c) ? '_' : c).ToArray());
            return string.IsNullOrEmpty(id) || id == TrakHoundLogStoreBase.ArchiveDirectoryName ? loggerName.ToMD5Hash() : id;
        }


        /// <summary>
        /// Add the Log History Target to the NLog Configuration (and remove the legacy default File Target from its rules).
        /// Called again whenever nlog.config is reloaded.
        /// </summary>
        /// <returns>Messages describing changes made to the Configuration (to log once the Configuration is applied)</returns>
        public static List<string> Apply(LoggingConfiguration configuration, ITrakHoundLogStore store, TrakHoundLogConfiguration logConfiguration)
        {
            var messages = new List<string>();
            if (configuration == null || store == null) return messages;

            try
            {
                // Remove a previously added Log History Target (ex. on a reload of the same configuration)
                var existing = configuration.FindTargetByName(TargetName);
                if (existing != null) configuration.RemoveTarget(TargetName);

                // The legacy default File Target writes the same entries in the old format, so remove it from all Rules
                foreach (var target in configuration.AllTargets.OfType<FileTarget>().ToList())
                {
                    if (target.FileName is SimpleLayout fileName && _legacyFileNames.Contains(fileName.OriginalText, StringComparer.OrdinalIgnoreCase))
                    {
                        foreach (var rule in configuration.LoggingRules)
                        {
                            rule.Targets.Remove(target);
                        }

                        messages.Add($"Legacy Log File Target ({target.Name}) replaced by Log History ({logConfiguration.ProviderType} : {store.Directory})");
                    }
                }

                var historyTarget = new TrakHoundLogHistoryTarget(store, logConfiguration.FrameworkMinimumLogLevel);
                // Log History is best-effort : when storage falls behind (ex. during Maintenance), entries are dropped instead of blocking the threads that log
                var asyncTarget = new AsyncTargetWrapper(TargetName, historyTarget)
                {
                    BatchSize = 1000,
                    TimeToSleepBetweenBatches = 50,
                    QueueLimit = 100000,
                    OverflowAction = AsyncTargetWrapperOverflowAction.Discard
                };
                configuration.AddTarget(asyncTarget);

                // First (and not Final) so that rules in nlog.config marked 'final' don't stop entries reaching Log History
                var minimumLevel = ToNLogLevel(logConfiguration.MinimumLogLevel);
                var frameworkLevel = ToNLogLevel(logConfiguration.FrameworkMinimumLogLevel);
                var ruleLevel = minimumLevel.Ordinal < frameworkLevel.Ordinal ? minimumLevel : frameworkLevel;
                configuration.LoggingRules.Insert(0, new LoggingRule("*", ruleLevel, LogLevel.Fatal, asyncTarget) { RuleName = TargetName });

                // The "*" Rule enables every Level from ruleLevel for framework Loggers too, so (ex.) ASP.NET Core would create
                // "Request starting/finished" entries for every Request only for Log History to drop them (frameworkMinimumLevel).
                // These Rules stop framework Loggers below the lowest Level that Log History or another Rule writes for them
                foreach (var pattern in _frameworkLoggerPatterns)
                {
                    var ruleName = $"{TargetName}.{pattern}";
                    configuration.RemoveRuleByName(ruleName);

                    var finalLevel = GetFrameworkFinalLevel(configuration, pattern, frameworkLevel);
                    if (finalLevel.Ordinal > LogLevel.Trace.Ordinal)
                    {
                        configuration.LoggingRules.Insert(0, new LoggingRule { RuleName = ruleName, LoggerNamePattern = pattern, FinalMinLevel = finalLevel });
                    }
                }
            }
            catch (Exception ex)
            {
                InternalLogger.Error(ex, "TrakHound Log History : Error Configuring NLog");
            }

            return messages;
        }


        private static readonly string[] _frameworkLoggerPatterns = new[] { "Microsoft.*", "System.*" };

        /// <summary>
        /// The lowest Level written for a framework Logger : frameworkMinimumLevel, or a lower Level that another Rule (ex. in nlog.config) writes for it
        /// </summary>
        private static LogLevel GetFrameworkFinalLevel(LoggingConfiguration configuration, string pattern, LogLevel frameworkLevel)
        {
            var finalLevel = frameworkLevel;
            var sampleName = pattern.TrimEnd('*') + "AspNetCore";

            foreach (var rule in configuration.LoggingRules)
            {
                if (rule.RuleName == TargetName || rule.RuleName?.StartsWith(TargetName + ".") == true) continue;
                if (rule.Targets.Count < 1 || !rule.NameMatches(sampleName)) continue;

                foreach (var level in rule.Levels)
                {
                    if (level.Ordinal < finalLevel.Ordinal) finalLevel = level;
                }
            }

            return finalLevel;
        }

        public static LogLevel ToNLogLevel(TrakHoundLogLevel level)
        {
            switch (level)
            {
                case TrakHoundLogLevel.Critical: return LogLevel.Fatal;
                case TrakHoundLogLevel.Error: return LogLevel.Error;
                case TrakHoundLogLevel.Warning: return LogLevel.Warn;
                case TrakHoundLogLevel.Debug: return LogLevel.Debug;
                case TrakHoundLogLevel.Trace: return LogLevel.Trace;
                default: return LogLevel.Info;
            }
        }

        public static TrakHoundLogLevel ToTrakHoundLevel(LogLevel level)
        {
            if (level == LogLevel.Fatal) return TrakHoundLogLevel.Critical;
            if (level == LogLevel.Error) return TrakHoundLogLevel.Error;
            if (level == LogLevel.Warn) return TrakHoundLogLevel.Warning;
            if (level == LogLevel.Debug) return TrakHoundLogLevel.Debug;
            if (level == LogLevel.Trace) return TrakHoundLogLevel.Trace;
            return TrakHoundLogLevel.Information;
        }
    }
}
