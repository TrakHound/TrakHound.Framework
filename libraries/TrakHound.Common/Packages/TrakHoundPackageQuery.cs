// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// A query used to check a Package Server for an update to an installed Package
    /// </summary>
    public class TrakHoundPackageQuery
    {
        /// <summary>
        /// ID of the Package
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The currently installed Version of the Package
        /// </summary>
        [JsonPropertyName("installed")]
        public string Installed { get; set; }

        /// <summary>
        /// The Version (may contain '*' wildcards) that updates must match
        /// </summary>
        [JsonPropertyName("update")]
        public string Update { get; set; }


        /// <summary>
        /// Create an empty Query
        /// </summary>
        public TrakHoundPackageQuery() { }

        /// <summary>
        /// Create a Query for the specified Package ID, installed Version and update Version
        /// </summary>
        public TrakHoundPackageQuery(string id, string installedVersion, string updateVersion = null)
        {
            Id = id;
            Installed = installedVersion;
            Update = updateVersion;
        }
    }
}
