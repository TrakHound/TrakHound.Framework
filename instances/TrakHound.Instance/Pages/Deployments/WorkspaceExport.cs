// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Deployments;
using TrakHound.Instance.Logging;
using TrakHound.Instance.Pages.Configuration;
using TrakHound.Instances;
using TrakHound.Packages;

namespace TrakHound.Instance.Pages.Deployments
{
    public enum WorkspacePackageSource
    {
        /// <summary>
        /// The version is installed on this Instance (copied from its packages directory)
        /// </summary>
        Installed,

        /// <summary>
        /// The version is downloaded from the Package Source (it isn't installed here)
        /// </summary>
        PackageSource,

        /// <summary>
        /// The version isn't installed here and isn't on the Package Source
        /// </summary>
        Missing
    }

    /// <summary>
    /// A Package version a Workspace's configurations use, and where the Export gets it from
    /// </summary>
    public class WorkspacePackage
    {
        public string PackageId { get; set; }

        /// <summary>
        /// The version the configurations ask for ("*" for the latest)
        /// </summary>
        public string RequestedVersion { get; set; }

        /// <summary>
        /// The version the Export includes (null if it is Missing)
        /// </summary>
        public string Version { get; set; }

        public WorkspacePackageSource Source { get; set; }

        /// <summary>
        /// The configurations that use it (ex. "Worker : Notifications")
        /// </summary>
        public List<string> UsedBy { get; } = new List<string>();
    }

    /// <summary>
    /// What every Device's Deployment includes (see <see cref="WorkspaceExport.CreateDeviceDeployments"/>)
    /// </summary>
    public class WorkspaceDeviceExportOptions
    {
        /// <summary>
        /// The Profile the Workspace replaces on each Device
        /// </summary>
        public string ProfileName { get; set; }

        public bool IncludeSettings { get; set; }

        public bool IncludeSecrets { get; set; }

        /// <summary>
        /// The Workspace's Licenses (each Device's Deployment includes only its own)
        /// </summary>
        public TrakHound.Licenses.TrakHoundLicenseFile Licenses { get; set; }
    }

    /// <summary>
    /// Exports a Workspace as a Deployment : its configurations are copied to a staging directory under the target's Profile name, and the
    /// Package versions they use (and their Dependencies) are copied from this Instance or downloaded from the Package Source. Nothing is installed here
    /// </summary>
    public class WorkspaceExport
    {
        // Instance Settings files at the Profile root. They replace the target's whole files, so they are only included when asked
        private static readonly string[] _settingsFilenames = new[] { TrakHoundInstanceConfiguration.Filename, TrakHoundLogConfiguration.Filename };

        // The Categories a Workspace edits. They replace the target's (also when the Workspace has none), so the target matches the Workspace
        private static readonly string[] _workspaceCategories = ConfigurationCategories.All.Select(o => o.ConfigurationCategory)
            .Concat(new[] { "router", "group", "zone", "diagram" })
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        private readonly TrakHoundPackageManager _packageManager;
        private readonly TrakHoundPackageClient _client;


        public WorkspaceExport(TrakHoundPackageManager packageManager, TrakHoundPackageClient client)
        {
            _packageManager = packageManager;
            _client = client;
        }


        /// <summary>
        /// Returns true if the name can be used for the target's Profile (it is a directory on the target)
        /// </summary>
        public static bool IsValidProfileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || name == "." || name == "..") return false;
            return name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !name.Contains('/') && !name.Contains('\\');
        }

        /// <summary>
        /// Returns true if the Workspace has Instance Settings (saved on its Settings page)
        /// </summary>
        public static bool HasSettings(WorkspaceConfigurationTarget workspace)
        {
            return _settingsFilenames.Any(o => File.Exists(Path.Combine(workspace.Path, o)));
        }


        #region "Packages"

        /// <summary>
        /// The Package versions the Workspace's configurations use, and where each comes from (nothing is downloaded). Their Dependencies are found when exported
        /// </summary>
        public async Task<List<WorkspacePackage>> GetPackages(WorkspaceConfigurationTarget workspace)
        {
            var packages = new List<WorkspacePackage>();

            foreach (var category in ConfigurationCategories.All)
            {
                foreach (var configuration in category.Load(workspace.Profile) ?? Enumerable.Empty<ConfigurationModel>())
                {
                    if (string.IsNullOrEmpty(configuration.PackageId)) continue;

                    var requested = string.IsNullOrEmpty(configuration.PackageVersion) ? TrakHoundPackage.WilcardVersion : configuration.PackageVersion;
                    var package = packages.FirstOrDefault(o => string.Equals(o.PackageId, configuration.PackageId, StringComparison.OrdinalIgnoreCase) && o.RequestedVersion == requested);
                    if (package == null)
                    {
                        package = new WorkspacePackage { PackageId = configuration.PackageId, RequestedVersion = requested };
                        packages.Add(package);
                    }

                    package.UsedBy.Add($"{category.SingularTitle} : {category.GetDisplayName(configuration)}");
                }
            }

            foreach (var package in packages)
            {
                var (version, source) = await Resolve(package.PackageId, package.RequestedVersion);
                package.Version = version;
                package.Source = source;
            }

            return packages.OrderBy(o => o.PackageId, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// The version to include and where it comes from. A specific version is copied when it's installed here. The latest ("*") is the
        /// Package Source's latest (copied if that version is installed here), or the newest installed version when there is no Package Source
        /// </summary>
        private async Task<(string Version, WorkspacePackageSource Source)> Resolve(string packageId, string requestedVersion)
        {
            var isLatest = requestedVersion.StartsWith(TrakHoundPackage.WilcardVersion);

            if (!isLatest && _packageManager?.Get(packageId, requestedVersion) != null) return (requestedVersion, WorkspacePackageSource.Installed);

            var sourceVersions = await GetSourceVersions(packageId);
            if (isLatest)
            {
                // The Version the Instance would select for it ("*" : the highest Stable Version, else the highest Prerelease; "*-*" : the highest of either)
                var latest = TrakHoundPackage.SelectVersion(sourceVersions, requestedVersion);
                if (latest != null) return (latest, _packageManager?.Get(packageId, latest) != null ? WorkspacePackageSource.Installed : WorkspacePackageSource.PackageSource);

                var installed = _packageManager?.GetLatest(packageId);
                return installed != null ? (installed.Version, WorkspacePackageSource.Installed) : (null, WorkspacePackageSource.Missing);
            }

            return sourceVersions.Contains(requestedVersion) ? (requestedVersion, WorkspacePackageSource.PackageSource) : (null, WorkspacePackageSource.Missing);
        }

        private async Task<IReadOnlyList<string>> GetSourceVersions(string packageId)
        {
            if (_client == null) return Array.Empty<string>();

            try
            {
                return (await _client.GetVersions(packageId))?.ToList() ?? new List<string>();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        #endregion

        #region "Stage"

        /// <summary>
        /// Copies the Workspace and its Packages to the staging directory : "{stagingPath}/config/{profileName}" and "{stagingPath}/packages/{category}/{id}/{version}".
        /// Returns the errors (empty if everything was staged)
        /// </summary>
        public async Task<List<string>> Stage(WorkspaceConfigurationTarget workspace, IEnumerable<WorkspacePackage> packages, string stagingPath, string profileName, bool includeSettings, IProgress<string> progress = null)
        {
            StageConfiguration(workspace, GetConfigurationPath(stagingPath), profileName, includeSettings, null, progress);
            return await StagePackages(packages, GetPackagesPath(stagingPath), progress);
        }

        /// <summary>
        /// Copies the Workspace to "{configurationPath}/{profileName}". A Device's values replace the Workspace's Variables (the Workspace isn't changed)
        /// </summary>
        public static void StageConfiguration(WorkspaceConfigurationTarget workspace, string configurationPath, string profileName, bool includeSettings, WorkspaceDevice device = null, IProgress<string> progress = null)
        {
            progress?.Report(device != null ? $"Copying the workspace for {device.Label}.." : "Copying the workspace..");
            var profilePath = Path.Combine(configurationPath, profileName);
            CopyDirectory(workspace.Path, profilePath, file => includeSettings || !IsSettingsFile(workspace.Path, file));

            if (device?.Variables?.Count > 0)
            {
                // Values are kept as stored (secret values stay encrypted until the Deployment is created)
                var variables = TrakHoundConfigurationVariables.Read(workspace.Path);
                foreach (var variable in device.Variables) variables[variable.Key] = variable.Value;
                TrakHoundConfigurationVariables.Save(profilePath, variables);
            }
        }

        /// <summary>
        /// Copies the Packages (and their Dependencies) to "{packagesPath}/{category}/{id}/{version}". Returns the errors (empty if everything was staged)
        /// </summary>
        public async Task<List<string>> StagePackages(IEnumerable<WorkspacePackage> packages, string packagesPath, IProgress<string> progress = null)
        {
            var errors = new List<string>();
            Directory.CreateDirectory(packagesPath);

            var queue = new Queue<(string Id, string Version, WorkspacePackageSource Source)>(packages.Where(o => o.Source != WorkspacePackageSource.Missing).Select(o => (o.PackageId, o.Version, o.Source)));
            foreach (var missing in packages.Where(o => o.Source == WorkspacePackageSource.Missing))
            {
                errors.Add($"{missing.PackageId} {DescribeVersion(missing.RequestedVersion)} isn't installed here or on the Package Source");
            }

            var staged = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            while (queue.Count > 0)
            {
                var (id, version, source) = queue.Dequeue();
                if (!staged.Add($"{id}:{version}")) continue;

                progress?.Report($"Adding {id} v{version}..");
                var information = await StagePackage(id, version, source, packagesPath, errors);

                foreach (var dependency in information?.Dependencies ?? Enumerable.Empty<ITrakHoundPackageDependency>())
                {
                    if (string.IsNullOrEmpty(dependency.Id) || staged.Any(o => o.StartsWith(dependency.Id + ":", StringComparison.OrdinalIgnoreCase))) continue;

                    var requested = string.IsNullOrEmpty(dependency.Version) ? TrakHoundPackage.WilcardVersion : dependency.Version;
                    var (dependencyVersion, dependencySource) = await ResolveDependency(dependency.Id, requested);
                    if (dependencySource == WorkspacePackageSource.Missing)
                    {
                        errors.Add($"{dependency.Id} {DescribeVersion(requested)} (used by {id}) isn't installed here or on the Package Source");
                        continue;
                    }

                    queue.Enqueue((dependency.Id, dependencyVersion, dependencySource));
                }
            }

            return errors;
        }

        /// <summary>
        /// Creates a Deployment for each Device in the output directory ("{deploymentId}-{label}-{version}.thd"). Each is for the Device's ID, with its Licenses and its
        /// values for the Variables, and the Packages staged in "{stagingPath}/packages" (stage them first with <see cref="StagePackages"/>)
        /// </summary>
        public static List<(WorkspaceDevice Device, string FilePath, TrakHoundDeploymentCreateFileResult Result)> CreateDeviceDeployments(
            WorkspaceConfigurationTarget workspace,
            IEnumerable<WorkspaceDevice> devices,
            TrakHoundDeployment deployment,
            WorkspaceDeviceExportOptions options,
            string stagingPath,
            string outputDirectory,
            IProgress<string> progress = null
            )
        {
            var results = new List<(WorkspaceDevice, string, TrakHoundDeploymentCreateFileResult)>();
            Directory.CreateDirectory(outputDirectory);

            var index = 0;
            foreach (var device in devices)
            {
                index++;

                // Each Device's configuration is staged on its own (its Variables differ). The Packages are shared
                var deviceStagingPath = Path.Combine(stagingPath, "devices", index.ToString());
                StageConfiguration(workspace, GetConfigurationPath(deviceStagingPath), options.ProfileName, options.IncludeSettings, device, progress);

                var filePath = Path.Combine(outputDirectory, GetDeviceFilename(deployment, device));
                var information = new TrakHoundDeployment
                {
                    ProfileId = deployment.ProfileId,
                    Version = deployment.Version,
                    Description = deployment.Description,
                    BuildDate = deployment.BuildDate
                };

                progress?.Report($"Creating the deployment for {device.Label}..");
                var result = information.CreateFile(filePath, new TrakHoundDeploymentCreateOptions
                {
                    ConfigurationPath = GetConfigurationPath(deviceStagingPath),
                    PackagesPath = GetPackagesPath(stagingPath),
                    Profiles = new[] { options.ProfileName },
                    Categories = GetCategories(deviceStagingPath, options.ProfileName),
                    IncludeSecrets = options.IncludeSecrets,
                    TargetDeviceId = device.DeviceId,
                    Licenses = options.Licenses
                });

                results.Add((device, filePath, result));
            }

            return results;
        }

        /// <summary>
        /// The name of a Device's Deployment file (ex. "plant-a-Line-3-Press-1.0.0.thd")
        /// </summary>
        public static string GetDeviceFilename(TrakHoundDeployment deployment, WorkspaceDevice device)
        {
            return $"{ToFilename(deployment.ProfileId)}-{ToFilename(device.Label)}-{ToFilename(deployment.Version)}{TrakHoundDeployment.FileExtension}";
        }

        /// <summary>
        /// The name of the .zip file that holds every Device's Deployment (ex. "plant-a-1.0.0-devices.zip")
        /// </summary>
        public static string GetDevicesFilename(TrakHoundDeployment deployment) => $"{ToFilename(deployment.ProfileId)}-{ToFilename(deployment.Version)}-devices.zip";

        /// <summary>
        /// The Licenses in the file that are for the Device (or for any Device), the way a Deployment for its ID includes them
        /// </summary>
        public static List<TrakHound.Licenses.TrakHoundLicenseFileEntry> GetDeviceLicenses(TrakHound.Licenses.TrakHoundLicenseFile licenses, string deviceId)
        {
            var normalized = TrakHound.Licenses.TrakHoundDeviceId.Normalize(deviceId);
            if (normalized == null || licenses == null) return new List<TrakHound.Licenses.TrakHoundLicenseFileEntry>();

            return licenses.Licenses.Where(o => TrakHound.Licenses.TrakHoundLicenseCode.Parse(o.LicenseCode)?.IsForDevice(normalized) == true).ToList();
        }

        private static string ToFilename(string value)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var name = new string((value ?? "").Trim().Select(o => invalid.Contains(o) || char.IsWhiteSpace(o) ? '-' : o).ToArray());
            return string.IsNullOrEmpty(name) ? "deployment" : name;
        }

        public static string GetConfigurationPath(string stagingPath) => Path.Combine(stagingPath, "config");

        public static string GetPackagesPath(string stagingPath) => Path.Combine(stagingPath, "packages");

        /// <summary>
        /// The Categories the Deployment replaces on the target : the Workspace's (also the ones it has none of) and the Profile root files it includes.
        /// The target's other Categories (ex. security) are kept
        /// </summary>
        public static List<string> GetCategories(string stagingPath, string profileName)
        {
            var categories = new HashSet<string>(_workspaceCategories, StringComparer.OrdinalIgnoreCase);
            foreach (var category in TrakHoundDeployment.GetCategories(GetConfigurationPath(stagingPath), new[] { profileName })) categories.Add(category);
            return categories.OrderBy(o => o).ToList();
        }

        // A Dependency's version range is resolved by the Package Source (or by the installed versions without one)
        private async Task<(string Version, WorkspacePackageSource Source)> ResolveDependency(string packageId, string requestedVersion)
        {
            var installed = _packageManager?.Resolve(packageId, requestedVersion);
            if (installed != null) return (installed.Version, WorkspacePackageSource.Installed);

            if (_client != null)
            {
                try
                {
                    var package = await _client.Get(packageId, requestedVersion);
                    if (package != null) return (package.Version, WorkspacePackageSource.PackageSource);
                }
                catch { }
            }

            return (null, WorkspacePackageSource.Missing);
        }

        private async Task<TrakHoundPackage> StagePackage(string id, string version, WorkspacePackageSource source, string packagesPath, List<string> errors)
        {
            try
            {
                if (source == WorkspacePackageSource.Installed)
                {
                    var installed = _packageManager.Get(id, version);
                    var installedPath = installed != null ? Path.Combine(TrakHoundPackage.GetDirectory(_packageManager.Path), installed.Category, installed.Id, installed.Version) : null;
                    if (installedPath == null || !Directory.Exists(installedPath))
                    {
                        errors.Add($"{id} v{version} isn't installed here");
                        return null;
                    }

                    CopyDirectory(installedPath, Path.Combine(packagesPath, installed.Category, installed.Id, installed.Version), null);
                    return installed;
                }
                else
                {
                    var bytes = await _client.Download(id, version);
                    if (bytes == null || bytes.Length == 0)
                    {
                        errors.Add($"{id} v{version} couldn't be downloaded from the Package Source");
                        return null;
                    }

                    // Extracted the way it is installed (it isn't added to this Instance's packages). The target verifies it when installed
                    if (!TrakHoundPackage.Install(bytes, packagesPath))
                    {
                        errors.Add($"{id} v{version} couldn't be extracted");
                        return null;
                    }

                    return TrakHoundPackage.ReadInformationFromPackage(bytes);
                }
            }
            catch (Exception ex)
            {
                errors.Add($"{id} v{version} : {ex.Message}");
                return null;
            }
        }

        private static bool IsSettingsFile(string profilePath, string file)
        {
            return string.Equals(Path.GetDirectoryName(file), profilePath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar), StringComparison.OrdinalIgnoreCase) &&
                   _settingsFilenames.Contains(Path.GetFileName(file), StringComparer.OrdinalIgnoreCase);
        }

        private static void CopyDirectory(string source, string destination, Func<string, bool> include)
        {
            Directory.CreateDirectory(destination);

            foreach (var file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (include != null && !include(file)) continue;

                var target = Path.Combine(destination, Path.GetRelativePath(source, file));
                Directory.CreateDirectory(Path.GetDirectoryName(target));
                File.Copy(file, target, true);
            }
        }

        private static string DescribeVersion(string version) => version?.StartsWith(TrakHoundPackage.WilcardVersion) == true ? "(latest)" : $"v{version}";

        #endregion
    }
}
