// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Mcp
{
    /// <summary>
    /// Metrics for the MCP Server (Model Context Protocol) of an Instance.
    /// Rates are calculated over a recent window, Totals are since the Instance started
    /// </summary>
    public class TrakHoundMcpMetrics
    {
        /// <summary>
        /// The window (in seconds) that the Rates are calculated over
        /// </summary>
        [JsonPropertyName("window")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int Window { get; set; }

        /// <summary>
        /// Tool calls since the Instance started
        /// </summary>
        [JsonPropertyName("totalCallCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalCallCount { get; set; }

        /// <summary>
        /// Tool calls that returned an error (including calls that were denied)
        /// </summary>
        [JsonPropertyName("totalErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorCount { get; set; }

        /// <summary>
        /// Tool calls that were denied because the caller doesn't have the required Permission (also counted in TotalErrorCount)
        /// </summary>
        [JsonPropertyName("totalDeniedCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalDeniedCount { get; set; }

        /// <summary>
        /// Clients that connected (MCP initialize requests)
        /// </summary>
        [JsonPropertyName("totalInitializeCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalInitializeCount { get; set; }

        /// <summary>
        /// Tool calls per second
        /// </summary>
        [JsonPropertyName("callRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double CallRate { get; set; }

        /// <summary>
        /// Tool calls that returned an error, per second
        /// </summary>
        [JsonPropertyName("errorRate")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double ErrorRate { get; set; }

        /// <summary>
        /// Average Tool call duration (milliseconds) over the window
        /// </summary>
        [JsonPropertyName("averageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageDuration { get; set; }

        /// <summary>
        /// Longest Tool call duration (milliseconds) over the window
        /// </summary>
        [JsonPropertyName("maxDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxDuration { get; set; }

        /// <summary>
        /// The time (UTC) of the most recent Tool call
        /// </summary>
        [JsonPropertyName("lastCalled")]
        public DateTime? LastCalled { get; set; }

        /// <summary>
        /// Metrics for each Tool
        /// </summary>
        [JsonPropertyName("tools")]
        public List<TrakHoundMcpToolMetrics> Tools { get; set; }

        /// <summary>
        /// The Clients that connected (from the MCP initialize request)
        /// </summary>
        [JsonPropertyName("clients")]
        public List<TrakHoundMcpClientMetrics> Clients { get; set; }
    }

    /// <summary>
    /// Metrics for an MCP Tool since the Instance started
    /// </summary>
    public class TrakHoundMcpToolMetrics
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("totalCallCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalCallCount { get; set; }

        /// <summary>
        /// Calls that returned an error (including calls that were denied)
        /// </summary>
        [JsonPropertyName("totalErrorCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalErrorCount { get; set; }

        /// <summary>
        /// Calls denied because the caller doesn't have the required Permission
        /// </summary>
        [JsonPropertyName("totalDeniedCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long TotalDeniedCount { get; set; }

        /// <summary>
        /// Average call duration (milliseconds)
        /// </summary>
        [JsonPropertyName("averageDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double AverageDuration { get; set; }

        /// <summary>
        /// Longest call duration (milliseconds)
        /// </summary>
        [JsonPropertyName("maxDuration")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public double MaxDuration { get; set; }

        /// <summary>
        /// The error of the most recent call that failed
        /// </summary>
        [JsonPropertyName("lastError")]
        public string LastError { get; set; }

        [JsonPropertyName("lastCalled")]
        public DateTime? LastCalled { get; set; }
    }

    /// <summary>
    /// An MCP Client (by name, version, and address) that connected since the Instance started
    /// </summary>
    public class TrakHoundMcpClientMetrics
    {
        /// <summary>
        /// The name the Client reported (ex. claude-code)
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        /// <summary>
        /// The Address of the remote Client
        /// </summary>
        [JsonPropertyName("remoteAddress")]
        public string RemoteAddress { get; set; }

        [JsonPropertyName("initializeCount")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public long InitializeCount { get; set; }

        [JsonPropertyName("lastConnected")]
        public DateTime? LastConnected { get; set; }
    }
}
