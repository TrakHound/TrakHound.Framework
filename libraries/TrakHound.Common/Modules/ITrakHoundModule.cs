// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using TrakHound.Packages;

namespace TrakHound.Modules
{
    /// <summary>
    /// A Module is the set of Types of a specific base Type that were loaded from a Package
    /// </summary>
    public interface ITrakHoundModule
    {
        /// <summary>
        /// The Id of the Package the Module was loaded from
        /// </summary>
        string PackageId { get; }

        /// <summary>
        /// The Version of the Package the Module was loaded from
        /// </summary>
        string PackageVersion { get; }

        /// <summary>
        /// The directory the Package Version is installed in
        /// </summary>
        string PackageDirectory { get; }

        /// <summary>
        /// The Package the Module was loaded from
        /// </summary>
        public TrakHoundPackage Package { get; }

        /// <summary>
        /// The contents of the Package's ReadMe file (null if not found)
        /// </summary>
        public string PackageReadMe { get; }

        /// <summary>
        /// The Types loaded from the Package that implement the Module Type
        /// </summary>
        IEnumerable<Type> ModuleTypes { get; }
    }
}
