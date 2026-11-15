// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Functions
{
    /// <summary>
    /// A JSON serializable Status update for a Function Run
    /// </summary>
    public class TrakHoundFunctionStatusJsonResponse
    {
        /// <summary>
        /// The Status of the Function Run (serialized as "statusCode")
        /// </summary>
        [JsonPropertyName("statusCode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundFunctionStatusType Status { get; set; }

        /// <summary>
        /// The time the Status occurred (Unix Nanoseconds)
        /// </summary>
        [JsonPropertyName("timestamp")]
        public long Timestamp { get; set; }
    }
}
