// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// Configures a Package to be updated automatically
    /// </summary>
    public class TrakHoundPackageUpdateConfiguration
    {
        /// <summary>
        /// Category of the Package (ex. app, api, service)
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; }

        /// <summary>
        /// ID of the Package
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The Version (may contain '*' wildcards) that updates must match
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; }


        /// <summary>
        /// Create an empty Update Configuration
        /// </summary>
        public TrakHoundPackageUpdateConfiguration() { }

        /// <summary>
        /// Create an Update Configuration for the specified Package Category, ID and Version
        /// </summary>
        public TrakHoundPackageUpdateConfiguration(string category, string id, string version = null)
        {
            Category = category;
            Id = id;
            Version = version;
        }
    }
}
