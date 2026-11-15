// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Apps
{
    /// <summary>
    /// Information about an App loaded on the Instance (its Configuration, Package, and Pages)
    /// </summary>
    public class TrakHoundAppInformation
    {
        /// <summary>
        /// The ID of the App (App Configuration)
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The display Name of the App
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The base Route (URL path) that the App's Pages are hosted at
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        /// <summary>
        /// The ID of the Router used by the App's Client
        /// </summary>
        [JsonPropertyName("routerId")]
        public string RouterId { get; set; }

        /// <summary>
        /// The ID of the Group the App belongs to
        /// </summary>
        [JsonPropertyName("groupId")]
        public string GroupId { get; set; }

        /// <summary>
        /// The ID of the App's Package
        /// </summary>
        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the App's Package that is loaded
        /// </summary>
        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        /// <summary>
        /// When the loaded Package was built
        /// </summary>
        [JsonPropertyName("packageBuildDate")]
        public DateTime PackageBuildDate { get; set; }

        /// <summary>
        /// The Hash of the loaded Package
        /// </summary>
        [JsonPropertyName("packageHash")]
        public string PackageHash { get; set; }

        /// <summary>
        /// The Icon (image) from the Package's Metadata
        /// </summary>
        [JsonPropertyName("packageIcon")]
        public string PackageIcon { get; set; }

        /// <summary>
        /// The contents of the Package's ReadMe
        /// </summary>
        [JsonPropertyName("packageReadMe")]
        public string PackageReadMe { get; set; }

        /// <summary>
        /// The source Repository from the Package's Metadata
        /// </summary>
        [JsonPropertyName("repository")]
        public string Repository { get; set; }

        /// <summary>
        /// The source Repository Branch from the Package's Metadata
        /// </summary>
        [JsonPropertyName("repositoryBranch")]
        public string RepositoryBranch { get; set; }

        /// <summary>
        /// The Directory within the source Repository from the Package's Metadata
        /// </summary>
        [JsonPropertyName("repositoryDirectory")]
        public string RepositoryDirectory { get; set; }

        /// <summary>
        /// The source Repository Commit from the Package's Metadata
        /// </summary>
        [JsonPropertyName("repositoryCommit")]
        public string RepositoryCommit { get; set; }

        /// <summary>
        /// The TrakHound Version the Package was built against (from the Package's Metadata)
        /// </summary>
        [JsonPropertyName("trakhoundVersion")]
        public string TrakHoundVersion { get; set; }

        /// <summary>
        /// The Pages (routable Components) in the App
        /// </summary>
        [JsonPropertyName("pages")]
        public IEnumerable<TrakHoundAppPageInformation> Pages { get; set; }
    }
}
