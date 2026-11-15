// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Packages
{
    /// <summary>
    /// The result of installing a Package
    /// </summary>
    public struct TrakHoundPackageInstallResult
    {
        /// <summary>
        /// True if the Package was installed successfully
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// ID of the Package that was installed (null if the install failed)
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// Version of the Package that was installed (null if the install failed)
        /// </summary>
        public string PackageVersion { get; set; }

        /// <summary>
        /// A message describing the result
        /// </summary>
        public string Message { get; set; }
    }
}
