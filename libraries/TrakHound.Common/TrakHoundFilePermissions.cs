// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.IO;

namespace TrakHound
{
    /// <summary>
    /// Limits files that hold keys or password hashes to the user the Instance runs as. On Linux and macOS a new file is readable by
    /// other users (the umask usually allows it), so these set owner only permissions. On Windows they do nothing, since the Instance's
    /// data folders already only allow Administrators and SYSTEM (and the folder's permissions are inherited)
    /// </summary>
    public static class TrakHoundFilePermissions
    {
        public const UnixFileMode OwnerFile = UnixFileMode.UserRead | UnixFileMode.UserWrite;
        public const UnixFileMode OwnerDirectory = UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute;


        /// <summary>
        /// Allow only the owner to read and write the file (returns false if it couldn't be changed)
        /// </summary>
        public static bool RestrictFile(string path)
        {
            if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(path)) return true;

            try
            {
                if (!File.Exists(path)) return false;
                if (File.GetUnixFileMode(path) != OwnerFile) File.SetUnixFileMode(path, OwnerFile);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Allow only the owner to use the directory, and to read and write the files directly in it (returns false if any couldn't be changed)
        /// </summary>
        public static bool RestrictDirectory(string path)
        {
            if (OperatingSystem.IsWindows() || string.IsNullOrEmpty(path)) return true;

            try
            {
                if (!Directory.Exists(path)) Directory.CreateDirectory(path, OwnerDirectory);
                else if (File.GetUnixFileMode(path) != OwnerDirectory) File.SetUnixFileMode(path, OwnerDirectory);

                var restricted = true;
                foreach (var file in Directory.GetFiles(path))
                {
                    if (!RestrictFile(file)) restricted = false;
                }
                return restricted;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// The FileStream Options for creating a new file that only the owner can read and write
        /// </summary>
        public static FileStreamOptions CreateNewOptions()
        {
            var options = new FileStreamOptions { Mode = FileMode.CreateNew, Access = FileAccess.Write, Share = FileShare.None };
            if (!OperatingSystem.IsWindows()) options.UnixCreateMode = OwnerFile;
            return options;
        }
    }
}
