// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Apps
{
    /// <summary>
    /// The number of Sessions (Browser connections) using an App Configuration's Package Version
    /// </summary>
    public class TrakHoundAppVersionSessions
    {
        public string ConfigurationId { get; set; }

        public string PackageId { get; set; }

        public string PackageVersion { get; set; }

        public int SessionCount { get; set; }
    }
}
