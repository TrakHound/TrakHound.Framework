// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;
using TrakHound.Extensions;

namespace TrakHound.Packages
{
    /// <summary>
    /// A dependency on another Package (as read from and written to the package.json file)
    /// </summary>
    public class TrakHoundPackageDependency : ITrakHoundPackageDependency
    {
        /// <summary>
        /// ID of the Package that is depended on
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// Version of the Package that is depended on (may contain '*' wildcards)
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; }

        /// <summary>
        /// The lowest Version of the Package that satisfies the Dependency (ex. "1.2.0"), used with a wildcard Version (ex. "1.*"). Null if there is no minimum.
        /// Older Instances ignore this property and only match the Version
        /// </summary>
        [JsonPropertyName("minimumVersion")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string MinimumVersion { get; set; }


        /// <summary>
        /// Create an empty Dependency
        /// </summary>
        public TrakHoundPackageDependency() { }

        /// <summary>
        /// Create a Dependency on the specified Package ID and Version
        /// </summary>
        public TrakHoundPackageDependency(string id, string version)
        {
            Id = id;
            Version = version;
        }

        /// <summary>
        /// Create a Dependency on the specified Package ID, Version and Minimum Version
        /// </summary>
        public TrakHoundPackageDependency(string id, string version, string minimumVersion)
        {
            Id = id;
            Version = version;
            MinimumVersion = minimumVersion;
        }


        /// <summary>
        /// Returns true if the Package Version satisfies the Dependency: it matches the Version ('*' wildcards; any Version if null)
        /// and is at least the Minimum Version (if set). A Minimum Version that is not a valid version is ignored (see <see cref="Validate(ITrakHoundPackageDependency)"/>)
        /// </summary>
        /// <param name="dependency">Dependency to check</param>
        /// <param name="packageVersion">Version of the Package (ex. an installed or downloaded Version)</param>
        public static bool IsSatisfiedBy(ITrakHoundPackageDependency dependency, string packageVersion)
        {
            if (dependency == null || string.IsNullOrEmpty(packageVersion)) return false;

            var matchVersion = !string.IsNullOrEmpty(dependency.Version) ? dependency.Version : TrakHoundPackage.WilcardVersion;
            if (!StringFunctions.MatchVersion(packageVersion, matchVersion)) return false;

            var minimumVersion = dependency.MinimumVersion.ToVersion();
            if (minimumVersion != null)
            {
                return VersionComparer.Default.Compare(packageVersion, dependency.MinimumVersion) >= 0;
            }

            return true;
        }

        /// <summary>
        /// Returns an error message if the Dependency is not valid (no ID, a Minimum Version that is not a valid version (ex. "1.2.0"),
        /// or an exact Version lower than the Minimum Version). Returns null if the Dependency is valid
        /// </summary>
        /// <param name="dependency">Dependency to validate</param>
        public static string Validate(ITrakHoundPackageDependency dependency)
        {
            if (dependency == null) return "Dependency is null";
            if (string.IsNullOrEmpty(dependency.Id)) return "Dependency has no ID";

            if (!string.IsNullOrEmpty(dependency.MinimumVersion))
            {
                var minimumVersion = dependency.MinimumVersion.ToVersion();
                if (minimumVersion == null)
                {
                    return $"Dependency ({dependency.Id}) has an invalid Minimum Version : {dependency.MinimumVersion}";
                }

                if (!string.IsNullOrEmpty(dependency.Version) && !dependency.Version.Contains('*'))
                {
                    var version = dependency.Version.ToVersion();
                    if (version != null && version < minimumVersion)
                    {
                        return $"Dependency ({dependency.Id}) Version ({dependency.Version}) is lower than its Minimum Version ({dependency.MinimumVersion})";
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns the Version and Minimum Version as a string (ex. "1.* (>= 1.2.0)")
        /// </summary>
        public static string ToVersionString(ITrakHoundPackageDependency dependency)
        {
            if (dependency == null) return null;

            var version = !string.IsNullOrEmpty(dependency.Version) ? dependency.Version : TrakHoundPackage.WilcardVersion;
            return !string.IsNullOrEmpty(dependency.MinimumVersion) ? $"{version} (>= {dependency.MinimumVersion})" : version;
        }
    }
}
