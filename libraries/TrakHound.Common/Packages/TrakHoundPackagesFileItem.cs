// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// A Package listed in the packages file
    /// </summary>
    public class TrakHoundPackagesFileItem
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
        /// The requested Version of the Package (may contain '*' wildcards)
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; }

        /// <summary>
        /// The installed Version of the Package
        /// </summary>
        [JsonPropertyName("installed")]
        public string Installed { get; set; }

        /// <summary>
        /// The Package Server and Organization the installed Version was downloaded from (null if it was installed from a file, or before this was recorded)
        /// </summary>
        [JsonPropertyName("origin")]
        public TrakHoundPackageOrigin Origin { get; set; }


        /// <summary>
        /// Create an empty item
        /// </summary>
        public TrakHoundPackagesFileItem() { }

        /// <summary>
        /// Create an item for the specified Package Category, ID, requested Version and installed Version
        /// </summary>
        public TrakHoundPackagesFileItem(string category, string id, string version, string installed = null)
        {
            Category = category;
            Id = id;
            Version = version;
            Installed = installed;
        }
    }
}
