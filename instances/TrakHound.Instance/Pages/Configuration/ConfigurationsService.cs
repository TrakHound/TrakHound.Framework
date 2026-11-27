// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using TrakHound.Configurations;
using TrakHound.Instance.Configurations;
using TrakHound.Instance.Pages.Packages;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Packages;
using TrakHound.Security;

namespace TrakHound.Instance.Pages.Configuration
{
    public enum ConfigurationStatus
    {
        Unknown,
        Running,
        NotLoaded,
        PackageMissing
    }

    /// <summary>
    /// State and actions shared by the Configuration pages (one per browser session)
    /// </summary>
    public class ConfigurationsService
    {
        /// <summary>
        /// The pages of a Workspace are at "_admin/workspaces/{workspaceId}/..." (ex. "_admin/workspaces/site-b/configuration/api")
        /// </summary>
        public const string WorkspaceRoutePrefix = "_admin/workspaces/";

        // The Admin pages that can edit a Workspace (other pages, ex. Users or Packages, are only for the running Instance)
        private static readonly string[] _workspacePaths = new[] { "_admin/configuration" };

        // Configuration pages that are only for the running Instance (Security Profiles are edited through its Security Manager)
        private static readonly string[] _liveOnlyPaths = new[] { "_admin/configuration/security-profiles" };

        private readonly ITrakHoundInstance _instance;
        private readonly PackagesContext _packages;
        private readonly LiveConfigurationTarget _liveTarget;
        private readonly ConfigurationWorkspaces _workspaces;
        private readonly NavigationManager _navigationManager;
        private readonly AdminAccess _adminAccess;
        private Task _sourceTask;


        public ITrakHoundInstance Instance => _instance;

        /// <summary>
        /// The ID of the Workspace in the current URL (null for the running Instance's pages)
        /// </summary>
        public string WorkspaceId => GetWorkspaceId(_navigationManager.ToBaseRelativePath(_navigationManager.Uri));

        /// <summary>
        /// The current URL is for a Workspace that doesn't exist. The pages show that instead of their content, and <see cref="Target"/> can't be used
        /// </summary>
        public bool IsWorkspaceMissing => WorkspaceId != null && !_workspaces.Exists(WorkspaceId);

        /// <summary>
        /// The Configurations the pages edit : the Workspace in the URL, or the running Instance's Profile
        /// </summary>
        public IConfigurationTarget Target
        {
            get
            {
                var workspaceId = WorkspaceId;
                if (workspaceId == null) return _liveTarget;

                // Never fall back to the running Instance : a missing Workspace's pages must not edit it
                return _workspaces.Get(workspaceId) ?? throw new InvalidOperationException($"The Workspace '{workspaceId}' doesn't exist");
            }
        }

        public ITrakHoundConfigurationProfile ConfigurationProfile => Target.Profile;

        public TrakHoundPackageManager PackageManager => _instance?.PackageManager;

        /// <summary>
        /// The Package Source, Updates and Notifications (shared with the Packages page components)
        /// </summary>
        public PackagesContext Packages => _packages;

        public TrakHoundPackageClient PackageClient => _packages.PackageClient;

        public ConfigurationsService(
            ITrakHoundInstance instance,
            TrakHoundInstanceConfiguration instanceConfiguration,
            RemotesConfiguration remotesConfiguration,
            TrakHound.Instance.Logging.History.TrakHoundLogHistoryService logHistoryService,
            ConfigurationWorkspaces workspaces,
            NavigationManager navigationManager,
            AdminAccess adminAccess = null
            )
        {
            _adminAccess = adminAccess;
            _instance = instance;
            _liveTarget = new LiveConfigurationTarget(instance, instanceConfiguration, remotesConfiguration, logHistoryService);
            _workspaces = workspaces;
            _navigationManager = navigationManager;
            _packages = new PackagesContext(instance, remotesConfiguration);
        }


        #region "Links"

        /// <summary>
        /// Gets the Workspace ID from a base relative path (null if it isn't a Workspace page)
        /// </summary>
        public static string GetWorkspaceId(string relativePath)
        {
            if (relativePath == null || !relativePath.StartsWith(WorkspaceRoutePrefix, StringComparison.OrdinalIgnoreCase)) return null;

            var rest = relativePath.Substring(WorkspaceRoutePrefix.Length);
            var end = rest.IndexOfAny(new[] { '/', '?', '#' });
            var id = Uri.UnescapeDataString(end >= 0 ? rest.Substring(0, end) : rest);
            return !string.IsNullOrEmpty(id) ? id : null;
        }

        /// <summary>
        /// Gets the link to an Admin page (ex. "_admin/configuration/api") for the current target : the Workspace's page when a Workspace is open.
        /// Pages that can't edit a Workspace (ex. Users) are linked as they are
        /// </summary>
        public string GetLink(string path) => GetLink(path, WorkspaceId);

        public static string GetLink(string path, string workspaceId)
        {
            if (string.IsNullOrEmpty(path) || workspaceId == null) return path;

            var relative = path.TrimStart('/');
            bool IsUnder(string o) => relative.Equals(o, StringComparison.OrdinalIgnoreCase) || relative.StartsWith(o + "/", StringComparison.OrdinalIgnoreCase) || relative.StartsWith(o + "?", StringComparison.OrdinalIgnoreCase);
            if (!_workspacePaths.Any(IsUnder) || _liveOnlyPaths.Any(IsUnder)) return path;

            return $"{WorkspaceRoutePrefix}{Uri.EscapeDataString(workspaceId)}/{relative.Substring("_admin/".Length)}";
        }

        #endregion


        /// <summary>
        /// Select the Package Source (once per session) : the requested one (ex. ?source=dev), else the one last browsed, else the first that answers
        /// </summary>
        public Task InitializeSource(string requestedSourceId = null, string rememberedSourceId = null)
        {
            return _sourceTask ??= _packages.SelectInitialSource(requestedSourceId, rememberedSourceId);
        }


        #region "Packages"

        /// <summary>
        /// The installed Package the Configuration resolves to (the newest installed version for a wildcard)
        /// </summary>
        public TrakHoundPackage ResolvePackage(ConfigurationModel configuration)
        {
            if (configuration == null || string.IsNullOrEmpty(configuration.PackageId) || PackageManager == null) return null;

            return PackageManager.Resolve(configuration.PackageId, configuration.PackageVersion);
        }

        /// <summary>
        /// Make sure the Package version the Configuration uses is installed (downloading it from the selected Source if needed)
        /// </summary>
        /// <returns>Null if the Package is installed, otherwise the error</returns>
        public async Task<string> EnsurePackageInstalled(string packageId, string packageVersion)
        {
            if (string.IsNullOrEmpty(packageId)) return "No Package is selected";

            var useLatest = string.IsNullOrEmpty(packageVersion) || packageVersion.StartsWith(TrakHoundPackage.WilcardVersion);
            var installed = PackageManager.Resolve(packageId, packageVersion);
            if (installed != null) return null;

            var description = useLatest ? packageId : $"{packageId} v{packageVersion}";
            if (PackageClient == null) return $"{description} is not installed and no Package Source is selected to download it from";

            try
            {
                // Any other wildcard (ex. "*-*" to include Prereleases, or "1.*") is resolved by the Source, the same way the Instance selects it
                var packageBytes = string.IsNullOrEmpty(packageVersion) || packageVersion == TrakHoundPackage.WilcardVersion
                    ? await PackageClient.DownloadLatest(packageId)
                    : await PackageClient.Download(packageId, packageVersion);

                if (packageBytes == null) return $"{description} could not be downloaded from {_packages.SelectedSource?.Name ?? PackageClient.BaseUrl}";

                var results = (await _packages.Install(packageBytes, PackageClient.Origin))?.ToList();
                var failed = results?.Where(o => !o.Success).Select(o => string.IsNullOrEmpty(o.Message) ? $"{o.PackageId} {o.PackageVersion}".Trim() : o.Message).ToList();

                if (results.IsNullOrEmpty() || !results.Any(o => o.Success && o.PackageId == packageId))
                {
                    return $"{description} could not be installed{(failed.IsNullOrEmpty() ? "" : $" : {string.Join(", ", failed)}")}";
                }

                var installedVersions = results.Where(o => o.Success).Select(o => $"{o.PackageId} v{o.PackageVersion}");
                _packages.Notify(failed.IsNullOrEmpty() ? PackageNotificationType.Success : PackageNotificationType.Warning,
                    $"Installed {packageId}",
                    $"{string.Join(", ", installedVersions)}{(failed.IsNullOrEmpty() ? "" : $". Failed : {string.Join(", ", failed)}")}");

                return null;
            }
            catch (Exception ex)
            {
                return $"{description} could not be installed : {ex.Message}";
            }
        }

        /// <summary>
        /// Get the versions of a Package that can be selected (installed versions and versions available from the Source), newest first
        /// </summary>
        public async Task<IReadOnlyList<(string Version, bool Installed)>> GetPackageVersions(string packageId)
        {
            var versions = new Dictionary<string, bool>();

            foreach (var version in PackageManager.GetVersions(packageId) ?? Enumerable.Empty<string>()) versions[version] = true;

            if (PackageClient != null)
            {
                try
                {
                    var sourceVersions = await PackageClient.GetVersions(packageId);
                    if (sourceVersions != null)
                    {
                        foreach (var version in sourceVersions) versions.TryAdd(version, false);
                    }
                }
                catch { }
            }

            // Newest first in Semantic Versioning order, with Prereleases (ex. 0.3.4-dev.20261114153000) next to their Stable Version
            return versions.OrderByDescending(o => o.Key, TrakHound.Extensions.VersionComparer.Default).Select(o => (o.Key, o.Value)).ToList();
        }

        /// <summary>
        /// Returns true if no other Configuration uses the Package version
        /// </summary>
        public bool IsPackageUsedByOthers(TrakHoundPackage package, string configurationId)
        {
            if (package == null) return false;

            var usages = _instance.GetPackageUsage();
            return usages.Any(o => o.PackageUuid == package.Uuid && o.ConfigurationId != configurationId);
        }

        #endregion

        #region "Status"

        /// <summary>
        /// Get the IDs of the Configurations of the Category that are currently loaded by the Instance (null if unknown)
        /// </summary>
        public async Task<HashSet<string>> GetLoadedConfigurationIds(ConfigurationCategoryDefinition category)
        {
            // Only the running Instance loads its Configurations
            if (!Target.IsLive) return null;

            try
            {
                IEnumerable<string> ids = null;
                var client = _instance.ClientProvider?.GetClient();

                switch (category.ConfigurationCategory)
                {
                    case "app":
                        ids = (await client.Apps.GetInformation())?.Select(o => o.Id);
                        break;

                    case "api":
                        ids = (await client.Api.GetRouteInformation())?.Select(o => o.Id);
                        break;

                    case "function":
                        ids = (await client.Functions.GetInformation())?.Select(o => o.ConfigurationId);
                        break;

                    case "worker":
                        ids = (await client.Workers.GetInformation())?.Select(o => o.ConfigurationId);
                        break;

                    case "service":
                        ids = (await client.Services.Drivers.GetDrivers())?.Select(o => o.ConfigurationId);
                        break;

                    case "identity":
                        // Providers are registered by their ID (the Configuration ID, lowercase)
                        ids = (_instance.SecurityManager as TrakHoundSecurityManager)?.IdentityProviders?.SelectMany(o => new[] { o.Id, o.Configuration?.Id });
                        break;
                }

                return ids != null ? new HashSet<string>(ids.Where(o => o != null), StringComparer.OrdinalIgnoreCase) : null;
            }
            catch
            {
                return null;
            }
        }

        public ConfigurationStatus GetStatus(ConfigurationModel configuration, HashSet<string> loadedIds)
        {
            if (ResolvePackage(configuration) == null) return ConfigurationStatus.PackageMissing;
            if (loadedIds == null) return ConfigurationStatus.Unknown;
            return loadedIds.Contains(configuration.Id) ? ConfigurationStatus.Running : ConfigurationStatus.NotLoaded;
        }

        /// <summary>
        /// The Diagnostics Log page for the Configuration's Package (Loggers are named "{packageCategory}/{packageId}/...")
        /// </summary>
        public string GetLogLink(ConfigurationCategoryDefinition category, ConfigurationModel configuration)
        {
            if (string.IsNullOrEmpty(configuration?.PackageId)) return null;

            var pattern = Uri.EscapeDataString($"{category.PackageCategory}/{configuration.PackageId}/**");
            return $"_admin/explorer/{_instance.Id}/diagnostics/logs/view?pattern={pattern}";
        }

        #endregion

        #region "Save / Remove"

        // Every change the pages make to the target's Configurations goes through these, so each is checked against the signed in Admin's Roles

        public void Save(ConfigurationCategoryDefinition category, ConfigurationModel configuration)
        {
            Save(category.Build(configuration));
        }

        /// <summary>
        /// Saves a Configuration to the target. The Admin Resource is the one for its Category unless another is given (ex. a diagram's layout)
        /// </summary>
        public void Save(ITrakHoundConfiguration configuration, string resourceId = null)
        {
            if (resourceId != null || WorkspaceId != null)
            {
                DemandWrite(resourceId ?? TrakHoundAdminSecurity.GetConfigurationResource(configuration.Category));
            }
            else
            {
                // An Admin whose Role is limited to Zones can only save a Module that is in one of them, as it is now (so one can't be
                // taken from another Zone) and as it is being saved (so one can't be moved out of reach, or created outside)
                var liveResource = TrakHoundAdminSecurity.GetConfigurationResource(configuration.Category);
                if (ConfigurationProfile.Get<ITrakHoundConfiguration>(configuration.Category, configuration.Id) != null)
                {
                    TrakHoundAdminContext.Demand(liveResource, TrakHoundAdminSecurity.WritePermission, TrakHoundAdminTarget.Module(configuration.Category, configuration.Id));
                }

                TrakHoundAdminContext.Demand(liveResource, TrakHoundAdminSecurity.WritePermission, TrakHoundAdminTarget.Module(configuration.Category, configuration.Id, GetGroupId(configuration)));
            }

            ConfigurationProfile.Add(configuration, true);
        }

        private static string GetGroupId(ITrakHoundConfiguration configuration)
        {
            switch (configuration)
            {
                case TrakHound.Api.TrakHoundApiConfiguration api: return api.GroupId;
                case TrakHound.Apps.TrakHoundAppConfiguration app: return app.GroupId;
                case TrakHound.Functions.TrakHoundFunctionConfiguration function: return function.GroupId;
                case TrakHound.Workers.TrakHoundWorkerConfiguration worker: return worker.GroupId;
                default: return null;
            }
        }

        /// <summary>
        /// Whether the signed in Admin can view a Configuration (false for a Module outside the Zones their Role is limited to)
        /// </summary>
        public bool CanView(string configurationCategory, string configurationId)
        {
            if (WorkspaceId != null) return true;

            return _adminAccess == null || _adminAccess.Can(TrakHoundAdminSecurity.GetConfigurationResource(configurationCategory), TrakHoundAdminSecurity.ReadPermission, TrakHoundAdminTarget.Module(configurationCategory, configurationId));
        }

        public void Remove(ConfigurationCategoryDefinition category, string configurationId)
        {
            Remove(category.ConfigurationCategory, configurationId);
        }

        public void Remove(string configurationCategory, string configurationId)
        {
            if (WorkspaceId != null) DemandWrite(null);
            else TrakHoundAdminContext.Demand(TrakHoundAdminSecurity.GetConfigurationResource(configurationCategory), TrakHoundAdminSecurity.WritePermission, TrakHoundAdminTarget.Module(configurationCategory, configurationId));

            ConfigurationProfile.Remove(configurationCategory, configurationId);
        }

        /// <summary>
        /// Throws if the signed in Admin can't change the Resource. A Workspace's Configurations are part of Deployments, whichever they are
        /// </summary>
        public void DemandWrite(string resourceId)
        {
            TrakHoundAdminContext.DemandWrite(WorkspaceId != null ? TrakHoundAdminSecurity.Deployments : resourceId);
        }

        #endregion
    }
}
