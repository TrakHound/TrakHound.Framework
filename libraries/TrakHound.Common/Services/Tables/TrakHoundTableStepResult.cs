// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// The result of one step of a Table Request
    /// </summary>
    public class TrakHoundTableStepResult
    {
        /// <summary>
        /// A message returned by the database (ex. the number of rows affected)
        /// </summary>
        [JsonPropertyName("message")]
        public string Message { get; set; }

        /// <summary>
        /// The result sets of a Query step (null for other steps)
        /// </summary>
        [JsonPropertyName("results")]
        public TrakHoundTableResult[] Results { get; set; }
    }
}
