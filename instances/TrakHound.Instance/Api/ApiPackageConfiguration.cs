// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Packages;

namespace TrakHound.Api
{
    class ApiPackageConfiguration
    {
        public ITrakHoundApiController Api { get; set; }

        public TrakHoundPackage Package { get; set; }

        public string PackageReadMe { get; set; }


        public ApiPackageConfiguration(ITrakHoundApiController api, TrakHoundPackage package, string packageReadMe = null)
        {
            Api = api;
            Package= package;
            PackageReadMe = packageReadMe;
        }
    }
}
