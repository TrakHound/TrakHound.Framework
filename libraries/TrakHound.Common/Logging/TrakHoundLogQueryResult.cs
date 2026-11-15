// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TrakHound.Logging
{
    /// <summary>
    /// The result of a <see cref="TrakHoundLogQuery"/>
    /// </summary>
    public class TrakHoundLogQueryResult
    {
        /// <summary>
        /// The matching entries, in the requested Sort Order
        /// </summary>
        public IEnumerable<TrakHoundLogItem> Items { get; set; }

        /// <summary>
        /// True if more entries match the Query than were returned
        /// </summary>
        public bool HasMore { get; set; }

        /// <summary>
        /// Pass as <see cref="TrakHoundLogQuery.Cursor"/> to get the next page (null if there are no more entries)
        /// </summary>
        public string NextCursor { get; set; }
    }

    /// <summary>
    /// The number of entries for a Logger, by Level
    /// </summary>
    public class TrakHoundLoggerLogCount
    {
        /// <summary>
        /// The ID of the Logger
        /// </summary>
        public string LoggerId { get; set; }

        /// <summary>
        /// The Name of the Logger
        /// </summary>
        public string LoggerName { get; set; }

        /// <summary>
        /// The number of entries for each Level (Levels without entries may be omitted)
        /// </summary>
        public Dictionary<TrakHoundLogLevel, long> Counts { get; set; } = new Dictionary<TrakHoundLogLevel, long>();

        /// <summary>
        /// The total number of entries across all Levels
        /// </summary>
        public long Total => Counts != null ? Counts.Values.Sum() : 0;
    }

    /// <summary>
    /// A Logger known to the Log History (including Loggers from previous runs)
    /// </summary>
    public class TrakHoundLoggerInformation
    {
        /// <summary>
        /// The ID of the Logger
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The Name of the Logger (ex. "api/TrakHound.Packages.Api")
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// When the Logger was first seen (Unix Timestamp in nanoseconds)
        /// </summary>
        [JsonPropertyName("firstSeen")]
        public long FirstSeen { get; set; }

        /// <summary>
        /// When the Logger was last seen (Unix Timestamp in nanoseconds)
        /// </summary>
        [JsonPropertyName("lastSeen")]
        public long LastSeen { get; set; }
    }


    /// <summary>
    /// The JSON representation of a <see cref="TrakHoundLogQueryResult"/>
    /// </summary>
    public class TrakHoundLogQueryResultJson
    {
        /// <summary>
        /// The matching entries, in the requested Sort Order
        /// </summary>
        [JsonPropertyName("items")]
        public IEnumerable<TrakHoundLogItemJson> Items { get; set; }

        /// <summary>
        /// True if more entries match the Query than were returned
        /// </summary>
        [JsonPropertyName("hasMore")]
        public bool HasMore { get; set; }

        /// <summary>
        /// The Cursor to pass to get the next page (null if there are no more entries)
        /// </summary>
        [JsonPropertyName("nextCursor")]
        public string NextCursor { get; set; }


        /// <summary>
        /// Creates an empty Result (for deserialization)
        /// </summary>
        public TrakHoundLogQueryResultJson() { }

        /// <summary>
        /// Creates a JSON Result from the specified Query Result
        /// </summary>
        public TrakHoundLogQueryResultJson(TrakHoundLogQueryResult result)
        {
            if (result != null)
            {
                Items = result.Items?.Select(o => new TrakHoundLogItemJson(o)).ToList();
                HasMore = result.HasMore;
                NextCursor = result.NextCursor;
            }
        }

        /// <summary>
        /// Converts to a <see cref="TrakHoundLogQueryResult"/> (Items is empty if null)
        /// </summary>
        public TrakHoundLogQueryResult ToResult()
        {
            return new TrakHoundLogQueryResult
            {
                Items = Items?.Select(o => o.ToItem()).ToList() ?? new List<TrakHoundLogItem>(),
                HasMore = HasMore,
                NextCursor = NextCursor
            };
        }
    }

    /// <summary>
    /// The JSON representation of a <see cref="TrakHoundLoggerLogCount"/>
    /// </summary>
    public class TrakHoundLoggerLogCountJson
    {
        /// <summary>
        /// The ID of the Logger
        /// </summary>
        [JsonPropertyName("loggerId")]
        public string LoggerId { get; set; }

        /// <summary>
        /// The Name of the Logger
        /// </summary>
        [JsonPropertyName("loggerName")]
        public string LoggerName { get; set; }

        /// <summary>
        /// The number of entries for each Level (keyed by Level name)
        /// </summary>
        [JsonPropertyName("counts")]
        public Dictionary<string, long> Counts { get; set; }


        /// <summary>
        /// Creates an empty Count (for deserialization)
        /// </summary>
        public TrakHoundLoggerLogCountJson() { }

        /// <summary>
        /// Creates a JSON Count from the specified Logger Log Count
        /// </summary>
        public TrakHoundLoggerLogCountJson(TrakHoundLoggerLogCount count)
        {
            if (count != null)
            {
                LoggerId = count.LoggerId;
                LoggerName = count.LoggerName;
                Counts = count.Counts?.ToDictionary(o => o.Key.ToString(), o => o.Value);
            }
        }

        /// <summary>
        /// Converts to a <see cref="TrakHoundLoggerLogCount"/> (Counts with unrecognized Level names are ignored)
        /// </summary>
        public TrakHoundLoggerLogCount ToCount()
        {
            var count = new TrakHoundLoggerLogCount();
            count.LoggerId = LoggerId;
            count.LoggerName = LoggerName;

            if (Counts != null)
            {
                foreach (var entry in Counts)
                {
                    if (System.Enum.TryParse<TrakHoundLogLevel>(entry.Key, true, out var level))
                    {
                        count.Counts[level] = entry.Value;
                    }
                }
            }

            return count;
        }
    }
}
