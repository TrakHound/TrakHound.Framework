// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Configurations;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Packages;

namespace TrakHound.Instance.Pages.Packages
{
    public enum PackageSourceStatus
    {
        None,
        Checking,
        Online,
        Offline
    }

    public enum PackageNotificationType
    {
        Success,
        Warning,
        Error
    }

    /// <summary>
    /// What a Package Source last answered : shown for every Source, so the one to browse can be chosen without selecting each
    /// </summary>
    public class PackageSourceState
    {
        public PackageSourceStatus Status { get; set; }

        public int PackageCount { get; set; }

        public string Error { get; set; }
    }

    public class PackageNotification
    {
        public string Id { get; } = Guid.NewGuid().ToString();

        public DateTime Timestamp { get; } = DateTime.Now;

        public PackageNotificationType Type { get; set; }

        public string Title { get; set; }

        public string Message { get; set; }
    }


    /// <summary>
    /// State shared by the Packages page tabs : the selected Package Source, its latest Packages (for Update detection), counts and notifications
    /// </summary>
    public class PackagesContext
    {
        public static readonly string[] Categories = new[] { "bundle", "app", "api", "function", "worker", "service", "identity" };

        private readonly ITrakHoundInstance _instance;
        private readonly RemotesConfiguration _remotesConfiguration;
        private readonly object _lock = new object();
        private Dictionary<string, TrakHoundPackage> _sourcePackages = new Dictionary<string, TrakHoundPackage>(StringComparer.OrdinalIgnoreCase); // Package ID => Latest on the selected Source
        private Dictionary<string, TrakHoundPackage> _availablePackages = new Dictionary<string, TrakHoundPackage>(StringComparer.OrdinalIgnoreCase); // Package ID => What an Update is compared with (from the installed Package's origin)
        private Dictionary<string, TrakHoundPackageClient> _availableClients = new Dictionary<string, TrakHoundPackageClient>(); // Package UUID => Client for an available Package that isn't on the selected Source
        private int _sourceVersion;
        private HashSet<string> _sourcePrereleasePackageIds; // The Packages that the Source's Versions were selected with Prereleases for


        public ITrakHoundInstance Instance => _instance;

        public TrakHoundPackageManager PackageManager => _instance.PackageManager;

        public IEnumerable<RemoteConfiguration> Sources => _remotesConfiguration?.Remotes ?? Enumerable.Empty<RemoteConfiguration>();

        public RemoteConfiguration SelectedSource { get; private set; }

        public TrakHoundPackageClient PackageClient { get; private set; }

        public PackageSourceStatus SourceStatus { get; private set; }

        public string SourceError { get; private set; }

        public int SourcePackageCount { get { lock (_lock) return _sourcePackages.Count; } }

        public List<PackageNotification> Notifications { get; } = new List<PackageNotification>();

        /// <summary>
        /// Raised when the Source, installed Packages or Notifications change (the page re-renders)
        /// </summary>
        public event Action Changed;


        public PackagesContext(ITrakHoundInstance instance, RemotesConfiguration remotesConfiguration)
        {
            _instance = instance;
            _remotesConfiguration = remotesConfiguration;
        }


        #region "Source"

        public static TrakHoundPackageClient CreateClient(RemoteConfiguration source)
        {
            if (source == null || string.IsNullOrEmpty(source.BaseUrl)) return null;

            var client = new TrakHoundPackageClient(source.BaseUrl, source.Organization);

            // Sent as the API Token header for Sources that require one
            if (!string.IsNullOrEmpty(source.AccessTokenCode)) client.SetToken(source.AccessTokenCode);

            return client;
        }

        /// <summary>
        /// The Local Storage key of the Source the Admin last browsed (so the Packages and Configuration pages open on it)
        /// </summary>
        public const string SelectedSourceStorageKey = "trakhound.packages.source";

        // How long choosing the first reachable Source waits for the Sources to answer, so an unreachable one doesn't hold up the page
        private static readonly TimeSpan SourceCheckWait = TimeSpan.FromSeconds(5);

        private readonly Dictionary<string, PackageSourceState> _sourceStates = new Dictionary<string, PackageSourceState>(StringComparer.OrdinalIgnoreCase);
        private Task _sourceCheck;

        /// <summary>
        /// What a Source last answered (Status None if it hasn't been checked)
        /// </summary>
        public PackageSourceState GetSourceState(string sourceId)
        {
            lock (_lock) return sourceId != null && _sourceStates.TryGetValue(sourceId, out var state) ? state : new PackageSourceState();
        }

        /// <summary>
        /// Select a Source (by ID) and load its latest Packages. Without an ID (or one that isn't found), the first Source that answers
        /// is used, in the order they're listed (the first Source if none answers). Every Source is checked, so each one's status is shown
        /// </summary>
        public async Task SelectSource(string sourceId)
        {
            var source = Sources.FirstOrDefault(o => string.Equals(o.Id, sourceId, StringComparison.OrdinalIgnoreCase));

            var check = CheckSources();
            if (source == null)
            {
                await Task.WhenAny(check, Task.Delay(SourceCheckWait));

                lock (_lock)
                {
                    source = Sources.FirstOrDefault(o => _sourceStates.TryGetValue(o.Id ?? "", out var state) && state.Status == PackageSourceStatus.Online) ?? Sources.FirstOrDefault();
                }
            }

            SelectedSource = source;
            PackageClient = CreateClient(source);
            await RefreshSource();
        }

        /// <summary>
        /// Select the Source a link asked for (ex. ?source=dev), else the one the Admin last browsed, else the first Source that answers
        /// </summary>
        public Task SelectInitialSource(string requestedSourceId, string rememberedSourceId)
        {
            bool Exists(string id) => !string.IsNullOrEmpty(id) && Sources.Any(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase));

            return SelectSource(Exists(requestedSourceId) ? requestedSourceId : Exists(rememberedSourceId) ? rememberedSourceId : null);
        }

        /// <summary>
        /// Check every Source at the same time (the latest Packages of each), for the status and Package count shown for each one.
        /// Only one check runs at a time
        /// </summary>
        public Task CheckSources()
        {
            lock (_lock)
            {
                if (_sourceCheck == null || _sourceCheck.IsCompleted) _sourceCheck = CheckAllSources();
                return _sourceCheck;
            }
        }

        private async Task CheckAllSources()
        {
            var sources = Sources.Where(o => !string.IsNullOrEmpty(o.Id)).ToList();

            lock (_lock)
            {
                foreach (var source in sources) _sourceStates[source.Id] = new PackageSourceState { Status = PackageSourceStatus.Checking };
            }
            NotifyChanged();

            await Task.WhenAll(sources.Select(async source =>
            {
                var (packages, error) = await QueryLatest(CreateClient(source));
                lock (_lock)
                {
                    _sourceStates[source.Id] = new PackageSourceState
                    {
                        Status = packages != null ? PackageSourceStatus.Online : PackageSourceStatus.Offline,
                        PackageCount = packages?.Select(o => o.Id).Distinct(StringComparer.OrdinalIgnoreCase).Count() ?? 0,
                        Error = error
                    };
                }
                NotifyChanged();
            }));
        }

        private void SetSourceState(RemoteConfiguration source, PackageSourceStatus status, int packageCount, string error)
        {
            if (string.IsNullOrEmpty(source?.Id)) return;
            lock (_lock) _sourceStates[source.Id] = new PackageSourceState { Status = status, PackageCount = packageCount, Error = error };
        }

        /// <summary>
        /// Reload the latest Packages from the selected Source
        /// </summary>
        public async Task RefreshSource()
        {
            var version = Interlocked.Increment(ref _sourceVersion);

            if (PackageClient == null)
            {
                lock (_lock)
                {
                    _sourcePackages = new Dictionary<string, TrakHoundPackage>(StringComparer.OrdinalIgnoreCase);
                    _availablePackages = new Dictionary<string, TrakHoundPackage>(StringComparer.OrdinalIgnoreCase);
                    _availableClients = new Dictionary<string, TrakHoundPackageClient>();
                }
                SourceStatus = PackageSourceStatus.None;
                SourceError = null;
                NotifyChanged();
                return;
            }

            SourceStatus = PackageSourceStatus.Checking;
            SourceError = null;
            NotifyChanged();

            var prereleasePackageIds = GetPrereleasePackageIds(_instance);
            lock (_lock) _sourcePrereleasePackageIds = prereleasePackageIds;

            var (packages, error) = await QueryAvailable(PackageClient, PackageManager, prereleasePackageIds);
            var (availablePackages, availableClients) = await QueryOrigins(PackageClient, packages, Sources, PackageManager, prereleasePackageIds);

            // Ignore the result if another Source was selected meanwhile
            if (version != _sourceVersion) return;

            lock (_lock)
            {
                _sourcePackages = packages ?? new Dictionary<string, TrakHoundPackage>(StringComparer.OrdinalIgnoreCase);
                _availablePackages = availablePackages;
                _availableClients = availableClients;
            }

            SourceStatus = packages != null ? PackageSourceStatus.Online : PackageSourceStatus.Offline;
            SourceError = error;
            SetSourceState(SelectedSource, SourceStatus, SourcePackageCount, error);
            NotifyChanged();
        }

        /// <summary>
        /// Reload the Source if the Packages that a Configuration includes Prereleases for changed since it was loaded (ex. Include Prereleases
        /// was turned on), since their available Versions are selected differently
        /// </summary>
        public async Task RefreshSourceIfPrereleasesChanged()
        {
            HashSet<string> loaded;
            lock (_lock) loaded = _sourcePrereleasePackageIds;

            if (loaded == null || PackageClient == null) return;
            if (!loaded.SetEquals(GetPrereleasePackageIds(_instance))) await RefreshSource();
        }

        /// <summary>
        /// Query the latest Packages from a Source
        /// </summary>
        /// <returns>The Packages (null if the Source couldn't be reached) and an error description</returns>
        public static async Task<(IEnumerable<TrakHoundPackage> Packages, string Error)> QueryLatest(TrakHoundPackageClient client)
        {
            if (client == null) return (null, "No Source selected");
            if (string.IsNullOrEmpty(client.Organization)) return (null, "The Source has no Organization");

            try
            {
                var packages = await client.QueryLatestByOrganization();
                if (packages == null) return (null, $"No response from {client.BaseUrl}");
                return (packages.ToList(), null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        /// <summary>
        /// The Version of each Package that an Update is compared with, from a Source (null if the Source couldn't be reached) and an error description.
        /// A Source's latest list has one Version of each Package, which is the highest Stable Version on some Package Servers and the most recently
        /// built Version (Prereleases included) on others. So an installed Package gets the Version its Configurations select from the Source itself :
        /// the newest ("*-*") for a Package that a Configuration includes Prereleases for, and the highest Stable Version ("*") for one whose listed
        /// Version is a Prerelease. Other Packages keep the listed Version
        /// </summary>
        public static async Task<(Dictionary<string, TrakHoundPackage> Packages, string Error)> QueryAvailable(TrakHoundPackageClient client, TrakHoundPackageManager packageManager, HashSet<string> prereleasePackageIds)
        {
            var (packages, error) = await QueryLatest(client);
            if (packages == null) return (null, error);

            var result = new Dictionary<string, TrakHoundPackage>(StringComparer.OrdinalIgnoreCase);
            foreach (var package in packages)
            {
                if (string.IsNullOrEmpty(package?.Id)) continue;

                var existing = result.GetValueOrDefault(package.Id);
                // The Version "*" selects : the highest Stable Version, else the highest Prerelease
                if (existing == null || TrakHound.Extensions.VersionComparer.PreferStable.Compare(package.Version, existing.Version) > 0) result[package.Id] = package;
            }

            var installedIds = packageManager?.PackageIds?.ToHashSet(StringComparer.OrdinalIgnoreCase) ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var lookups = new List<(string Id, string Version)>();
            foreach (var package in result.Values)
            {
                if (!installedIds.Contains(package.Id)) continue;

                if (prereleasePackageIds?.Contains(package.Id) == true) lookups.Add((package.Id, TrakHoundPackage.WilcardVersion + TrakHoundPackage.PrereleaseWildcardSuffix));
                else if (!TrakHound.Extensions.VersionComparer.IsStable(package.Version)) lookups.Add((package.Id, TrakHoundPackage.WilcardVersion));
            }

            if (lookups.Count > 0)
            {
                var resolved = await Task.WhenAll(lookups.Select(async o =>
                {
                    try
                    {
                        return await client.Get(o.Id, o.Version);
                    }
                    catch
                    {
                        return null; // The listed Version is used (ex. a Package Server without Version wildcards)
                    }
                }));

                foreach (var package in resolved)
                {
                    if (!string.IsNullOrEmpty(package?.Id) && !string.IsNullOrEmpty(package.Version)) result[package.Id] = package;
                }
            }

            return (result, null);
        }

        /// <summary>
        /// What each installed Package's Updates are compared with. A Package downloaded from a Package Server is compared with the Server and
        /// Organization it came from (its origin), whichever Source is selected, so a Package with the same ID in another Organization isn't
        /// offered as its Update. An origin that can't be checked (its server isn't a Source any more, or it didn't answer) offers no Update.
        /// A Package without an origin (ex. uploaded) is compared with the selected Source, as before origins were recorded.
        /// Returns the Packages by ID, and the Client for each (by UUID) that isn't on the selected Source
        /// </summary>
        public static async Task<(Dictionary<string, TrakHoundPackage> Packages, Dictionary<string, TrakHoundPackageClient> Clients)> QueryOrigins(
            TrakHoundPackageClient selectedClient, Dictionary<string, TrakHoundPackage> selectedPackages, IEnumerable<RemoteConfiguration> sources,
            TrakHoundPackageManager packageManager, HashSet<string> prereleasePackageIds)
        {
            var available = new Dictionary<string, TrakHoundPackage>(StringComparer.OrdinalIgnoreCase);
            var clients = new Dictionary<string, TrakHoundPackageClient>();

            // Installed Packages by origin. Others (no origin, or from the selected Source) use the selected Source
            var otherOrigins = new Dictionary<string, (TrakHoundPackageOrigin Origin, List<string> PackageIds)>(StringComparer.OrdinalIgnoreCase);
            var otherPackageIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var packageId in packageManager?.PackageIds ?? Enumerable.Empty<string>())
            {
                var origin = GetInstalledOrigin(packageManager, packageId);
                if (IsFrom(origin, selectedClient)) continue;

                otherPackageIds.Add(packageId);
                var key = GetOriginKey(origin);
                if (!otherOrigins.TryGetValue(key, out var group)) otherOrigins[key] = group = (origin, new List<string>());
                group.PackageIds.Add(packageId);
            }

            if (selectedPackages != null)
            {
                foreach (var package in selectedPackages.Values)
                {
                    if (!otherPackageIds.Contains(package.Id)) available[package.Id] = package;
                }
            }

            await Task.WhenAll(otherOrigins.Values.Select(async group =>
            {
                var client = CreateOriginClient(sources, group.Origin);
                if (client == null) return;

                var (packages, _) = await QueryAvailable(client, packageManager, prereleasePackageIds);
                if (packages == null) return;

                lock (available)
                {
                    foreach (var packageId in group.PackageIds)
                    {
                        var package = packages.GetValueOrDefault(packageId);
                        if (package == null) continue;

                        available[packageId] = package;
                        if (!string.IsNullOrEmpty(package.Uuid)) clients[package.Uuid] = client;
                    }
                }
            }));

            return (available, clients);
        }

        /// <summary>
        /// Where the newest installed Version of a Package was downloaded from (null if unknown, ex. uploaded)
        /// </summary>
        public static TrakHoundPackageOrigin GetInstalledOrigin(TrakHoundPackageManager packageManager, string packageId)
        {
            var installed = packageManager?.Resolve(packageId, TrakHoundPackage.WilcardVersion + TrakHoundPackage.PrereleaseWildcardSuffix);
            return installed != null ? packageManager.GetOrigin(installed.Id, installed.Version) : null;
        }

        /// <summary>
        /// Whether a Package with the origin can be updated from the Client : the origin is the Client's server and Organization, or isn't known
        /// </summary>
        public static bool IsFrom(TrakHoundPackageOrigin origin, TrakHoundPackageClient client)
        {
            return origin == null || (client != null && origin.Matches(client.BaseUrl, client.Organization));
        }

        public static string GetOriginKey(TrakHoundPackageOrigin origin) => $"{origin?.Server?.TrimEnd('/')}|{origin?.Organization}";

        /// <summary>
        /// A Client for an origin : the Source with that server and Organization, else another Organization of a Source with that server (with its
        /// Access Token). Null if no Source is on that server any more, so a server that was removed isn't contacted
        /// </summary>
        public static TrakHoundPackageClient CreateOriginClient(IEnumerable<RemoteConfiguration> sources, TrakHoundPackageOrigin origin)
        {
            if (origin == null || sources == null) return null;

            var source = sources.FirstOrDefault(o => origin.Matches(o.BaseUrl, o.Organization));
            if (source != null) return CreateClient(source);

            source = sources.FirstOrDefault(o => TrakHoundPackageOrigin.IsSameServer(o.BaseUrl, origin.Server));
            return source != null ? CreateClient(source)?.ForOrganization(origin.Organization) : null;
        }

        /// <summary>
        /// The Client for a Package listed as available from another origin than the selected Source (null if it's from the selected Source)
        /// </summary>
        public TrakHoundPackageClient GetAvailableClient(TrakHoundPackage package)
        {
            if (string.IsNullOrEmpty(package?.Uuid)) return null;
            lock (_lock) return _availableClients.GetValueOrDefault(package.Uuid);
        }

        /// <summary>
        /// The Version of the Package that an Update is compared with (null if not available) : from the installed Package's origin, else the selected Source
        /// </summary>
        public TrakHoundPackage GetSourceLatest(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return null;
            lock (_lock) return _availablePackages.GetValueOrDefault(packageId);
        }

        /// <summary>
        /// Returns the Source's newer version of the Package (null if the installed version is the latest). A Prerelease is only an update
        /// when a Configuration of the Package includes Prereleases (ex. "*-*")
        /// </summary>
        public TrakHoundPackage GetUpdate(string packageId)
        {
            return GetUpdate(packageId, IncludesPrereleases(_instance, packageId));
        }

        private TrakHoundPackage GetUpdate(string packageId, bool includePrereleases)
        {
            return GetUpdate(GetSourceLatest(packageId), GetInstalledLatest(PackageManager, packageId, includePrereleases), includePrereleases);
        }

        /// <summary>
        /// Returns the available Package if it is newer than the installed Package (null otherwise). Without includePrereleases, a Prerelease
        /// is only an update over another Prerelease (or an older one), the same way "*" only selects one without a Stable Version
        /// </summary>
        public static TrakHoundPackage GetUpdate(TrakHoundPackage available, TrakHoundPackage installed, bool includePrereleases = false)
        {
            if (available == null || installed == null) return null;

            var comparer = includePrereleases ? TrakHound.Extensions.VersionComparer.Default : TrakHound.Extensions.VersionComparer.PreferStable;
            var comparison = comparer.Compare(available.Version, installed.Version);
            if (comparison > 0) return available;

            // Same version republished with a newer build
            if (comparison == 0 && string.Equals(available.Version, installed.Version, StringComparison.OrdinalIgnoreCase) && available.BuildDate.ToUniversalTime() > installed.BuildDate.ToUniversalTime()) return available;

            return null;
        }

        /// <summary>
        /// The installed Version an update is compared with : the one "*" selects, or with includePrereleases the newest (the one "*-*" selects)
        /// </summary>
        public static TrakHoundPackage GetInstalledLatest(TrakHoundPackageManager packageManager, string packageId, bool includePrereleases)
        {
            if (packageManager == null || string.IsNullOrEmpty(packageId)) return null;

            return includePrereleases
                ? packageManager.Resolve(packageId, TrakHoundPackage.WilcardVersion + TrakHoundPackage.PrereleaseWildcardSuffix)
                : packageManager.GetLatest(packageId);
        }

        /// <summary>
        /// Whether a requested Version includes Prereleases (ex. "*-*" or "1.*-*")
        /// </summary>
        public static bool IncludesPrereleases(string requestedVersion)
        {
            return !string.IsNullOrEmpty(requestedVersion) && requestedVersion.EndsWith(TrakHoundPackage.PrereleaseWildcardSuffix);
        }

        /// <summary>
        /// Whether a Configuration of the Package includes Prereleases (its Package Version ends with "-*")
        /// </summary>
        public static bool IncludesPrereleases(ITrakHoundInstance instance, string packageId)
        {
            return !string.IsNullOrEmpty(packageId) && GetPrereleasePackageIds(instance).Contains(packageId);
        }

        /// <summary>
        /// The Packages that a Configuration includes Prereleases for (its Package Version ends with "-*")
        /// </summary>
        public static HashSet<string> GetPrereleasePackageIds(ITrakHoundInstance instance)
        {
            var usages = instance?.GetPackageUsage();
            return usages != null
                ? usages.Where(o => IncludesPrereleases(o.RequestedVersion)).Select(o => o.PackageId).ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        }

        #endregion

        #region "Counts"

        public int InstalledCount => PackageManager?.PackageIds?.Count() ?? 0;

        public int UpdateCount
        {
            get
            {
                // The Configurations are read once for every Package
                var prereleasePackageIds = GetPrereleasePackageIds(_instance);
                return PackageManager?.PackageIds?.Count(o => GetUpdate(o, prereleasePackageIds.Contains(o)) != null) ?? 0;
            }
        }

        #endregion

        #region "Install"

        /// <summary>
        /// Whether the signed in Admin can uninstall Packages
        /// </summary>
        public bool CanUninstall => TrakHoundAdminContext.IsAllowed(TrakHoundAdminSecurity.Packages, TrakHoundAdminSecurity.WritePermission);

        /// <summary>
        /// Throws if the signed in Admin can't install the Package. Installing needs "write" on Packages, and on the Resource for the
        /// Package's Category (ex. Service Configurations for a Service Package), since an installed Package gets a default Configuration
        /// </summary>
        public static void DemandInstall(byte[] packageBytes)
        {
            TrakHoundAdminContext.DemandWrite(TrakHoundAdminSecurity.Packages);
            if (TrakHoundAdminContext.Current == null) return;

            string category = null;
            try
            {
                category = TrakHoundPackage.ReadInformationFromPackage(packageBytes)?.Category;
            }
            catch { }

            // A Package that can't be read isn't installed anyway
            var resourceId = TrakHoundAdminSecurity.GetPackageResource(category);
            if (resourceId != null) TrakHoundAdminContext.DemandWrite(resourceId);
        }

        /// <summary>
        /// Install a Package (with its Dependencies, downloaded from the selected Source, or from packageClient for a Package found in
        /// another Organization). The origin is where the Package file was downloaded from (null for an uploaded file), so that its Updates
        /// are checked there. Every install the pages make goes through this
        /// </summary>
        public async Task<IEnumerable<TrakHoundPackageInstallResult>> Install(byte[] packageBytes, TrakHoundPackageOrigin origin, TrakHoundPackageClient packageClient = null)
        {
            DemandInstall(packageBytes);
            return await PackageManager.Install(packageBytes, packageClient: packageClient ?? PackageClient, origin: origin);
        }

        public bool Uninstall(string packageId, string packageVersion)
        {
            TrakHoundAdminContext.DemandWrite(TrakHoundAdminSecurity.Packages);
            return PackageManager.Uninstall(packageId, packageVersion);
        }

        // A Dependency is only known once it is downloaded. One in a Category the signed in Admin can't configure is installed (the Package
        // needs it) but isn't given a default Configuration, so it doesn't start running
        private static bool CanConfigure(TrakHoundPackage package)
        {
            var resourceId = TrakHoundAdminSecurity.GetPackageResource(package?.Category);
            return resourceId == null || TrakHoundAdminContext.IsAllowed(resourceId, TrakHoundAdminSecurity.WritePermission);
        }

        /// <summary>
        /// Download a Package from the selected Source and install it (with its Dependencies), adding a default Configuration if it doesn't have one.
        /// A Package found in another Organization of the Source's server is downloaded with packageClient (that Organization), and so are its Dependencies.
        /// An Update found at an installed Package's origin (see GetUpdate) is downloaded from there
        /// </summary>
        public async Task<bool> InstallFromSource(TrakHoundPackage package, TrakHoundPackageClient packageClient = null)
        {
            if (package == null) return false;

            var client = packageClient ?? GetAvailableClient(package) ?? PackageClient;
            if (client == null)
            {
                Notify(PackageNotificationType.Error, $"Install {package.Id} Failed", "No Package Source is selected");
                return false;
            }

            try
            {
                var packageBytes = await client.Download(package.Id, package.Version);
                if (packageBytes == null)
                {
                    var from = client.Organization != PackageClient?.Organization ? $"{client.Organization} on {SelectedSource?.Name ?? client.BaseUrl}" : SelectedSource?.Name ?? client.BaseUrl;
                    Notify(PackageNotificationType.Error, $"Install {package.Id} Failed", $"Could not download v{package.Version} from {from}");
                    return false;
                }

                var results = await Install(packageBytes, client.Origin, client);
                return await ReportInstallResults(results, package.Id, package.Version);
            }
            catch (Exception ex)
            {
                Notify(PackageNotificationType.Error, $"Install {package.Id} Failed", ex.Message);
                return false;
            }
        }

        /// <summary>
        /// Add default Configurations for installed Packages and report the results
        /// </summary>
        public async Task<bool> ReportInstallResults(IEnumerable<TrakHoundPackageInstallResult> results, string packageId, string packageVersion)
        {
            var success = false;

            if (results.IsNullOrEmpty())
            {
                Notify(PackageNotificationType.Error, $"Install {packageId} Failed", "The Package could not be installed (invalid or unreadable package)");
                return false;
            }

            var installed = new List<string>();
            var failed = new List<string>();

            foreach (var result in results)
            {
                if (result.Success)
                {
                    var installedPackage = PackageManager.Get(result.PackageId, result.PackageVersion);
                    if (installedPackage != null && CanConfigure(installedPackage)) await _instance.InstallPackage(installedPackage, false);

                    installed.Add($"{result.PackageId} v{result.PackageVersion}");
                    if (result.PackageId == packageId) success = true;
                }
                else
                {
                    failed.Add(string.IsNullOrEmpty(result.Message) ? $"{result.PackageId ?? packageId} {result.PackageVersion}".Trim() : result.Message);
                }
            }

            if (failed.Count > 0)
            {
                Notify(success ? PackageNotificationType.Warning : PackageNotificationType.Error,
                    success ? $"{packageId} Installed with Errors" : $"Install {packageId} Failed",
                    $"{(installed.Count > 0 ? $"Installed : {string.Join(", ", installed)}. " : "")}Failed : {string.Join(", ", failed)}");
            }
            else
            {
                var dependencies = installed.Count > 1 ? $" (and {installed.Count - 1} {(installed.Count == 2 ? "dependency" : "dependencies")})" : "";
                Notify(PackageNotificationType.Success, $"{packageId} Installed", $"v{packageVersion ?? results.First().PackageVersion}{dependencies}. A default Configuration is added if it doesn't already have one, and Configurations without a Volume ID get the Package's default.");
            }

            NotifyChanged();

            // The installed Packages' origins changed, so what their Updates are compared with is read again
            if (installed.Count > 0) _ = RefreshAvailable();

            return success;
        }

        /// <summary>
        /// Read again what the installed Packages' Updates are compared with (their origins changed), keeping the selected Source's Packages
        /// </summary>
        private async Task RefreshAvailable()
        {
            try
            {
                var version = _sourceVersion;
                var client = PackageClient;
                var prereleasePackageIds = GetPrereleasePackageIds(_instance);

                Dictionary<string, TrakHoundPackage> sourcePackages;
                lock (_lock) sourcePackages = _sourcePackages;

                var (availablePackages, availableClients) = await QueryOrigins(client, sourcePackages, Sources, PackageManager, prereleasePackageIds);
                if (version != _sourceVersion) return; // A newer refresh applies its own

                lock (_lock)
                {
                    _availablePackages = availablePackages;
                    _availableClients = availableClients;
                }
                NotifyChanged();
            }
            catch { } // Shown again on the next refresh
        }

        #endregion

        #region "Notifications"

        public void Notify(PackageNotificationType type, string title, string message = null)
        {
            var notification = new PackageNotification { Type = type, Title = title, Message = message };
            lock (Notifications) Notifications.Add(notification);
            NotifyChanged();

            // Success notifications close automatically (Warnings and Errors stay until closed)
            if (type == PackageNotificationType.Success)
            {
                _ = Task.Delay(6000).ContinueWith(_ => Dismiss(notification.Id));
            }
        }

        public void Dismiss(string notificationId)
        {
            lock (Notifications) Notifications.RemoveAll(o => o.Id == notificationId);
            NotifyChanged();
        }

        public void NotifyChanged() => Changed?.Invoke();

        #endregion


        public static string GetDescription(TrakHoundPackage package)
        {
            if (package?.Metadata == null) return null;
            var entry = package.Metadata.FirstOrDefault(o => string.Equals(o.Key, TrakHoundPackage.DescriptionName, StringComparison.OrdinalIgnoreCase));
            return entry.Value?.ToString();
        }

        public static string GetPublisher(TrakHoundPackage package)
        {
            if (package?.Metadata == null) return null;
            var entry = package.Metadata.FirstOrDefault(o => string.Equals(o.Key, TrakHoundPackage.PublisherName, StringComparison.OrdinalIgnoreCase));
            return entry.Value?.ToString();
        }

        /// <summary>
        /// The address of the Package's icon (null if it doesn't have one)
        /// </summary>
        public static string GetImage(TrakHoundPackage package)
        {
            if (package?.Metadata == null) return null;
            var entry = package.Metadata.FirstOrDefault(o => string.Equals(o.Key, TrakHoundPackage.ImageName, StringComparison.OrdinalIgnoreCase));
            return entry.Value?.ToString();
        }

        public static string FormatSize(long bytes)
        {
            if (bytes >= 1073741824) return $"{bytes / 1073741824.0:0.00} GB";
            if (bytes >= 1048576) return $"{bytes / 1048576.0:0.0} MB";
            if (bytes >= 1024) return $"{bytes / 1024.0:0.0} KB";
            return $"{bytes} B";
        }
    }
}
