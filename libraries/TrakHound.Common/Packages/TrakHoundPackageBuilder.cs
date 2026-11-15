// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Packages
{
    /// <summary>
    /// Mutable builder used to create an immutable <see cref="TrakHoundPackage"/>
    /// </summary>
    public class TrakHoundPackageBuilder
    {
        private readonly Dictionary<string, object> _metadata = new Dictionary<string, object>();
        private List<ITrakHoundPackageDependency> _dependencies = new List<ITrakHoundPackageDependency>();


        /// <summary>
        /// ID of the package
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Version of the package
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Category of the package (ex. app, api, service)
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// Directory the package information was read from
        /// </summary>
        public string Location { get; set; }

        /// <summary>
        /// Date the package was built. This is not set automatically and is included in the package Hash, so set it (typically to <see cref="DateTime.UtcNow"/>) when creating a new build
        /// </summary>
        public DateTime BuildDate { get; set; }

        /// <summary>
        /// SHA-256 of the package's files (see <see cref="TrakHoundPackageManifest.GetContentHash"/>). Set when the package is packed
        /// </summary>
        public string ContentHash { get; set; }

        /// <summary>
        /// Additional package information. Use <see cref="SetMetadata(string, object)"/> to add or overwrite entries
        /// </summary>
        public Dictionary<string, object> Metadata => _metadata;

        /// <summary>
        /// Other packages that this package depends on. Assigning a collection copies it
        /// </summary>
        public IEnumerable<ITrakHoundPackageDependency> Dependencies
        {
            get => _dependencies;
            set => _dependencies = value != null ? new List<ITrakHoundPackageDependency>(value) : new List<ITrakHoundPackageDependency>();
        }


        /// <summary>
        /// Create an empty builder
        /// </summary>
        public TrakHoundPackageBuilder() { }

        /// <summary>
        /// Create a builder initialized with the values of an existing package
        /// </summary>
        public TrakHoundPackageBuilder(TrakHoundPackage package)
        {
            if (package != null)
            {
                Id = package.Id;
                Category = package.Category;
                Version = package.Version;
                Location = package.Location;
                BuildDate = package.BuildDate;
                ContentHash = package.ContentHash;
                SetMetadata(package.Metadata);
                Dependencies = package.Dependencies;
            }
        }


        /// <summary>
        /// Add or overwrite a metadata entry. A null value removes the entry
        /// </summary>
        public TrakHoundPackageBuilder SetMetadata(string key, object value)
        {
            if (!string.IsNullOrEmpty(key))
            {
                if (value != null) _metadata[key] = value;
                else _metadata.Remove(key);
            }
            return this;
        }

        /// <summary>
        /// Add or overwrite each entry in <paramref name="metadata"/>
        /// </summary>
        public TrakHoundPackageBuilder SetMetadata(IDictionary<string, object> metadata)
        {
            if (metadata != null)
            {
                foreach (var entry in metadata) SetMetadata(entry.Key, entry.Value);
            }
            return this;
        }

        /// <summary>
        /// Add a dependency on another package
        /// </summary>
        public TrakHoundPackageBuilder AddDependency(ITrakHoundPackageDependency dependency)
        {
            if (dependency != null) _dependencies.Add(dependency);
            return this;
        }


        /// <summary>
        /// Create a new <see cref="TrakHoundPackage"/> from the current values
        /// </summary>
        public TrakHoundPackage Build()
        {
            return new TrakHoundPackage(this);
        }
    }
}
