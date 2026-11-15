// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Instances
{
    /// <summary>
    /// Information about the Operating System an Instance is running on
    /// </summary>
    public class TrakHoundInstanceOperatingSystemInformation
    {
        /// <summary>
        /// The Operating System platform (ex. Win32NT, Unix)
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// The full Operating System name and version string
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The Operating System version number
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; }
    }
}
