// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace TrakHound.Packages
{
    /// <summary>
    /// The packages file (trakhound-packages.json) that lists the installed Packages and their Auto Update configurations
    /// </summary>
    public class TrakHoundPackagesFile
    {
        /// <summary>
        /// The filename of the packages file
        /// </summary>
        public const string Filename = "trakhound-packages.json";


        /// <summary>
        /// Packages that are configured to update automatically
        /// </summary>
        [JsonPropertyName("autoUpdates")]
        public IEnumerable<TrakHoundPackageUpdateConfiguration> AutoUpdates { get; set; }

        /// <summary>
        /// The installed Packages
        /// </summary>
        [JsonPropertyName("packages")]
        public IEnumerable<TrakHoundPackagesFileItem> Packages { get; set; }


        /// <summary>
        /// Read the packages file from the specified directory (the application base directory if null). Returns null if not found or it cannot be read
        /// </summary>
        public static TrakHoundPackagesFile Read(string path = null)
        {
            string filePath;
            if (!string.IsNullOrEmpty(path)) filePath = Path.Combine(path, Filename);
            else filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Filename);

            if (File.Exists(filePath))
            {
                try
                {
                    var json = File.ReadAllText(filePath);
                    return JsonSerializer.Deserialize<TrakHoundPackagesFile>(json);
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Read the packages file from the specified directory (the application base directory if null). Returns null if not found or it cannot be read
        /// </summary>
        public static async Task<TrakHoundPackagesFile> ReadAsync(string path = null)
        {
            string filePath;
            if (!string.IsNullOrEmpty(path)) filePath = Path.Combine(path, Filename);
            else filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Filename);

            if (File.Exists(filePath))
            {
                try
                {
                    var json = await File.ReadAllTextAsync(filePath);
                    return JsonSerializer.Deserialize<TrakHoundPackagesFile>(json);
                }
                catch { }
            }

            return null;
        }


        /// <summary>
        /// Write the packages file to the specified directory (the application base directory if null). Returns false (and nothing is written) if there are no Packages
        /// </summary>
        public static bool Write(IEnumerable<TrakHoundPackagesFileItem> packages, IEnumerable<TrakHoundPackageUpdateConfiguration> autoUpdates = null, string path = null)
        {
            if (!packages.IsNullOrEmpty())
            {
                string filePath;
                if (!string.IsNullOrEmpty(path)) filePath = Path.Combine(path, Filename);
                else filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Filename);

                var file = new TrakHoundPackagesFile();
                file.Packages = packages;
                file.AutoUpdates = autoUpdates;

                Json.Write(file, filePath, atomic: true);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Write the packages file to the specified directory (the application base directory if null). Returns false (and nothing is written) if there are no Packages
        /// </summary>
        public static async Task<bool> WriteAsync(IEnumerable<TrakHoundPackagesFileItem> packages, IEnumerable<TrakHoundPackageUpdateConfiguration> autoUpdates = null, string path = null)
        {
            if (!packages.IsNullOrEmpty())
            {
                string filePath;
                if (!string.IsNullOrEmpty(path)) filePath = Path.Combine(path, Filename);
                else filePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Filename);

                var file = new TrakHoundPackagesFile();
                file.Packages = packages;
                file.AutoUpdates = autoUpdates;

                await Json.WriteAsync(file, filePath, atomic: true);
                return true;
            }

            return false;
        }
    }
}
