// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Extensions
{
    /// <summary>
    /// Compares Version strings, including those with a Prerelease Label, using the Semantic Versioning order
    /// (ex. 1.0.0-alpha &lt; 1.0.0-alpha.2 &lt; 1.0.0-alpha.10 &lt; 1.0.0-beta &lt; 1.0.0 &lt; 1.0.1-dev). Strings that aren't Versions sort lowest
    /// </summary>
    public class VersionComparer : IComparer<string>
    {
        /// <summary>
        /// A shared instance of the Comparer
        /// </summary>
        public static readonly VersionComparer Default = new VersionComparer();

        /// <summary>
        /// Orders every Stable Version above every Prerelease, and otherwise the same as <see cref="Default"/>. The highest is what "*" selects
        /// </summary>
        public static readonly VersionComparer PreferStable = new VersionComparer(true);

        private readonly bool _preferStable;


        /// <summary>
        /// Create a Comparer that uses the Semantic Versioning order
        /// </summary>
        public VersionComparer() { }

        private VersionComparer(bool preferStable)
        {
            _preferStable = preferStable;
        }


        /// <summary>
        /// Returns true if the string is a Version without a Prerelease Label (ex. 1.2.3, but not 1.2.3-beta)
        /// </summary>
        public static bool IsStable(string version)
        {
            return version.ToBaseVersion() != null && GetPrereleaseLabel(version) == null;
        }


        /// <summary>
        /// Compares two Version strings. Returns a positive number if x is newer than y, a negative number if x is older, and 0 if they are equal
        /// </summary>
        public int Compare(string x, string y)
        {
            if (x == null || y == null) return (x != null ? 1 : 0) - (y != null ? 1 : 0);

            if (_preferStable)
            {
                var stable = IsStable(x).CompareTo(IsStable(y));
                if (stable != 0) return stable;
            }

            var xV = x.ToBaseVersion();
            var yV = y.ToBaseVersion();

            if (xV == null || yV == null)
            {
                if (xV != null) return 1;
                if (yV != null) return -1;
                return string.CompareOrdinal(x, y);
            }

            var result = xV.CompareTo(yV);
            return result != 0 ? result : ComparePrereleaseLabels(GetPrereleaseLabel(x), GetPrereleaseLabel(y));
        }


        private static string GetPrereleaseLabel(string version)
        {
            var buildIndex = version.IndexOf('+');
            if (buildIndex >= 0) version = version.Substring(0, buildIndex);

            var labelIndex = version.IndexOf('-');
            return labelIndex >= 0 ? version.Substring(labelIndex + 1) : null;
        }

        private static int ComparePrereleaseLabels(string x, string y)
        {
            // A Release is newer than its Prereleases
            if (x == null || y == null) return (x == null ? 1 : 0) - (y == null ? 1 : 0);

            var xParts = x.Split('.');
            var yParts = y.Split('.');

            for (var i = 0; i < Math.Min(xParts.Length, yParts.Length); i++)
            {
                var result = ComparePrereleasePart(xParts[i], yParts[i]);
                if (result != 0) return result;
            }

            return xParts.Length.CompareTo(yParts.Length);
        }

        private static int ComparePrereleasePart(string x, string y)
        {
            var xIsNumber = long.TryParse(x, out var xNumber);
            var yIsNumber = long.TryParse(y, out var yNumber);

            if (xIsNumber && yIsNumber) return xNumber.CompareTo(yNumber);

            // Numbers sort below text
            if (xIsNumber) return -1;
            if (yIsNumber) return 1;

            return string.CompareOrdinal(x, y);
        }
    }
}
