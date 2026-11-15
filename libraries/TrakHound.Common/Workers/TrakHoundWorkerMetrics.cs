// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Workers
{
    /// <summary>
    /// Lifecycle, Error and CPU Metrics for a Worker since the Instance started. Durations are in milliseconds.
    /// </summary>
    public class TrakHoundWorkerMetrics
    {
        /// <summary>
        /// The Identifier of the Worker
        /// </summary>
        [JsonPropertyName("workerId")]
        public string WorkerId { get; set; }

        /// <summary>
        /// The Id of the Worker's Configuration
        /// </summary>
        [JsonPropertyName("configurationId")]
        public string ConfigurationId { get; set; }

        /// <summary>
        /// The display Name of the Worker
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The Id of the Package that contains the Worker
        /// </summary>
        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the Package
        /// </summary>
        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        /// <summary>
        /// The current Status of the Worker
        /// </summary>
        [JsonPropertyName("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundWorkerStatusType Status { get; set; }

        /// <summary>
        /// When the Worker changed to its current Status
        /// </summary>
        [JsonPropertyName("statusChanged")]
        public DateTime? StatusChanged { get; set; }

        /// <summary>
        /// Times the Worker Started (including Restarts)
        /// </summary>
        [JsonPropertyName("totalStartCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalStartCount { get; set; }

        /// <summary>
        /// Times the Engine Restarted the Worker after an Error
        /// </summary>
        [JsonPropertyName("totalRestartCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalRestartCount { get; set; }

        /// <summary>
        /// Times the Worker reported (or failed with) an Error Status
        /// </summary>
        [JsonPropertyName("totalErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorCount { get; set; }

        /// <summary>
        /// Warning Log Items
        /// </summary>
        [JsonPropertyName("totalWarningLogCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalWarningLogCount { get; set; }

        /// <summary>
        /// Error and Critical Log Items
        /// </summary>
        [JsonPropertyName("totalErrorLogCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorLogCount { get; set; }

        /// <summary>
        /// How long the Worker has been Started (0 if it is not currently Started)
        /// </summary>
        [JsonPropertyName("currentUptime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double CurrentUptime { get; set; }

        /// <summary>
        /// Total time the Worker has been Started
        /// </summary>
        [JsonPropertyName("totalUptime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double TotalUptime { get; set; }

        /// <summary>
        /// Percent (0 - 100) of the time since the Worker first Started that it has been Started
        /// </summary>
        [JsonPropertyName("availability")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double Availability { get; set; }

        /// <summary>
        /// When the Worker first Started
        /// </summary>
        [JsonPropertyName("firstStarted")]
        public DateTime? FirstStarted { get; set; }

        /// <summary>
        /// When the Worker last Started
        /// </summary>
        [JsonPropertyName("lastStarted")]
        public DateTime? LastStarted { get; set; }

        /// <summary>
        /// When the Worker last Stopped
        /// </summary>
        [JsonPropertyName("lastStopped")]
        public DateTime? LastStopped { get; set; }

        /// <summary>
        /// When the Worker last reported an Error
        /// </summary>
        [JsonPropertyName("lastError")]
        public DateTime? LastError { get; set; }

        /// <summary>
        /// The first line of the most recent Error or Critical Log Item
        /// </summary>
        [JsonPropertyName("lastErrorMessage")]
        public string LastErrorMessage { get; set; }

        /// <summary>
        /// The current CPU usage (percent of total machine capacity, 0 - 100)
        /// </summary>
        [JsonPropertyName("cpuUsage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double CpuUsage { get; set; }

        /// <summary>
        /// The window (in seconds) that the Average and Max CPU usage are calculated over
        /// </summary>
        [JsonPropertyName("cpuWindow")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int CpuWindow { get; set; }

        /// <summary>
        /// The average CPU usage over the CPU Window (percent of total machine capacity, 0 - 100)
        /// </summary>
        [JsonPropertyName("averageCpuUsage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageCpuUsage { get; set; }

        /// <summary>
        /// The maximum CPU usage over the CPU Window (percent of total machine capacity, 0 - 100)
        /// </summary>
        [JsonPropertyName("maxCpuUsage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxCpuUsage { get; set; }

        /// <summary>
        /// Total CPU time consumed by the current Worker instance
        /// </summary>
        [JsonPropertyName("cpuTime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double CpuTime { get; set; }

        /// <summary>
        /// The most recent Status changes (newest first)
        /// </summary>
        [JsonPropertyName("recentEvents")]
        public IEnumerable<TrakHoundWorkerStatusEvent> RecentEvents { get; set; }
    }

    /// <summary>
    /// A change in a Worker's Status
    /// </summary>
    public class TrakHoundWorkerStatusEvent
    {
        /// <summary>
        /// When the Status changed
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        /// <summary>
        /// The Status the Worker changed to
        /// </summary>
        [JsonPropertyName("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundWorkerStatusType Status { get; set; }

        /// <summary>
        /// The first line of the Error Log Item that accompanied an Error Status (if any)
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
