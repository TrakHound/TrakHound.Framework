// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// JSON representation of a Package, used for the package.json file and Package Server responses
    /// </summary>
    public class TrakHoundPackageJson
    {
        /// <summary>
        /// Unique identifier of the Package version, generated from the ID and Version
        /// </summary>
        [JsonPropertyName("uuid")]
        public string Uuid { get; set; }

        /// <summary>
        /// ID of the Package
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// Version of the Package
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; }

        /// <summary>
        /// Category of the Package (ex. app, api, service)
        /// </summary>
        [JsonPropertyName("category")]
        public string Category { get; set; }

        /// <summary>
        /// Date the Package was built
        /// </summary>
        [JsonPropertyName("buildDate")]
        public DateTime BuildDate { get; set; }

        /// <summary>
        /// Hash of this specific build of the Package, generated from the Category, ID, Version and BuildDate
        /// </summary>
        [JsonPropertyName("hash")]
        public string Hash { get; set; }

        /// <summary>
        /// SHA-256 of the Package's files. Null for a Package without a Manifest
        /// </summary>
        [JsonPropertyName("contentHash")]
        public string ContentHash { get; set; }

        /// <summary>
        /// Additional Package information (ex. description, publisher, repository)
        /// </summary>
        [JsonPropertyName("metadata")]
        public Dictionary<string, object> Metadata { get; set; }

        /// <summary>
        /// Other Packages that this Package depends on
        /// </summary>
        [JsonPropertyName("dependencies")]
        public IEnumerable<TrakHoundPackageDependency> Dependencies { get; set; }


        /// <summary>
        /// Create an empty Package JSON
        /// </summary>
        public TrakHoundPackageJson() { }

        /// <summary>
        /// Create the JSON representation of a Package
        /// </summary>
        public TrakHoundPackageJson(TrakHoundPackage package)
        {
            if (package != null)
            {
                Uuid = package.Uuid;
                Id = package.Id;
                Version = package.Version;
                Category = package.Category;
                BuildDate = package.BuildDate;
                Hash = package.Hash;
                ContentHash = package.ContentHash;
                Metadata = package.Metadata;

                if (!package.Dependencies.IsNullOrEmpty())
                {
                    var dependencies = new List<TrakHoundPackageDependency>();
                    foreach (var dependency in package.Dependencies)
                    {
                        dependencies.Add((TrakHoundPackageDependency)dependency);
                    }

                    Dependencies = dependencies;             
                }
            }
        }


        /// <summary>
        /// Convert to a TrakHoundPackage (the Uuid and Hash are regenerated from the other values)
        /// </summary>
        public TrakHoundPackage ToPackage()
        {
            var builder = new TrakHoundPackageBuilder();
            builder.Id = Id;
            builder.Version = Version;
            builder.Category = Category;
            builder.BuildDate = BuildDate;
            builder.ContentHash = ContentHash;
            builder.SetMetadata(Metadata);

            if (!Dependencies.IsNullOrEmpty())
            {
                var dependencies = new List<ITrakHoundPackageDependency>();

                foreach (var dependency in Dependencies)
                {
                    dependencies.Add(dependency);
                }

                builder.Dependencies = dependencies;
            }

            return builder.Build();
        }
    }
}
