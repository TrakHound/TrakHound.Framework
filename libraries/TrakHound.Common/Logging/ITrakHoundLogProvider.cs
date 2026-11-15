// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Logging
{
    /// <summary>
    /// Creates and tracks Loggers, forwards their Log Entries, and queries Log History
    /// </summary>
    public interface ITrakHoundLogProvider
    {
        /// <summary>
        /// Event Handler for when a new Log Entry is received
        /// </summary>
        event EventHandler<TrakHoundLogItem> LogEntryReceived;


        /// <summary>
        /// Gets the ID of the Provider's root Logger (ex. the Instance's Logger). Null if the Provider doesn't have one
        /// </summary>
        string GetLoggerId();

        /// <summary>
        /// Gets the Logger ID for the specified Logger Name (a Hash of the Name)
        /// </summary>
        string GetLoggerId(string name);


        /// <summary>
        /// Gets the Logger with the specified Name, adding it if it doesn't exist yet (null if the Name is null or empty)
        /// </summary>
        ITrakHoundLogger GetLoggerByName(string name);

        /// <summary>
        /// Gets an existing Logger by its ID (null if not found)
        /// </summary>
        ITrakHoundLogger GetLoggerById(string loggerId);

        /// <summary>
        /// Adds a new Logger with the specified Name. Returns null if a Logger with the Name already exists
        /// </summary>
        ITrakHoundLogger AddLogger(string name);


        /// <summary>
        /// Registers a live receiver of <see cref="LogEntryReceived"/> (ex. a Log Subscription) that needs entries at or more severe than the Level,
        /// so the Provider's Loggers keep raising them. Dispose the result when the receiver stops. Null if the Provider doesn't skip any Levels
        /// </summary>
        IDisposable RegisterReceiver(TrakHoundLogLevel minimumLevel) => null;


        /// <summary>
        /// Gets the Logger directories (parent paths of Logger Names) that match the Pattern
        /// </summary>
        string[] QueryDirectories(string pattern);

        /// <summary>
        /// Gets the IDs of the Loggers whose Names match the Pattern (see <see cref="TrakHoundLoggerPattern"/>)
        /// </summary>
        string[] QueryLoggerIds(string pattern);

        /// <summary>
        /// Gets the IDs of the Loggers whose Names match any of the Patterns (see <see cref="TrakHoundLoggerPattern"/>)
        /// </summary>
        string[] QueryLoggerIds(IEnumerable<string> patterns);


        /// <summary>
        /// Queries Log History for the Loggers matching the filter Pattern within the time range (all Levels)
        /// </summary>
        TrakHoundLogItem[] QueryLogs(string filter, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending);

        /// <summary>
        /// Queries Log History for the Loggers matching the filter Pattern within the time range, including only entries at or more severe than the minimum Level
        /// </summary>
        TrakHoundLogItem[] QueryLogsByMinimumLevel(string filter, TrakHoundLogLevel minimumLogLevel, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending);

        /// <summary>
        /// Queries Log History for the Loggers matching the filter Pattern within the time range, including only entries with exactly the specified Level
        /// </summary>
        TrakHoundLogItem[] QueryLogsByLevel(string filter, TrakHoundLogLevel logLevel, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending);


        /// <summary>
        /// Query Log History (entries are filtered, sorted and paged across all matching Loggers)
        /// </summary>
        TrakHoundLogQueryResult Query(TrakHoundLogQuery query) => null;

        /// <summary>
        /// Count the entries matching the Query (Skip, Take and Cursor are ignored), by Logger and Level
        /// </summary>
        TrakHoundLoggerLogCount[] QueryCounts(TrakHoundLogQuery query) => null;

        /// <summary>
        /// Get the Loggers known to the Log History that match the Pattern (including Loggers from previous runs)
        /// </summary>
        TrakHoundLoggerInformation[] QueryLoggers(string pattern) => null;
    }
}
