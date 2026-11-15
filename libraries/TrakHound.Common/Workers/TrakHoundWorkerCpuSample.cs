// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Text.Json.Serialization;

namespace TrakHound.Workers
{
    /// <summary>
    /// A single CPU Usage measurement for a Worker
    /// </summary>
    public class TrakHoundWorkerCpuSample
    {
        /// <summary>
        /// The time (UTC) the sample was taken
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// The CPU usage (percent of total machine capacity, 0 - 100)
        /// </summary>
        [JsonPropertyName("usage")]
        public double Usage { get; set; }


        /// <summary>
        /// Creates an empty CPU Sample
        /// </summary>
        public TrakHoundWorkerCpuSample() { }

        /// <summary>
        /// Creates a CPU Sample with the specified timestamp and usage
        /// </summary>
        public TrakHoundWorkerCpuSample(DateTime timestamp, double usage)
        {
            Timestamp = timestamp;
            Usage = usage;
        }
    }
}
