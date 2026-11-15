// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using TrakHound.Extensions;

namespace TrakHound.Packages
{
    /// <summary>
    /// Information about a Package (Category, ID, Version, Metadata and Dependencies) along with methods to build, read, install and uninstall Packages
    /// </summary>
    public sealed class TrakHoundPackage
    {
        /// <summary>
        /// The file extension of a Package file
        /// </summary>
        public const string FileExtension = ".thp";
        /// <summary>
        /// The name of the Package information file at the root of a Package
        /// </summary>
        public const string InformationFilename = "package.json";
        /// <summary>
        /// The name of the file at the root of a Package that lists its files and their hashes (see <see cref="TrakHoundPackageManifest"/>)
        /// </summary>
        public const string ManifestFilename = "package.manifest.json";
        /// <summary>
        /// The name of the file at the root of a signed Package that holds the signature of its Manifest (see <see cref="TrakHoundPackageSignature"/>)
        /// </summary>
        public const string SignatureFilename = "package.sig";
        /// <summary>
        /// The name of the file at the root of a Package that lists the NuGet Packages it was built with (CycloneDX format)
        /// </summary>
        public const string SbomFilename = "sbom.cdx.json";
        /// <summary>
        /// The name of the default root directory that Packages are installed to
        /// </summary>
        public const string PackageDirectory = "packages";
        /// <summary>
        /// The name of the distributable directory within an installed Package version
        /// </summary>
        public const string DistributableDirectory = "dist";
        /// <summary>
        /// The name of the source directory within an installed Package version
        /// </summary>
        public const string SourceDirectory = "src";
        /// <summary>
        /// The name of the Package configuration file in a project directory, used to build a Package
        /// </summary>
        public const string PackageConfigurationFilename = "trakhound.package.json";
        /// <summary>
        /// The wildcard used in a Version to match any version (ex. "*" uses the latest version)
        /// </summary>
        public const string WilcardVersion = "*";

        /// <summary>
        /// Added to a wildcard Version to include Prereleases when selecting the newest Version (ex. "*-*" or "1.*-*").
        /// Without it, the newest Stable Version is selected and a Prerelease only if no Stable Version matches
        /// </summary>
        public const string PrereleaseWildcardSuffix = "-*";
        /// <summary>
        /// The name of the directory (within the root package directory) that packages are extracted to before being moved into place
        /// </summary>
        public const string StagingDirectory = ".staging";

        /// <summary>
        /// The filename (without extension) of a Package's Readme
        /// </summary>
        public const string ReadMeFilename = "Readme";
        /// <summary>
        /// The filename of a Package's License
        /// </summary>
        public const string LicenseFilename = "License";
        /// <summary>
        /// The file extensions checked (in order) when reading a Package's Readme
        /// </summary>
        public static readonly IEnumerable<string> ReadMeFileExtensions = new string[] { ".md", ".txt" };

        /// <summary>
        /// Metadata key for the Package description
        /// </summary>
        public const string DescriptionName = "description";
        /// <summary>
        /// Metadata key for the Package publisher
        /// </summary>
        public const string PublisherName = "publisher";
        /// <summary>
        /// Metadata key for the Package project
        /// </summary>
        public const string ProjectName = "project";
        /// <summary>
        /// Metadata key for the Package source repository
        /// </summary>
        public const string RepositoryName = "repository";
        /// <summary>
        /// Metadata key for the directory of the Package within its source repository
        /// </summary>
        public const string RepositoryDirectoryName = "repositoryDirectory";
        /// <summary>
        /// Metadata key for the source repository branch the Package was built from
        /// </summary>
        public const string RepositoryBranchName = "repositoryBranch";
        /// <summary>
        /// Metadata key for the source repository commit the Package was built from
        /// </summary>
        public const string RepositoryCommitName = "repositoryCommit";
        /// <summary>
        /// Metadata key for whether the working tree had uncommitted changes when the Package was built ("true" or "false")
        /// </summary>
        public const string RepositoryDirtyName = "repositoryDirty";
        /// <summary>
        /// Metadata key for what built the Package (ex. the URL of a CI run). Not set for a build on a developer's machine
        /// </summary>
        public const string BuilderName = "builder";
        /// <summary>
        /// Metadata key for the DotNet build configuration (ex. Release). Only set for Packages built from a DotNet project
        /// </summary>
        public const string BuildConfigurationName = "buildConfiguration";
        /// <summary>
        /// Metadata key for the version of the DotNet SDK the Package was built with
        /// </summary>
        public const string DotnetSdkVersionName = "dotnetSdkVersion";
        /// <summary>
        /// Metadata key for the TrakHound version the Package was built against
        /// </summary>
        public const string TrakHoundVersionName = "trakhoundVersion";
        /// <summary>
        /// Metadata key for the Package image (icon)
        /// </summary>
        public const string ImageName = ".image";


        /// <summary>
        /// Unique identifier of this package version, generated from the ID and Version (see <see cref="GenerateUuid(string, string)"/>)
        /// </summary>
        public string Uuid => GenerateUuid(this);

        /// <summary>
        /// ID of the package
        /// </summary>
        public string Id { get; }

        /// <summary>
        /// Version of the package
        /// </summary>
        public string Version { get; }

        /// <summary>
        /// Category of the package (ex. app, api, service). Used as the top level directory that the package is installed to
        /// </summary>
        public string Category { get; }

        /// <summary>
        /// Date the package was built
        /// </summary>
        public DateTime BuildDate { get; }

        /// <summary>
        /// Directory the package information was read from. Null if the package was read from a package (.thp) file or stream
        /// </summary>
        public string Location { get; internal set; }

        /// <summary>
        /// Hash of this specific build of the package, generated from the Category, ID, Version, and BuildDate (see <see cref="GenerateHash(string, string, string, DateTime)"/>)
        /// </summary>
        public string Hash => GenerateHash(this);

        /// <summary>
        /// SHA-256 of the package's files (see <see cref="TrakHoundPackageManifest.GetContentHash"/>). Null for a package without a Manifest. This is the value recorded when the package was packed: use <see cref="TrakHoundPackageVerifier"/> to check the files themselves
        /// </summary>
        public string ContentHash { get; }

        /// <summary>
        /// Additional package information (ex. description, publisher, repository) ordered by key. See the *Name constants for common keys
        /// </summary>
        public Dictionary<string, object> Metadata { get; }

        /// <summary>
        /// Other packages that this package depends on
        /// </summary>
        public IEnumerable<ITrakHoundPackageDependency> Dependencies { get; }


        internal TrakHoundPackage(TrakHoundPackageBuilder builder)
        {
            if (builder != null)
            {
                Category = builder.Category;
                Id = builder.Id;
                Version = builder.Version;
                Location = builder.Location;
                BuildDate = builder.BuildDate;
                ContentHash = builder.ContentHash;
                Metadata = builder.Metadata?.OrderBy(o => o.Key).ToDictionary();
                Dependencies = builder.Dependencies?.ToList();
            }
        }

        /// <summary>
        /// Returns the Category, ID, and Version of the package
        /// </summary>
        public override string ToString()
        {
            return $"{Category} : {Id} : v{Version}";
        }

        /// <summary>
        /// Generate the UUID of a package from its ID and Version. Returns null if the package is null
        /// </summary>
        /// <param name="package">Package to generate the UUID for</param>
        public static string GenerateUuid(TrakHoundPackage package)
        {
            if (package != null)
            {
                return GenerateUuid(package.Id, package.Version);
            }

            return null;
        }

        /// <summary>
        /// Generate a package UUID as an MD5 hash of the (case-insensitive) ID and Version. Returns null if either is empty
        /// </summary>
        /// <param name="packageId">ID of the package</param>
        /// <param name="packageVersion">Version of the package</param>
        public static string GenerateUuid(string packageId, string packageVersion)
        {
            if (!string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(packageVersion))
            {
                return $"{packageId.ToLower()}:{packageVersion.ToLower()}".ToMD5Hash();
            }

            return null;
        }

        /// <summary>
        /// Generate the Hash of a package from its Category, ID, Version, and BuildDate. Returns null if the package is null
        /// </summary>
        /// <param name="package">Package to generate the Hash for</param>
        public static string GenerateHash(TrakHoundPackage package)
        {
            if (package != null)
            {
                return GenerateHash(package.Category, package.Id, package.Version, package.BuildDate);
            }

            return null;
        }

        /// <summary>
        /// Generate a package Hash as an MD5 hash of the (case-insensitive) Category, ID, Version, and BuildDate
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        /// <param name="packageVersion">Version of the package</param>
        /// <param name="buildDate">Date the package was built</param>
        public static string GenerateHash(string category, string packageId, string packageVersion, DateTime buildDate)
        {
            return $"{category?.ToLower()}:{packageId?.ToLower()}:{packageVersion?.ToLower()}:{buildDate.ToUnixTime()}".ToMD5Hash();
        }

        /// <summary>
        /// Returns true if two packages are the same build. The Hash doesn't cover the files, so a different ContentHash means a different build even when the Hash matches. A package without a ContentHash (ex. from a Package Server that doesn't report it) is compared by its Hash alone
        /// </summary>
        public static bool IsSameBuild(TrakHoundPackage a, TrakHoundPackage b)
        {
            if (a == null || b == null || a.Hash != b.Hash) return false;

            return string.IsNullOrEmpty(a.ContentHash) || string.IsNullOrEmpty(b.ContentHash) || string.Equals(a.ContentHash, b.ContentHash, StringComparison.OrdinalIgnoreCase);
        }


        /// <summary>
        /// Returns true if the package Metadata contains the specified key
        /// </summary>
        /// <param name="name">Metadata key to look for</param>
        public bool MetadataExists(string name)
        {
            if (!string.IsNullOrEmpty(name) && !Metadata.IsNullOrEmpty())
            {
                return Metadata.ContainsKey(name);
            }

            return false;
        }

        /// <summary>
        /// Get a Metadata value as a string. Returns null if the key is not found
        /// </summary>
        /// <param name="name">Metadata key to read</param>
        public string GetMetadata(string name)
        {
            if (!string.IsNullOrEmpty(name) && !Metadata.IsNullOrEmpty())
            {
                var value = Metadata.GetValueOrDefault(name);
                if (value != null)
                {
                    return value.ToString();
                }
            }

            return null;
        }

        /// <summary>
        /// Get a Metadata value converted to <typeparamref name="T"/>. Returns the default value if the key is not found or the value cannot be converted
        /// </summary>
        /// <param name="name">Metadata key to read</param>
        public T GetMetadata<T>(string name)
        {
            if (!string.IsNullOrEmpty(name) && !Metadata.IsNullOrEmpty())
            {
                var value = Metadata.GetValueOrDefault(name);
                if (value != null)
                {
                    try
                    {
                        return (T)Convert.ChangeType(value, typeof(T));
                    }
                    catch { }
                }
            }

            return default;
        }


        /// <summary>
        /// Create a package (.thp) archive of this package from the files in a source directory. A package.json is added with the BuildDate set to the current UTC time, a Manifest of the files is added, and existing .thp files are excluded. Returns null on failure
        /// </summary>
        /// <param name="sourcePath">Directory containing the files to add to the package</param>
        /// <param name="signer">Signs the Manifest. If null, the package is unsigned</param>
        public byte[] Package(string sourcePath, TrakHoundPackageSigner signer = null)
        {
            if (!string.IsNullOrEmpty(sourcePath))
            {
                try
                {
                    // Get the Source files (path Relative to Archive root). The generated Information, Manifest and Signature Files are used instead of any existing ones
                    var files = Directory.GetFiles(sourcePath, "*.*", SearchOption.AllDirectories)
                        .Where(o => !o.EndsWith(FileExtension))
                        .Select(o => (Path: o, Entry: Path.GetRelativePath(sourcePath, o)))
                        .Where(o => o.Entry != InformationFilename && o.Entry != ManifestFilename && o.Entry != SignatureFilename)
                        .ToList();

                    var buildDate = DateTime.UtcNow;
                    var manifest = TrakHoundPackageManifest.Create(this, buildDate, files.Select(o => (o.Entry, o.Path)));

                    using (var outputStream = new MemoryStream())
                    {
                        using (var archive = new ZipArchive(outputStream, ZipArchiveMode.Create))
                        {
                            // Create Information File Entry
                            var information = archive.CreateEntry(InformationFilename);
                            using (var informationStream = information.Open())
                            {
                                var jsonPackage = new TrakHoundPackageJson(this);
                                jsonPackage.BuildDate = buildDate;
                                jsonPackage.ContentHash = manifest.GetContentHash();

                                // Regenerate the Hash since it includes the BuildDate
                                jsonPackage.Hash = GenerateHash(Category, Id, Version, jsonPackage.BuildDate);

                                var jsonOptions = new JsonSerializerOptions
                                {
                                    WriteIndented = true
                                };

                                // Write as UTF-8 (no BOM) so that non-ASCII Metadata is preserved
                                var bytes = JsonSerializer.SerializeToUtf8Bytes(jsonPackage, jsonOptions);
                                informationStream.Write(bytes, 0, bytes.Length);
                            }

                            // Create Manifest File Entry
                            var manifestBytes = manifest.ToBytes();
                            var manifestEntry = archive.CreateEntry(ManifestFilename);
                            using (var manifestStream = manifestEntry.Open())
                            {
                                manifestStream.Write(manifestBytes, 0, manifestBytes.Length);
                            }

                            // Create Signature File Entry
                            if (signer != null)
                            {
                                var signatureBytes = signer.Sign(manifestBytes).ToBytes();
                                var signatureEntry = archive.CreateEntry(SignatureFilename);
                                using (var signatureStream = signatureEntry.Open())
                                {
                                    signatureStream.Write(signatureBytes, 0, signatureBytes.Length);
                                }
                            }

                            // Add Source files from Directory
                            foreach (var file in files)
                            {
                                archive.CreateEntryFromFile(file.Path, file.Entry);
                            }
                        }

                        return outputStream.ToArray();
                    }
                }
                catch { }
            }

            return null;
        }


        #region "Validation"

        /// <summary>
        /// Returns true if the package Category, ID and Version can each be used as a single directory name (see <see cref="IsValidPathSegment(string)"/>)
        /// </summary>
        /// <param name="package">Package to validate</param>
        public static bool IsValid(TrakHoundPackage package)
        {
            return package != null && IsValidPathSegment(package.Category) && IsValidPathSegment(package.Id) && IsValidPathSegment(package.Version);
        }

        /// <summary>
        /// Returns true if the value can be used as a single directory name. Empty values, values starting with '.' (including "." and ".."),
        /// and values containing directory separators or invalid filename characters are not valid
        /// </summary>
        /// <param name="value">Value to validate (ex. a package Category, ID or Version)</param>
        public static bool IsValidPathSegment(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            if (value.StartsWith('.')) return false;
            if (value.IndexOf('/') >= 0 || value.IndexOf('\\') >= 0 || value.IndexOf(':') >= 0) return false;
            if (value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return false;

            return true;
        }

        /// <summary>
        /// Get the full path that an archive entry extracts to within a root directory. Returns null if the entry would extract outside of the root directory
        /// </summary>
        /// <param name="rootPath">Directory the archive is extracted to</param>
        /// <param name="entryName">Full name of the archive entry (either '/' or '\' separators)</param>
        internal static string GetEntryPath(string rootPath, string entryName)
        {
            if (string.IsNullOrEmpty(rootPath) || string.IsNullOrEmpty(entryName)) return null;

            var relativePath = entryName.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(relativePath) || relativePath.IndexOf(':') >= 0) return null;

            var root = Path.GetFullPath(rootPath);
            if (!root.EndsWith(Path.DirectorySeparatorChar)) root += Path.DirectorySeparatorChar;

            var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
            var comparison = Path.DirectorySeparatorChar == '\\' ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

            return fullPath.StartsWith(root, comparison) && fullPath.Length > root.Length ? fullPath : null;
        }

        #endregion

        #region "Directories"

        /// <summary>
        /// Get the root package directory. Returns <paramref name="path"/> if specified, otherwise the 'packages' directory in the application base directory
        /// </summary>
        /// <param name="path">Root directory that packages are installed to</param>
        public static string GetDirectory(string path = null)
        {
            if (!string.IsNullOrEmpty(path))
            {
                return path;
                //return Path.Combine(path, PackageDirectory);
            }
            else
            {
                return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PackageDirectory);
            }
        }


        /// <summary>
        /// Get the directory for a package Category in the default package directory
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        public static string GetCategoryDirectory(string category)
        {
            return Path.Combine(GetDirectory(), category);
        }

        /// <summary>
        /// Get the directory for a package Category
        /// </summary>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        public static string GetCategoryDirectory(string installPath, string category)
        {
            return Path.Combine(GetDirectory(installPath), category);
        }


        /// <summary>
        /// Get the directory containing all installed versions of a package in the default package directory. Returns null if the Category or ID is empty
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        public static string GetPackageDirectory(string category, string packageId)
        {
            if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(packageId))
            {
                return Path.Combine(GetCategoryDirectory(category), packageId);
            }

            return null;
        }

        /// <summary>
        /// Get the directory containing all installed versions of a package. Returns null if the Category or ID is empty
        /// </summary>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        public static string GetPackageDirectory(string installPath, string category, string packageId)
        {
            if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(packageId))
            {
                return Path.Combine(GetCategoryDirectory(installPath, category), packageId);
            }

            return null;
        }

        /// <summary>
        /// Select the Version that a requested Version resolves to. "*" (or nothing) and patterns (ex. "1.2.*") select the newest Stable Version that matches,
        /// or the newest Prerelease if no Stable Version matches. With a "-*" suffix (ex. "*-*" or "1.2.*-*") they select the newest of both. Returns null if nothing matches
        /// </summary>
        /// <param name="versions">The Versions to select from</param>
        /// <param name="requestedVersion">The requested Version. May contain '*' wildcards</param>
        public static string SelectVersion(IEnumerable<string> versions, string requestedVersion)
        {
            if (versions == null) return null;

            var requested = !string.IsNullOrEmpty(requestedVersion) ? requestedVersion : WilcardVersion;
            var comparer = requested.EndsWith(PrereleaseWildcardSuffix) ? VersionComparer.Default : VersionComparer.PreferStable;

            return versions.Where(o => StringFunctions.MatchVersion(o, requested)).OrderByDescending(o => o, comparer).FirstOrDefault();
        }

        /// <summary>
        /// Get the directory for a specific version of a package in the default package directory. The directory is not checked for existence unless the version contains wildcards. Returns null if any argument is empty or no wildcard match is found
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        /// <param name="version">Version of the package. May contain '*' wildcards to match the newest installed version (see <see cref="SelectVersion"/>)</param>
        public static string GetPackageVersionDirectory(string category, string packageId, string version)
        {
            if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(version))
            {
                if (version.Contains('*'))
                {
                    try
                    {
                        var packageDirectory = Path.Combine(GetCategoryDirectory(category), packageId);
                        var selectedVersion = SelectVersion(Directory.GetDirectories(packageDirectory).Select(o => Path.GetFileName(o)), version);
                        if (selectedVersion != null) return Path.Combine(packageDirectory, selectedVersion);
                    }
                    catch { }
                }
                else
                {
                    return Path.Combine(GetCategoryDirectory(category), packageId, version);
                }
            }

            return null;
        }

        /// <summary>
        /// Get the directory for a specific version of a package. The directory is not checked for existence unless the version contains wildcards. Returns null if any argument is empty or no wildcard match is found
        /// </summary>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        /// <param name="version">Version of the package. May contain '*' wildcards to match the newest installed version (see <see cref="SelectVersion"/>)</param>
        public static string GetPackageVersionDirectory(string installPath, string category, string packageId, string version)
        {
            if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(version))
            {
                if (version.Contains('*'))
                {
                    try
                    {
                        var packageDirectory = Path.Combine(GetCategoryDirectory(installPath, category), packageId);
                        var selectedVersion = SelectVersion(Directory.GetDirectories(packageDirectory).Select(o => Path.GetFileName(o)), version);
                        if (selectedVersion != null) return Path.Combine(packageDirectory, selectedVersion);
                    }
                    catch { }
                }
                else
                {
                    return Path.Combine(GetCategoryDirectory(installPath, category), packageId, version);
                }
            }

            return null;
        }


        /// <summary>
        /// Get the directory of the newest installed version of a package (a Stable version if there is one) in the default package directory. Only directories named with a valid version (ex. 1.2.3 or 1.2.3-beta) are considered. Returns null if none are found
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        public static string GetLatestPackageDirectory(string category, string packageId)
        {
            var dir = GetCategoryDirectory(category);
            dir = Path.Combine(dir, packageId);
            if (Directory.Exists(dir))
            {
                return Directory.GetDirectories(dir)
                    .Where(o => Path.GetFileName(o).ToBaseVersion() != null)
                    .OrderByDescending(o => Path.GetFileName(o), VersionComparer.PreferStable)
                    .FirstOrDefault();
            }

            return null;
        }

        /// <summary>
        /// Get the directory of the newest installed version of a package (a Stable version if there is one). Only directories named with a valid version (ex. 1.2.3 or 1.2.3-beta) are considered. Returns null if none are found
        /// </summary>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        public static string GetLatestPackageDirectory(string installPath, string category, string packageId)
        {
            var dir = GetCategoryDirectory(installPath, category);
            dir = Path.Combine(dir, packageId);
            if (Directory.Exists(dir))
            {
                return Directory.GetDirectories(dir)
                    .Where(o => Path.GetFileName(o).ToBaseVersion() != null)
                    .OrderByDescending(o => Path.GetFileName(o), VersionComparer.PreferStable)
                    .FirstOrDefault();
            }

            return null;
        }



        /// <summary>
        /// Get the distributable ('dist') directory for a specific version of a package in the default package directory. Returns null if the version directory cannot be resolved
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        /// <param name="version">Version of the package. May contain '*' wildcards to match the newest installed version (see <see cref="SelectVersion"/>)</param>
        public static string GetPackageVersionDistributableDirectory(string category, string packageId, string version)
            => GetSubDirectory(GetPackageVersionDirectory(category, packageId, version), DistributableDirectory);

        /// <summary>
        /// Get the distributable ('dist') directory for a specific version of a package. Returns null if the version directory cannot be resolved
        /// </summary>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        /// <param name="version">Version of the package. May contain '*' wildcards to match the newest installed version (see <see cref="SelectVersion"/>)</param>
        public static string GetPackageVersionDistributableDirectory(string installPath, string category, string packageId, string version) 
            => GetSubDirectory(GetPackageVersionDirectory(installPath, category, packageId, version), DistributableDirectory);

        /// <summary>
        /// Get the distributable ('dist') directory of the highest installed version of a package in the default package directory. Returns null if no version is installed
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        public static string GetLatestPackageDistributableDirectory(string category, string packageId) 
            => GetSubDirectory(GetLatestPackageDirectory(category, packageId), DistributableDirectory);

        /// <summary>
        /// Get the distributable ('dist') directory of the highest installed version of a package. Returns null if no version is installed
        /// </summary>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        public static string GetLatestPackageDistributableDirectory(string installPath, string category, string packageId)
            => GetSubDirectory(GetLatestPackageDirectory(installPath, category, packageId), DistributableDirectory);



        /// <summary>
        /// Get the source ('src') directory for a specific version of a package in the default package directory. Returns null if the version directory cannot be resolved
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        /// <param name="version">Version of the package. May contain '*' wildcards to match the newest installed version (see <see cref="SelectVersion"/>)</param>
        public static string GetPackageVersionSourceDirectory(string category, string packageId, string version)
            => GetSubDirectory(GetPackageVersionDirectory(category, packageId, version), SourceDirectory);

        /// <summary>
        /// Get the source ('src') directory for a specific version of a package. Returns null if the version directory cannot be resolved
        /// </summary>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        /// <param name="version">Version of the package. May contain '*' wildcards to match the newest installed version (see <see cref="SelectVersion"/>)</param>
        public static string GetPackageVersionSourceDirectory(string installPath, string category, string packageId, string version)
            => GetSubDirectory(GetPackageVersionDirectory(installPath, category, packageId, version), SourceDirectory);

        /// <summary>
        /// Get the source ('src') directory of the highest installed version of a package in the default package directory. Returns null if no version is installed
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        public static string GetLatestPackageSourceDirectory(string category, string packageId)
            => GetSubDirectory(GetLatestPackageDirectory(category, packageId), SourceDirectory);

        /// <summary>
        /// Get the source ('src') directory of the highest installed version of a package. Returns null if no version is installed
        /// </summary>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        public static string GetLatestPackageSourceDirectory(string installPath, string category, string packageId)
            => GetSubDirectory(GetLatestPackageDirectory(installPath, category, packageId), SourceDirectory);


        private static string GetSubDirectory(string path, string subDirectoryName)
        {
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(subDirectoryName))
            {
                return Path.Combine(path, subDirectoryName);
            }

            return null;
        }

        #endregion

        #region "Information"

        /// <summary>
        /// Read the package.json information of an installed package. Returns null if the package is not found or cannot be read
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        /// <param name="packageVersion">Version of the package. If null, the latest installed version is used</param>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        public static TrakHoundPackage ReadInformation(string category, string packageId, string packageVersion = null, string installPath = null)
        {
            if (!string.IsNullOrEmpty(packageId))
            {
                try
                {
                    string packageDirectory;

                    if (!string.IsNullOrEmpty(packageVersion))
                    {
                        if (!string.IsNullOrEmpty(installPath)) packageDirectory = GetPackageVersionDirectory(installPath, category, packageId, packageVersion);
                        else packageDirectory = GetPackageVersionDirectory(category, packageId, packageVersion);
                    }
                    else
                    {
                        if (!string.IsNullOrEmpty(installPath)) packageDirectory = GetLatestPackageDirectory(installPath, category, packageId);
                        else packageDirectory = GetLatestPackageDirectory(category, packageId);
                    }

                    if (Directory.Exists(packageDirectory))
                    {
                        var informationPath = Path.Combine(packageDirectory, InformationFilename);
                        if (File.Exists(informationPath))
                        {
                            // Read JSON Information File
                            var json = File.ReadAllText(informationPath);

                            // Deserialize to TrakHoundPackage
                            var jsonPackage = JsonSerializer.Deserialize<TrakHoundPackageJson>(json);
                            if (jsonPackage != null)
                            {
                                var package = jsonPackage.ToPackage();
                                if (package != null)
                                {
                                    package.Location = packageDirectory;

                                    return package;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Read the package.json information from an installed package version directory. Returns null if not found or it cannot be read
        /// </summary>
        /// <param name="packageDirectory">Package version directory containing the package.json file</param>
        public static TrakHoundPackage ReadInformation(string packageDirectory)
        {
            if (!string.IsNullOrEmpty(packageDirectory))
            {
                try
                {
                    if (Directory.Exists(packageDirectory))
                    {
                        // Get Package ID
                        var packageId = Path.GetFileName(Path.GetDirectoryName(packageDirectory));

                        var informationPath = Path.Combine(packageDirectory, InformationFilename);
                        if (File.Exists(informationPath))
                        {
                            // Read JSON Information File
                            var json = File.ReadAllText(informationPath);

                            // Deserialize to TrakHoundPackage
                            var jsonPackage = JsonSerializer.Deserialize<TrakHoundPackageJson>(json);
                            if (jsonPackage != null)
                            {
                                var package = jsonPackage.ToPackage();
                                if (package != null)
                                {
                                    package.Location = packageDirectory;

                                    return package;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Read package information from a package.json file. Location is set to the file's directory. Returns null if not found or it cannot be read
        /// </summary>
        /// <param name="packageInformationPath">Path to the package.json file</param>
        public static TrakHoundPackage ReadInformationFromFile(string packageInformationPath)
        {
            if (!string.IsNullOrEmpty(packageInformationPath))
            {
                try
                {
                    if (File.Exists(packageInformationPath))
                    {
                        // Read JSON Information File
                        var json = File.ReadAllText(packageInformationPath);

                        // Deserialize to TrakHoundPackage
                        var jsonPackage = JsonSerializer.Deserialize<TrakHoundPackageJson>(json);
                        if (jsonPackage != null)
                        {
                            var package = jsonPackage.ToPackage();
                            if (package != null)
                            {
                                package.Location = Path.GetDirectoryName(packageInformationPath);

                                return package;
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Read the package information from a package (.thp) file without extracting it. Returns null if not found or it cannot be read
        /// </summary>
        /// <param name="packagePath">Path to the package (.thp) file</param>
        public static TrakHoundPackage ReadInformationFromPackage(string packagePath)
        {
            if (!string.IsNullOrEmpty(packagePath))
            {
                try
                {
                    if (File.Exists(packagePath))
                    {
                        using (var fileStream = new FileStream(packagePath, FileMode.Open))
                        {
                            return ReadInformationFromStream(fileStream);
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Read the package information from the bytes of a package (.thp) file. Returns null if it cannot be read
        /// </summary>
        /// <param name="packageBytes">Contents of the package (.thp) file</param>
        public static TrakHoundPackage ReadInformationFromPackage(byte[] packageBytes)
        {
            if (packageBytes != null)
            {
                try
                {
                    using (var inputStream = new MemoryStream(packageBytes))
                    {
                        return ReadInformationFromStream(inputStream);
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Read the package information from the root package.json entry of a package (.thp) archive. The stream is rewound if seekable and is left open. Returns null if it cannot be read
        /// </summary>
        /// <param name="stream">Stream containing the package (.thp) archive</param>
        public static TrakHoundPackage ReadInformationFromStream(Stream stream)
        {
            if (stream != null)
            {
                try
                {
                    if (stream.CanSeek) stream.Seek(0, SeekOrigin.Begin);

                    using (var archive = new ZipArchive(stream, ZipArchiveMode.Read, true))
                    {
                        // Only match the Information File at the Archive root (ignore nested package.json files)
                        // Note: Use the first match since Package() writes the generated Information File first
                        var entry = archive.Entries.FirstOrDefault(o => o.FullName == InformationFilename);
                        if (entry != null)
                        {
                            using (var entryStream = entry.Open())
                            using (var outputStream = new MemoryStream())
                            {
                                entryStream.CopyTo(outputStream);
                                var bytes = outputStream.ToArray();

                                // Deserialize to TrakHoundPackage (UTF-8, which also reads older ASCII Information Files)
                                var jsonPackage = JsonSerializer.Deserialize<TrakHoundPackageJson>(bytes);
                                if (jsonPackage != null)
                                {
                                    // The Category, ID and Version are used as directory names when installing
                                    var package = jsonPackage.ToPackage();
                                    if (IsValid(package)) return package;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        #endregion

        #region "Get"

        /// <summary>
        /// Get the information for every installed version of every package
        /// </summary>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        public static IEnumerable<TrakHoundPackage> GetInstalled(string installPath = null)
        {
            var installedPackages = new List<TrakHoundPackage>();

            var packagePath = GetDirectory(installPath);
            if (!string.IsNullOrEmpty(packagePath))
            {
                try
                {
                    if (Directory.Exists(packagePath))
                    {
                        // Read Categories
                        var categoryDirectories = Directory.GetDirectories(packagePath);
                        if (categoryDirectories != null && categoryDirectories.Length > 0)
                        {
                            foreach (var categoryDirectory in categoryDirectories)
                            {
                                // Skip the Staging directory (and any other directory that can't be a Category)
                                if (!IsValidPathSegment(Path.GetFileName(categoryDirectory))) continue;

                                // Read Packages (Package ID)
                                var packageDirectories = Directory.GetDirectories(categoryDirectory);
                                if (packageDirectories != null && packageDirectories.Length > 0)
                                {
                                    foreach (var packageDirectory in packageDirectories)
                                    {
                                        var packageId = Path.GetFileName(packageDirectory);

                                        // Read Versions
                                        var versionDirectories = Directory.GetDirectories(packageDirectory);
                                        if (versionDirectories != null && versionDirectories.Length > 0)
                                        {
                                            foreach (var versionDirectory in versionDirectories)
                                            {
                                                // Read Package Information
                                                var packageInformation = ReadInformation(versionDirectory);
                                                if (packageInformation != null)
                                                {
                                                    installedPackages.Add(packageInformation);
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        } 
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            }

            return installedPackages;
        }

        /// <summary>
        /// Get the IDs of all packages installed in the default package directory. An ID is listed once per Category it is installed in
        /// </summary>
        public static IEnumerable<string> GetInstalledIds()
        {
            var installedPackageIds = new List<string>();

            var packagePath = GetDirectory();
            if (!string.IsNullOrEmpty(packagePath))
            {
                try
                {
                    if (Directory.Exists(packagePath))
                    {
                        // Read Categories
                        var categoryDirectories = Directory.GetDirectories(packagePath);
                        if (categoryDirectories != null && categoryDirectories.Length > 0)
                        {
                            foreach (var categoryDirectory in categoryDirectories)
                            {
                                // Skip the Staging directory (and any other directory that can't be a Category)
                                if (!IsValidPathSegment(Path.GetFileName(categoryDirectory))) continue;

                                // Read Packages (Package ID)
                                var packageDirectories = Directory.GetDirectories(categoryDirectory);
                                if (packageDirectories != null && packageDirectories.Length > 0)
                                {
                                    foreach (var packageDirectory in packageDirectories)
                                    {
                                        // Read Package ID
                                        var packageId = Path.GetFileName(packageDirectory);

                                        installedPackageIds.Add(packageId);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return installedPackageIds;
        }

        /// <summary>
        /// Get the highest installed version of a package in the default package directory. Returns null if the package is not installed
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        public static string GetLatestVersion(string category, string packageId)
        {
            if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(packageId))
            {
                var dir = GetLatestPackageDirectory(category, packageId);
                if (dir != null)
                {
                    var info = ReadInformation(dir);
                    if (info != null)
                    {
                        return info.Version;
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Get the information for every installed version of a package. Returns an empty list if the package is not installed
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        public static IEnumerable<TrakHoundPackage> GetInstalledVersions(string category, string packageId, string installPath = null)
        {
            var packages = new List<TrakHoundPackage>();

            if (!string.IsNullOrEmpty(category) && !string.IsNullOrEmpty(packageId))
            {
                try
                {
                    var dir = GetPackageDirectory(installPath, category, packageId);
                    if (Directory.Exists(dir))
                    {
                        var versionDirs = Directory.GetDirectories(dir);
                        foreach (var versionDir in versionDirs)
                        {
                            var package = ReadInformation(versionDir);
                            if (package != null) packages.Add(package);
                        }
                    }
                }
                catch { }
            }

            return packages;
        }

        /// <summary>
        /// Get the contents of the Readme (.md or .txt) in the package's distributable directory. Returns null if not found
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        /// <param name="packageVersion">Version of the package. If null, the latest installed version is used</param>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        public static string GetReadMe(string category, string packageId, string packageVersion = null, string installPath = null)
        {
            if (!string.IsNullOrEmpty(packageId))
            {
                try
                {
                    var packageDirectory = GetDistributableDirectory(category, packageId, packageVersion, installPath);
                    if (Directory.Exists(packageDirectory))
                    {
                        foreach (var fileExtension in ReadMeFileExtensions)
                        {
                            // Set Filename
                            var filename = ReadMeFilename + fileExtension;

                            var filePath = Path.Combine(packageDirectory, filename);
                            if (File.Exists(filePath))
                            {
                                // Read JSON Information File
                                var content = File.ReadAllText(filePath);
                                if (!string.IsNullOrEmpty(content)) return content;
                            }
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Get the contents of the License file in the package's distributable directory. Returns null if not found
        /// </summary>
        /// <param name="category">Category of the package (ex. app, api, service)</param>
        /// <param name="packageId">ID of the package</param>
        /// <param name="packageVersion">Version of the package. If null, the latest installed version is used</param>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        public static string GetLicense(string category, string packageId, string packageVersion = null, string installPath = null)
        {
            if (!string.IsNullOrEmpty(packageId))
            {
                try
                {
                    var packageDirectory = GetDistributableDirectory(category, packageId, packageVersion, installPath);
                    if (Directory.Exists(packageDirectory))
                    {
                        var filePath = Path.Combine(packageDirectory, LicenseFilename);
                        if (File.Exists(filePath))
                        {
                            // Read License Text File
                            var content = File.ReadAllText(filePath);
                            if (!string.IsNullOrEmpty(content)) return content;
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private static string GetDistributableDirectory(string category, string packageId, string packageVersion, string installPath)
        {
            // Fall back to the Latest installed Version if no Version is specified
            if (!string.IsNullOrEmpty(packageVersion))
            {
                if (!string.IsNullOrEmpty(installPath)) return GetPackageVersionDistributableDirectory(installPath, category, packageId, packageVersion);
                else return GetPackageVersionDistributableDirectory(category, packageId, packageVersion);
            }
            else
            {
                if (!string.IsNullOrEmpty(installPath)) return GetLatestPackageDistributableDirectory(installPath, category, packageId);
                else return GetLatestPackageDistributableDirectory(category, packageId);
            }
        }

        #endregion

        #region "Install / Uninstall"

        /// <summary>
        /// Install a package (.thp) to [installPath]/[Category]/[ID]/[Version]. The package is extracted to the Staging directory first and then moved into place,
        /// replacing an existing install of the same Version. If anything fails, the existing install is left unchanged. The package.json is written last.
        /// Packages with an invalid Category, ID or Version, or with entries that would extract outside of the version directory, are rejected. Returns true if successful
        /// </summary>
        /// <param name="packageBytes">Contents of the package (.thp) file</param>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        public static bool Install(byte[] packageBytes, string installPath = null)
        {
            if (packageBytes != null && packageBytes.Length > 0)
            {
                // Note: Returns null if the Category, ID or Version is not a valid directory name
                var information = ReadInformationFromPackage(packageBytes);
                if (information != null)
                {
                    var packageDirectory = Path.GetFullPath(GetDirectory(installPath));
                    var packagePath = Path.Combine(packageDirectory, information.Category, information.Id, information.Version);

                    // Use a Staging directory within the root package directory so that it is on the same volume (Directory.Move can't move across volumes)
                    var stagingPath = Path.Combine(packageDirectory, StagingDirectory, Guid.NewGuid().ToString("N"));
                    var extractPath = Path.Combine(stagingPath, "package");
                    var backupPath = Path.Combine(stagingPath, "backup");

                    try
                    {
                        Directory.CreateDirectory(extractPath);

                        // Extract to the Staging directory
                        using (var inputStream = new MemoryStream(packageBytes))
                        {
                            using (var archive = new ZipArchive(inputStream, ZipArchiveMode.Read))
                            {
                                foreach (var entry in archive.Entries)
                                {
                                    if (entry.FullName != InformationFilename)
                                    {
                                        var entryPath = GetEntryPath(extractPath, entry.FullName);
                                        if (entryPath == null) throw new InvalidDataException($"Package ({information}) contains an invalid entry : {entry.FullName}");

                                        if (string.IsNullOrEmpty(entry.Name))
                                        {
                                            // Directory Entry
                                            Directory.CreateDirectory(entryPath);
                                        }
                                        else
                                        {
                                            Directory.CreateDirectory(Path.GetDirectoryName(entryPath));
                                            entry.ExtractToFile(entryPath, true);
                                        }
                                    }
                                }
                            }
                        }

                        Directory.CreateDirectory(Path.GetDirectoryName(packagePath));

                        // Move an existing install of this Version aside so that it can be restored on failure
                        var replaced = false;
                        if (Directory.Exists(packagePath))
                        {
                            Directory.Move(packagePath, backupPath);
                            replaced = true;
                        }

                        try
                        {
                            Directory.Move(extractPath, packagePath);

                            // Write Information file
                            // Note: Do this last so that all files will be in place before notifying the FileSystemWatcher
                            var jsonInformation = new TrakHoundPackageJson(information);
                            Json.Write(jsonInformation, Path.Combine(packagePath, InformationFilename));
                        }
                        catch
                        {
                            // Restore the previous install
                            if (replaced)
                            {
                                if (Directory.Exists(packagePath)) Directory.Delete(packagePath, true);
                                Directory.Move(backupPath, packagePath);
                            }

                            throw;
                        }

                        return true;
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine(ex.Message);
                    }
                    finally
                    {
                        RemoveStagingDirectory(packageDirectory, stagingPath);
                    }
                }
            }

            return false;
        }

        private static void RemoveStagingDirectory(string packageDirectory, string stagingPath)
        {
            try
            {
                if (Directory.Exists(stagingPath)) Directory.Delete(stagingPath, true);

                // Remove the Staging root if no other installs are in progress
                var stagingRoot = Path.Combine(packageDirectory, StagingDirectory);
                if (Directory.Exists(stagingRoot) && !Directory.EnumerateFileSystemEntries(stagingRoot).Any())
                {
                    Directory.Delete(stagingRoot);
                }
            }
            catch { }
        }

        /// <summary>
        /// Remove an installed package listed in the packages file. Returns true if the package was removed
        /// </summary>
        /// <param name="packageId">ID of the package</param>
        /// <param name="packageVersion">Version to remove (may contain wildcards). If null, all versions of the package are removed</param>
        /// <param name="installPath">Root directory that packages are installed to. If null, the default package directory is used</param>
        public static bool Uninstall(string packageId, string packageVersion = null, string installPath = null)
        {
            if (!string.IsNullOrEmpty(packageId))
            {
                try
                {
                    var packagesInformation = TrakHoundPackagesFile.Read(installPath);
                    if (packagesInformation != null)
                    {
                        TrakHoundPackagesFileItem packageInformation;

                        if (!string.IsNullOrEmpty(packageVersion)) packageInformation = packagesInformation.Packages.FirstOrDefault(o => o.Id.ToLower() == packageId.ToLower() && StringFunctions.MatchVersion(o.Version, packageVersion));
                        else packageInformation = packagesInformation.Packages.FirstOrDefault(o => o.Id.ToLower() == packageId.ToLower());

                        if (packageInformation != null)
                        {
                            var packageDirectory = GetPackageDirectory(installPath, packageInformation.Category, packageId);
                            if (!string.IsNullOrEmpty(packageDirectory))
                            {
                                if (Directory.Exists(packageDirectory))
                                {
                                    if (!string.IsNullOrEmpty(packageVersion))
                                    {
                                        var versionDirectory = GetPackageVersionDirectory(installPath, packageInformation.Category, packageId, packageVersion);
                                        return RemovePackageVersion(versionDirectory);
                                    }
                                    else
                                    {
                                        return RemovePackage(packageDirectory);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            return false;
        }

        /// <summary>
        /// Delete a package version directory, and its parent package directory if no other versions remain. Returns true if the version was deleted
        /// </summary>
        /// <param name="packageVersionDirectory">Package version directory to delete</param>
        public static bool RemovePackageVersion(string packageVersionDirectory)
        {
            if (!string.IsNullOrEmpty(packageVersionDirectory))
            {
                try
                {
                    if (Directory.Exists(packageVersionDirectory))
                    {
                        // Delete the Package Directory for the specified Version
                        Directory.Delete(packageVersionDirectory, true);

                        // Get Parent Directory (Package Directory)
                        var packageDirectory = Path.GetDirectoryName(packageVersionDirectory);

                        // Check if Package Directory is empty
                        var remainingDirectories = Directory.GetDirectories(packageDirectory);
                        if (remainingDirectories == null || remainingDirectories.Length < 1)
                        {
                            // Delete the empty Package Directory
                            Directory.Delete(packageDirectory, true);
                        }

                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        /// <summary>
        /// Delete a package directory including all installed versions. Returns true if the directory was deleted
        /// </summary>
        /// <param name="packageDirectory">Package directory to delete</param>
        public static bool RemovePackage(string packageDirectory)
        {
            if (!string.IsNullOrEmpty(packageDirectory))
            {
                try
                {
                    if (Directory.Exists(packageDirectory))
                    {
                        // Delete the entire Package Directory
                        Directory.Delete(packageDirectory, true);
                        return true;
                    }
                }
                catch { }
            }

            return false;
        }

        #endregion

    }
}
