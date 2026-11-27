// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        /// Write the installed Packages to the packages file in the directory, keeping what the file already has that isn't known from the
        /// installed Packages : its Auto Updates, and for each installed Version that it already listed, the requested Version (when the item
        /// has none) and with keepOrigins the Origin (when the item has none). An item that is new and has no requested Version requests its
        /// installed Version. Returns false (and nothing is written) if there are no Packages
        /// </summary>
        public static bool Update(IEnumerable<TrakHoundPackagesFileItem> packages, string path = null, bool keepOrigins = false)
        {
            if (packages.IsNullOrEmpty()) return false;

            var existing = Read(path);
            var items = new List<TrakHoundPackagesFileItem>();

            foreach (var package in packages)
            {
                if (package == null) continue;

                var previous = FindInstalled(existing?.Packages, package.Id, package.Installed);
                var item = new TrakHoundPackagesFileItem(package.Category, package.Id, package.Version ?? previous?.Version ?? package.Installed, package.Installed);
                item.Origin = package.Origin ?? (keepOrigins ? previous?.Origin : null);
                items.Add(item);
            }

            return Write(items, existing?.AutoUpdates, path);
        }

        /// <summary>
        /// The item for an installed Version of a Package. Items written before the installed Version was recorded list it as their Version
        /// </summary>
        public static TrakHoundPackagesFileItem FindInstalled(IEnumerable<TrakHoundPackagesFileItem> items, string packageId, string installedVersion)
        {
            if (items == null || string.IsNullOrEmpty(packageId) || string.IsNullOrEmpty(installedVersion)) return null;

            return items.FirstOrDefault(o => o != null && string.Equals(o.Id, packageId, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(o.Installed ?? o.Version, installedVersion, StringComparison.OrdinalIgnoreCase));
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
