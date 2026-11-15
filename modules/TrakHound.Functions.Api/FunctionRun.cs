// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Text.Json.Serialization;

namespace TrakHound.Functions
{
    public class FunctionRun
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("functionId")]
        public string FunctionId { get; set; }

        [JsonPropertyName("status")]
        public string Status { get; set; }

        [JsonPropertyName("statusCode")]
        public int StatusCode { get; set; }

        [JsonPropertyName("start")]
        public DateTime Start { get; set; }

        [JsonPropertyName("end")]
        public DateTime End { get; set; }

        [JsonPropertyName("duration")]
        public TimeSpan Duration { get; set; }

        /// <summary>
        /// What started the Run: scheduled, manual or api (null for Runs recorded before triggers were)
        /// </summary>
        [JsonPropertyName("trigger")]
        public string Trigger { get; set; }

        /// <summary>
        /// The Scheduled Task that started the Run
        /// </summary>
        [JsonPropertyName("taskId")]
        public string TaskId { get; set; }

        /// <summary>
        /// The version of the Function's Package that ran (null when it wasn't recorded)
        /// </summary>
        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        /// <summary>
        /// The Job whose Parameters the Run used
        /// </summary>
        [JsonPropertyName("jobId")]
        public string JobId { get; set; }

        /// <summary>
        /// CPU time in milliseconds (null when it wasn't recorded)
        /// </summary>
        [JsonPropertyName("cpuTime")]
        public double? CpuTime { get; set; }

        /// <summary>
        /// CPU usage over the Run (percent of total machine capacity, 0 - 100)
        /// </summary>
        [JsonPropertyName("cpuUsage")]
        public double? CpuUsage { get; set; }
    }
}
