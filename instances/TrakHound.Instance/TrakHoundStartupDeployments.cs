// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrakHound.Deployments;

namespace TrakHound.Instance
{
    /// <summary>
    /// Installs Deployment files left for the Instance (ex. by the Installer) before the Instance starts.
    /// Each file is installed once : it is recorded by its hash, and files in the Startup Deployment directory
    /// are moved to "installed" or "failed" afterwards.
    /// </summary>
    public class TrakHoundStartupDeployments
    {
        public const string EnvironmentVariable = "TRAKHOUND_DEPLOYMENT_PATH";
        public const string InstalledDirectory = "installed";
        public const string FailedDirectory = "failed";

        /// <summary>
        /// Kept in the Configuration directory (not the program directory) so that it is kept with the configuration it changed (ex. a Docker volume)
        /// </summary>
        public const string RecordFilename = "deployment.startup.json";

        private const int _maxRecords = 50;
        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };


        /// <summary>
        /// The default Startup Deployment directory (in the Data Path, since installed files are moved to "installed" or "failed")
        /// </summary>
        public static string DefaultPath => TrakHoundDataDirectory.GetPath("deployments");

        /// <summary>
        /// Where Deployment Backups are created (also used by the Admin Interface Backups page)
        /// </summary>
        public static string BackupPath => TrakHoundDataDirectory.GetPath("backup", "deployments");

        /// <summary>
        /// Where the installed Deployment Information file is kept : the Configuration directory (not the program directory),
        /// like the Startup Record, so that it is kept with the configuration it describes (ex. a Docker volume)
        /// </summary>
        /// <param name="configurationPath">The directory containing the Configuration Profile directories</param>
        public static string GetInformationPath(string configurationPath) => configurationPath;


        /// <summary>
        /// A Deployment file, or a directory whose Deployment files (top level only) are installed
        /// </summary>
        public string SourcePath { get; }

        public string RecordPath { get; }

        /// <summary>
        /// Where the installed Deployment Information file is kept (see GetInformationPath)
        /// </summary>
        public string InformationPath { get; }

        /// <summary>
        /// The Deployments installed (or that failed) when this Instance started
        /// </summary>
        public IReadOnlyList<TrakHoundStartupDeploymentRecord> Results { get; private set; } = new List<TrakHoundStartupDeploymentRecord>();


        /// <param name="sourcePath">A Deployment file or a directory of Deployment files (the default directory if null)</param>
        /// <param name="configurationPath">The directory containing the Configuration Profile directories</param>
        public TrakHoundStartupDeployments(string sourcePath, string configurationPath)
        {
            SourcePath = !string.IsNullOrEmpty(sourcePath) ? Path.GetFullPath(sourcePath) : DefaultPath;
            RecordPath = Path.Combine(configurationPath, RecordFilename);
            InformationPath = GetInformationPath(configurationPath);
        }


        /// <summary>
        /// The Deployment files waiting to be installed (in name order)
        /// </summary>
        public IEnumerable<string> GetPendingFiles()
        {
            var installed = ReadRecords().Select(o => o.Hash).ToHashSet(StringComparer.OrdinalIgnoreCase);

            return GetFiles().Where(o =>
            {
                try { return !installed.Contains(ComputeHash(File.ReadAllBytes(o))); }
                catch { return false; }
            }).ToList();
        }

        /// <summary>
        /// The Startup Deployments installed (newest first)
        /// </summary>
        public List<TrakHoundStartupDeploymentRecord> ReadRecords()
        {
            try
            {
                if (File.Exists(RecordPath))
                {
                    var records = JsonSerializer.Deserialize<List<TrakHoundStartupDeploymentRecord>>(File.ReadAllText(RecordPath));
                    if (records != null) return records;
                }
            }
            catch { }

            return new List<TrakHoundStartupDeploymentRecord>();
        }

        /// <summary>
        /// Install the Deployment files that haven't been installed. Must be run while the Instance is stopped.
        /// A Deployment that fails is rolled back from its Backup (when one was created).
        /// </summary>
        public IReadOnlyList<TrakHoundStartupDeploymentRecord> Install(TrakHoundDeploymentInstallOptions options, Logger logger = null)
        {
            var results = new List<TrakHoundStartupDeploymentRecord>();

            foreach (var file in GetFiles())
            {
                var filename = Path.GetFileName(file);

                byte[] bytes;
                try
                {
                    bytes = File.ReadAllBytes(file);
                }
                catch (Exception ex)
                {
                    logger?.Error($"Startup Deployment : Could not read {file} : {ex.Message}");
                    continue;
                }

                var hash = ComputeHash(bytes);
                var records = ReadRecords();
                var existing = records.FirstOrDefault(o => string.Equals(o.Hash, hash, StringComparison.OrdinalIgnoreCase));
                if (existing != null)
                {
                    logger?.Info($"Startup Deployment : {filename} was already installed. Skipped.");
                    if (Directory.Exists(SourcePath)) Move(file, existing.Success ? InstalledDirectory : FailedDirectory, logger);
                    continue;
                }

                var record = Install(bytes, hash, filename, options, logger);
                results.Add(record);

                records.Insert(0, record);
                if (records.Count > _maxRecords) records.RemoveRange(_maxRecords, records.Count - _maxRecords);
                SaveRecords(records, logger);

                // A file named directly (ex. a read only Docker mount) is left in place. The record keeps it from being installed again.
                if (Directory.Exists(SourcePath)) Move(file, record.Success ? InstalledDirectory : FailedDirectory, logger);
            }

            Results = results;
            return results;
        }

        private static TrakHoundStartupDeploymentRecord Install(byte[] bytes, string hash, string filename, TrakHoundDeploymentInstallOptions options, Logger logger)
        {
            var record = new TrakHoundStartupDeploymentRecord();
            record.Hash = hash;
            record.Filename = filename;
            record.Timestamp = DateTime.UtcNow;

            var information = TrakHoundDeployment.ReadInformationFromDeployment(bytes);
            record.ProfileId = information?.ProfileId;
            record.Version = information?.Version;

            logger?.Info($"Startup Deployment : Installing {filename} ({record.ProfileId} v{record.Version})..");

            try
            {
                var result = TrakHoundDeployment.Install(bytes, options);
                record.Success = result.Success;
                record.BackupPath = result.BackupPath;
                if (result.Errors != null) record.Errors.AddRange(result.Errors);
            }
            catch (Exception ex)
            {
                record.Success = false;
                record.Errors.Add(ex.Message);
            }

            if (record.Success)
            {
                logger?.Info($"Startup Deployment : {filename} Installed. Backup : {record.BackupPath}");
            }
            else
            {
                foreach (var error in record.Errors) logger?.Error($"Startup Deployment : {filename} : {error}");

                // Return to the previous configuration and packages instead of starting with a partial install
                if (!string.IsNullOrEmpty(record.BackupPath))
                {
                    try
                    {
                        var restoreOptions = new TrakHoundDeploymentInstallOptions
                        {
                            ConfigurationPath = options.ConfigurationPath,
                            PackagesPath = options.PackagesPath,
                            VolumesPath = options.VolumesPath,
                            VariablesPath = options.VariablesPath,
                            InformationPath = options.InformationPath
                        };

                        var restoreResult = TrakHoundDeployment.Restore(record.BackupPath, restoreOptions);
                        record.Restored = restoreResult.Success;
                        if (restoreResult.Errors != null) record.Errors.AddRange(restoreResult.Errors.Select(o => $"Restore : {o}"));
                    }
                    catch (Exception ex)
                    {
                        record.Errors.Add($"Restore : {ex.Message}");
                    }

                    if (record.Restored) logger?.Warn($"Startup Deployment : {filename} was rolled back from Backup : {record.BackupPath}");
                    else logger?.Error($"Startup Deployment : {filename} could not be rolled back from Backup : {record.BackupPath}");
                }
            }

            return record;
        }

        private IEnumerable<string> GetFiles()
        {
            if (File.Exists(SourcePath)) return new[] { SourcePath };

            if (Directory.Exists(SourcePath))
            {
                return Directory.GetFiles(SourcePath, $"*{TrakHoundDeployment.FileExtension}", SearchOption.TopDirectoryOnly)
                    .Where(o => string.Equals(Path.GetExtension(o), TrakHoundDeployment.FileExtension, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(o => Path.GetFileName(o), StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return Enumerable.Empty<string>();
        }

        private void SaveRecords(List<TrakHoundStartupDeploymentRecord> records, Logger logger)
        {
            try
            {
                var directory = Path.GetDirectoryName(RecordPath);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

                File.WriteAllText(RecordPath, JsonSerializer.Serialize(records, _jsonOptions));
            }
            catch (Exception ex)
            {
                logger?.Error($"Startup Deployment : Could not write {RecordPath} : {ex.Message}");
            }
        }

        private void Move(string file, string directoryName, Logger logger)
        {
            try
            {
                var directory = Path.Combine(SourcePath, directoryName);
                if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
                var destination = Path.Combine(directory, $"{timestamp}-{Path.GetFileName(file)}");
                var suffix = 1;
                while (File.Exists(destination)) destination = Path.Combine(directory, $"{timestamp}-{suffix++}-{Path.GetFileName(file)}");

                File.Move(file, destination);
            }
            catch (Exception ex)
            {
                logger?.Warn($"Startup Deployment : Could not move {file} : {ex.Message}");
            }
        }

        private static string ComputeHash(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    /// <summary>
    /// A Deployment installed when the Instance started
    /// </summary>
    public class TrakHoundStartupDeploymentRecord
    {
        /// <summary>
        /// The SHA-256 hash of the Deployment file
        /// </summary>
        [JsonPropertyName("hash")]
        public string Hash { get; set; }

        [JsonPropertyName("filename")]
        public string Filename { get; set; }

        [JsonPropertyName("profileId")]
        public string ProfileId { get; set; }

        [JsonPropertyName("version")]
        public string Version { get; set; }

        /// <summary>
        /// When the Deployment was installed (UTC)
        /// </summary>
        [JsonPropertyName("timestamp")]
        public DateTime Timestamp { get; set; }

        [JsonPropertyName("success")]
        public bool Success { get; set; }

        [JsonPropertyName("backupPath")]
        public string BackupPath { get; set; }

        /// <summary>
        /// True if the Deployment failed and the Backup was restored
        /// </summary>
        [JsonPropertyName("restored")]
        public bool Restored { get; set; }

        [JsonPropertyName("errors")]
        public List<string> Errors { get; set; } = new List<string>();
    }
}
