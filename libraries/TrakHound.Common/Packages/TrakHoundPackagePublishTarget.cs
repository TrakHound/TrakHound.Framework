// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// A named set of Package Servers that Packages are published to, and how their Versions are built (ex. "development" or "release")
    /// </summary>
    public class TrakHoundPackagePublishTarget
    {
        /// <summary>
        /// The Placeholder in <see cref="VersionSuffix"/> replaced with the UTC time of the run (yyyyMMddHHmmss)
        /// </summary>
        public const string TimestampPlaceholder = "{timestamp}";

        /// <summary>
        /// The Placeholder in <see cref="VersionSuffix"/> replaced with nothing on the default branch (main or master), and on any other branch
        /// with '-' and the branch name (characters other than letters, digits and '-' are replaced with '-')
        /// </summary>
        public const string BranchPlaceholder = "{branch}";


        /// <summary>
        /// The Package Servers to publish to
        /// </summary>
        [JsonPropertyName("destinations")]
        public IEnumerable<TrakHoundPackagePublishDestination> Destinations { get; set; }

        /// <summary>
        /// The Prerelease Label added to every Version published to this Target (ex. "dev{branch}.{timestamp}" creates 0.3.4-dev.20261114153000).
        /// No suffix publishes Stable Versions
        /// </summary>
        [JsonPropertyName("versionSuffix")]
        public string VersionSuffix { get; set; }

        /// <summary>
        /// Packages can be built from a working tree with uncommitted changes. Requires a <see cref="VersionSuffix"/> with {timestamp},
        /// so that every build has its own Version
        /// </summary>
        [JsonPropertyName("allowDirty")]
        public bool AllowDirty { get; set; }

        /// <summary>
        /// Packages must be signed to be published to this Target
        /// </summary>
        [JsonPropertyName("requireSigning")]
        public bool RequireSigning { get; set; }
    }
}
