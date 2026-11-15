// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Modules
{
    /// <summary>
    /// Loads Modules (Types of a specific base Type) from installed Packages and unloads them when the Package is removed
    /// </summary>
    public interface ITrakHoundModuleManager
    {
        /// <summary>
        /// The Package Categories that Modules are loaded from
        /// </summary>
        IEnumerable<string> PackageCategories { get; }

        /// <summary>
        /// Maps the Module key ("{PackageId}-{PackageVersion}") to the filename of the Assembly to load
        /// </summary>
        Func<string, string> FilterFunction { get; }

        /// <summary>
        /// The Modules that are currently loaded
        /// </summary>
        IEnumerable<ITrakHoundModule> Modules { get; }

        /// <summary>
        /// All of the Types from the Modules that are currently loaded
        /// </summary>
        IEnumerable<Type> ModuleTypes { get; }

        /// <summary>
        /// Invoked when a Module is loaded
        /// </summary>
        EventHandler<ITrakHoundModule> ModuleAdded { get; set; }

        /// <summary>
        /// Invoked when a Module is unloaded because its Package was removed
        /// </summary>
        EventHandler<ITrakHoundModule> ModuleRemoved { get; set; }


        /// <summary>
        /// Gets the Module for the specified Package, loading it if needed. The Version can contain wildcards ("*" = latest). Returns null if the Package is not found or fails to load
        /// </summary>
        ITrakHoundModule Get(string packageId, string packageVersion);

        /// <summary>
        /// Gets the Module Types for the specified Package, loading it if needed (empty if not found)
        /// </summary>
        IEnumerable<Type> GetTypes(string packageId, string packageVersion);

        /// <summary>
        /// Gets Information about the loaded Modules and the Modules that failed to load
        /// </summary>
        IEnumerable<TrakHoundModuleInformation> GetInformation();
    }
}
