// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Packages;

namespace TrakHound.Hosting
{
    /// <summary>
    /// Reads a module's Package Information (trakhound.package.json) outside of a package (ex. to name and version the Api's OpenAPI Document)
    /// </summary>
    public static class TrakHoundPackageInformation
    {
        /// <summary>
        /// Reads the Package Information at path. Its version is the two part prefix in the file (ex. "0.3"), since the git height
        /// is only added when the CLI builds the Package. Returns null if the file doesn't exist or can't be read
        /// </summary>
        public static TrakHoundPackage Read(string path)
        {
            return TrakHoundPackage.ReadInformationFromFile(path);
        }
    }
}
