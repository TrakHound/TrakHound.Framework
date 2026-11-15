// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Services
{
    /// <summary>
    /// Information about a loaded Service Configuration
    /// </summary>
    public class TrakHoundServiceInformation
    {
        /// <summary>
        /// The ID of the Service Configuration
        /// </summary>
        [JsonPropertyName("configurationId")]
        public string ConfigurationId { get; set; }

        /// <summary>
        /// The ID of the Package that contains the Service
        /// </summary>
        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        /// <summary>
        /// The version of the Package that contains the Service
        /// </summary>
        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        /// <summary>
        /// The name of the Service Configuration
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The description of the Service Configuration
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The Routes the Service handles (ex. Blobs, Tables, MessageQueues)
        /// </summary>
        [JsonPropertyName("routes")]
        public IEnumerable<string> Routes { get; set; }
    }
}
