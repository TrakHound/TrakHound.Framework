// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Http
{
    /// <summary>
    /// The most recent HTTP Requests received by an Instance
    /// </summary>
    public class TrakHoundHttpRecentRequests
    {
        /// <summary>
        /// The most Requests the Instance keeps (Settings > Metrics > Recent Requests). 0 when Recent Requests are not kept.
        /// </summary>
        [JsonPropertyName("capacity")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public int Capacity { get; set; }

        /// <summary>
        /// The Requests, newest first
        /// </summary>
        [JsonPropertyName("requests")]
        public IEnumerable<TrakHoundHttpRequestRecord> Requests { get; set; }
    }
}
