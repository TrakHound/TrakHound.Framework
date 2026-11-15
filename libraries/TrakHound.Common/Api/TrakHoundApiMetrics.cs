// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Api
{
    /// <summary>
    /// Request Metrics for an API (Rates, Average and Max are calculated over a recent window, Totals are since the Instance started)
    /// </summary>
    public class TrakHoundApiMetrics
    {
        /// <summary>
        /// The ID of the Api Configuration
        /// </summary>
        [JsonPropertyName("apiId")]
        public string ApiId { get; set; }

        /// <summary>
        /// The ID of the Package that contains the Api
        /// </summary>
        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the Package that is loaded
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
        /// Total number of Requests since the Instance started
        /// </summary>
        [JsonPropertyName("totalRequestCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalRequestCount { get; set; }

        /// <summary>
        /// Requests that responded with a Status Code >= 400
        /// </summary>
        [JsonPropertyName("totalErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorCount { get; set; }

        /// <summary>
        /// Requests that responded with a Status Code >= 500 (also counted in TotalErrorCount)
        /// </summary>
        [JsonPropertyName("totalServerErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalServerErrorCount { get; set; }

        /// <summary>
        /// Total Request Body bytes since the Instance started
        /// </summary>
        [JsonPropertyName("totalRequestBytes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalRequestBytes { get; set; }

        /// <summary>
        /// Total Response Body bytes since the Instance started
        /// </summary>
        [JsonPropertyName("totalResponseBytes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalResponseBytes { get; set; }

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
        /// Server Errors (Status Code >= 500) per second
        /// </summary>
        [JsonPropertyName("serverErrorRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double ServerErrorRate { get; set; }

        /// <summary>
        /// Request Body bytes per second
        /// </summary>
        [JsonPropertyName("requestByteRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double RequestByteRate { get; set; }

        /// <summary>
        /// Response Body bytes per second
        /// </summary>
        [JsonPropertyName("responseByteRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double ResponseByteRate { get; set; }

        /// <summary>
        /// Average Response Duration (milliseconds)
        /// </summary>
        [JsonPropertyName("averageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageDuration { get; set; }

        /// <summary>
        /// Maximum Response Duration (milliseconds)
        /// </summary>
        [JsonPropertyName("maxDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxDuration { get; set; }

        /// <summary>
        /// The time of the most recent Request
        /// </summary>
        [JsonPropertyName("lastRequested")]
        public DateTime? LastRequested { get; set; }

        /// <summary>
        /// The CPU used handling the API's Requests and Streams (percent of total machine capacity, 0 - 100) over the last second. 0 when CPU usage isn't measured (Settings > Metrics > Measure CPU Usage).
        /// </summary>
        [JsonPropertyName("cpuUsage")]
        public double CpuUsage { get; set; }

        /// <summary>
        /// Milliseconds of CPU time used handling the API's Requests and Streams since the Instance started
        /// </summary>
        [JsonPropertyName("cpuTime")]
        public double CpuTime { get; set; }

        /// <summary>
        /// Metrics for each Endpoint that has received Requests (ordered by TotalRequestCount, highest first)
        /// </summary>
        [JsonPropertyName("endpoints")]
        public IEnumerable<TrakHoundApiEndpointMetrics> Endpoints { get; set; }

        /// <summary>
        /// What made the Requests (App, Api, Function, Worker, Http, or the Instance itself)
        /// </summary>
        [JsonPropertyName("sources")]
        public IEnumerable<TrakHoundApiSourceMetrics> Sources { get; set; }
    }

    /// <summary>
    /// Request Metrics for a single Source of Requests to an API since the Instance started
    /// </summary>
    public class TrakHoundApiSourceMetrics
    {
        /// <summary>
        /// The Source Type (Instance, App, Api, Function, Worker, or Http)
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// The Configuration ID of the Source (or the remote address for Http)
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// A display name for the Source (the Configuration Name, or Package ID if not named)
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Total number of Requests from the Source
        /// </summary>
        [JsonPropertyName("totalRequestCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalRequestCount { get; set; }

        /// <summary>
        /// Requests from the Source that responded with a Status Code >= 400
        /// </summary>
        [JsonPropertyName("totalErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorCount { get; set; }

        /// <summary>
        /// Average Response Duration (milliseconds)
        /// </summary>
        [JsonPropertyName("averageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageDuration { get; set; }

        /// <summary>
        /// Maximum Response Duration (milliseconds)
        /// </summary>
        [JsonPropertyName("maxDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxDuration { get; set; }

        /// <summary>
        /// The time of the most recent Request from the Source
        /// </summary>
        [JsonPropertyName("lastRequested")]
        public DateTime? LastRequested { get; set; }
    }

    /// <summary>
    /// Request Metrics for a single API Endpoint (Method + Route) since the Instance started
    /// </summary>
    public class TrakHoundApiEndpointMetrics
    {
        /// <summary>
        /// The Endpoint Type (Request or Stream)
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// The HTTP Method of the Endpoint
        /// </summary>
        [JsonPropertyName("method")]
        public string Method { get; set; }

        /// <summary>
        /// The Route of the Endpoint
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        /// <summary>
        /// Total number of Requests to the Endpoint
        /// </summary>
        [JsonPropertyName("totalRequestCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalRequestCount { get; set; }

        /// <summary>
        /// Requests to the Endpoint that responded with a Status Code >= 400
        /// </summary>
        [JsonPropertyName("totalErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorCount { get; set; }

        /// <summary>
        /// Average Response Duration (milliseconds)
        /// </summary>
        [JsonPropertyName("averageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageDuration { get; set; }

        /// <summary>
        /// Maximum Response Duration (milliseconds)
        /// </summary>
        [JsonPropertyName("maxDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxDuration { get; set; }

        /// <summary>
        /// The Status Code of the most recent Response
        /// </summary>
        [JsonPropertyName("lastStatusCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int LastStatusCode { get; set; }

        /// <summary>
        /// The time of the most recent Request to the Endpoint
        /// </summary>
        [JsonPropertyName("lastRequested")]
        public DateTime? LastRequested { get; set; }

        /// <summary>
        /// The CPU used handling the Endpoint's Requests (percent of total machine capacity, 0 - 100) over the last second
        /// </summary>
        [JsonPropertyName("cpuUsage")]
        public double CpuUsage { get; set; }

        /// <summary>
        /// Milliseconds of CPU time used handling the Endpoint's Requests since the Instance started
        /// </summary>
        [JsonPropertyName("cpuTime")]
        public double CpuTime { get; set; }
    }
}
