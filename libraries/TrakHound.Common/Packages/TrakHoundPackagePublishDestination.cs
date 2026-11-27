// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// A Package Server that a Package is published to
    /// </summary>
    public class TrakHoundPackagePublishDestination
    {
        /// <summary>
        /// Url of the Package Server
        /// </summary>
        [JsonPropertyName("packageServer")]
        public string PackageServer { get; set; }

        /// <summary>
        /// Organization to publish the Package to
        /// </summary>
        [JsonPropertyName("organization")]
        public string Organization { get; set; }

        /// <summary>
        /// The name of the Environment Variable that holds the Api Token for this Package Server (ex. TRAKHOUND_PACKAGES_RELEASE_TOKEN).
        /// If not set, the CLI's Api Token is used
        /// </summary>
        [JsonPropertyName("apiTokenVariable")]
        public string ApiTokenVariable { get; set; }
    }
}
