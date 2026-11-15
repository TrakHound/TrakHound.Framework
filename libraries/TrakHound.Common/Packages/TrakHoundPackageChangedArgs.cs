// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Packages
{
    /// <summary>
    /// Identifies a Package version that was added or removed
    /// </summary>
    public struct TrakHoundPackageChangedArgs
    {
        /// <summary>
        /// Category of the Package (ex. app, api, service)
        /// </summary>
        public string PackageCategory { get; set; }

        /// <summary>
        /// ID of the Package
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// Version of the Package
        /// </summary>
        public string PackageVersion { get; set; }


        /// <summary>
        /// Create the arguments for the specified Package Category, ID and Version
        /// </summary>
        public TrakHoundPackageChangedArgs(string packageCategory, string packageId, string packageVersion)
        {
            PackageCategory = packageCategory;
            PackageId = packageId;
            PackageVersion = packageVersion;
        }
    }
}
