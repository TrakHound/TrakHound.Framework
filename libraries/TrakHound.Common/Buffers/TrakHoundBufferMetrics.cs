// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Buffers
{
    /// <summary>
    /// Runtime Metrics for an Operation Buffer (its Working Queue and File Buffer)
    /// </summary>
    public class TrakHoundBufferMetrics
    {
        /// <summary>
        /// The unique Identifier of the Buffer
        /// </summary>
        [JsonPropertyName("bufferId")]
        public string BufferId { get; set; }

        /// <summary>
        /// The Identifier of the Driver that owns the Buffer
        /// </summary>
        [JsonPropertyName("driverId")]
        public string DriverId { get; set; }

        /// <summary>
        /// The display Name of the Buffer
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The type of Operation the Buffer handles
        /// </summary>
        [JsonPropertyName("operationType")]
        public string OperationType { get; set; }

        /// <summary>
        /// The time the Buffer was last started (Unix Nanoseconds)
        /// </summary>
        [JsonPropertyName("startTime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long StartTime { get; set; }

        /// <summary>
        /// The time the Buffer was last stopped (Unix Nanoseconds)
        /// </summary>
        [JsonPropertyName("stopTime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long StopTime { get; set; }

        /// <summary>
        /// The time the Buffer was last started or stopped (Unix Nanoseconds)
        /// </summary>
        [JsonPropertyName("lastUpdated")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long LastUpdated { get; set; }

        /// <summary>
        /// Metrics for the in-memory Working Queue
        /// </summary>
        [JsonPropertyName("queue")]
        public TrakHoundBufferQueueMetrics Queue { get; set; }

        /// <summary>
        /// Metrics for the "Store and Forward" File Buffer
        /// </summary>
        [JsonPropertyName("fileBuffer")]
        public TrakHoundFileBufferMetrics FileBuffer { get; set; }


        /// <summary>
        /// Creates a new set of Buffer Metrics with empty Queue and File Buffer Metrics
        /// </summary>
        public TrakHoundBufferMetrics()
        {
            Queue = new TrakHoundBufferQueueMetrics();
            FileBuffer = new TrakHoundFileBufferMetrics();
        }
    }
}
