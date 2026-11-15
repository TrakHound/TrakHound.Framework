// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;

namespace TrakHound.Logging
{
    /// <summary>
    /// Base class for Log Providers. Tracks the Loggers that have been added and forwards their Log Entries
    /// (Log History queries return null unless overridden)
    /// </summary>
    public abstract class TrakHoundLogProviderBase : ITrakHoundLogProvider
    {
        private readonly Dictionary<string, ITrakHoundLogger> _loggers = new Dictionary<string, ITrakHoundLogger>(); // Logger ID => Logger
        private readonly Dictionary<string, string> _loggerPaths = new Dictionary<string, string>(); // Logger Path => LoggerId
        private readonly HashSet<string> _directories = new HashSet<string>();
        private readonly object _lock = new object();
        private readonly int[] _receiverCounts = new int[(int)TrakHoundLogLevel.Trace + 1]; // Log Level => Registered Receivers
        private readonly object _levelLock = new object();
        private volatile TrakHoundLogLevel _loggerMinimumLevel = TrakHoundLogLevel.Trace;

        /// <summary>
        /// A global minimum Log Level (defaults to Information). Not applied by the base Provider itself
        /// </summary>
        public static TrakHoundLogLevel MinimumLogLevel = TrakHoundLogLevel.Information;

        /// <summary>
        /// Entries at or more severe than this Level are always raised by the Provider's Loggers. They are infrequent, and
        /// permanent LogEntryReceived subscribers (ex. the Health trackers, which count Errors) aren't registered as receivers
        /// </summary>
        public const TrakHoundLogLevel AlwaysEnabledLevel = TrakHoundLogLevel.Information;

        /// <summary>
        /// The least severe Level that the Provider's Loggers raise : the most detailed of <see cref="AlwaysEnabledLevel"/>,
        /// the Provider's own output (<see cref="GetOutputMinimumLevel"/>) and any registered receiver (<see cref="RegisterReceiver"/>).
        /// Less severe entries are skipped before an Entry is created. A Logger with its own LogEntryReceived subscribers raises every Level
        /// </summary>
        public TrakHoundLogLevel LoggerMinimumLevel => _loggerMinimumLevel;


        /// <summary>
        /// Event Handler for when a new Log Entry is received
        /// </summary>
        public event EventHandler<TrakHoundLogItem> LogEntryReceived;


        /// <summary>
        /// Gets the ID of the Provider's root Logger (null unless overridden)
        /// </summary>
        public virtual string GetLoggerId() => null;

        /// <summary>
        /// Gets the Logger ID for the specified Logger Name (an MD5 Hash of the Name unless overridden)
        /// </summary>
        public virtual string GetLoggerId(string name) => name?.ToMD5Hash();


        /// <summary>
        /// Gets the IDs of the Loggers that have been added
        /// </summary>
        public string[] GetLoggerIds()
        {
            lock (_lock)
            {
                return _loggers.Keys.ToArray();
            }
        }

        /// <summary>
        /// Gets the Names of the Loggers that have been added
        /// </summary>
        public string[] GetLoggerNames()
        {
            lock (_lock)
            {
                return _loggerPaths.Keys.ToArray();
            }
        }

        /// <summary>
        /// Gets the Loggers that have been added
        /// </summary>
        public ITrakHoundLogger[] GetLoggers()
        {
            lock (_lock)
            {
                return _loggers.Values.ToArray();
            }
        }

        /// <summary>
        /// Gets the directories (each parent path, lowercase) of the Names of the Loggers that have been added
        /// </summary>
        public string[] GetDirectories()
        {
            lock (_lock)
            {
                return _directories.ToArray();
            }
        }

        /// <summary>
        /// Gets the Logger with the specified Name, adding it if it doesn't exist yet (null if the Name is null or empty)
        /// </summary>
        public ITrakHoundLogger GetLoggerByName(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var loggerId = GetLoggerId(name);
                if (loggerId != null)
                {
                    lock (_lock)
                    {
                        var logger = _loggers.GetValueOrDefault(loggerId);
                        if (logger == null)
                        {
                            logger = AddLogger(name);
                            //logger = new TrakHoundLogger(name);
                            //logger.LogEntryReceived += EntryReceived;

                            //_loggers.Add(loggerId, logger);
                            //_loggerPaths.Add(name, loggerId);

                            //var directory = TrakHoundPath.GetParentPath(name);
                            //if (!string.IsNullOrEmpty(directory)) _directories.Add(directory.ToLower());
                        }

                        return logger;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets an existing Logger by its ID (null if not found)
        /// </summary>
        public ITrakHoundLogger GetLoggerById(string loggerId)
        {
            if (!string.IsNullOrEmpty(loggerId))
            {
                lock (_lock)
                {
                    return _loggers.GetValueOrDefault(loggerId);
                    //if (logger == null)
                    //{
                    //    logger = new TrakHoundLogger(loggerId);
                    //    logger.LogEntryReceived += EntryReceived;
                    //    _loggers.Add(loggerId, logger);
                    //}

                    //return logger;
                }
            }

            return null;
        }

        /// <summary>
        /// Adds a new Logger with the specified Name and subscribes to its Log Entries. Returns null if a Logger with the Name already exists
        /// </summary>
        public ITrakHoundLogger AddLogger(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                var loggerId = GetLoggerId(name);
                if (loggerId != null)
                {
                    lock (_lock)
                    {
                        if (!_loggers.ContainsKey(loggerId))
                        {
                            var logger = new ProviderLogger(this, name);

                            _loggers.Add(loggerId, logger);
                            _loggerPaths.Add(name, loggerId);

                            var directory = TrakHoundPath.GetParentPath(name);
                            if (!string.IsNullOrEmpty(directory))
                            {
                                var directoryPaths = TrakHoundPath.GetPaths(directory);
                                if (!directoryPaths.IsNullOrEmpty())
                                {
                                    foreach (var path in directoryPaths)
                                    {
                                        _directories.Add(path.ToLower());
                                    }
                                }
                            }

                            OnLoggerAdded(logger);

                            return logger;
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Called when a new Logger is added (within the provider's lock)
        /// </summary>
        protected virtual void OnLoggerAdded(ITrakHoundLogger logger) { }

        private void EntryReceived(ITrakHoundLogger logger, TrakHoundLogItem item)
        {
            if (LogEntryReceived != null) LogEntryReceived.Invoke(logger, item);
            HandleEntryReceived(logger, item);
        }

        /// <summary>
        /// Called for each Log Entry written by one of the Provider's Loggers (after LogEntryReceived is raised). Override to write the Entry
        /// </summary>
        protected virtual void HandleEntryReceived(ITrakHoundLogger logger, TrakHoundLogItem item) { }

        /// <summary>
        /// Raise LogEntryReceived for an entry that didn't come from a TrakHound Logger (ex. an NLog or ASP.NET Core Logger)
        /// </summary>
        protected void RaiseLogEntryReceived(object sender, TrakHoundLogItem item)
        {
            LogEntryReceived?.Invoke(sender, item);
        }


        /// <summary>
        /// Registers a live receiver of <see cref="LogEntryReceived"/> (ex. a Log Subscription) that needs entries at or more severe than the Level.
        /// Registrations are counted, so the Level is released when the last receiver for it is disposed
        /// </summary>
        public IDisposable RegisterReceiver(TrakHoundLogLevel minimumLevel)
        {
            var index = Math.Clamp((int)minimumLevel, 0, _receiverCounts.Length - 1);

            lock (_levelLock) _receiverCounts[index]++;
            UpdateLoggerMinimumLevel();

            return new ReceiverRegistration(this, index);
        }

        private void ReleaseReceiver(int index)
        {
            lock (_levelLock)
            {
                if (_receiverCounts[index] > 0) _receiverCounts[index]--;
            }

            UpdateLoggerMinimumLevel();
        }

        /// <summary>
        /// The most detailed Level that the Provider writes in <see cref="HandleEntryReceived"/> (Trace, so nothing is skipped, unless overridden).
        /// Call <see cref="UpdateLoggerMinimumLevel"/> when it changes (ex. when a logging configuration is reloaded)
        /// </summary>
        protected virtual TrakHoundLogLevel GetOutputMinimumLevel() => TrakHoundLogLevel.Trace;

        /// <summary>
        /// Recalculates <see cref="LoggerMinimumLevel"/>
        /// </summary>
        protected void UpdateLoggerMinimumLevel()
        {
            lock (_levelLock)
            {
                TrakHoundLogLevel outputLevel;
                try
                {
                    outputLevel = GetOutputMinimumLevel();
                }
                catch
                {
                    outputLevel = TrakHoundLogLevel.Trace; // Skip nothing if the output Level can't be read
                }

                var level = outputLevel > AlwaysEnabledLevel ? outputLevel : AlwaysEnabledLevel;

                for (var i = _receiverCounts.Length - 1; i > (int)level; i--)
                {
                    if (_receiverCounts[i] > 0)
                    {
                        level = (TrakHoundLogLevel)i;
                        break;
                    }
                }

                _loggerMinimumLevel = level;
            }
        }


        /// <summary>
        /// A Logger created by the Provider. Entries are passed to the Provider directly (before the Logger's own LogEntryReceived subscribers)
        /// and Levels that nothing receives are skipped
        /// </summary>
        private sealed class ProviderLogger : TrakHoundLogger
        {
            private readonly TrakHoundLogProviderBase _provider;


            public ProviderLogger(TrakHoundLogProviderBase provider, string name) : base(name)
            {
                _provider = provider;
            }


            // Subscribers to the Logger's own LogEntryReceived (ex. the Api Provider's ApiLogUpdated) aren't registered with the Provider, so every Level is raised while there are any
            public override bool IsEnabled(TrakHoundLogLevel logLevel) => base.IsEnabled(logLevel) && (logLevel <= _provider._loggerMinimumLevel || HasLogEntryReceivedHandlers);

            protected override void OnLogEntryReceived(TrakHoundLogItem logEntry)
            {
                if (!IsEnabled(logEntry.LogLevel)) return;

                try
                {
                    _provider.EntryReceived(this, logEntry);
                }
                catch { } // Logging never fails the caller

                base.OnLogEntryReceived(logEntry);
            }
        }

        private sealed class ReceiverRegistration : IDisposable
        {
            private readonly TrakHoundLogProviderBase _provider;
            private readonly int _index;
            private int _disposed;


            public ReceiverRegistration(TrakHoundLogProviderBase provider, int index)
            {
                _provider = provider;
                _index = index;
            }

            public void Dispose()
            {
                if (System.Threading.Interlocked.Exchange(ref _disposed, 1) == 0) _provider.ReleaseReceiver(_index);
            }
        }


        /// <inheritdoc/>
        public virtual string[] QueryLoggerIds(string pattern) => null;

        /// <inheritdoc/>
        public virtual string[] QueryLoggerIds(IEnumerable<string> pattern) => null;

        /// <inheritdoc/>
        public virtual string[] QueryDirectories(string pattern) => null;


        /// <inheritdoc/>
        public virtual TrakHoundLogItem[] QueryLogs(string filter, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending) => null;
        
        /// <inheritdoc/>
        public virtual TrakHoundLogItem[] QueryLogsByMinimumLevel(string filter, TrakHoundLogLevel minimumLogLevel, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending) => null;
        
        /// <inheritdoc/>
        public virtual TrakHoundLogItem[] QueryLogsByLevel(string filter, TrakHoundLogLevel logLevel, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending) => null;


        /// <inheritdoc/>
        public virtual TrakHoundLogQueryResult Query(TrakHoundLogQuery query) => null;

        /// <inheritdoc/>
        public virtual TrakHoundLoggerLogCount[] QueryCounts(TrakHoundLogQuery query) => null;

        /// <inheritdoc/>
        public virtual TrakHoundLoggerInformation[] QueryLoggers(string pattern) => null;
    }
}
