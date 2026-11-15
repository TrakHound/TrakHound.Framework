// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// A count of items for a Target
    /// </summary>
    public class TrakHoundCount
    { 
        /// <summary>
        /// The Target (ex. Object path or Uuid) that was counted
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// The number of items
        /// </summary>
        public long Count { get; set; }


        /// <summary>
        /// Creates a new TrakHoundCount for the specified Target and Count
        /// </summary>
        public TrakHoundCount(string target, long count)
        {
            Target = target;
            Count = count;
        }
    }
}
