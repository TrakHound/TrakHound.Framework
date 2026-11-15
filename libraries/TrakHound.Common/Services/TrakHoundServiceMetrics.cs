// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Services
{
    /// <summary>
    /// Request Metrics for a Service (Rates, Average and Max are calculated over a recent window, Totals are since the Instance started).
    /// Durations are in milliseconds.
    /// </summary>
    public class TrakHoundServiceMetrics
    {
        /// <summary>
        /// The Configuration ID of the Service
        /// </summary>
        [JsonPropertyName("serviceId")]
        public string ServiceId { get; set; }

        /// <summary>
        /// The name of the Service Configuration
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The ID of the Package that contains the Service
        /// </summary>
        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        /// <summary>
        /// The version of the Package that contains the Service
        /// </summary>
        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        /// <summary>
        /// The window (in seconds) that the Rates, Average and Max values are calculated over
        /// </summary>
        [JsonPropertyName("window")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int Window { get; set; }

        /// <summary>
        /// Requests since the Instance started
        /// </summary>
        [JsonPropertyName("totalRequestCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalRequestCount { get; set; }

        /// <summary>
        /// Requests that did not succeed (Status Code >= 400)
        /// </summary>
        [JsonPropertyName("totalErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorCount { get; set; }

        /// <summary>
        /// Requests per second
        /// </summary>
        [JsonPropertyName("requestRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double RequestRate { get; set; }

        /// <summary>
        /// Errors (Status Code >= 400) per second
        /// </summary>
        [JsonPropertyName("errorRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double ErrorRate { get; set; }

        /// <summary>
        /// Average Request duration over the window
        /// </summary>
        [JsonPropertyName("averageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageDuration { get; set; }

        /// <summary>
        /// Longest Request duration over the window
        /// </summary>
        [JsonPropertyName("maxDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxDuration { get; set; }

        /// <summary>
        /// When the most recent Request was received (UTC, null if none)
        /// </summary>
        [JsonPropertyName("lastRequested")]
        public DateTime? LastRequested { get; set; }

        /// <summary>
        /// When the most recent Request that did not succeed was received (UTC, null if none)
        /// </summary>
        [JsonPropertyName("lastError")]
        public DateTime? LastError { get; set; }

        /// <summary>
        /// The Status Code and Message of the most recent request that did not succeed
        /// </summary>
        [JsonPropertyName("lastErrorMessage")]
        public string LastErrorMessage { get; set; }

        /// <summary>
        /// Totals for each Operation (Service Type + Request Name) since the Instance started
        /// </summary>
        [JsonPropertyName("operations")]
        public IEnumerable<TrakHoundServiceOperationMetrics> Operations { get; set; }

        /// <summary>
        /// Totals for each Router that sent requests to the Service since the Instance started
        /// </summary>
        [JsonPropertyName("routers")]
        public IEnumerable<TrakHoundServiceRouterMetrics> Routers { get; set; }
    }

    /// <summary>
    /// Request Metrics for a single Operation of a Service since the Instance started
    /// </summary>
    public class TrakHoundServiceOperationMetrics
    {
        /// <summary>
        /// The Service Type (ex. Table, KeyValue, Blob, Message, MessageQueue, Command)
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// The Request Name (ex. Query, Get, Publish)
        /// </summary>
        [JsonPropertyName("operation")]
        public string Operation { get; set; }

        /// <summary>
        /// Requests for the Operation
        /// </summary>
        [JsonPropertyName("totalRequestCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalRequestCount { get; set; }

        /// <summary>
        /// Requests for the Operation that did not succeed (Status Code &gt;= 400)
        /// </summary>
        [JsonPropertyName("totalErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorCount { get; set; }

        /// <summary>
        /// Average Request duration
        /// </summary>
        [JsonPropertyName("averageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageDuration { get; set; }

        /// <summary>
        /// Longest Request duration
        /// </summary>
        [JsonPropertyName("maxDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxDuration { get; set; }

        /// <summary>
        /// The Status Code of the most recent Request
        /// </summary>
        [JsonPropertyName("lastStatusCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int LastStatusCode { get; set; }

        /// <summary>
        /// When the most recent Request was received (UTC, null if none)
        /// </summary>
        [JsonPropertyName("lastRequested")]
        public DateTime? LastRequested { get; set; }
    }

    /// <summary>
    /// Request Metrics for a single Router's requests to a Service since the Instance started
    /// </summary>
    public class TrakHoundServiceRouterMetrics
    {
        /// <summary>
        /// The ID of the Router that sent the Requests
        /// </summary>
        [JsonPropertyName("routerId")]
        public string RouterId { get; set; }

        /// <summary>
        /// Requests sent by the Router
        /// </summary>
        [JsonPropertyName("totalRequestCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalRequestCount { get; set; }

        /// <summary>
        /// Requests sent by the Router that did not succeed (Status Code &gt;= 400)
        /// </summary>
        [JsonPropertyName("totalErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorCount { get; set; }

        /// <summary>
        /// Average Request duration
        /// </summary>
        [JsonPropertyName("averageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageDuration { get; set; }

        /// <summary>
        /// When the most recent Request was received (UTC, null if none)
        /// </summary>
        [JsonPropertyName("lastRequested")]
        public DateTime? LastRequested { get; set; }
    }
}
