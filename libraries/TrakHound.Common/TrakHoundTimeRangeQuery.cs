// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// A query for a Target within a time range, grouped by a span of time
    /// </summary>
    public struct TrakHoundTimeRangeQuery
    {
        /// <summary>
        /// The Target (ex. Object path or Uuid) of the query
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// The start of the time range as Unix time (in nanoseconds)
        /// </summary>
        public long From { get; set; }

        /// <summary>
        /// The end of the time range as Unix time (in nanoseconds)
        /// </summary>
        public long To { get; set; }

        /// <summary>
        /// The length of each span within the time range (in nanoseconds)
        /// </summary>
        public long Span { get; set; }


        /// <summary>
        /// Creates a new TrakHoundTimeRangeQuery for the specified Target, time range, and span (all in nanoseconds)
        /// </summary>
        public TrakHoundTimeRangeQuery(string target, long from, long to, long span)
        {
            Target = target;
            From = from;
            To = to;
            Span = span;
        }


        /// <summary>
        /// Gets an Identifier for the time range and span of the query ("From:To:Span")
        /// </summary>
        public static string GetRangeId(TrakHoundTimeRangeQuery query)
        {
            return $"{query.From}:{query.To}:{query.Span}";
        }
    }
}
