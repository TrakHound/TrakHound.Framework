// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Workers
{
    /// <summary>
    /// Information about a Worker on an Instance (its Status, Configuration, and the Package it was loaded from)
    /// </summary>
    public class TrakHoundWorkerInformation
    {
        /// <summary>
        /// The unique Identifier of the Worker
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The Name of the Worker (the Package Id when created using Create)
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// A description of the Worker (from the Configuration, or the Package's description metadata)
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The current Status of the Worker
        /// </summary>
        [JsonPropertyName("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundWorkerStatusType Status { get; set; }

        /// <summary>
        /// When the Information was last updated
        /// </summary>
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// The CPU usage of the Worker (percent of total machine capacity, 0 - 100)
        /// </summary>
        [JsonPropertyName("cpuUsage")]
        public double CpuUsage { get; set; }

        /// <summary>
        /// The total CPU time consumed by the Worker (in milliseconds)
        /// </summary>
        [JsonPropertyName("cpuTime")]
        public double CpuTime { get; set; }

        /// <summary>
        /// The Id of the Worker's Configuration
        /// </summary>
        [JsonPropertyName("configurationId")]
        public string ConfigurationId { get; set; }

        /// <summary>
        /// The Id of the Volume the Worker uses
        /// </summary>
        [JsonPropertyName("volumeId")]
        public string VolumeId { get; set; }

        /// <summary>
        /// The ID of the Group the Worker belongs to
        /// </summary>
        [JsonPropertyName("groupId")]
        public string GroupId { get; set; }

        /// <summary>
        /// The Id of the Package that contains the Worker
        /// </summary>
        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the Package
        /// </summary>
        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        /// <summary>
        /// The unique Identifier of the Package build
        /// </summary>
        [JsonPropertyName("packageUuid")]
        public string PackageUuid { get; set; }

        /// <summary>
        /// The date the Package was built
        /// </summary>
        [JsonPropertyName("packageBuildDate")]
        public DateTime PackageBuildDate { get; set; }

        /// <summary>
        /// The hash of the Package
        /// </summary>
        [JsonPropertyName("packageHash")]
        public string PackageHash { get; set; }

        /// <summary>
        /// The Package's image (icon) metadata
        /// </summary>
        [JsonPropertyName("packageIcon")]
        public string PackageIcon { get; set; }

        /// <summary>
        /// The contents of the Package's ReadMe
        /// </summary>
        [JsonPropertyName("packageReadMe")]
        public string PackageReadMe { get; set; }

        /// <summary>
        /// The source code Repository the Package was built from
        /// </summary>
        [JsonPropertyName("repository")]
        public string Repository { get; set; }

        /// <summary>
        /// The Repository branch the Package was built from
        /// </summary>
        [JsonPropertyName("repositoryBranch")]
        public string RepositoryBranch { get; set; }

        /// <summary>
        /// The directory within the Repository the Package was built from
        /// </summary>
        [JsonPropertyName("repositoryDirectory")]
        public string RepositoryDirectory { get; set; }

        /// <summary>
        /// The Repository commit the Package was built from
        /// </summary>
        [JsonPropertyName("repositoryCommit")]
        public string RepositoryCommit { get; set; }

        /// <summary>
        /// The TrakHound Version the Package was built against
        /// </summary>
        [JsonPropertyName("trakhoundVersion")]
        public string TrakHoundVersion { get; set; }
    }
}
