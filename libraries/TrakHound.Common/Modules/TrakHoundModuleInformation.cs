// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Modules
{
    /// <summary>
    /// Information about a Module (a Package's loaded Types) or a Module that failed to load
    /// </summary>
    public class TrakHoundModuleInformation
    {
        /// <summary>
        /// The Type that the Module Manager loads (ex. ITrakHoundWorker)
        /// </summary>
        [JsonPropertyName("moduleType")]
        public string ModuleType { get; set; }

        /// <summary>
        /// The Package Categories that the Module Manager loads from
        /// </summary>
        [JsonPropertyName("managerCategories")]
        public IEnumerable<string> ManagerCategories { get; set; }

        /// <summary>
        /// The Category of the Package (null for a Package that was not found)
        /// </summary>
        [JsonPropertyName("packageCategory")]
        public string PackageCategory { get; set; }

        /// <summary>
        /// The Id of the Package
        /// </summary>
        [JsonPropertyName("packageId")]
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the Package (as requested when the Package was not found)
        /// </summary>
        [JsonPropertyName("packageVersion")]
        public string PackageVersion { get; set; }

        /// <summary>
        /// The directory the Package Version is installed in (null if not loaded)
        /// </summary>
        [JsonPropertyName("packageDirectory")]
        public string PackageDirectory { get; set; }

        /// <summary>
        /// True if the Module is loaded, False if it failed to load (see Error)
        /// </summary>
        [JsonPropertyName("isLoaded")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool IsLoaded { get; set; }

        /// <summary>
        /// The Full Names of the loaded Types
        /// </summary>
        [JsonPropertyName("types")]
        public IEnumerable<string> Types { get; set; }

        /// <summary>
        /// When the Module was loaded (Unix Nanoseconds)
        /// </summary>
        [JsonPropertyName("loadedTime")]
        public long LoadedTime { get; set; }

        /// <summary>
        /// How long the Module took to load (Milliseconds)
        /// </summary>
        [JsonPropertyName("loadDuration")]
        public long LoadDuration { get; set; }

        /// <summary>
        /// Problems that did not stop the Module from loading (ex. some Types could not be loaded)
        /// </summary>
        [JsonPropertyName("warnings")]
        public IEnumerable<string> Warnings { get; set; }

        /// <summary>
        /// Why the Module failed to load
        /// </summary>
        [JsonPropertyName("error")]
        public string Error { get; set; }

        /// <summary>
        /// When the Module last failed to load (Unix Nanoseconds)
        /// </summary>
        [JsonPropertyName("errorTime")]
        public long ErrorTime { get; set; }

        /// <summary>
        /// How many times loading the Module has failed
        /// </summary>
        [JsonPropertyName("errorCount")]
        public int ErrorCount { get; set; }
    }
}
