// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Packages.App
{
    /// <summary>
    /// A Package Server (an Instance running the Packages API, or a TrakHound Package Manager) that Packages are uploaded to, downloaded from, and copied between
    /// </summary>
    public class PackageServer
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Base URL of the Server (the Packages API is at "{BaseUrl}/packages")
        /// </summary>
        [JsonPropertyName("baseUrl")]
        public string BaseUrl { get; set; }

        [JsonPropertyName("organization")]
        public string Organization { get; set; }

        /// <summary>
        /// Sent as the API Token header for Servers that require one
        /// </summary>
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; }


        [JsonIgnore]
        public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : BaseUrl;

        [JsonIgnore]
        public string Description => $"{BaseUrl} · {Organization}";


        /// <summary>
        /// Returns true if both Servers point at the same Organization on the same Base URL
        /// </summary>
        public bool IsSameLocation(PackageServer other)
        {
            if (other == null) return false;

            return string.Equals(BaseUrl?.TrimEnd('/'), other.BaseUrl?.TrimEnd('/'), System.StringComparison.OrdinalIgnoreCase)
                && string.Equals(Organization, other.Organization, System.StringComparison.OrdinalIgnoreCase);
        }
    }
}
