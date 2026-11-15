// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using TrakHound.Packages;

namespace TrakHound.Modules
{
    /// <summary>
    /// The set of Types of a specific base Type that were loaded from a Package
    /// </summary>
    public sealed class TrakHoundModule : ITrakHoundModule
    {
        /// <summary>
        /// The Id of the Package the Module was loaded from
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the Package the Module was loaded from
        /// </summary>
        public string PackageVersion { get; set; }

        /// <summary>
        /// The directory the Package Version is installed in
        /// </summary>
        public string PackageDirectory { get; set; }

        /// <summary>
        /// The Package the Module was loaded from
        /// </summary>
        public TrakHoundPackage Package { get; set; }

        /// <summary>
        /// The contents of the Package's ReadMe file (null if not found)
        /// </summary>
        public string PackageReadMe { get; set; }

        /// <summary>
        /// The Types loaded from the Package that implement the Module Type
        /// </summary>
        public IEnumerable<Type> ModuleTypes { get; set; }

        /// <summary>
        /// When the Module was loaded (UTC)
        /// </summary>
        public DateTime LoadedTime { get; set; }

        /// <summary>
        /// How long the Module took to load
        /// </summary>
        public TimeSpan LoadDuration { get; set; }

        /// <summary>
        /// Problems that did not stop the Module from loading (ex. some Types could not be loaded)
        /// </summary>
        public IEnumerable<string> Warnings { get; set; }


        /// <summary>
        /// Create a new Module for the specified Package and Types
        /// </summary>
        public TrakHoundModule(TrakHoundPackage package, string packageDirectory, IEnumerable<Type> moduleTypes, string packageReadme = null)
        {
            if (package != null)
            {
                PackageId = package.Id;
                PackageVersion = package.Version;
                PackageDirectory = packageDirectory;
                Package = package;
            }

            PackageReadMe = packageReadme;

            ModuleTypes = moduleTypes;
        }
    }
}
