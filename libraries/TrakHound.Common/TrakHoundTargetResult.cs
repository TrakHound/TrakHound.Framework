// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// Maps a query Target to the Uuid it resolved to
    /// </summary>
    public struct TrakHoundTargetResult
    {
        /// <summary>
        /// The Target that was queried
        /// </summary>
        public string Target { get; }

        /// <summary>
        /// The Uuid that the Target resolved to
        /// </summary>
        public string Uuid { get; }


        /// <summary>
        /// Creates a new TrakHoundTargetResult for the specified query Target and Uuid
        /// </summary>
        public TrakHoundTargetResult(string query, string uuid)
        {
            Target = query;
            Uuid = uuid;
        }
    }
}
