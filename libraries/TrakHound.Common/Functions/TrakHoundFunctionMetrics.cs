// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Functions
{
    /// <summary>
    /// Run Metrics for a Function since the Instance started. Durations are in milliseconds.
    /// </summary>
    public class TrakHoundFunctionMetrics
    {
        /// <summary>
        /// The Id of the Function
        /// </summary>
        [JsonPropertyName("functionId")]
        public string FunctionId { get; set; }

        /// <summary>
        /// The Name of the Function
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The Id of the Package that contains the Function
        /// </summary>
        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the Package that contains the Function
        /// </summary>
        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        /// <summary>
        /// Completed Runs (Successful, Failed and Cancelled)
        /// </summary>
        [JsonPropertyName("totalRunCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalRunCount { get; set; }

        /// <summary>
        /// Runs that responded with a 2xx Status Code
        /// </summary>
        [JsonPropertyName("totalSuccessCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalSuccessCount { get; set; }

        /// <summary>
        /// Runs that did not succeed and were not Cancelled
        /// </summary>
        [JsonPropertyName("totalErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorCount { get; set; }

        /// <summary>
        /// Runs that were Cancelled
        /// </summary>
        [JsonPropertyName("totalCancelledCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalCancelledCount { get; set; }

        /// <summary>
        /// Runs that are currently running
        /// </summary>
        [JsonPropertyName("activeRunCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int ActiveRunCount { get; set; }

        /// <summary>
        /// Total duration of all completed Runs
        /// </summary>
        [JsonPropertyName("totalDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double TotalDuration { get; set; }

        /// <summary>
        /// Average duration of a completed Run
        /// </summary>
        [JsonPropertyName("averageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageDuration { get; set; }

        /// <summary>
        /// Shortest duration of a completed Run
        /// </summary>
        [JsonPropertyName("minDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MinDuration { get; set; }

        /// <summary>
        /// Longest duration of a completed Run
        /// </summary>
        [JsonPropertyName("maxDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxDuration { get; set; }

        /// <summary>
        /// CPU time consumed by the Function's code (including in-process API and Service calls it makes) across all Runs
        /// </summary>
        [JsonPropertyName("totalCpuTime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double TotalCpuTime { get; set; }

        /// <summary>
        /// CPU time per Run
        /// </summary>
        [JsonPropertyName("averageCpuTime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageCpuTime { get; set; }

        /// <summary>
        /// Longest CPU time of a single Run
        /// </summary>
        [JsonPropertyName("maxCpuTime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxCpuTime { get; set; }

        /// <summary>
        /// CPU usage while Running (percent of total machine capacity, 0 - 100)
        /// </summary>
        [JsonPropertyName("averageCpuUsage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageCpuUsage { get; set; }

        /// <summary>
        /// The time (UTC) the most recent Run started (null if never Run)
        /// </summary>
        [JsonPropertyName("lastStarted")]
        public DateTime? LastStarted { get; set; }

        /// <summary>
        /// The time (UTC) the most recent Run completed (null if no Runs have completed)
        /// </summary>
        [JsonPropertyName("lastCompleted")]
        public DateTime? LastCompleted { get; set; }

        /// <summary>
        /// The Status Code of the most recent completed Run (0 if no Runs have completed)
        /// </summary>
        [JsonPropertyName("lastStatusCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int LastStatusCode { get; set; }

        /// <summary>
        /// The duration of the most recent completed Run
        /// </summary>
        [JsonPropertyName("lastDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double LastDuration { get; set; }

        /// <summary>
        /// The time (UTC) of the most recent successful Run (null if none)
        /// </summary>
        [JsonPropertyName("lastSuccess")]
        public DateTime? LastSuccess { get; set; }

        /// <summary>
        /// The time (UTC) of the most recent Run that did not succeed and was not Cancelled (null if none)
        /// </summary>
        [JsonPropertyName("lastError")]
        public DateTime? LastError { get; set; }

        /// <summary>
        /// The Message of the most recent Run that did not succeed
        /// </summary>
        [JsonPropertyName("lastErrorMessage")]
        public string LastErrorMessage { get; set; }

        /// <summary>
        /// The most recent Runs (newest first)
        /// </summary>
        [JsonPropertyName("recentRuns")]
        public IEnumerable<TrakHoundFunctionRunMetrics> RecentRuns { get; set; }
    }

    /// <summary>
    /// The Result of a single Function Run
    /// </summary>
    public class TrakHoundFunctionRunMetrics
    {
        /// <summary>
        /// The Id of the Run
        /// </summary>
        [JsonPropertyName("runId")]
        public string RunId { get; set; }

        /// <summary>
        /// The time (UTC) the Run started
        /// </summary>
        [JsonPropertyName("started")]
        public DateTime Started { get; set; }

        /// <summary>
        /// Null while the Run is still running
        /// </summary>
        [JsonPropertyName("completed")]
        public DateTime? Completed { get; set; }

        /// <summary>
        /// The Status Code returned by the Run (0 while still running)
        /// </summary>
        [JsonPropertyName("statusCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int StatusCode { get; set; }

        /// <summary>
        /// Duration in milliseconds (the time so far while still running)
        /// </summary>
        [JsonPropertyName("duration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double Duration { get; set; }

        /// <summary>
        /// CPU time consumed by the Run in milliseconds (0 while still running)
        /// </summary>
        [JsonPropertyName("cpuTime")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double CpuTime { get; set; }

        /// <summary>
        /// CPU usage over the Run (percent of total machine capacity, 0 - 100)
        /// </summary>
        [JsonPropertyName("cpuUsage")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double CpuUsage { get; set; }

        /// <summary>
        /// The Message returned by the Run
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
