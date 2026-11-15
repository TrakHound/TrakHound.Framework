// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Workers
{
    /// <summary>
    /// The JSON representation of a Worker Status Response (sent by the Instance HTTP API)
    /// </summary>
    public class TrakHoundWorkerStatusJsonResponse
    {
        /// <summary>
        /// The Identifier of the Worker
        /// </summary>
        [JsonPropertyName("workerId")]
        public string WorkerId { get; set; }

        /// <summary>
        /// The Status of the Worker (serialized as "statusCode")
        /// </summary>
        [JsonPropertyName("statusCode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundWorkerStatusType Status { get; set; }

        /// <summary>
        /// When the Status was received (Unix Nanoseconds)
        /// </summary>
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }
    }
}
