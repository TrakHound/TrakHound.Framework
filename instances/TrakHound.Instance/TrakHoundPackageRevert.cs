// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instances
{
    /// <summary>
    /// Whether an installed Package can be reverted to an older installed Version (by removing the Versions above it)
    /// </summary>
    public class TrakHoundPackageRevert
    {
        public string PackageId { get; set; }

        /// <summary>
        /// The latest installed Version
        /// </summary>
        public string CurrentVersion { get; set; }

        /// <summary>
        /// The installed Version to revert to (null if no older Version is installed)
        /// </summary>
        public string TargetVersion { get; set; }

        /// <summary>
        /// The installed Versions above the Target Version (removed by the Revert, highest first)
        /// </summary>
        public List<string> RemovedVersions { get; set; } = new List<string>();

        /// <summary>
        /// True if every Configuration using a Removed Version would use the Target Version once they are removed
        /// </summary>
        public bool CanRevert { get; set; }

        /// <summary>
        /// The number of Configurations that switch to the Target Version
        /// </summary>
        public int ConfigurationCount { get; set; }

        /// <summary>
        /// Why the Package can't be reverted to the Target Version (when CanRevert is false)
        /// </summary>
        public string Message { get; set; }
    }
}
