// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instances
{
    /// <summary>
    /// A Configuration that uses an installed Package
    /// </summary>
    public class TrakHoundPackageUsage
    {
        /// <summary>
        /// The UUID of the installed Package Version the Configuration resolves to
        /// </summary>
        public string PackageUuid { get; set; }

        public string PackageId { get; set; }

        /// <summary>
        /// The installed Version the Configuration resolves to
        /// </summary>
        public string PackageVersion { get; set; }

        /// <summary>
        /// The Version requested by the Configuration (ex. "*" for the latest)
        /// </summary>
        public string RequestedVersion { get; set; }

        /// <summary>
        /// The Configuration Category (ex. "app", "api", "worker")
        /// </summary>
        public string ConfigurationCategory { get; set; }

        public string ConfigurationId { get; set; }

        public string ConfigurationName { get; set; }
    }
}
