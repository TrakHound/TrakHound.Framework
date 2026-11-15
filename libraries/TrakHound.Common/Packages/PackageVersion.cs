// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Packages
{
    struct PackageVersion
    {
        public Version Version { get; set; }

        public string VersionValue { get; set; }

        public string PackageId { get; set; }


        public PackageVersion(Version version, string packageId, string packageVersion)
        {
            Version = version;
            PackageId = packageId;
            VersionValue = packageVersion;
        }
    }
}
