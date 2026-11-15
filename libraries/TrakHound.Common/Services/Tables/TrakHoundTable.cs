// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Services.Tables
{
    /// <summary>
    /// A Table or View in a database
    /// </summary>
    public class TrakHoundTable
    {
        /// <summary>
        /// A unique Identifier generated from the Schema and Name
        /// </summary>
        [JsonPropertyName("id")]
        public string Id => $"{Schema}:{Name}".ToSHA256Hash();

        /// <summary>
        /// The Schema that contains the Table
        /// </summary>
        [JsonPropertyName("schema")]
        public string Schema { get; set; }

        /// <summary>
        /// The name of the Table
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Whether this is a Table or a View
        /// </summary>
        [JsonPropertyName("type")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TrakHoundTableType Type { get; set; }
    }
}
