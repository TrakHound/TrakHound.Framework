// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.IO;

namespace TrakHound
{
    /// <summary>
    /// Manages temporary directories located in the "_temp" directory under the BaseDirectory
    /// </summary>
    public static class TrakHoundTemp
    {
        private const string _directory = "_temp";

        /// <summary>
        /// The base directory that the "_temp" directory is created in (defaults to the AppDomain BaseDirectory)
        /// </summary>
        public static string BaseDirectory { get; set; } = AppDomain.CurrentDomain.BaseDirectory;


        /// <summary>
        /// Gets the path of the "_temp" directory
        /// </summary>
        public static string GetDirectory()
        {
            return Path.Combine(BaseDirectory, _directory);
        }

        /// <summary>
        /// Creates a new uniquely named directory in the "_temp" directory and returns its path (null on error)
        /// </summary>
        public static string CreateDirectory()
        {
            try
            {
                var dir = Path.Combine(GetDirectory(), Guid.NewGuid().ToString());
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                return dir;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Deletes the "_temp" directory and everything in it
        /// </summary>
        public static void Clear()
        {
            var dir = GetDirectory();

            try
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, true);
                }
            }
            catch { }
        }
    }
}
