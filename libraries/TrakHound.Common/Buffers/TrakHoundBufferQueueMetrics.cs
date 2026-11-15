// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Buffers
{
    /// <summary>
    /// Metrics for an Operation Buffer's in-memory Working Queue
    /// </summary>
    public class TrakHoundBufferQueueMetrics
    {
        /// <summary>
        /// True while the Buffer is processing Items from the Working Queue
        /// </summary>
        [JsonPropertyName("isActive")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool IsActive { get; set; }

        /// <summary>
        /// The number of Items currently in the Working Queue
        /// </summary>
        [JsonPropertyName("itemCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int ItemCount { get; set; }

        /// <summary>
        /// The maximum number of Items the Working Queue can hold
        /// </summary>
        [JsonPropertyName("itemLimit")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int ItemLimit { get; set; }

        /// <summary>
        /// The total number of Items added to the Working Queue since the Buffer was created
        /// </summary>
        [JsonPropertyName("totalItemCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalItemCount { get; set; }

        /// <summary>
        /// The average number of Items added to the Working Queue per second (over the last 10 seconds)
        /// </summary>
        [JsonPropertyName("itemRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double ItemRate { get; set; }

        /// <summary>
        /// The time the Working Queue was last added to or processed (Unix Nanoseconds)
        /// </summary>
        [JsonPropertyName("lastUpdated")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long LastUpdated { get; set; }
    }
}
