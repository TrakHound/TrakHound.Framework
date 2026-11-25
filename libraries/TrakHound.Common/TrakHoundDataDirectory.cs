// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.IO;

namespace TrakHound
{
    /// <summary>
    /// The directory a process writes its working files in (ex. "_temp", "_modules", "buffer", "cache" and backups),
    /// so that its program directory can stay read-only. Defaults to the application's base directory; a host that keeps its
    /// data elsewhere (ex. the Instance, in its Data Path) sets it before anything is written
    /// </summary>
    public static class TrakHoundDataDirectory
    {
        private static string _path;


        /// <summary>
        /// The Data Directory (the application's base directory unless it has been set)
        /// </summary>
        public static string Path
        {
            get => !string.IsNullOrEmpty(_path) ? _path : AppContext.BaseDirectory;
            set => _path = !string.IsNullOrEmpty(value) ? System.IO.Path.GetFullPath(value) : null;
        }


        /// <summary>
        /// A path in the Data Directory (a rooted path is returned unchanged)
        /// </summary>
        public static string GetPath(params string[] paths)
        {
            var path = paths != null && paths.Length > 0 ? System.IO.Path.Combine(paths) : string.Empty;
            return System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(Path, path);
        }
    }
}
