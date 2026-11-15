// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Logging;

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to list, query, and subscribe to the Logs of a TrakHound Instance
    /// </summary>
    public interface ITrakHoundLogsClient
    {
        /// <summary>
        /// Lists the Logger IDs that match the specified pattern
        /// </summary>
        Task<IEnumerable<string>> List(string pattern);

        /// <summary>
        /// Lists the Log directories that match the specified pattern
        /// </summary>
        Task<IEnumerable<string>> ListDirectories(string pattern);

        /// <summary>
        /// Queries the Log entries for Loggers matching the pattern between the from and to timestamps (null if none are found)
        /// </summary>
        Task<IEnumerable<TrakHoundLogItem>> Query(string pattern, DateTime from, DateTime to, TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending);

        /// <summary>
        /// Subscribes to new Log entries for Loggers matching the pattern at or above the minimum Log Level
        /// </summary>
        Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> Subscribe(string pattern, TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace);


        /// <summary>
        /// Query Log History with filtering (Levels, Search, Code) and paging (HasMore / Cursor)
        /// </summary>
        async Task<TrakHoundLogQueryResult> Query(TrakHoundLogQuery query)
        {
            // Default for Clients that only support the basic Query
            if (query == null) return null;

            var pattern = query.Patterns != null ? System.Linq.Enumerable.FirstOrDefault(query.Patterns) : null;
            var items = await Query(pattern, query.From, query.To, query.MinimumLevel, query.Skip, query.Take, query.SortOrder);
            return new TrakHoundLogQueryResult { Items = items ?? new List<TrakHoundLogItem>() };
        }

        /// <summary>
        /// Count the entries matching the Query, by Logger and Level
        /// </summary>
        Task<IEnumerable<TrakHoundLoggerLogCount>> QueryCounts(TrakHoundLogQuery query) => Task.FromResult<IEnumerable<TrakHoundLoggerLogCount>>(null);

        /// <summary>
        /// Get the Loggers known to the Log History (including Loggers from previous runs)
        /// </summary>
        Task<IEnumerable<TrakHoundLoggerInformation>> QueryLoggers(string pattern) => Task.FromResult<IEnumerable<TrakHoundLoggerInformation>>(null);
    }
}
