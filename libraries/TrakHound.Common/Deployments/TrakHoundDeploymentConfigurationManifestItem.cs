// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Deployments
{
    /// <summary>
    /// A Configuration file included in a Deployment Manifest
    /// </summary>
    public class TrakHoundDeploymentConfigurationManifestItem
    {
        /// <summary>
        /// The Configuration Profile (directory) the file is in. Null in deployments created by earlier versions.
        /// </summary>
        [JsonPropertyName("profile")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string Profile { get; set; }

        /// <summary>
        /// The Configuration Category (the name of the directory the file is in)
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; }

        /// <summary>
        /// The Configuration ID (the filename without its extension)
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// When the file was last modified (Unix Timestamp in nanoseconds)
        /// </summary>
        [JsonPropertyName("lastUpdated")]
        public long LastUpdated { get; set; }
    }
}
