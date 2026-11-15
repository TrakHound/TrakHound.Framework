// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Text.Json.Serialization;

namespace TrakHound.Instances
{
    /// <summary>
    /// Information about the Deployment that is installed on an Instance
    /// </summary>
    public class TrakHoundInstanceDeploymentInformation
    {
        /// <summary>
        /// The Identifier of the Deployment Profile the Deployment belongs to
        /// </summary>
        [JsonPropertyName("profileId")]
        public string ProfileId { get; set; }

        /// <summary>
        /// The unique Identifier of the Deployment
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The Version of the Deployment
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; }

        /// <summary>
        /// A description of the Deployment
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The date the Deployment was built
        /// </summary>
        [JsonPropertyName("buildDate")]
        public DateTime BuildDate { get; set; }
    }
}
