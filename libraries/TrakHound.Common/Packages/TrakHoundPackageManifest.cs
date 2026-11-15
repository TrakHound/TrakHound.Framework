// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// A file listed in a <see cref="TrakHoundPackageManifest"/>
    /// </summary>
    public class TrakHoundPackageManifestFile
    {
        /// <summary>
        /// Path of the file relative to the Package root, using '/' separators
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }

        /// <summary>
        /// SHA-256 of the file (lowercase hex)
        /// </summary>
        [JsonPropertyName("sha256")]
        public string Sha256 { get; set; }

        /// <summary>
        /// Size of the file in bytes
        /// </summary>
        [JsonPropertyName("size")]
        public long Size { get; set; }
    }

    /// <summary>
    /// Lists every file in a Package with its hash, along with the Package identity, Metadata, Dependencies and the source it was built from.
    /// The package.json is rewritten on install, so it isn't listed: what it says is compared with the Manifest instead.
    /// The Manifest and its signature aren't listed either
    /// </summary>
    public class TrakHoundPackageManifest
    {
        /// <summary>
        /// The Manifest format written by this version of TrakHound
        /// </summary>
        public const int CurrentFormatVersion = 1;

        // The Manifest comes from the Package, and is read into memory (ex. by a Package Server for each request)
        private const int _maxSize = 16 * 1024 * 1024;


        [JsonPropertyName("formatVersion")]
        public int FormatVersion { get; set; }

        [JsonPropertyName("category")]
        public string Category { get; set; }

        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        [JsonPropertyName("buildDate")]
        public DateTime BuildDate { get; set; }

        [JsonPropertyName("repository")]
        public string Repository { get; set; }

        [JsonPropertyName("repositoryDirectory")]
        public string RepositoryDirectory { get; set; }

        [JsonPropertyName("repositoryBranch")]
        public string RepositoryBranch { get; set; }

        [JsonPropertyName("repositoryCommit")]
        public string RepositoryCommit { get; set; }

        /// <summary>
        /// Whether the working tree had uncommitted changes when the Package was built. Null if it isn't known
        /// </summary>
        [JsonPropertyName("repositoryDirty")]
        public bool? RepositoryDirty { get; set; }

        [JsonPropertyName("builder")]
        public string Builder { get; set; }

        [JsonPropertyName("buildConfiguration")]
        public string BuildConfiguration { get; set; }

        [JsonPropertyName("dotnetSdkVersion")]
        public string DotnetSdkVersion { get; set; }

        [JsonPropertyName("metadata")]
        public Dictionary<string, object> Metadata { get; set; }

        [JsonPropertyName("dependencies")]
        public IEnumerable<TrakHoundPackageDependency> Dependencies { get; set; }

        [JsonPropertyName("files")]
        public List<TrakHoundPackageManifestFile> Files { get; set; }


        /// <summary>
        /// Create a Manifest for a Package from its files
        /// </summary>
        /// <param name="package">Package the files belong to</param>
        /// <param name="buildDate">Build date written to the Package</param>
        /// <param name="files">Each file's entry in the Package (either separator) and its path on disk</param>
        internal static TrakHoundPackageManifest Create(TrakHoundPackage package, DateTime buildDate, IEnumerable<(string Entry, string Path)> files)
        {
            var manifest = new TrakHoundPackageManifest();
            manifest.FormatVersion = CurrentFormatVersion;
            manifest.Category = package.Category;
            manifest.Id = package.Id;
            manifest.Version = package.Version;
            manifest.BuildDate = buildDate;
            manifest.Repository = package.GetMetadata(TrakHoundPackage.RepositoryName);
            manifest.RepositoryDirectory = package.GetMetadata(TrakHoundPackage.RepositoryDirectoryName);
            manifest.RepositoryBranch = package.GetMetadata(TrakHoundPackage.RepositoryBranchName);
            manifest.RepositoryCommit = package.GetMetadata(TrakHoundPackage.RepositoryCommitName);
            if (bool.TryParse(package.GetMetadata(TrakHoundPackage.RepositoryDirtyName), out var dirty)) manifest.RepositoryDirty = dirty;
            manifest.Builder = package.GetMetadata(TrakHoundPackage.BuilderName);
            manifest.BuildConfiguration = package.GetMetadata(TrakHoundPackage.BuildConfigurationName);
            manifest.DotnetSdkVersion = package.GetMetadata(TrakHoundPackage.DotnetSdkVersionName);

            var json = new TrakHoundPackageJson(package);
            manifest.Metadata = json.Metadata;
            manifest.Dependencies = json.Dependencies;

            manifest.Files = files
                .Select(o =>
                {
                    using (var stream = File.OpenRead(o.Path))
                    {
                        return new TrakHoundPackageManifestFile { Path = NormalizePath(o.Entry), Sha256 = GetHash(stream), Size = stream.Length };
                    }
                })
                .OrderBy(o => o.Path, StringComparer.Ordinal)
                .ToList();

            return manifest;
        }

        /// <summary>
        /// SHA-256 of the file list (paths and hashes). It's the same for two builds with identical files, whatever their build dates
        /// </summary>
        public string GetContentHash()
        {
            var builder = new StringBuilder();
            foreach (var file in Files.OrderBy(o => o.Path, StringComparer.Ordinal))
            {
                builder.Append(file.Path).Append('\0').Append(file.Sha256).Append('\n');
            }

            return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())));
        }

        /// <summary>
        /// Read the exact bytes of the Manifest and its signature from a package (.thp) file, since the signature only matches those bytes. Either is null if the package doesn't have it
        /// </summary>
        /// <param name="packageStream">Contents of the package (.thp) file. It's left open</param>
        /// <exception cref="InvalidDataException">The stream isn't a package file, or the Manifest or signature is too large</exception>
        public static (byte[] Manifest, byte[] Signature) ReadFromArchive(Stream packageStream)
        {
            using (var archive = new ZipArchive(packageStream, ZipArchiveMode.Read, true))
            {
                return (ReadEntry(archive, TrakHoundPackage.ManifestFilename), ReadEntry(archive, TrakHoundPackage.SignatureFilename));
            }
        }

        private static byte[] ReadEntry(ZipArchive archive, string name)
        {
            // The last entry is the one that extracting the package would leave, and what the verifier reads
            var entry = archive.Entries.LastOrDefault(o => o.FullName == name);
            if (entry == null) return null;

            using (var stream = entry.Open())
            using (var output = new MemoryStream())
            {
                // Limited by what's read, since the size in the archive is whatever the package says it is
                var buffer = new byte[81920];
                int count;
                while ((count = stream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    if (output.Length + count > _maxSize) throw new InvalidDataException($"{name} is larger than the limit of {_maxSize} bytes");
                    output.Write(buffer, 0, count);
                }

                return output.ToArray();
            }
        }

        internal byte[] ToBytes() => JsonSerializer.SerializeToUtf8Bytes(this, new JsonSerializerOptions { WriteIndented = true });

        internal static string NormalizePath(string path) => path?.Replace('\\', '/');

        internal static string GetHash(Stream stream) => Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
