// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// Where Packages are published (read from the trakhound.package.publish.json file). The nearest file to a Package's directory
    /// (the directory itself, then each of its parents) is used, so one file at the root of a repository covers every Package below it
    /// </summary>
    public class TrakHoundPackagePublishSettings
    {
        /// <summary>
        /// The filename of the Publish Settings (looked up from the Package directory through its parent directories)
        /// </summary>
        public const string Filename = "trakhound.package.publish.json";


        /// <summary>
        /// The Target used when none is specified (ex. "development")
        /// </summary>
        [JsonPropertyName("defaultTarget")]
        public string DefaultTarget { get; set; }

        /// <summary>
        /// The Targets that Packages can be published to, by name (ex. "development" and "release")
        /// </summary>
        [JsonPropertyName("targets")]
        public Dictionary<string, TrakHoundPackagePublishTarget> Targets { get; set; } = new Dictionary<string, TrakHoundPackagePublishTarget>(StringComparer.OrdinalIgnoreCase);
    }
}
