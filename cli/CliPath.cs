// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.CLI
{
    internal static class CliPath
    {
        /// <summary>
        /// Resolve a path argument. Empty paths resolve to the Current Directory and relative paths are resolved against the Current Directory
        /// </summary>
        public static string Resolve(string path)
        {
            var value = path?.Trim().Trim('"');
            if (string.IsNullOrEmpty(value)) return Environment.CurrentDirectory;

            return Path.GetFullPath(value, Environment.CurrentDirectory);
        }
    }
}
