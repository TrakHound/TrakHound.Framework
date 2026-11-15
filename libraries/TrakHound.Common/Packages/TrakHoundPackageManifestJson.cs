// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// A Package's Manifest and signature from a Package Server. They're kept as bytes (Base64 in JSON) since a signature only matches the exact bytes it signed
    /// </summary>
    public class TrakHoundPackageManifestJson
    {
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; }

        /// <summary>
        /// The Manifest, or null if the Package doesn't have one
        /// </summary>
        [JsonPropertyName("manifest")]
        public byte[] Manifest { get; set; }

        /// <summary>
        /// The signature of the Manifest, or null if the Package isn't signed
        /// </summary>
        [JsonPropertyName("signature")]
        public byte[] Signature { get; set; }

        /// <summary>
        /// Why the Manifest couldn't be read from the stored Package (ex. it's too large), which isn't the same as the Package not having one
        /// </summary>
        [JsonPropertyName("error")]
        public string Error { get; set; }
    }
}
