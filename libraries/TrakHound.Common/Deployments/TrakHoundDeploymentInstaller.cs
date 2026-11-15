// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Text.Json;
using TrakHound.Configurations;
using TrakHound.Licenses;

namespace TrakHound.Deployments
{
    /// <summary>
    /// Create, Preview, Plan, Install and Restore Deployments using explicit Configuration and Package directories
    /// </summary>
    public partial class TrakHoundDeployment
    {
        /// <summary>
        /// The directory in the Deployment archive (and in Backups) that contains the Configuration Profiles
        /// </summary>
        public const string ConfigurationArchiveDirectory = "config";

        /// <summary>
        /// The directory in the Deployment archive (and in Backups) that contains the Packages ("{category}/{id}/{version}")
        /// </summary>
        public const string PackagesArchiveDirectory = "packages";

        /// <summary>
        /// The directory in the Deployment archive (and in Backups) that contains the Volume files ("{volumeId}/{path}")
        /// </summary>
        public const string VolumesArchiveDirectory = "volumes";

        /// <summary>
        /// The directory in the Deployment archive (and in Backups) that contains the Package Variable Collections ("{id}.json")
        /// </summary>
        public const string VariablesArchiveDirectory = "variables";

        /// <summary>
        /// The Licenses file in the Deployment archive (only in a Deployment with a Target Device ID)
        /// </summary>
        public const string LicensesArchiveFile = "licenses/licenses.json";

        /// <summary>
        /// The Licenses folder in a Profile. It isn't a Configuration Category : Licenses are for one Device, so they are only deployed
        /// with a Target Device ID (and added to the target's, never replacing or removing them)
        /// </summary>
        public const string LicensesDirectory = TrakHoundLicenseManagers.DirectoryName;

        /// <summary>
        /// The folder in a Profile for files that belong to this Instance only (ex. the keys that protect Admin sign-ins, and revoked sessions).
        /// It is never deployed, backed up, restored or removed by a Deployment
        /// </summary>
        public const string LocalDirectory = ".local";

        private const string _variableExtension = ".json";

        /// <summary>
        /// Metadata Key set to true if the Deployment is Incremental (only contains files changed since a timestamp)
        /// </summary>
        public const string IncrementalMetadataKey = "incremental";

        /// <summary>
        /// Metadata Key for the timestamp (ISO 8601, UTC) that an Incremental Deployment includes changes since
        /// </summary>
        public const string IncrementalSinceMetadataKey = "incrementalSince";

        /// <summary>
        /// Metadata Key set to true if secret values were included in the Deployment's Configuration files
        /// </summary>
        public const string SecretsIncludedMetadataKey = "secretsIncluded";

        /// <summary>
        /// The Configuration Category of the files at the root of a Profile (ex. instance.config.yaml)
        /// </summary>
        public const string ProfileSettingsCategory = ".";

        /// <summary>
        /// Runtime files that are never deployed (ex. the Instance's run marker, and its Boot History and the History's lock file)
        /// </summary>
        public static readonly IReadOnlyList<string> ExcludedFilenames = new[] { "instance.running", "boots.json", "boots.json.lock", "audit.jsonl", "audit.snapshot.json", "audit.lock", TrakHound.Instances.TrakHoundInstanceIdentity.Filename };

        /// <summary>
        /// File extensions that are never deployed or backed up (temporary files and Deployment files)
        /// </summary>
        public static readonly IReadOnlyList<string> ExcludedExtensions = new[] { ".tmp", FileExtension };

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };


        private class SelectedFile
        {
            public string SourcePath { get; set; }
            public string EntryName { get; set; }
            public bool IsConfiguration { get; set; }
            public long Size { get; set; }
        }


        #region "Create"

        /// <summary>
        /// Get what a Deployment created with the options would contain (without creating it)
        /// </summary>
        public static TrakHoundDeploymentPreview Preview(TrakHoundDeploymentCreateOptions options)
        {
            var (files, manifest, preview) = SelectFiles(options);
            return preview;
        }

        /// <summary>
        /// Create the Deployment file (written directly to disk, so large Deployments don't need to fit in memory)
        /// </summary>
        public TrakHoundDeploymentCreateFileResult CreateFile(string outputPath, TrakHoundDeploymentCreateOptions options)
        {
            var result = new TrakHoundDeploymentCreateFileResult();
            result.FilePath = outputPath;

            try
            {
                Report(options?.Progress, result.Messages, "Selecting files..");
                var (files, manifest, preview) = SelectFiles(options);
                result.Manifest = manifest;
                result.Contents = preview;

                Metadata[IncrementalMetadataKey] = options.IncrementalSince.HasValue;
                if (options.IncrementalSince.HasValue) Metadata[IncrementalSinceMetadataKey] = options.IncrementalSince.Value.ToUniversalTime().ToString("o", CultureInfo.InvariantCulture);
                Metadata[SecretsIncludedMetadataKey] = options.IncludeSecrets;
                TargetDeviceId = !string.IsNullOrWhiteSpace(options.TargetDeviceId) ? TrakHoundDeviceId.Normalize(options.TargetDeviceId) : null;
                if (!string.IsNullOrWhiteSpace(options.TargetDeviceId) && TargetDeviceId == null) throw new ArgumentException($"'{options.TargetDeviceId}' isn't a Device ID");
                result.Information = this;

                var (licenses, _) = SelectLicenses(options);

                var directory = Path.GetDirectoryName(outputPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

                using (var fileStream = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None))
                using (var archive = new ZipArchive(fileStream, ZipArchiveMode.Create))
                {
                    WriteJsonEntry(archive, InformationFile, this);
                    WriteJsonEntry(archive, ManifestFile, manifest);

                    if (licenses.Licenses.Count > 0)
                    {
                        var entry = archive.CreateEntry(LicensesArchiveFile, CompressionLevel.Optimal);
                        using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                        {
                            writer.Write(licenses.ToJson());
                        }
                    }

                    var index = 0;
                    foreach (var file in files)
                    {
                        index++;
                        if (index % 50 == 0 || index == files.Count) Report(options.Progress, null, $"Adding files.. {index:N0} of {files.Count:N0}");

                        if (file.IsConfiguration && !options.IncludeSecrets && TrakHoundDeploymentSecrets.IsYamlFile(file.SourcePath))
                        {
                            // Replace secret values with the Marker (the target keeps its own values)
                            var (yaml, count) = TrakHoundDeploymentSecrets.Remove(File.ReadAllText(file.SourcePath));
                            result.SecretsRemoved += count;

                            var entry = archive.CreateEntry(file.EntryName, CompressionLevel.Optimal);
                            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                            {
                                writer.Write(yaml);
                            }
                        }
                        else if (file.IsConfiguration && TrakHoundDeploymentSecrets.IsYamlFile(file.SourcePath))
                        {
                            // Encrypted secrets are decrypted (the target encrypts them with its own key when loaded)
                            var (yaml, _) = TrakHoundSecrets.DecryptYaml(File.ReadAllText(file.SourcePath));

                            var entry = archive.CreateEntry(file.EntryName, CompressionLevel.Optimal);
                            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                            {
                                writer.Write(yaml);
                            }
                        }
                        else
                        {
                            archive.CreateEntryFromFile(file.SourcePath, file.EntryName, CompressionLevel.Optimal);
                        }
                    }
                }

                result.FileSize = new FileInfo(outputPath).Length;
                result.Success = true;

                result.Messages.Add($"{(options.IncrementalSince.HasValue ? "Incremental" : "Full")} Deployment Created : {preview.ConfigurationFileCount:N0} configuration files, {preview.PackageCount:N0} package versions, {preview.VolumeFileCount:N0} volume files, {preview.VariableCount:N0} variable collections, {preview.LicenseCount:N0} licenses");
                if (!options.IncludeSecrets) result.Messages.Add($"Secrets not included : {result.SecretsRemoved:N0} values and {preview.SecretFilesExcluded:N0} files");
            }
            catch (Exception ex)
            {
                result.Errors.Add($"Error Creating Deployment : {ex.Message}");
                try { if (File.Exists(outputPath)) File.Delete(outputPath); } catch { }
            }

            return result;
        }

        private static (List<SelectedFile> Files, TrakHoundDeploymentManifest Manifest, TrakHoundDeploymentPreview Preview) SelectFiles(TrakHoundDeploymentCreateOptions options)
        {
            var files = new List<SelectedFile>();
            var manifestConfigurations = new List<TrakHoundDeploymentConfigurationManifestItem>();
            var manifestPackages = new List<TrakHoundDeploymentPackageManifestItem>();
            var profiles = new List<string>();
            var preview = new TrakHoundDeploymentPreview();

            var since = options.IncrementalSince?.ToUniversalTime();
            var categories = options.Categories?.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(o => o).ToList();

            // Configuration Profiles
            if (!string.IsNullOrEmpty(options.ConfigurationPath) && Directory.Exists(options.ConfigurationPath))
            {
                foreach (var profileDirectory in Directory.GetDirectories(options.ConfigurationPath).OrderBy(o => o))
                {
                    var profile = Path.GetFileName(profileDirectory);
                    if (options.Profiles != null && !options.Profiles.Contains(profile, StringComparer.OrdinalIgnoreCase)) continue;

                    profiles.Add(profile);

                    foreach (var file in Directory.GetFiles(profileDirectory, "*", SearchOption.AllDirectories))
                    {
                        if (IsExcluded(file)) continue;
                        var profileCategory = GetProfileCategory(profileDirectory, file);
                        if (IsLicensesCategory(profileCategory)) continue;
                        if (categories != null && !categories.Contains(profileCategory, StringComparer.OrdinalIgnoreCase)) continue;

                        if (!options.IncludeSecrets && TrakHoundDeploymentSecrets.IsSecretFile(file))
                        {
                            preview.SecretFilesExcluded++;
                            continue;
                        }

                        var fileInfo = new FileInfo(file);

                        manifestConfigurations.Add(new TrakHoundDeploymentConfigurationManifestItem
                        {
                            Profile = profile,
                            Category = Path.GetFileName(Path.GetDirectoryName(file)),
                            Id = Path.GetFileNameWithoutExtension(file),
                            LastUpdated = fileInfo.LastWriteTimeUtc.ToUnixTime()
                        });

                        if (!since.HasValue || fileInfo.LastWriteTimeUtc > since.Value)
                        {
                            files.Add(new SelectedFile
                            {
                                SourcePath = file,
                                EntryName = ToEntryName(ConfigurationArchiveDirectory, Path.GetRelativePath(options.ConfigurationPath, file)),
                                IsConfiguration = true,
                                Size = fileInfo.Length
                            });
                            preview.ConfigurationFileCount++;
                        }
                    }
                }
            }

            // Packages ("{category}/{id}/{version}")
            if (!string.IsNullOrEmpty(options.PackagesPath) && Directory.Exists(options.PackagesPath))
            {
                foreach (var categoryDirectory in Directory.GetDirectories(options.PackagesPath))
                {
                    var category = Path.GetFileName(categoryDirectory);
                    foreach (var packageDirectory in Directory.GetDirectories(categoryDirectory))
                    {
                        var packageId = Path.GetFileName(packageDirectory);
                        foreach (var versionDirectory in Directory.GetDirectories(packageDirectory))
                        {
                            var version = Path.GetFileName(versionDirectory);
                            if (options.PackageFilter != null && !options.PackageFilter(category, packageId, version)) continue;

                            manifestPackages.Add(new TrakHoundDeploymentPackageManifestItem { Category = category, Id = packageId, Version = version });

                            var versionFiles = Directory.GetFiles(versionDirectory, "*", SearchOption.AllDirectories).Where(o => !IsExcluded(o)).ToList();

                            // A Package version is deployed whole (if anything in it changed)
                            var changed = !since.HasValue ||
                                new DirectoryInfo(versionDirectory).LastWriteTimeUtc > since.Value ||
                                versionFiles.Any(o => File.GetLastWriteTimeUtc(o) > since.Value);

                            if (changed)
                            {
                                preview.PackageCount++;
                                foreach (var file in versionFiles)
                                {
                                    files.Add(new SelectedFile
                                    {
                                        SourcePath = file,
                                        EntryName = ToEntryName(PackagesArchiveDirectory, Path.GetRelativePath(options.PackagesPath, file)),
                                        IsConfiguration = false,
                                        Size = new FileInfo(file).Length
                                    });
                                }
                            }
                        }
                    }
                }
            }

            // Volumes ("{volumeId}/{path}"). Only the files are deployed; the target's other files are kept
            List<string> volumes = null;
            var volumeIds = GetSelectedIds(options.Volumes);
            if (volumeIds.Count > 0 && !string.IsNullOrEmpty(options.VolumesPath) && Directory.Exists(options.VolumesPath))
            {
                volumes = new List<string>();
                foreach (var volumeId in volumeIds)
                {
                    var volumeDirectory = Path.Combine(options.VolumesPath, volumeId);
                    if (!Directory.Exists(volumeDirectory)) continue;

                    volumes.Add(volumeId);

                    foreach (var file in Directory.GetFiles(volumeDirectory, "*", SearchOption.AllDirectories).OrderBy(o => o))
                    {
                        if (IsExcluded(file)) continue;

                        var fileInfo = new FileInfo(file);
                        if (!since.HasValue || fileInfo.LastWriteTimeUtc > since.Value)
                        {
                            files.Add(new SelectedFile
                            {
                                SourcePath = file,
                                EntryName = ToEntryName(VolumesArchiveDirectory, Path.GetRelativePath(options.VolumesPath, file)),
                                Size = fileInfo.Length
                            });
                            preview.VolumeFileCount++;
                        }
                    }
                }
            }

            // Package Variables ("{id}.json")
            List<string> variables = null;
            var variableIds = GetSelectedIds(options.Variables);
            if (variableIds.Count > 0 && !string.IsNullOrEmpty(options.VariablesPath) && Directory.Exists(options.VariablesPath))
            {
                variables = new List<string>();
                foreach (var variableId in variableIds)
                {
                    var file = Path.Combine(options.VariablesPath, variableId + _variableExtension);
                    if (!File.Exists(file)) continue;

                    variables.Add(variableId);

                    var fileInfo = new FileInfo(file);
                    if (!since.HasValue || fileInfo.LastWriteTimeUtc > since.Value)
                    {
                        files.Add(new SelectedFile
                        {
                            SourcePath = file,
                            EntryName = ToEntryName(VariablesArchiveDirectory, Path.GetFileName(file)),
                            Size = fileInfo.Length
                        });
                        preview.VariableCount++;
                    }
                }
            }

            var (selectedLicenses, licensesExcluded) = SelectLicenses(options);
            preview.LicenseCount = selectedLicenses.Licenses.Count;
            preview.LicensesExcluded = licensesExcluded;

            preview.Profiles = profiles;
            preview.Categories = categories;
            preview.ManifestConfigurationCount = manifestConfigurations.Count;
            preview.ManifestPackageCount = manifestPackages.Count;
            preview.FileCount = files.Count;
            preview.TotalBytes = files.Sum(o => o.Size);

            var manifest = new TrakHoundDeploymentManifest
            {
                Configurations = manifestConfigurations,
                Packages = manifestPackages,
                Profiles = profiles,
                Categories = categories,
                Volumes = volumes,
                Variables = variables
            };

            return (files, manifest, preview);
        }

        /// <summary>
        /// Get the Configuration Categories in the Profiles (the top level directories, plus <see cref="ProfileSettingsCategory"/>
        /// if a Profile has files at its root). All Profiles if none are specified.
        /// </summary>
        public static IEnumerable<string> GetCategories(string configurationPath, IEnumerable<string> profiles = null)
        {
            var categories = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(configurationPath) || !Directory.Exists(configurationPath)) return categories;

            foreach (var profileDirectory in Directory.GetDirectories(configurationPath))
            {
                if (profiles != null && !profiles.Contains(Path.GetFileName(profileDirectory), StringComparer.OrdinalIgnoreCase)) continue;

                if (Directory.GetFiles(profileDirectory).Any(o => !IsExcluded(o))) categories.Add(ProfileSettingsCategory);
                foreach (var categoryDirectory in Directory.GetDirectories(profileDirectory))
                {
                    var category = Path.GetFileName(categoryDirectory);
                    if (!IsLicensesCategory(category) && !string.Equals(category, LocalDirectory, StringComparison.OrdinalIgnoreCase)) categories.Add(category);
                }
            }

            return categories.OrderBy(o => o).ToList();
        }

        /// <summary>
        /// Get the Volumes (a directory per Volume) in the Volume directory, with their file counts and sizes
        /// </summary>
        public static IEnumerable<TrakHoundDeploymentDataItem> GetVolumes(string volumesPath)
        {
            var items = new List<TrakHoundDeploymentDataItem>();
            if (string.IsNullOrEmpty(volumesPath) || !Directory.Exists(volumesPath)) return items;

            foreach (var volumeDirectory in Directory.GetDirectories(volumesPath).OrderBy(o => o))
            {
                var volumeFiles = Directory.GetFiles(volumeDirectory, "*", SearchOption.AllDirectories).Where(o => !IsExcluded(o)).ToList();
                items.Add(new TrakHoundDeploymentDataItem
                {
                    Id = Path.GetFileName(volumeDirectory),
                    FileCount = volumeFiles.Count,
                    Size = volumeFiles.Sum(o => new FileInfo(o).Length)
                });
            }

            return items;
        }

        /// <summary>
        /// Get the Package Variable Collections ("{id}.json" files) in the Variables directory, with their sizes
        /// </summary>
        public static IEnumerable<TrakHoundDeploymentDataItem> GetVariables(string variablesPath)
        {
            var items = new List<TrakHoundDeploymentDataItem>();
            if (string.IsNullOrEmpty(variablesPath) || !Directory.Exists(variablesPath)) return items;

            foreach (var file in Directory.GetFiles(variablesPath, "*" + _variableExtension).OrderBy(o => o))
            {
                items.Add(new TrakHoundDeploymentDataItem
                {
                    Id = Path.GetFileNameWithoutExtension(file),
                    FileCount = 1,
                    Size = new FileInfo(file).Length
                });
            }

            return items;
        }

        /// <summary>
        /// The selected Volume or Variable IDs that name a single directory or file (an ID such as "../config" is ignored)
        /// </summary>
        private static List<string> GetSelectedIds(IEnumerable<string> ids)
        {
            if (ids == null) return new List<string>();

            return ids
                .Where(o => !string.IsNullOrWhiteSpace(o) && o != "." && o != ".." && o.IndexOfAny(new[] { '/', '\\' }) < 0 && o.IndexOfAny(Path.GetInvalidFileNameChars()) < 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(o => o)
                .ToList();
        }

        /// <summary>
        /// The Category of a file in a Profile : its top level directory, or <see cref="ProfileSettingsCategory"/> for files at the Profile root
        /// </summary>
        private static bool IsLicensesCategory(string category) => string.Equals(category, LicensesDirectory, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The Licenses for the Target Device ID (or for any Device), and how many were left out (for another Device, without a Target Device ID, or not a License Code)
        /// </summary>
        private static (TrakHoundLicenseFile Licenses, int Excluded) SelectLicenses(TrakHoundDeploymentCreateOptions options)
        {
            var targetDeviceId = !string.IsNullOrWhiteSpace(options.TargetDeviceId) ? TrakHoundDeviceId.Normalize(options.TargetDeviceId) : null;
            var selected = new TrakHoundLicenseFile { DeviceId = targetDeviceId };
            var excluded = 0;

            foreach (var entry in options.Licenses?.Licenses ?? Enumerable.Empty<TrakHoundLicenseFileEntry>())
            {
                var code = TrakHoundLicenseCode.Parse(entry?.LicenseCode);
                if (targetDeviceId == null || code == null || string.IsNullOrWhiteSpace(entry.ProductId) || !code.IsForDevice(targetDeviceId))
                {
                    excluded++;
                    continue;
                }

                var publisherId = entry.PublisherId ?? options.Licenses.PublisherId ?? TrakHoundLicenseManagers.DefaultPublisherId;
                if (selected.Licenses.Any(o => o.PublisherId == publisherId && o.LicenseCode == code.ToString())) continue;

                selected.Licenses.Add(new TrakHoundLicenseFileEntry
                {
                    PublisherId = publisherId,
                    ProductId = entry.ProductId.Trim(),
                    LicenseCode = code.ToString()
                });
            }

            return (selected, excluded);
        }

        private static string GetProfileCategory(string profileDirectory, string file)
        {
            var parts = Path.GetRelativePath(profileDirectory, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            return parts.Length > 1 ? parts[0] : ProfileSettingsCategory;
        }

        private static bool IsExcluded(string path)
        {
            var name = Path.GetFileName(path);
            if (ExcludedFilenames.Any(o => string.Equals(o, name, StringComparison.OrdinalIgnoreCase))) return true;

            // This Instance's own files (in a Profile's '.local' folder)
            var separators = new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar };
            if (path.Split(separators).Any(o => string.Equals(o, LocalDirectory, StringComparison.OrdinalIgnoreCase))) return true;

            var extension = Path.GetExtension(path);
            return ExcludedExtensions.Any(o => string.Equals(o, extension, StringComparison.OrdinalIgnoreCase));
        }

        private static string ToEntryName(string root, string relativePath)
        {
            return $"{root}/{relativePath.Replace('\\', '/')}";
        }

        private static void WriteJsonEntry(ZipArchive archive, string name, object value)
        {
            var entry = archive.CreateEntry(name);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
            {
                writer.Write(JsonSerializer.Serialize(value, _jsonOptions));
            }
        }

        #endregion

        #region "Plan"

        /// <summary>
        /// Get what installing the Deployment would change, without changing anything
        /// </summary>
        public static TrakHoundDeploymentPlan CreatePlan(byte[] deploymentBytes, TrakHoundDeploymentInstallOptions options)
        {
            var plan = new TrakHoundDeploymentPlan();

            if (deploymentBytes == null || deploymentBytes.Length == 0)
            {
                plan.Errors.Add("The deployment file is empty");
                return plan;
            }

            plan.Information = ReadInformationFromDeployment(deploymentBytes);
            plan.Manifest = ReadManifestFromDeployment(deploymentBytes);

            if (plan.Information == null || plan.Manifest == null)
            {
                plan.Errors.Add("Not a valid TrakHound deployment (the information or manifest could not be read)");
                return plan;
            }

            plan.IsIncremental = GetMetadataBool(plan.Information, IncrementalMetadataKey);

            try
            {
                using (var stream = new MemoryStream(deploymentBytes))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    BuildPlan(plan, archive, options);
                }
            }
            catch (Exception ex)
            {
                plan.Errors.Add($"Error reading the deployment : {ex.Message}");
            }

            return plan;
        }

        private static void BuildPlan(TrakHoundDeploymentPlan plan, ZipArchive archive, TrakHoundDeploymentInstallOptions options)
        {
            var configurationPath = options.ConfigurationPath;
            var packagesPath = options.PackagesPath;
            var manifest = plan.Manifest;
            var manifestConfigurations = manifest.Configurations?.ToList() ?? new List<TrakHoundDeploymentConfigurationManifestItem>();
            var manifestPackages = manifest.Packages?.ToList() ?? new List<TrakHoundDeploymentPackageManifestItem>();
            var profileAware = manifestConfigurations.Any(o => o.Profile != null);

            var configurationEntries = archive.Entries.Where(o => o.FullName.StartsWith(ConfigurationArchiveDirectory + "/") && !string.IsNullOrEmpty(o.Name)).ToList();
            var packageEntries = archive.Entries.Where(o => o.FullName.StartsWith(PackagesArchiveDirectory + "/") && !string.IsNullOrEmpty(o.Name)).ToList();

            // Profiles changed by the Deployment (earlier deployments don't list them : all Profiles in the Deployment or on the target)
            var profiles = manifest.Profiles?.ToList();
            if (profiles == null)
            {
                profiles = configurationEntries.Select(o => o.FullName.Split('/')[1]).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
                if (Directory.Exists(configurationPath))
                {
                    profiles = profiles.Union(Directory.GetDirectories(configurationPath).Select(Path.GetFileName), StringComparer.OrdinalIgnoreCase).ToList();
                }
            }
            plan.Profiles = profiles.OrderBy(o => o).ToList();

            // Categories changed by the Deployment (null for all)
            var categories = manifest.Categories?.ToList();
            plan.Categories = categories;

            // A Deployment for one Device is only installed on that Device (checked before anything else, so nothing is changed on another Device)
            var targetDeviceId = !string.IsNullOrEmpty(plan.Information.TargetDeviceId) ? TrakHoundDeviceId.Normalize(plan.Information.TargetDeviceId) : null;
            var deviceId = TrakHoundDeviceId.Normalize(options.DeviceId ?? TrakHoundDeviceId.Get());
            if (!string.IsNullOrEmpty(plan.Information.TargetDeviceId) && targetDeviceId != deviceId)
            {
                plan.Errors.Add($"This deployment is for the Device ID {plan.Information.TargetDeviceId}, but this device's ID is {deviceId}. Nothing was changed");
                return;
            }

            AddLicenseItems(plan, archive, options, targetDeviceId, profiles);

            // Configuration files in the Deployment : Add / Update / Unchanged
            foreach (var entry in configurationEntries)
            {
                var relativePath = entry.FullName.Substring(ConfigurationArchiveDirectory.Length + 1);
                var destination = Path.Combine(configurationPath, relativePath.Replace('/', Path.DirectorySeparatorChar));
                var parts = relativePath.Split('/');

                var action = TrakHoundDeploymentPlanAction.Add;
                if (File.Exists(destination))
                {
                    action = IsSameContent(entry, destination) ? TrakHoundDeploymentPlanAction.Unchanged : TrakHoundDeploymentPlanAction.Update;
                }

                plan.Items.Add(new TrakHoundDeploymentPlanItem
                {
                    Kind = TrakHoundDeploymentPlan.ConfigurationKind,
                    Profile = parts[0],
                    Category = parts.Length > 2 ? parts[parts.Length - 2] : parts[0],
                    Id = Path.GetFileNameWithoutExtension(entry.Name),
                    RelativePath = relativePath,
                    Action = action
                });
            }

            // Configuration files on the target that aren't in the Manifest : Remove
            // (only files in Category directories of the Deployment's Profiles and Categories. Files at the Profile root, such as instance.config.yaml, are never removed)
            // A Deployment with selected Categories replaces them even if it contains no files (ex. to remove all Services from the target)
            if ((manifestConfigurations.Count > 0 || categories != null) && Directory.Exists(configurationPath))
            {
                foreach (var profile in profiles)
                {
                    var profileDirectory = Path.Combine(configurationPath, profile);
                    if (!Directory.Exists(profileDirectory)) continue;

                    foreach (var categoryDirectory in Directory.GetDirectories(profileDirectory))
                    {
                        var category = Path.GetFileName(categoryDirectory);
                        if (IsLicensesCategory(category)) continue; // The target's Licenses are never removed
                        if (categories != null && !categories.Contains(category, StringComparer.OrdinalIgnoreCase)) continue;

                        foreach (var file in Directory.GetFiles(categoryDirectory))
                        {
                            if (IsExcluded(file)) continue;

                            var id = Path.GetFileNameWithoutExtension(file);
                            var inManifest = manifestConfigurations.Any(o =>
                                o.Category == category && o.Id == id &&
                                (!profileAware || string.Equals(o.Profile, profile, StringComparison.OrdinalIgnoreCase)));

                            if (!inManifest)
                            {
                                plan.Items.Add(new TrakHoundDeploymentPlanItem
                                {
                                    Kind = TrakHoundDeploymentPlan.ConfigurationKind,
                                    Profile = profile,
                                    Category = category,
                                    Id = id,
                                    RelativePath = Path.GetRelativePath(configurationPath, file).Replace('\\', '/'),
                                    Action = TrakHoundDeploymentPlanAction.Remove
                                });
                            }
                        }
                    }
                }
            }

            // Configuration files in the Manifest that are neither in the Deployment nor on the target : Missing
            if (profileAware)
            {
                foreach (var item in manifestConfigurations)
                {
                    var included = plan.Items.Any(o => o.IsConfiguration && o.Action != TrakHoundDeploymentPlanAction.Remove &&
                        string.Equals(o.Profile, item.Profile, StringComparison.OrdinalIgnoreCase) && o.Category == item.Category && o.Id == item.Id);
                    if (included) continue;

                    var profileDirectory = Path.Combine(configurationPath, item.Profile);
                    var exists = Directory.Exists(profileDirectory) &&
                        Directory.EnumerateFiles(profileDirectory, item.Id + ".*", SearchOption.AllDirectories)
                            .Any(o => Path.GetFileName(Path.GetDirectoryName(o)) == item.Category);

                    if (!exists)
                    {
                        plan.Items.Add(new TrakHoundDeploymentPlanItem
                        {
                            Kind = TrakHoundDeploymentPlan.ConfigurationKind,
                            Profile = item.Profile,
                            Category = item.Category,
                            Id = item.Id,
                            Action = TrakHoundDeploymentPlanAction.Missing
                        });
                    }
                }
            }

            // Package versions in the Deployment : Add / Update
            var deployedPackages = packageEntries
                .Select(o => o.FullName.Split('/'))
                .Where(o => o.Length >= 5)
                .Select(o => (Category: o[1], Id: o[2], Version: o[3]))
                .Distinct()
                .ToList();

            foreach (var package in deployedPackages)
            {
                var destination = Path.Combine(packagesPath, package.Category, package.Id, package.Version);
                plan.Items.Add(new TrakHoundDeploymentPlanItem
                {
                    Kind = TrakHoundDeploymentPlan.PackageKind,
                    Category = package.Category,
                    Id = package.Id,
                    Version = package.Version,
                    RelativePath = $"{package.Category}/{package.Id}/{package.Version}",
                    Action = Directory.Exists(destination) ? TrakHoundDeploymentPlanAction.Update : TrakHoundDeploymentPlanAction.Add
                });
            }

            // Package versions on the target that aren't in the Manifest : Remove (only when the Deployment lists its Packages
            // and includes all Categories. Packages used by Categories that aren't in the Deployment are kept)
            if (manifestPackages.Count > 0 && categories == null && Directory.Exists(packagesPath))
            {
                foreach (var categoryDirectory in Directory.GetDirectories(packagesPath))
                {
                    var category = Path.GetFileName(categoryDirectory);
                    foreach (var packageDirectory in Directory.GetDirectories(categoryDirectory))
                    {
                        var packageId = Path.GetFileName(packageDirectory);
                        foreach (var versionDirectory in Directory.GetDirectories(packageDirectory))
                        {
                            var version = Path.GetFileName(versionDirectory);
                            if (!manifestPackages.Any(o => o.Category == category && o.Id == packageId && o.Version == version))
                            {
                                plan.Items.Add(new TrakHoundDeploymentPlanItem
                                {
                                    Kind = TrakHoundDeploymentPlan.PackageKind,
                                    Category = category,
                                    Id = packageId,
                                    Version = version,
                                    RelativePath = $"{category}/{packageId}/{version}",
                                    Action = TrakHoundDeploymentPlanAction.Remove
                                });
                            }
                        }
                    }
                }
            }

            // Package versions in the Manifest that are neither in the Deployment nor on the target : Missing
            foreach (var item in manifestPackages)
            {
                var deployed = deployedPackages.Any(o => o.Category == item.Category && o.Id == item.Id && o.Version == item.Version);
                if (!deployed && !Directory.Exists(Path.Combine(packagesPath, item.Category, item.Id, item.Version)))
                {
                    plan.Items.Add(new TrakHoundDeploymentPlanItem
                    {
                        Kind = TrakHoundDeploymentPlan.PackageKind,
                        Category = item.Category,
                        Id = item.Id,
                        Version = item.Version,
                        RelativePath = $"{item.Category}/{item.Id}/{item.Version}",
                        Action = TrakHoundDeploymentPlanAction.Missing
                    });
                }
            }

            // Volume files in the Deployment : Add / Update / Unchanged (files on the target that aren't in the Deployment are kept)
            var volumeEntries = archive.Entries.Where(o => o.FullName.StartsWith(VolumesArchiveDirectory + "/") && !string.IsNullOrEmpty(o.Name)).ToList();
            if (volumeEntries.Count > 0)
            {
                if (!string.IsNullOrEmpty(options.VolumesPath))
                {
                    foreach (var entry in volumeEntries)
                    {
                        var relativePath = entry.FullName.Substring(VolumesArchiveDirectory.Length + 1);
                        var separator = relativePath.IndexOf('/');
                        if (separator < 1) continue;

                        plan.Items.Add(new TrakHoundDeploymentPlanItem
                        {
                            Kind = TrakHoundDeploymentPlan.VolumeKind,
                            Category = relativePath.Substring(0, separator),
                            Id = relativePath.Substring(separator + 1),
                            RelativePath = relativePath,
                            Action = GetFileAction(entry, Path.Combine(options.VolumesPath, relativePath.Replace('/', Path.DirectorySeparatorChar)))
                        });
                    }
                }
                else
                {
                    plan.Warnings.Add($"The deployment contains {volumeEntries.Count:N0} volume files, but no volume directory was given. They will not be installed.");
                }
            }

            // Package Variable Collections in the Deployment : Add / Update / Unchanged
            var variableEntries = archive.Entries.Where(o => o.FullName.StartsWith(VariablesArchiveDirectory + "/") && !string.IsNullOrEmpty(o.Name)).ToList();
            if (variableEntries.Count > 0)
            {
                if (!string.IsNullOrEmpty(options.VariablesPath))
                {
                    foreach (var entry in variableEntries)
                    {
                        var relativePath = entry.FullName.Substring(VariablesArchiveDirectory.Length + 1);
                        if (relativePath.Contains('/')) continue;

                        plan.Items.Add(new TrakHoundDeploymentPlanItem
                        {
                            Kind = TrakHoundDeploymentPlan.VariableKind,
                            Id = Path.GetFileNameWithoutExtension(relativePath),
                            RelativePath = relativePath,
                            Action = GetFileAction(entry, Path.Combine(options.VariablesPath, relativePath))
                        });
                    }
                }
                else
                {
                    plan.Warnings.Add($"The deployment contains {variableEntries.Count:N0} package variable collections, but no variables directory was given. They will not be installed.");
                }
            }

            var missing = plan.Items.Count(o => o.Action == TrakHoundDeploymentPlanAction.Missing);
            if (missing > 0)
            {
                plan.Warnings.Add(plan.IsIncremental
                    ? $"{missing:N0} files or package versions this incremental deployment depends on are not on this Instance. It was created for an Instance that has an earlier deployment of '{plan.Information.ProfileId}' installed. Install a full deployment instead."
                    : $"{missing:N0} files or package versions listed by the deployment are not included in it.");
            }

            if (manifestConfigurations.Count == 0 && categories == null) plan.Warnings.Add("The deployment contains no configurations. Existing configurations are kept.");
            if (manifestPackages.Count == 0) plan.Warnings.Add("The deployment contains no packages. Existing packages are kept.");
        }

        /// <summary>
        /// The Licenses in the Deployment : added to each of its Profiles (Unchanged if a Profile already has the License)
        /// </summary>
        private static void AddLicenseItems(TrakHoundDeploymentPlan plan, ZipArchive archive, TrakHoundDeploymentInstallOptions options, string targetDeviceId, IEnumerable<string> profiles)
        {
            var entry = archive.GetEntry(LicensesArchiveFile);
            if (entry == null) return;

            if (targetDeviceId == null)
            {
                plan.Warnings.Add("The deployment contains licenses but isn't for a Device ID. They will not be installed.");
                return;
            }

            string text;
            using (var reader = new StreamReader(entry.Open())) text = reader.ReadToEnd();

            var licenseFile = TrakHoundLicenseFile.Parse(text);
            foreach (var profile in profiles)
            {
                var directory = Path.Combine(options.ConfigurationPath, profile, LicensesDirectory);

                foreach (var publisher in licenseFile.Licenses.GroupBy(o => o.PublisherId ?? licenseFile.PublisherId ?? TrakHoundLicenseManagers.DefaultPublisherId))
                {
                    if (!TrakHoundLicenseManagers.IsValidPublisherId(publisher.Key))
                    {
                        plan.Warnings.Add($"The deployment contains licenses for '{publisher.Key}', which isn't a valid Publisher ID. They will not be installed.");
                        continue;
                    }

                    // Read without changing anything (the target's Licenses file for the Publisher)
                    var existing = new TrakHoundLicenseManager(publisher.Key.ToLowerInvariant(), directory);

                    foreach (var license in publisher)
                    {
                        plan.Items.Add(new TrakHoundDeploymentPlanItem
                        {
                            Kind = TrakHoundDeploymentPlan.LicenseKind,
                            Profile = profile,
                            Category = publisher.Key.ToLowerInvariant(),
                            Id = license.ProductId,
                            LicenseCode = license.LicenseCode,
                            RelativePath = $"{profile}/{LicensesDirectory}/{publisher.Key.ToLowerInvariant()}.json",
                            Action = existing.GetLicenseByLicenseCode(license.LicenseCode) != null ? TrakHoundDeploymentPlanAction.Unchanged : TrakHoundDeploymentPlanAction.Add
                        });
                    }
                }
            }
        }

        private static TrakHoundDeploymentPlanAction GetFileAction(ZipArchiveEntry entry, string destination)
        {
            if (!File.Exists(destination)) return TrakHoundDeploymentPlanAction.Add;
            return IsSameContent(entry, destination) ? TrakHoundDeploymentPlanAction.Unchanged : TrakHoundDeploymentPlanAction.Update;
        }

        private static bool IsSameContent(ZipArchiveEntry entry, string path)
        {
            try
            {
                var fileInfo = new FileInfo(path);
                if (fileInfo.Length != entry.Length) return false;

                using (var entryStream = entry.Open())
                using (var fileStream = File.OpenRead(path))
                {
                    int a, b;
                    do
                    {
                        a = entryStream.ReadByte();
                        b = fileStream.ReadByte();
                        if (a != b) return false;
                    }
                    while (a != -1);
                }

                return true;
            }
            catch
            {
                return false;
            }
        }

        private static bool GetMetadataBool(TrakHoundDeployment information, string key)
        {
            var value = information?.Metadata?.GetValueOrDefault(key);
            if (value is bool b) return b;
            if (value is JsonElement element) return element.ValueKind == JsonValueKind.True || (element.ValueKind == JsonValueKind.String && bool.TryParse(element.GetString(), out var parsed) && parsed);
            return value != null && bool.TryParse(value.ToString(), out var result) && result;
        }

        #endregion

        #region "Install"

        /// <summary>
        /// Install the Deployment : creates a Backup (if a Backup path is set), removes configurations and package versions that
        /// aren't in the Deployment (only in the Deployment's Profiles), then copies the Deployment's files.
        /// </summary>
        public static TrakHoundInstallDeploymentResult Install(byte[] deploymentBytes, TrakHoundDeploymentInstallOptions options)
        {
            var messages = new List<string>();
            var errors = new List<string>();
            string backupPath = null;

            var plan = CreatePlan(deploymentBytes, options);
            if (!plan.IsValid)
            {
                errors.AddRange(plan.Errors);
                return new TrakHoundInstallDeploymentResult(false, messages.ToArray(), errors.ToArray());
            }

            var information = plan.Information;
            Report(options.Progress, messages, $"Installing {information.ProfileId} v{information.Version} : Configuration = {options.ConfigurationPath} : Packages = {options.PackagesPath}");

            string tempPath = null;
            try
            {
                // Backup
                if (!string.IsNullOrEmpty(options.BackupPath))
                {
                    Report(options.Progress, messages, "Creating Backup..");
                    backupPath = CreateBackup(plan, options);
                    Report(options.Progress, messages, $"Backup Created : {backupPath}");
                }

                // Extract to a temp directory first (a bad archive fails before anything is changed)
                Report(options.Progress, messages, "Extracting Deployment..");
                tempPath = TrakHoundTemp.CreateDirectory() ?? Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString());
                using (var stream = new MemoryStream(deploymentBytes))
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Read))
                {
                    archive.ExtractToDirectory(tempPath, true);
                }

                // Remove
                var removeConfigurations = plan.Get(TrakHoundDeploymentPlan.ConfigurationKind, TrakHoundDeploymentPlanAction.Remove).ToList();
                var removePackages = plan.Get(TrakHoundDeploymentPlan.PackageKind, TrakHoundDeploymentPlanAction.Remove).ToList();
                Report(options.Progress, messages, $"Removing {removeConfigurations.Count:N0} configurations and {removePackages.Count:N0} package versions..");

                foreach (var item in removeConfigurations)
                {
                    TryDelete(Path.Combine(options.ConfigurationPath, item.RelativePath), errors);
                }

                foreach (var item in removePackages)
                {
                    var versionDirectory = Path.Combine(options.PackagesPath, item.Category, item.Id, item.Version);
                    TryDeleteDirectory(versionDirectory, errors);
                    DeleteIfEmpty(Path.GetDirectoryName(versionDirectory));
                }

                // Configurations (keeping the target's secrets where the Deployment doesn't include them)
                var tempConfiguration = Path.Combine(tempPath, ConfigurationArchiveDirectory);
                if (Directory.Exists(tempConfiguration))
                {
                    Report(options.Progress, messages, "Installing Configurations..");
                    foreach (var file in Directory.GetFiles(tempConfiguration, "*", SearchOption.AllDirectories))
                    {
                        var relativePath = Path.GetRelativePath(tempConfiguration, file);
                        var destination = Path.Combine(options.ConfigurationPath, relativePath);

                        if (TrakHoundDeploymentSecrets.IsYamlFile(file))
                        {
                            var deployed = File.ReadAllText(file);
                            if (deployed.Contains(TrakHoundDeploymentSecrets.Marker))
                            {
                                var existing = File.Exists(destination) ? File.ReadAllText(destination) : null;
                                File.WriteAllText(file, TrakHoundDeploymentSecrets.Merge(deployed, existing));
                            }
                        }

                        CopyFile(file, destination, errors);
                    }
                }

                // Licenses (added to the target's; its own Licenses are kept)
                var licenseItems = plan.Get(TrakHoundDeploymentPlan.LicenseKind, TrakHoundDeploymentPlanAction.Add).ToList();
                if (licenseItems.Count > 0)
                {
                    Report(options.Progress, messages, $"Installing {licenseItems.Count:N0} Licenses..");
                    var deviceId = TrakHoundDeviceId.Normalize(options.DeviceId ?? TrakHoundDeviceId.Get());

                    foreach (var group in licenseItems.GroupBy(o => (o.Profile, o.Category)))
                    {
                        var manager = new TrakHoundLicenseManager(group.Key.Category, Path.Combine(options.ConfigurationPath, group.Key.Profile, LicensesDirectory));
                        foreach (var item in group)
                        {
                            try
                            {
                                manager.AddLicense(item.Id, deviceId, item.LicenseCode);
                            }
                            catch (Exception ex)
                            {
                                errors.Add($"License for {item.Category} / {item.Id} wasn't added : {ex.Message}");
                            }
                        }
                    }
                }

                // Packages
                var tempPackages = Path.Combine(tempPath, PackagesArchiveDirectory);
                if (Directory.Exists(tempPackages))
                {
                    Report(options.Progress, messages, "Installing Packages..");
                    foreach (var file in Directory.GetFiles(tempPackages, "*", SearchOption.AllDirectories))
                    {
                        CopyFile(file, Path.Combine(options.PackagesPath, Path.GetRelativePath(tempPackages, file)), errors);
                    }
                }

                // Volume files (added or replaced; the target's other files are kept)
                var volumeItems = plan.Items.Where(o => o.IsVolume && o.Action != TrakHoundDeploymentPlanAction.Unchanged).ToList();
                if (volumeItems.Count > 0)
                {
                    Report(options.Progress, messages, $"Installing {volumeItems.Count:N0} Volume files..");
                    foreach (var item in volumeItems)
                    {
                        var relativePath = item.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                        CopyFile(Path.Combine(tempPath, VolumesArchiveDirectory, relativePath), Path.Combine(options.VolumesPath, relativePath), errors);
                    }
                }

                // Package Variables (each Collection replaces the target's)
                var variableItems = plan.Items.Where(o => o.IsVariable && o.Action != TrakHoundDeploymentPlanAction.Unchanged).ToList();
                if (variableItems.Count > 0)
                {
                    Report(options.Progress, messages, $"Installing {variableItems.Count:N0} Package Variable collections..");
                    foreach (var item in variableItems)
                    {
                        CopyFile(Path.Combine(tempPath, VariablesArchiveDirectory, item.RelativePath), Path.Combine(options.VariablesPath, item.RelativePath), errors);
                    }
                }

                // Installed Deployment Information
                if (!string.IsNullOrEmpty(options.InformationPath))
                {
                    if (!Directory.Exists(options.InformationPath)) Directory.CreateDirectory(options.InformationPath);
                    File.WriteAllText(Path.Combine(options.InformationPath, DeploymentFile), JsonSerializer.Serialize(information, _jsonOptions));
                }

                if (errors.Count == 0) Report(options.Progress, messages, "Deployment Installed Successfully");
                else Report(options.Progress, messages, $"Deployment Installed with {errors.Count:N0} errors");
            }
            catch (Exception ex)
            {
                errors.Add($"Error Installing Deployment : {ex.Message}");
            }
            finally
            {
                if (tempPath != null) TryDeleteDirectory(tempPath, null);
            }

            return new TrakHoundInstallDeploymentResult(errors.Count == 0, messages.ToArray(), errors.ToArray(), backupPath);
        }

        private static string CreateBackup(TrakHoundDeploymentPlan plan, TrakHoundDeploymentInstallOptions options)
        {
            var backupPath = Path.Combine(options.BackupPath, DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));
            var suffix = 1;
            while (Directory.Exists(backupPath)) backupPath = Path.Combine(options.BackupPath, $"{DateTime.Now:yyyyMMdd-HHmmss}-{suffix++}");
            Directory.CreateDirectory(backupPath);

            var backup = new TrakHoundDeploymentBackup();
            backup.Created = DateTime.UtcNow;
            backup.InstalledDeployment = plan.Information;
            backup.PreviousDeployment = !string.IsNullOrEmpty(options.InformationPath) ? ReadInformation(options.InformationPath) : null;

            // The Profiles the Deployment changes (whole directories, so Restore can return them exactly)
            foreach (var profile in plan.Profiles)
            {
                var source = Path.Combine(options.ConfigurationPath, profile);
                if (!Directory.Exists(source)) continue;

                CopyDirectory(source, Path.Combine(backupPath, ConfigurationArchiveDirectory, profile));
                backup.Profiles.Add(profile);
            }

            // Package versions the Deployment replaces or removes
            foreach (var item in plan.Items.Where(o => o.IsPackage && (o.Action == TrakHoundDeploymentPlanAction.Update || o.Action == TrakHoundDeploymentPlanAction.Remove)))
            {
                var source = Path.Combine(options.PackagesPath, item.Category, item.Id, item.Version);
                if (!Directory.Exists(source)) continue;

                CopyDirectory(source, Path.Combine(backupPath, PackagesArchiveDirectory, item.Category, item.Id, item.Version));
                backup.Packages.Add(item.RelativePath);
            }

            backup.AddedPackages = plan.Items.Where(o => o.IsPackage && o.Action == TrakHoundDeploymentPlanAction.Add).Select(o => o.RelativePath).ToList();

            // Volume files and Package Variables the Deployment replaces (only the files it writes)
            foreach (var item in plan.Items.Where(o => o.IsVolume && o.Action == TrakHoundDeploymentPlanAction.Update))
            {
                var relativePath = item.RelativePath.Replace('/', Path.DirectorySeparatorChar);
                CopyBackupFile(Path.Combine(options.VolumesPath, relativePath), Path.Combine(backupPath, VolumesArchiveDirectory, relativePath));
                backup.VolumeFiles.Add(item.RelativePath);
            }
            backup.AddedVolumeFiles = plan.Items.Where(o => o.IsVolume && o.Action == TrakHoundDeploymentPlanAction.Add).Select(o => o.RelativePath).ToList();

            foreach (var item in plan.Items.Where(o => o.IsVariable && o.Action == TrakHoundDeploymentPlanAction.Update))
            {
                CopyBackupFile(Path.Combine(options.VariablesPath, item.RelativePath), Path.Combine(backupPath, VariablesArchiveDirectory, item.RelativePath));
                backup.Variables.Add(item.RelativePath);
            }
            backup.AddedVariables = plan.Items.Where(o => o.IsVariable && o.Action == TrakHoundDeploymentPlanAction.Add).Select(o => o.RelativePath).ToList();

            File.WriteAllText(Path.Combine(backupPath, TrakHoundDeploymentBackup.Filename), JsonSerializer.Serialize(backup, _jsonOptions));
            return backupPath;
        }

        #endregion

        #region "Backups"

        /// <summary>
        /// Get the Backups in the directory (newest first)
        /// </summary>
        public static IEnumerable<TrakHoundDeploymentBackup> GetBackups(string backupRootPath)
        {
            var backups = new List<TrakHoundDeploymentBackup>();
            if (string.IsNullOrEmpty(backupRootPath) || !Directory.Exists(backupRootPath)) return backups;

            foreach (var directory in Directory.GetDirectories(backupRootPath))
            {
                var backup = ReadBackup(directory);
                if (backup != null) backups.Add(backup);
            }

            return backups.OrderByDescending(o => o.Created).ToList();
        }

        /// <summary>
        /// Read the Backup in the directory (null if the directory doesn't contain a Backup file or it can't be read)
        /// </summary>
        public static TrakHoundDeploymentBackup ReadBackup(string backupPath)
        {
            try
            {
                var path = Path.Combine(backupPath, TrakHoundDeploymentBackup.Filename);
                if (!File.Exists(path)) return null;

                var backup = JsonSerializer.Deserialize<TrakHoundDeploymentBackup>(File.ReadAllText(path));
                if (backup != null) backup.Path = backupPath;
                return backup;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Restore the Configuration Profiles, Package versions, Volume files and Package Variables from a Backup (undoing the Deployment installed after it)
        /// </summary>
        public static TrakHoundInstallDeploymentResult Restore(string backupPath, TrakHoundDeploymentInstallOptions options)
        {
            var messages = new List<string>();
            var errors = new List<string>();

            var backup = ReadBackup(backupPath);
            if (backup == null)
            {
                errors.Add($"Backup not found : {backupPath}");
                return new TrakHoundInstallDeploymentResult(false, messages.ToArray(), errors.ToArray());
            }

            try
            {
                // Configuration Profiles : replace with the Backup copy
                foreach (var profile in backup.Profiles)
                {
                    Report(options.Progress, messages, $"Restoring Configuration Profile '{profile}'..");

                    var source = Path.Combine(backupPath, ConfigurationArchiveDirectory, profile);
                    var destination = Path.Combine(options.ConfigurationPath, profile);
                    if (!Directory.Exists(source)) continue;

                    // Remove files added since the Backup (ex. configurations added by the Deployment)
                    if (Directory.Exists(destination))
                    {
                        foreach (var file in Directory.GetFiles(destination, "*", SearchOption.AllDirectories))
                        {
                            if (IsExcluded(file)) continue;

                            var relativePath = Path.GetRelativePath(destination, file);
                            if (!File.Exists(Path.Combine(source, relativePath))) TryDelete(file, errors);
                        }
                    }

                    CopyDirectory(source, destination, errors);
                }

                // Package versions added by the Deployment
                if (backup.AddedPackages.Count > 0) Report(options.Progress, messages, $"Removing {backup.AddedPackages.Count:N0} package versions added by the Deployment..");
                foreach (var package in backup.AddedPackages)
                {
                    var directory = Path.Combine(options.PackagesPath, package.Replace('/', Path.DirectorySeparatorChar));
                    if (Directory.Exists(directory))
                    {
                        TryDeleteDirectory(directory, errors);
                        DeleteIfEmpty(Path.GetDirectoryName(directory));
                    }
                }

                // Package versions replaced or removed by the Deployment
                if (backup.Packages.Count > 0) Report(options.Progress, messages, $"Restoring {backup.Packages.Count:N0} package versions..");
                foreach (var package in backup.Packages)
                {
                    var source = Path.Combine(backupPath, PackagesArchiveDirectory, package.Replace('/', Path.DirectorySeparatorChar));
                    CopyDirectory(source, Path.Combine(options.PackagesPath, package.Replace('/', Path.DirectorySeparatorChar)), errors);
                }

                // Volume files : remove those added by the Deployment, then restore those it replaced
                if (!string.IsNullOrEmpty(options.VolumesPath) && (backup.AddedVolumeFiles.Count > 0 || backup.VolumeFiles.Count > 0))
                {
                    Report(options.Progress, messages, "Restoring Volume files..");
                    foreach (var file in backup.AddedVolumeFiles)
                    {
                        var path = Path.Combine(options.VolumesPath, file.Replace('/', Path.DirectorySeparatorChar));
                        TryDelete(path, errors);

                        // Directories the Deployment created (up to and including the Volume's directory)
                        for (var directory = Path.GetDirectoryName(path); directory != null && directory.Length > options.VolumesPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Length; directory = Path.GetDirectoryName(directory))
                        {
                            DeleteIfEmpty(directory);
                        }
                    }
                    foreach (var file in backup.VolumeFiles)
                    {
                        var relativePath = file.Replace('/', Path.DirectorySeparatorChar);
                        CopyFile(Path.Combine(backupPath, VolumesArchiveDirectory, relativePath), Path.Combine(options.VolumesPath, relativePath), errors);
                    }
                }

                // Package Variables : remove those added by the Deployment, then restore those it replaced
                if (!string.IsNullOrEmpty(options.VariablesPath) && (backup.AddedVariables.Count > 0 || backup.Variables.Count > 0))
                {
                    Report(options.Progress, messages, "Restoring Package Variables..");
                    foreach (var file in backup.AddedVariables)
                    {
                        TryDelete(Path.Combine(options.VariablesPath, file), errors);
                    }
                    foreach (var file in backup.Variables)
                    {
                        CopyFile(Path.Combine(backupPath, VariablesArchiveDirectory, file), Path.Combine(options.VariablesPath, file), errors);
                    }
                }

                // Installed Deployment Information
                if (!string.IsNullOrEmpty(options.InformationPath))
                {
                    var informationFile = Path.Combine(options.InformationPath, DeploymentFile);
                    if (backup.PreviousDeployment != null) File.WriteAllText(informationFile, JsonSerializer.Serialize(backup.PreviousDeployment, _jsonOptions));
                    else if (File.Exists(informationFile)) File.Delete(informationFile);
                }

                if (errors.Count == 0) Report(options.Progress, messages, "Backup Restored Successfully");
            }
            catch (Exception ex)
            {
                errors.Add($"Error Restoring Backup : {ex.Message}");
            }

            return new TrakHoundInstallDeploymentResult(errors.Count == 0, messages.ToArray(), errors.ToArray(), backupPath);
        }

        #endregion

        #region "Files"

        private static void Report(IProgress<string> progress, List<string> messages, string message)
        {
            messages?.Add(message);
            progress?.Report(message);
        }

        private static void CopyFile(string source, string destination, List<string> errors)
        {
            try
            {
                var directory = Path.GetDirectoryName(destination);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);
                File.Copy(source, destination, true);
            }
            catch (Exception ex)
            {
                errors?.Add($"Could not write {destination} : {ex.Message}");
            }
        }

        private static void CopyDirectory(string source, string destination, List<string> errors = null)
        {
            if (!Directory.Exists(source)) return;

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (IsExcluded(file)) continue;

                if (errors != null)
                {
                    CopyFile(file, Path.Combine(destination, Path.GetRelativePath(source, file)), errors);
                }
                else
                {
                    // Backups must be complete : let the error stop the Install
                    var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                    Directory.CreateDirectory(Path.GetDirectoryName(target));
                    File.Copy(file, target, true);
                }
            }
        }

        /// <summary>
        /// Copy a file into a Backup (errors stop the Install, since Backups must be complete)
        /// </summary>
        private static void CopyBackupFile(string source, string destination)
        {
            if (!File.Exists(source)) return;

            Directory.CreateDirectory(Path.GetDirectoryName(destination));
            File.Copy(source, destination, true);
        }

        private static void TryDelete(string path, List<string> errors)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception ex)
            {
                errors?.Add($"Could not delete {path} : {ex.Message}");
            }
        }

        private static void TryDeleteDirectory(string path, List<string> errors)
        {
            try
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
            }
            catch (Exception ex)
            {
                errors?.Add($"Could not delete {path} : {ex.Message}");
            }
        }

        private static void DeleteIfEmpty(string directory)
        {
            try
            {
                if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any()) Directory.Delete(directory);
            }
            catch { }
        }

        #endregion
    }
}
