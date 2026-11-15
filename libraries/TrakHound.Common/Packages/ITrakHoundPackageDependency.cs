// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Packages
{
    /// <summary>
    /// A dependency on another Package
    /// </summary>
    public interface ITrakHoundPackageDependency
    {
        /// <summary>
        /// ID of the Package that is depended on
        /// </summary>
        string Id { get; }

        /// <summary>
        /// Version of the Package that is depended on (may contain '*' wildcards)
        /// </summary>
        string Version { get; }

        /// <summary>
        /// The lowest Version of the Package that satisfies the Dependency (ex. "1.2.0"), used with a wildcard <see cref="Version"/> (ex. "1.*"). Null if there is no minimum
        /// </summary>
        string MinimumVersion => null;
    }
}
