// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;
using TrakHound.Licenses;

namespace TrakHound.Deployments
{
    /// <summary>
    /// What a Deployment created by <see cref="TrakHoundDeployment.CreateFile"/> contains
    /// </summary>
    public class TrakHoundDeploymentCreateOptions
    {
        /// <summary>
        /// The directory containing the Configuration Profile directories (ex. "{instance}/config")
        /// </summary>
        public string ConfigurationPath { get; set; }

        /// <summary>
        /// The Package directory (ex. "{instance}/packages")
        /// </summary>
        public string PackagesPath { get; set; }

        /// <summary>
        /// The Volume directory (ex. "{instance}/volumes"), with a directory per Volume
        /// </summary>
        public string VolumesPath { get; set; }

        /// <summary>
        /// The Package Variables directory (ex. "{instance}/variables"), with a "{id}.json" file per Collection
        /// </summary>
        public string VariablesPath { get; set; }

        /// <summary>
        /// The Volumes (IDs) to include. Null or empty for none. Their files are added to or replace the target's files; other files on the target are kept.
        /// </summary>
        public IEnumerable<string> Volumes { get; set; }

        /// <summary>
        /// The Package Variable Collections (IDs, usually the Configuration ID) to include. Null or empty for none. Each replaces the target's Collection.
        /// </summary>
        public IEnumerable<string> Variables { get; set; }

        /// <summary>
        /// Only include files changed since this time (an Incremental Deployment). Null for a Full Deployment.
        /// </summary>
        public DateTime? IncrementalSince { get; set; }

        /// <summary>
        /// The Configuration Profiles to include (null for all)
        /// </summary>
        public IEnumerable<string> Profiles { get; set; }

        /// <summary>
        /// The Configuration Categories to include (the top level directories in a Profile, ex. "api", "app", "service", "worker").
        /// Use <see cref="TrakHoundDeployment.ProfileSettingsCategory"/> for the files at the Profile root (ex. instance.config.yaml).
        /// Null for all. Installing only replaces these Categories on the target.
        /// </summary>
        public IEnumerable<string> Categories { get; set; }

        /// <summary>
        /// Returns true for the Package versions to include (category, id, version). Null includes all installed versions.
        /// </summary>
        [JsonIgnore]
        public Func<string, string, string, bool> PackageFilter { get; set; }

        /// <summary>
        /// The Device ID the Deployment is for (null for any Device). Installing it on another Device fails before anything is changed
        /// </summary>
        public string TargetDeviceId { get; set; }

        /// <summary>
        /// Licenses to include. Only the ones for <see cref="TargetDeviceId"/> (or for any Device) are included, so a Deployment without one carries no Licenses.
        /// They are added to the target's Licenses (the target's own are kept)
        /// </summary>
        [JsonIgnore]
        public TrakHoundLicenseFile Licenses { get; set; }

        /// <summary>
        /// Include secrets (the admin accounts, access tokens, passwords). When false they are replaced with a
        /// <see cref="TrakHoundDeploymentSecrets.Marker"/> and the target keeps its existing values.
        /// </summary>
        public bool IncludeSecrets { get; set; }

        /// <summary>
        /// Receives progress messages while the Deployment is created (optional)
        /// </summary>
        [JsonIgnore]
        public IProgress<string> Progress { get; set; }
    }

    /// <summary>
    /// Where a Deployment is installed
    /// </summary>
    public class TrakHoundDeploymentInstallOptions
    {
        /// <summary>
        /// The directory containing the Configuration Profile directories (ex. "{instance}/config")
        /// </summary>
        public string ConfigurationPath { get; set; }

        /// <summary>
        /// The Package directory (ex. "{instance}/packages")
        /// </summary>
        public string PackagesPath { get; set; }

        /// <summary>
        /// The Volume directory (ex. "{instance}/volumes"). Volume files in the Deployment are skipped if not set.
        /// </summary>
        public string VolumesPath { get; set; }

        /// <summary>
        /// The Package Variables directory (ex. "{instance}/variables"). Variables in the Deployment are skipped if not set.
        /// </summary>
        public string VariablesPath { get; set; }

        /// <summary>
        /// This Device's ID, checked against a Deployment's Target Device ID (null for <see cref="TrakHoundDeviceId.Get"/>)
        /// </summary>
        public string DeviceId { get; set; }

        /// <summary>
        /// The directory the installed Deployment Information file ("trakhound-deployment.json") is written to
        /// </summary>
        public string InformationPath { get; set; }

        /// <summary>
        /// The directory Backups are created in (a sub directory per Install). Null to not create a Backup.
        /// </summary>
        public string BackupPath { get; set; }

        /// <summary>
        /// Receives progress messages while the Deployment is installed or a Backup is restored (optional)
        /// </summary>
        [JsonIgnore]
        public IProgress<string> Progress { get; set; }
    }


    /// <summary>
    /// The files a Deployment would contain (see <see cref="TrakHoundDeployment.Preview"/>)
    /// </summary>
    public class TrakHoundDeploymentPreview
    {
        /// <summary>
        /// The Configuration Profiles included in the Deployment
        /// </summary>
        public IReadOnlyList<string> Profiles { get; set; } = new List<string>();

        /// <summary>
        /// The Configuration Categories included in the Deployment (null for all)
        /// </summary>
        public IReadOnlyList<string> Categories { get; set; }

        /// <summary>
        /// Configuration files in the Deployment (changed files only for an Incremental Deployment)
        /// </summary>
        public int ConfigurationFileCount { get; set; }

        /// <summary>
        /// Package versions in the Deployment (changed versions only for an Incremental Deployment)
        /// </summary>
        public int PackageCount { get; set; }

        /// <summary>
        /// Volume files in the Deployment (changed files only for an Incremental Deployment)
        /// </summary>
        public int VolumeFileCount { get; set; }

        /// <summary>
        /// Package Variable Collections in the Deployment (changed Collections only for an Incremental Deployment)
        /// </summary>
        public int VariableCount { get; set; }

        /// <summary>
        /// Configuration files in the Manifest (every Configuration file the target should have)
        /// </summary>
        public int ManifestConfigurationCount { get; set; }

        /// <summary>
        /// Package versions in the Manifest (every Package version the target should have)
        /// </summary>
        public int ManifestPackageCount { get; set; }

        /// <summary>
        /// The total number of files (Configuration, Package, Volume and Variable) added to the Deployment
        /// </summary>
        public int FileCount { get; set; }

        /// <summary>
        /// The uncompressed size of the files
        /// </summary>
        public long TotalBytes { get; set; }

        /// <summary>
        /// The number of secret-only files (ex. the admin accounts) left out because secrets are not included
        /// </summary>
        public int SecretFilesExcluded { get; set; }

        /// <summary>
        /// The number of Licenses included (the ones for the Target Device ID)
        /// </summary>
        public int LicenseCount { get; set; }

        /// <summary>
        /// The number of Licenses left out because they are for another Device (or there is no Target Device ID), or aren't License Codes
        /// </summary>
        public int LicensesExcluded { get; set; }
    }

    /// <summary>
    /// A Volume or Package Variable Collection that can be included in a Deployment (see <see cref="TrakHoundDeployment.GetVolumes"/>)
    /// </summary>
    public class TrakHoundDeploymentDataItem
    {
        /// <summary>
        /// The Volume ID or Variable Collection ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The number of files (1 for a Variable Collection)
        /// </summary>
        public int FileCount { get; set; }

        /// <summary>
        /// The total size of the files (bytes)
        /// </summary>
        public long Size { get; set; }
    }

    /// <summary>
    /// The result of <see cref="TrakHoundDeployment.CreateFile"/>
    /// </summary>
    public class TrakHoundDeploymentCreateFileResult
    {
        /// <summary>
        /// True if the Deployment file was created successfully
        /// </summary>
        public bool Success { get; set; }

        /// <summary>
        /// The path of the Deployment file (deleted if creating it failed)
        /// </summary>
        public string FilePath { get; set; }

        /// <summary>
        /// The size of the created Deployment file (bytes)
        /// </summary>
        public long FileSize { get; set; }

        /// <summary>
        /// The Deployment Information written to the file (including its Metadata)
        /// </summary>
        public TrakHoundDeployment Information { get; set; }

        /// <summary>
        /// The Manifest written to the file
        /// </summary>
        public TrakHoundDeploymentManifest Manifest { get; set; }

        /// <summary>
        /// A summary of the files the Deployment contains
        /// </summary>
        public TrakHoundDeploymentPreview Contents { get; set; }

        /// <summary>
        /// The number of secret values replaced with the Marker (0 if secrets are included)
        /// </summary>
        public int SecretsRemoved { get; set; }

        /// <summary>
        /// Informational messages written while creating the Deployment
        /// </summary>
        public List<string> Messages { get; set; } = new List<string>();

        /// <summary>
        /// Errors that occurred while creating the Deployment
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();
    }


    /// <summary>
    /// What installing a Deployment would do to a Configuration file or Package version
    /// </summary>
    public enum TrakHoundDeploymentPlanAction
    {
        /// <summary>
        /// Added (doesn't exist on the target)
        /// </summary>
        Add,

        /// <summary>
        /// Replaces an existing file / Package version
        /// </summary>
        Update,

        /// <summary>
        /// Identical to the existing file
        /// </summary>
        Unchanged,

        /// <summary>
        /// Deleted (exists on the target but isn't in the Deployment)
        /// </summary>
        Remove,

        /// <summary>
        /// In the Manifest but neither in the Deployment nor on the target (an Incremental Deployment installed on the wrong base)
        /// </summary>
        Missing
    }

    /// <summary>
    /// A Configuration file or Package version in a Deployment Plan and what installing would do to it
    /// </summary>
    public class TrakHoundDeploymentPlanItem
    {
        /// <summary>
        /// "configuration", "package", "volume" or "variable"
        /// </summary>
        public string Kind { get; set; }

        /// <summary>
        /// The Configuration Profile the file is in (null for Packages, Volumes and Variables)
        /// </summary>
        public string Profile { get; set; }

        /// <summary>
        /// The Configuration Category (directory), the Package Category or the Volume ID
        /// </summary>
        public string Category { get; set; }

        /// <summary>
        /// The Configuration ID (filename without extension), the Package ID, the Volume file path (relative to the Volume) or the Variable Collection ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The Package Version (null for Configurations, Volumes and Variables)
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// The path relative to the Configuration, Package, Volumes or Variables directory
        /// </summary>
        public string RelativePath { get; set; }

        /// <summary>
        /// What installing the Deployment would do to the item
        /// </summary>
        public TrakHoundDeploymentPlanAction Action { get; set; }

        /// <summary>
        /// True if the item is a Configuration file
        /// </summary>
        public bool IsConfiguration => Kind == TrakHoundDeploymentPlan.ConfigurationKind;

        /// <summary>
        /// True if the item is a Package version
        /// </summary>
        public bool IsPackage => Kind == TrakHoundDeploymentPlan.PackageKind;

        /// <summary>
        /// True if the item is a Volume file
        /// </summary>
        public bool IsVolume => Kind == TrakHoundDeploymentPlan.VolumeKind;

        /// <summary>
        /// True if the item is a Package Variable Collection
        /// </summary>
        public bool IsVariable => Kind == TrakHoundDeploymentPlan.VariableKind;

        /// <summary>
        /// True if the Item is a License
        /// </summary>
        public bool IsLicense => Kind == TrakHoundDeploymentPlan.LicenseKind;

        /// <summary>
        /// The License Code of a License Item
        /// </summary>
        public string LicenseCode { get; set; }
    }

    /// <summary>
    /// What installing a Deployment would change (see <see cref="TrakHoundDeployment.CreatePlan"/>)
    /// </summary>
    public class TrakHoundDeploymentPlan
    {
        /// <summary>
        /// The Kind of a Configuration file Plan Item
        /// </summary>
        public const string ConfigurationKind = "configuration";

        /// <summary>
        /// The Kind of a Package version Plan Item
        /// </summary>
        public const string PackageKind = "package";

        /// <summary>
        /// The Kind of a Volume file Plan Item
        /// </summary>
        public const string VolumeKind = "volume";

        /// <summary>
        /// The Kind of a Package Variable Collection Plan Item
        /// </summary>
        public const string VariableKind = "variable";

        /// <summary>
        /// The Kind of a License Plan Item (Category is the Publisher ID and Id the Product ID)
        /// </summary>
        public const string LicenseKind = "license";

        /// <summary>
        /// The Information read from the Deployment (null if it could not be read)
        /// </summary>
        public TrakHoundDeployment Information { get; set; }

        /// <summary>
        /// The Manifest read from the Deployment (null if it could not be read)
        /// </summary>
        public TrakHoundDeploymentManifest Manifest { get; set; }

        /// <summary>
        /// True if the Deployment is Incremental (only contains files changed since an earlier Deployment)
        /// </summary>
        public bool IsIncremental { get; set; }

        /// <summary>
        /// The Configuration Profiles the Deployment changes
        /// </summary>
        public IReadOnlyList<string> Profiles { get; set; } = new List<string>();

        /// <summary>
        /// The Configuration Categories the Deployment changes (null for all)
        /// </summary>
        public IReadOnlyList<string> Categories { get; set; }

        /// <summary>
        /// The Configuration files and Package versions and what installing would do to each
        /// </summary>
        public List<TrakHoundDeploymentPlanItem> Items { get; set; } = new List<TrakHoundDeploymentPlanItem>();

        /// <summary>
        /// Warnings about installing the Deployment (ex. Missing items). Warnings don't prevent installing
        /// </summary>
        public List<string> Warnings { get; set; } = new List<string>();

        /// <summary>
        /// Errors that prevent the Deployment from being installed
        /// </summary>
        public List<string> Errors { get; set; } = new List<string>();


        /// <summary>
        /// True if the Deployment's Information and Manifest were read and there are no Errors
        /// </summary>
        public bool IsValid => Information != null && Manifest != null && Errors.Count == 0;

        /// <summary>
        /// Gets the number of Items of the Kind with the Action
        /// </summary>
        public int Count(string kind, TrakHoundDeploymentPlanAction action) => Items.Count(o => o.Kind == kind && o.Action == action);

        /// <summary>
        /// Gets the Items of the Kind with the Action
        /// </summary>
        public IEnumerable<TrakHoundDeploymentPlanItem> Get(string kind, TrakHoundDeploymentPlanAction action) => Items.Where(o => o.Kind == kind && o.Action == action);
    }


    /// <summary>
    /// A Backup created before installing a Deployment ("backup.json" in the Backup directory)
    /// </summary>
    public class TrakHoundDeploymentBackup
    {
        /// <summary>
        /// The name of the Backup Information file in the Backup directory
        /// </summary>
        public const string Filename = "backup.json";

        /// <summary>
        /// When the Backup was created (UTC)
        /// </summary>
        [JsonPropertyName("created")]
        public DateTime Created { get; set; }

        /// <summary>
        /// The Deployment that was installed after the Backup was created
        /// </summary>
        [JsonPropertyName("installedDeployment")]
        public TrakHoundDeployment InstalledDeployment { get; set; }

        /// <summary>
        /// The Deployment that was installed before (restored with the Backup)
        /// </summary>
        [JsonPropertyName("previousDeployment")]
        public TrakHoundDeployment PreviousDeployment { get; set; }

        /// <summary>
        /// The Configuration Profiles copied into the Backup (whole directories, replaced when the Backup is restored)
        /// </summary>
        [JsonPropertyName("profiles")]
        public List<string> Profiles { get; set; } = new List<string>();

        /// <summary>
        /// Package versions added by the Deployment (removed when the Backup is restored), as "{category}/{id}/{version}"
        /// </summary>
        [JsonPropertyName("addedPackages")]
        public List<string> AddedPackages { get; set; } = new List<string>();

        /// <summary>
        /// Package versions copied into the Backup (replaced or removed by the Deployment), as "{category}/{id}/{version}"
        /// </summary>
        [JsonPropertyName("packages")]
        public List<string> Packages { get; set; } = new List<string>();

        /// <summary>
        /// Volume files added by the Deployment (deleted when the Backup is restored), as "{volumeId}/{path}"
        /// </summary>
        [JsonPropertyName("addedVolumeFiles")]
        public List<string> AddedVolumeFiles { get; set; } = new List<string>();

        /// <summary>
        /// Volume files copied into the Backup (replaced by the Deployment), as "{volumeId}/{path}"
        /// </summary>
        [JsonPropertyName("volumeFiles")]
        public List<string> VolumeFiles { get; set; } = new List<string>();

        /// <summary>
        /// Package Variable Collection files added by the Deployment (deleted when the Backup is restored), as "{id}.json"
        /// </summary>
        [JsonPropertyName("addedVariables")]
        public List<string> AddedVariables { get; set; } = new List<string>();

        /// <summary>
        /// Package Variable Collection files copied into the Backup (replaced by the Deployment), as "{id}.json"
        /// </summary>
        [JsonPropertyName("variables")]
        public List<string> Variables { get; set; } = new List<string>();

        /// <summary>
        /// The Backup directory (set when the Backup is read; not serialized)
        /// </summary>
        [JsonIgnore]
        public string Path { get; set; }
    }
}
