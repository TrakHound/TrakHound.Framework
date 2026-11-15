// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// A library listed in a Package's SBOM
    /// </summary>
    public class TrakHoundPackageSbomComponent
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("purl")]
        public string Purl { get; set; }
    }

    /// <summary>
    /// Reads the libraries listed in a Package's CycloneDX SBOM (see <see cref="TrakHoundPackage.SbomFilename"/>)
    /// </summary>
    public static class TrakHoundPackageSbom
    {
        // The SBOM comes from the Package, and is only read into memory to be displayed
        private const long _maxSize = 16 * 1024 * 1024;


        /// <summary>
        /// Read the SBOM of an installed Package version. Returns null if it doesn't have one or it can't be read
        /// </summary>
        public static IEnumerable<TrakHoundPackageSbomComponent> Read(string packageDirectory)
        {
            try
            {
                var file = new FileInfo(Path.Combine(packageDirectory, TrakHoundPackage.SbomFilename));
                if (!file.Exists || file.Length > _maxSize) return null;

                using (var stream = file.OpenRead()) return Parse(stream);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Read the SBOM of a package (.thp) file. Returns null if it doesn't have one or it can't be read
        /// </summary>
        public static IEnumerable<TrakHoundPackageSbomComponent> ReadArchive(byte[] packageBytes)
        {
            try
            {
                using (var inputStream = new MemoryStream(packageBytes))
                using (var archive = new ZipArchive(inputStream, ZipArchiveMode.Read))
                {
                    var entry = archive.GetEntry(TrakHoundPackage.SbomFilename);
                    if (entry == null || entry.Length > _maxSize) return null;

                    using (var stream = entry.Open()) return Parse(stream);
                }
            }
            catch
            {
                return null;
            }
        }

        private static IEnumerable<TrakHoundPackageSbomComponent> Parse(Stream stream)
        {
            var sbom = JsonSerializer.Deserialize<SbomFile>(stream);

            return sbom?.Components?.Where(o => !string.IsNullOrEmpty(o?.Name)).OrderBy(o => o.Name, StringComparer.OrdinalIgnoreCase).ToList();
        }


        private class SbomFile
        {
            [JsonPropertyName("components")]
            public List<TrakHoundPackageSbomComponent> Components { get; set; }
        }
    }
}
