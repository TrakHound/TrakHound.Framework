// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Text.Json.Serialization;

namespace TrakHound.Functions
{
    /// <summary>
    /// The start of a Scheduled Task's latest recorded Run (null when it has none)
    /// </summary>
    public class FunctionTaskLastRun
    {
        [JsonPropertyName("taskId")]
        public string TaskId { get; set; }

        [JsonPropertyName("lastRun")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public DateTime? LastRun { get; set; }
    }
}
