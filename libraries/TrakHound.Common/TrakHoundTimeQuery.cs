// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// A query for a Target at a point in time
    /// </summary>
    public struct TrakHoundTimeQuery
    {
        /// <summary>
        /// The Target (ex. Object path or Uuid) of the query
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// The time of the query as Unix time (in nanoseconds)
        /// </summary>
        public long Timestamp { get; set; }


        /// <summary>
        /// Creates a new TrakHoundTimeQuery for the specified Target and timestamp (Unix nanoseconds)
        /// </summary>
        public TrakHoundTimeQuery(string target, long timestamp)
        {
            Target = target;
            Timestamp = timestamp;
        }
    }
}
