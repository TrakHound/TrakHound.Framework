// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Logging
{
    /// <summary>
    /// A query of Log History
    /// </summary>
    public class TrakHoundLogQuery
    {
        /// <summary>
        /// Logger Name Patterns to include (see <see cref="TrakHoundLoggerPattern"/>). Empty includes all Loggers.
        /// </summary>
        public IEnumerable<string> Patterns { get; set; }

        /// <summary>
        /// The start of the time range (inclusive). Times without a Kind are treated as Local.
        /// </summary>
        public DateTime From { get; set; }

        /// <summary>
        /// The end of the time range (inclusive). Times without a Kind are treated as Local.
        /// </summary>
        public DateTime To { get; set; }

        /// <summary>
        /// The least severe Level to include (used when <see cref="Levels"/> is not set)
        /// </summary>
        public TrakHoundLogLevel MinimumLevel { get; set; } = TrakHoundLogLevel.Trace;

        /// <summary>
        /// The exact Levels to include. Overrides <see cref="MinimumLevel"/> when set.
        /// </summary>
        public IEnumerable<TrakHoundLogLevel> Levels { get; set; }

        /// <summary>
        /// Case-insensitive text that the Message must contain
        /// </summary>
        public string Search { get; set; }

        /// <summary>
        /// The Code that entries must have
        /// </summary>
        public string Code { get; set; }

        /// <summary>
        /// The Sender (Source) that entries must have, ignoring case (an entry without a Sender has its Logger Name)
        /// </summary>
        public string Sender { get; set; }

        /// <summary>
        /// The number of entries to skip (after the <see cref="Cursor"/>, if set)
        /// </summary>
        public int Skip { get; set; }

        /// <summary>
        /// The maximum number of entries to return
        /// </summary>
        public int Take { get; set; } = 1000;

        /// <summary>
        /// The order of the entries by Timestamp (defaults to Ascending, oldest first)
        /// </summary>
        public TrakHoundSortOrder SortOrder { get; set; } = TrakHoundSortOrder.Ascending;

        /// <summary>
        /// Continue from a previous result's <see cref="TrakHoundLogQueryResult.NextCursor"/>
        /// (stable paging while new entries are written)
        /// </summary>
        public string Cursor { get; set; }


        /// <summary>
        /// Creates an empty Query (all Loggers)
        /// </summary>
        public TrakHoundLogQuery() { }

        /// <summary>
        /// Creates a Query for the Loggers matching the Pattern (all Loggers if null or empty) within the time range
        /// </summary>
        public TrakHoundLogQuery(string pattern, DateTime from, DateTime to)
        {
            Patterns = !string.IsNullOrEmpty(pattern) ? new[] { pattern } : null;
            From = from;
            To = to;
        }


        /// <summary>
        /// Returns true if the Level is included by this Query
        /// </summary>
        public bool IsLevelMatch(TrakHoundLogLevel level)
        {
            if (Levels != null)
            {
                foreach (var l in Levels)
                {
                    if (l == level) return true;
                }
                return false;
            }

            // Lower values are more severe (Critical = 0 ... Trace = 5)
            return level <= MinimumLevel;
        }
    }
}
