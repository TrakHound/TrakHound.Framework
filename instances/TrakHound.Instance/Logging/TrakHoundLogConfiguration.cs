// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Logging;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.Instance.Logging
{
    public enum TrakHoundLogStorageProvider
    {
        /// <summary>
        /// JSON Lines files (one file per Logger per UTC day)
        /// </summary>
        File,

        /// <summary>
        /// A single indexed SQLite database (with full-text search)
        /// </summary>
        Sqlite
    }

    public enum TrakHoundLogArchiveMode
    {
        /// <summary>
        /// Logs older than the Retention period are deleted
        /// </summary>
        Delete,

        /// <summary>
        /// Logs older than the Retention period are compressed (gzip) into the archive (and remain queryable)
        /// </summary>
        Compress
    }

    /// <summary>
    /// Log History settings, read from "logs.config.yaml" in the Configuration Profile directory
    /// </summary>
    public class TrakHoundLogConfiguration
    {
        public const string Filename = "logs.config.yaml";

        [YamlMember(Alias = "provider")]
        public string Provider { get; set; } = nameof(TrakHoundLogStorageProvider.File);

        [YamlMember(Alias = "path")]
        public string Path { get; set; } = "logs";

        [YamlMember(Alias = "minimumLevel")]
        public string MinimumLevel { get; set; } = nameof(TrakHoundLogLevel.Information);

        [YamlMember(Alias = "frameworkMinimumLevel")]
        public string FrameworkMinimumLevel { get; set; } = nameof(TrakHoundLogLevel.Warning);

        [YamlMember(Alias = "retentionDays")]
        public int RetentionDays { get; set; } = 30;

        [YamlMember(Alias = "archiveMode")]
        public string ArchiveMode { get; set; } = nameof(TrakHoundLogArchiveMode.Compress);

        [YamlMember(Alias = "archiveRetentionDays")]
        public int ArchiveRetentionDays { get; set; } = 365;

        [YamlMember(Alias = "maxSizeMB")]
        public long MaxSizeMB { get; set; } = 1024;

        [YamlMember(Alias = "archiveMaxSizeMB")]
        public long ArchiveMaxSizeMB { get; set; } = 0;

        [YamlMember(Alias = "maintenanceIntervalMinutes")]
        public int MaintenanceIntervalMinutes { get; set; } = 60;


        [YamlIgnore]
        public TrakHoundLogStorageProvider ProviderType => ParseEnum(Provider, TrakHoundLogStorageProvider.File);

        [YamlIgnore]
        public TrakHoundLogArchiveMode ArchiveModeType => ParseEnum(ArchiveMode, TrakHoundLogArchiveMode.Compress);

        [YamlIgnore]
        public TrakHoundLogLevel MinimumLogLevel => ParseEnum(MinimumLevel, TrakHoundLogLevel.Information);

        [YamlIgnore]
        public TrakHoundLogLevel FrameworkMinimumLogLevel => ParseEnum(FrameworkMinimumLevel, TrakHoundLogLevel.Warning);

        [YamlIgnore]
        public TimeSpan MaintenanceInterval => TimeSpan.FromMinutes(Math.Max(1, MaintenanceIntervalMinutes));


        /// <summary>
        /// The Configuration File content (with comments describing each setting)
        /// </summary>
        public string ToYaml()
        {
            return
$@"# TrakHound Instance Log History
#
# Log History is what the Explorer (Diagnostics > Logs) and the Logs API (/_logs) query.
# Changes to provider, path, minimumLevel and frameworkMinimumLevel require a restart.
# Retention settings are re-read at each maintenance run.

# Storage : File (JSON Lines files, one per Logger per UTC day) or Sqlite (indexed database with full-text search)
provider: {ProviderType}

# Directory for Log History (relative to the Instance directory, or absolute)
path: {QuoteYaml(string.IsNullOrWhiteSpace(Path) ? "logs" : Path.Trim())}

# Least severe level written to Log History (Critical, Error, Warning, Information, Debug, Trace)
minimumLevel: {MinimumLogLevel}

# Least severe level written for framework loggers (Microsoft.*, System.*)
frameworkMinimumLevel: {FrameworkMinimumLogLevel}

# Days that logs are kept as active (uncompressed) storage. 0 = no limit
retentionDays: {Math.Max(0, RetentionDays)}

# What happens to logs older than retentionDays (or over maxSizeMB) : Delete or Compress
# Compressed logs are moved to the '_archive' directory and remain queryable
archiveMode: {ArchiveModeType}

# Days that compressed archives are kept. 0 = no limit
archiveRetentionDays: {Math.Max(0, ArchiveRetentionDays)}

# Maximum size of active storage in MB. The oldest days are archived (or deleted) first. 0 = no limit
maxSizeMB: {Math.Max(0, MaxSizeMB)}

# Maximum size of the archive in MB. The oldest archives are deleted first. 0 = no limit
archiveMaxSizeMB: {Math.Max(0, ArchiveMaxSizeMB)}

# How often retention and archiving run (minutes)
maintenanceIntervalMinutes: {Math.Max(1, MaintenanceIntervalMinutes)}
";
        }

        public TrakHoundLogConfiguration Clone()
        {
            return (TrakHoundLogConfiguration)MemberwiseClone();
        }

        private static string QuoteYaml(string value)
        {
            // Single quoted so that paths with ':', '#' or '\' are read back unchanged
            return $"'{value.Replace("'", "''")}'";
        }

        /// <summary>
        /// Get the full Log History directory (relative paths are relative to the Instance directory)
        /// </summary>
        public string GetDirectory()
        {
            var path = string.IsNullOrWhiteSpace(Path) ? "logs" : Path;
            return System.IO.Path.IsPathRooted(path) ? path : System.IO.Path.Combine(AppContext.BaseDirectory, path);
        }


        /// <summary>
        /// Read the Configuration File (returns null if it doesn't exist or can't be read)
        /// </summary>
        public static TrakHoundLogConfiguration Read(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                var text = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(text)) return new TrakHoundLogConfiguration();

                var deserializer = new DeserializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .IgnoreUnmatchedProperties()
                    .Build();

                return deserializer.Deserialize<TrakHoundLogConfiguration>(text) ?? new TrakHoundLogConfiguration();
            }

            return null;
        }

        /// <summary>
        /// Write the default Configuration File (with comments describing each setting)
        /// </summary>
        public static void WriteDefault(string path)
        {
            new TrakHoundLogConfiguration().Save(path);
        }

        /// <summary>
        /// Write the Configuration File (with comments describing each setting)
        /// </summary>
        public void Save(string path)
        {
            var directory = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            var tempPath = path + ".tmp";
            File.WriteAllText(tempPath, ToYaml());
            File.Move(tempPath, path, true);
        }


        private static T ParseEnum<T>(string value, T defaultValue) where T : struct, Enum
        {
            return !string.IsNullOrEmpty(value) && Enum.TryParse<T>(value.Trim(), true, out var result) ? result : defaultValue;
        }
    }
}
