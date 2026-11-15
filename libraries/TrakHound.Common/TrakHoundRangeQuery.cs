// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// A query for a Target within a range (From / To)
    /// </summary>
    public struct TrakHoundRangeQuery
    {
        /// <summary>
        /// The Target (ex. Object path or Uuid) of the query
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// The start of the range
        /// </summary>
        public long From { get; set; }

        /// <summary>
        /// The end of the range
        /// </summary>
        public long To { get; set; }


        /// <summary>
        /// Creates a new TrakHoundRangeQuery for the specified Target and range
        /// </summary>
        public TrakHoundRangeQuery(string target, long from, long to)
        {
            Target = target;
            From = from;
            To = to;
        }


        /// <summary>
        /// Gets an Identifier for the range of the query ("From:To")
        /// </summary>
        public static string GetRangeId(TrakHoundRangeQuery query)
        {
            return $"{query.From}:{query.To}";
        }
    }
}
