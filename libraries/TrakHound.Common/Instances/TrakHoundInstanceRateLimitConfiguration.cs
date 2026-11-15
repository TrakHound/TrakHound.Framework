// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound
{
    /// <summary>
    /// Limits how often each caller (an Identity, or the IP Address of an anonymous Request) can call the Apis and the System API over HTTP,
    /// and the MCP Server's Tools. Calls made inside the Instance (ex. by Functions, Workers and Apps, or with a Module Token) are not limited.
    /// Applied when Saved.
    /// </summary>
    public class TrakHoundInstanceRateLimitConfiguration
    {
        /// <summary>
        /// Limit Requests (when false, nothing is limited regardless of the other settings)
        /// </summary>
        [JsonPropertyName("enabled")]
        public bool Enabled { get; set; }

        /// <summary>
        /// HTTP Requests to the Apis
        /// </summary>
        [JsonPropertyName("api")]
        public TrakHoundRateLimit Api { get; set; } = new TrakHoundRateLimit(600, 60, 20);

        /// <summary>
        /// HTTP Requests to the System API (not including the MCP Server, see McpTools)
        /// </summary>
        [JsonPropertyName("systemApi")]
        public TrakHoundRateLimit SystemApi { get; set; } = new TrakHoundRateLimit(600, 60, 20);

        /// <summary>
        /// MCP Tool calls (other MCP requests, such as listing the Tools, are not limited)
        /// </summary>
        [JsonPropertyName("mcpTools")]
        public TrakHoundRateLimit McpTools { get; set; } = new TrakHoundRateLimit(120, 60, 4);


        public TrakHoundInstanceRateLimitConfiguration Clone()
        {
            return new TrakHoundInstanceRateLimitConfiguration
            {
                Enabled = Enabled,
                Api = Api?.Clone(),
                SystemApi = SystemApi?.Clone(),
                McpTools = McpTools?.Clone()
            };
        }
    }


    /// <summary>
    /// How many Requests each caller can make (0 = no limit for either value)
    /// </summary>
    public class TrakHoundRateLimit
    {
        public const int MaxWindow = 3600;


        /// <summary>
        /// The most Requests in each Window (0 = no limit). Requests are allowed in bursts up to this number, and replenished evenly over the Window
        /// </summary>
        [JsonPropertyName("permitLimit")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int PermitLimit { get; set; }

        /// <summary>
        /// The Window (in Seconds, 1 to 3,600) that PermitLimit applies to
        /// </summary>
        [JsonPropertyName("window")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int Window { get; set; } = 60;

        /// <summary>
        /// The most Requests running at the same time (0 = no limit). WebSocket and Server-Sent Events streams are not counted
        /// </summary>
        [JsonPropertyName("concurrencyLimit")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int ConcurrencyLimit { get; set; }


        public TrakHoundRateLimit() { }

        public TrakHoundRateLimit(int permitLimit, int window, int concurrencyLimit)
        {
            PermitLimit = permitLimit;
            Window = window;
            ConcurrencyLimit = concurrencyLimit;
        }


        public TrakHoundRateLimit Clone() => (TrakHoundRateLimit)MemberwiseClone();
    }
}
