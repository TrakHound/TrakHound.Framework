// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Deployments
{
    /// <summary>
    /// A Package Version included in a Deployment Manifest
    /// </summary>
    public class TrakHoundDeploymentPackageManifestItem
    {
        /// <summary>
        /// How the Package changed compared to a previous Manifest (serialized as a string)
        /// </summary>
        [JsonPropertyName("changeType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundDeploymentChangeType ChangeType { get; set; }

        /// <summary>
        /// The Category of the Package (ex. "app", "api")
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; }

        /// <summary>
        /// The ID of the Package
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The Version of the Package
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; }
    }
}
