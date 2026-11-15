// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Packages;

namespace TrakHound.Functions
{
    class FunctionPackageConfiguration
    {
        public ITrakHoundFunction Function { get; set; }

        public TrakHoundPackage Package { get; set; }

        public string PackageReadMe { get; set; }


        public FunctionPackageConfiguration(ITrakHoundFunction function, TrakHoundPackage package, string packageReadMe = null)
        {
            Function = function;
            Package= package;
            PackageReadMe = packageReadMe;
        }
    }
}
