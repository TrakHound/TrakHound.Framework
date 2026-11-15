// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Http
{
    /// <summary>
    /// Metrics for the HTTP Requests received by an Instance (Rates, Average and Max are calculated over a recent window, Totals are since the Instance started).
    /// Upgraded (WebSocket) requests are counted, but not included in the Response Durations (they stay open for the life of the connection)
    /// </summary>
    public class TrakHoundHttpMetrics
    {
        /// <summary>
        /// The window (in seconds) that the Rates, Average and Max values are calculated over
        /// </summary>
        [JsonPropertyName("window")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int Window { get; set; }

        /// <summary>
        /// Requests currently being processed (including open WebSocket connections)
        /// </summary>
        [JsonPropertyName("activeRequestCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int ActiveRequestCount { get; set; }

        /// <summary>
        /// The most Requests processed at the same time since the Instance started
        /// </summary>
        [JsonPropertyName("peakActiveRequestCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int PeakActiveRequestCount { get; set; }

        /// <summary>
        /// Requests received since the Instance started
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
        /// Bytes received in Request bodies since the Instance started
        /// </summary>
        [JsonPropertyName("totalRequestBytes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalRequestBytes { get; set; }

        /// <summary>
        /// Bytes sent in Response bodies since the Instance started
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
        /// Request body bytes received per second
        /// </summary>
        [JsonPropertyName("requestByteRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double RequestByteRate { get; set; }

        /// <summary>
        /// Response body bytes sent per second
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
        /// The time (UTC) of the most recent Request (null if no Requests have been received)
        /// </summary>
        [JsonPropertyName("lastRequested")]
        public DateTime? LastRequested { get; set; }

        /// <summary>
        /// What the Requests were for (System API, API, Apps, Admin, Blazor, Static Files, Other)
        /// </summary>
        [JsonPropertyName("areas")]
        public IEnumerable<TrakHoundHttpRequestGroupMetrics> Areas { get; set; }

        /// <summary>
        /// Request counts by Response Status Code (ordered by Status Code)
        /// </summary>
        [JsonPropertyName("statusCodes")]
        public IEnumerable<TrakHoundHttpStatusCodeMetrics> StatusCodes { get; set; }

        /// <summary>
        /// Requests by Method and Route (the Route template when there is one)
        /// </summary>
        [JsonPropertyName("endpoints")]
        public IEnumerable<TrakHoundHttpRequestGroupMetrics> Endpoints { get; set; }

        /// <summary>
        /// Requests by Client Address
        /// </summary>
        [JsonPropertyName("clients")]
        public IEnumerable<TrakHoundHttpRequestGroupMetrics> Clients { get; set; }
    }

    /// <summary>
    /// Request Metrics for a group of Requests (an Area, an Endpoint or a Client) since the Instance started
    /// </summary>
    public class TrakHoundHttpRequestGroupMetrics
    {
        /// <summary>
        /// The Area, the Endpoint Route, or the Client Address
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The HTTP Method (Endpoints only)
        /// </summary>
        [JsonPropertyName("method")]
        public string Method { get; set; }

        /// <summary>
        /// The Area the Requests were for (Endpoints only)
        /// </summary>
        [JsonPropertyName("area")]
        public string Area { get; set; }

        /// <summary>
        /// Requests received in the group
        /// </summary>
        [JsonPropertyName("totalRequestCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalRequestCount { get; set; }

        /// <summary>
        /// Requests in the group that responded with a Status Code >= 400
        /// </summary>
        [JsonPropertyName("totalErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorCount { get; set; }

        /// <summary>
        /// Bytes received in Request bodies for the group
        /// </summary>
        [JsonPropertyName("totalRequestBytes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalRequestBytes { get; set; }

        /// <summary>
        /// Bytes sent in Response bodies for the group
        /// </summary>
        [JsonPropertyName("totalResponseBytes")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalResponseBytes { get; set; }

        /// <summary>
        /// Average Response Duration (milliseconds, not including Upgraded requests)
        /// </summary>
        [JsonPropertyName("averageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageDuration { get; set; }

        /// <summary>
        /// Maximum Response Duration (milliseconds, not including Upgraded requests)
        /// </summary>
        [JsonPropertyName("maxDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxDuration { get; set; }

        /// <summary>
        /// The Status Code of the most recent Request in the group
        /// </summary>
        [JsonPropertyName("lastStatusCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int LastStatusCode { get; set; }

        /// <summary>
        /// The time (UTC) of the most recent Request in the group
        /// </summary>
        [JsonPropertyName("lastRequested")]
        public DateTime? LastRequested { get; set; }
    }

    /// <summary>
    /// The number of Requests that responded with a Status Code since the Instance started
    /// </summary>
    public class TrakHoundHttpStatusCodeMetrics
    {
        /// <summary>
        /// The HTTP Response Status Code
        /// </summary>
        [JsonPropertyName("statusCode")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int StatusCode { get; set; }

        /// <summary>
        /// The number of Requests that responded with the Status Code
        /// </summary>
        [JsonPropertyName("count")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long Count { get; set; }
    }
}
