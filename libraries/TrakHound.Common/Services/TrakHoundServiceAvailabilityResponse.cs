// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Services
{
    /// <summary>
    /// The result of checking whether a Service's infrastructure is available
    /// </summary>
    public class TrakHoundServiceAvailabilityResponse
    {
        /// <summary>
        /// True if the Service's infrastructure is available
        /// </summary>
        [JsonPropertyName("isAvailable")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool IsAvailable { get; set; }

        /// <summary>
        /// A message describing the availability (ex. the reason it is not available)
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }
    }
}
