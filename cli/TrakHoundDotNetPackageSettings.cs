// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Packages
{
    public class TrakHoundDotNetPackageSettings
    {
        public string DotnetSdkLocation { get; set; }

        public string Version { get; set; }

        public string Configuration { get; set; }

        public bool IncludeDebugSymbols { get; set; }

        public bool IncludeSource { get; set; }

        public string GitBranch { get; set; }

        public string GitCommit { get; set; }

        public bool? GitDirty { get; set; }

        public TrakHoundPackageSigner Signer { get; set; }


        public TrakHoundDotNetPackageSettings()
        {
            Configuration = "Release";
            IncludeDebugSymbols = false;
            IncludeSource = true;
        }
    }
}
