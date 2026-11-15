// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace TrakHound
{
    /// <summary>
    /// Which Metrics the Instance collects (all are collected by default). Applied when the Instance (Runtime) is started.
    /// </summary>
    public class TrakHoundInstanceMetricsConfiguration
    {
        /// <summary>
        /// The default number of HTTP Requests, API Requests and closed API Streams kept for the Diagnostics
        /// </summary>
        public const int DefaultHistory = 100;

        /// <summary>
        /// Collect Metrics (when false, no Metrics are collected regardless of the other settings)
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// API Request Metrics (rates, errors, response time, sources)
        /// </summary>
        [JsonPropertyName("api")]
        public bool Api { get; set; } = true;

        /// <summary>
        /// Service Request Metrics (rates, errors, response time, operations, routers)
        /// </summary>
        [JsonPropertyName("services")]
        public bool Services { get; set; } = true;

        /// <summary>
        /// Function Run Metrics (runs, results, durations)
        /// </summary>
        [JsonPropertyName("functions")]
        public bool Functions { get; set; } = true;

        /// <summary>
        /// Worker Metrics (uptime, restarts, errors)
        /// </summary>
        [JsonPropertyName("workers")]
        public bool Workers { get; set; } = true;

        /// <summary>
        /// App Metrics (page views and time on page)
        /// </summary>
        [JsonPropertyName("apps")]
        public bool Apps { get; set; } = true;

        /// <summary>
        /// HTTP Request Metrics (rates, errors, response time, data, areas, endpoints, clients)
        /// </summary>
        [JsonPropertyName("http")]
        public bool Http { get; set; } = true;

        /// <summary>
        /// HTTP Stream Metrics for WebSockets and Server-Sent Events (open connections, messages, data, errors)
        /// </summary>
        [JsonPropertyName("streams")]
        public bool Streams { get; set; } = true;

        /// <summary>
        /// MCP Server Metrics (Tool calls, errors, denied calls, durations, and Clients)
        /// </summary>
        [JsonPropertyName("mcp")]
        public bool Mcp { get; set; } = true;

        /// <summary>
        /// CPU usage of Workers, Function Runs, APIs, Apps and the Admin Interface (measured on every async context switch in their code)
        /// </summary>
        [JsonPropertyName("cpu")]
        public bool Cpu { get; set; } = true;

        /// <summary>
        /// The most recent HTTP Requests to keep for the HTTP Requests Diagnostics (0 keeps none, up to 10,000). Kept even when Metrics are not collected.
        /// </summary>
        [JsonPropertyName("httpRequestHistory")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int HttpRequestHistory { get; set; } = DefaultHistory;

        /// <summary>
        /// The most recent API Requests to keep for the API Requests Diagnostics (0 keeps none, up to 10,000). Kept even when Metrics are not collected.
        /// </summary>
        [JsonPropertyName("apiRequestHistory")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int ApiRequestHistory { get; set; } = DefaultHistory;

        /// <summary>
        /// The most recently closed API Streams to keep for the API Streams Diagnostics (0 keeps none, up to 10,000). Open Streams are always shown.
        /// </summary>
        [JsonPropertyName("apiStreamHistory")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int ApiStreamHistory { get; set; } = DefaultHistory;


        [JsonIgnore]
        [YamlIgnore]
        public bool ApiEnabled => Enabled && Api;

        [JsonIgnore]
        [YamlIgnore]
        public bool ServicesEnabled => Enabled && Services;

        [JsonIgnore]
        [YamlIgnore]
        public bool FunctionsEnabled => Enabled && Functions;

        [JsonIgnore]
        [YamlIgnore]
        public bool WorkersEnabled => Enabled && Workers;

        [JsonIgnore]
        [YamlIgnore]
        public bool AppsEnabled => Enabled && Apps;

        [JsonIgnore]
        [YamlIgnore]
        public bool HttpEnabled => Enabled && Http;

        [JsonIgnore]
        [YamlIgnore]
        public bool StreamsEnabled => Enabled && Streams;

        [JsonIgnore]
        [YamlIgnore]
        public bool McpEnabled => Enabled && Mcp;

        [JsonIgnore]
        [YamlIgnore]
        public bool CpuEnabled => Enabled && Cpu;


        public TrakHoundInstanceMetricsConfiguration Clone() => (TrakHoundInstanceMetricsConfiguration)MemberwiseClone();
    }
}
