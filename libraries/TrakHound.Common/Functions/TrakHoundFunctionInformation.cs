// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Functions
{
    /// <summary>
    /// Information about a Function loaded in an Instance and the Package it was loaded from
    /// </summary>
    public class TrakHoundFunctionInformation
    {
        /// <summary>
        /// The Id of the Function used when Running it
        /// </summary>
        [JsonPropertyName("functionId")]
        public string FunctionId { get; set; }

        /// <summary>
        /// The Name of the Function (from the Configuration, or the Package if not set)
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// A description of the Function (from the Configuration, or the Package if not set)
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The Id of the Configuration the Function was created from
        /// </summary>
        [JsonPropertyName("configurationId")]
        public string ConfigurationId { get; set; }

        /// <summary>
        /// The Id of the Volume used to store Files for the Function
        /// </summary>
        [JsonPropertyName("volumeId")]
        public string VolumeId { get; set; }

        /// <summary>
        /// The ID of the Group the Function belongs to
        /// </summary>
        [JsonPropertyName("groupId")]
        public string GroupId { get; set; }

        /// <summary>
        /// The Id of the Package that contains the Function
        /// </summary>
        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the Package that was loaded
        /// </summary>
        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        /// <summary>
        /// The date the Package was built
        /// </summary>
        [JsonPropertyName("packageBuildDate")]
        public DateTime? PackageBuildDate { get; set; }

        /// <summary>
        /// The unique Identifier of the Package build
        /// </summary>
        [JsonPropertyName("packageUuid")]
        public string PackageUuid { get; set; }

        /// <summary>
        /// The Hash of the Package
        /// </summary>
        [JsonPropertyName("packageHash")]
        public string PackageHash { get; set; }

        /// <summary>
        /// The Icon (image) of the Package from its Metadata
        /// </summary>
        [JsonPropertyName("packageIcon")]
        public string PackageIcon { get; set; }

        /// <summary>
        /// The Input Parameters the Function describes (FunctionParameter attributes), in the order they are declared
        /// </summary>
        [JsonPropertyName("parameters")]
        public List<TrakHoundFunctionParameterInformation> Parameters { get; set; }

        /// <summary>
        /// The contents of the Package's ReadMe file
        /// </summary>
        [JsonPropertyName("packageReadMe")]
        public string PackageReadMe { get; set; }

        /// <summary>
        /// The source Repository the Package was built from
        /// </summary>
        [JsonPropertyName("repository")]
        public string Repository { get; set; }

        /// <summary>
        /// The Branch of the Repository the Package was built from
        /// </summary>
        [JsonPropertyName("repositoryBranch")]
        public string RepositoryBranch { get; set; }

        /// <summary>
        /// The Directory within the Repository the Package was built from
        /// </summary>
        [JsonPropertyName("repositoryDirectory")]
        public string RepositoryDirectory { get; set; }

        /// <summary>
        /// The Commit of the Repository the Package was built from
        /// </summary>
        [JsonPropertyName("repositoryCommit")]
        public string RepositoryCommit { get; set; }

        /// <summary>
        /// The TrakHound version the Package was built against
        /// </summary>
        [JsonPropertyName("trakhoundVersion")]
        public string TrakHoundVersion { get; set; }

        /// <summary>
        /// The time the Function was last updated (not set by TrakHoundFunctionInformationFactory.Create)
        /// </summary>
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// The average duration of a Run (not set by TrakHoundFunctionInformationFactory.Create)
        /// </summary>
        [JsonPropertyName("averageDuration")]
        public TimeSpan AverageDuration { get; set; }

        /// <summary>
        /// The number of times the Function has been Run (not set by TrakHoundFunctionInformationFactory.Create)
        /// </summary>
        [JsonPropertyName("runCount")]
        public long RunCount { get; set; }
    }
}
