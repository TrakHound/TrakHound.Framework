// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Timers;
using TrakHound.Extensions;

namespace TrakHound.Packages
{
    /// <summary>
    /// Manages the Packages installed in a directory. Tracks installed Packages (watching for package.json changes),
    /// installs and uninstalls Packages, and checks for updates through a Package Client when one is set
    /// </summary>
    public class TrakHoundPackageManager : IDisposable
    {
        private const int _fileWatcherBufferSize = 8192 * 100; // 8192 (8 kb) is default
        private const int _notifyInterval = 2000;
        private const int _updateInterval = 10000;
        private const int _maxConcurrentChecks = 8;

        private readonly string _path;
        private readonly TrakHoundPackageClient _client;
        private readonly List<TrakHoundPackagesFileItem> _packageFileItems = new List<TrakHoundPackagesFileItem>();
        private readonly Dictionary<string, TrakHoundPackageUpdateConfiguration> _updateConfigurations = new Dictionary<string, TrakHoundPackageUpdateConfiguration>(); // Sender => UpdateConfiguration
        private readonly Dictionary<string, TrakHoundPackage> _installedPackages = new Dictionary<string, TrakHoundPackage>(); // PackageUuid => Package
        private readonly ListDictionary<string, string> _installedPackageUuidsByCategory = new ListDictionary<string, string>(); // Category => PackageUuid
        private readonly ListDictionary<string, string> _installedPackageIdsByCategory = new ListDictionary<string, string>(); // Category => PackageId
        private readonly ListDictionary<string, string> _installedPackageUuidsById = new ListDictionary<string, string>(); // PackageId => PackageUuid
        private readonly ListDictionary<string, string> _installedPackageVersionsById = new ListDictionary<string, string>(); // PackageId => PackageVersion
        private readonly Dictionary<string, string> _latestPackages = new Dictionary<string, string>(); // PackageId => PackageUuid
        private readonly Dictionary<string, TrakHoundPackage> _addedPackages = new Dictionary<string, TrakHoundPackage>(); // PackageUuid => Package
        private readonly Dictionary<string, TrakHoundPackage> _refusedPackages = new Dictionary<string, TrakHoundPackage>(); // PackageUuid => Package
        private readonly Dictionary<string, TrakHoundPackageVerificationResult> _verificationResults = new Dictionary<string, TrakHoundPackageVerificationResult>(); // PackageUuid => Result
        private readonly HashSet<string> _alreadyVerified = new HashSet<string>(); // PackageUuid (so that a Package isn't checked again when it is added)
        private readonly object _lock = new object();
        private readonly DelayEvent _notifyDelay = new DelayEvent(_notifyInterval);
        private Timer _updateTimer;
        private FileSystemWatcher _fileSystemWatcher;
        private FileSystemWatcher _directoryWatcher;


        private class DirectoryChecks
        {
            public ConcurrentDictionary<TrakHoundPackage, TrakHoundPackageVerificationResult> Results { get; } = new ConcurrentDictionary<TrakHoundPackage, TrakHoundPackageVerificationResult>();

            public string TrustedPublishersError { get; set; }
        }


        /// <summary>
        /// Root directory that Packages are installed to (null to use the default Package directory)
        /// </summary>
        public string Path => _path;

        /// <summary>
        /// The installed Packages (every Version)
        /// </summary>
        public IEnumerable<TrakHoundPackage> Packages
        {
            get { lock (_lock) return _installedPackages.Values.ToList(); }
        }

        /// <summary>
        /// The distinct IDs of the installed Packages
        /// </summary>
        public IEnumerable<string> PackageIds
        {
            get { lock (_lock) return _installedPackages.Values.Select(o => o.Id).Distinct().ToList(); }
        }

        /// <summary>
        /// Whether Packages are checked when they're installed or loaded, and whether a Package that fails is used
        /// </summary>
        public TrakHoundPackageVerificationMode VerificationMode { get; set; }

        /// <summary>
        /// Reads the Trusted Publishers. It's called for each check so that changes apply without a restart
        /// </summary>
        public Func<TrakHoundTrustedPublishers> TrustedPublishersProvider { get; set; }

        /// <summary>
        /// Returns the attestation lists of the TrakHound Package Managers, for the RequireApproval Mode. If null (or it throws), every Package is refused under RequireApproval
        /// </summary>
        public Func<TrakHoundPackageApprovals> ApprovalsProvider { get; set; }

        /// <summary>
        /// Reads the attestation lists again before a Package is installed under the RequireApproval Mode. It shouldn't throw : the last lists are used if it fails
        /// </summary>
        public Func<System.Threading.CancellationToken, Task> ApprovalsRefresh { get; set; }

        /// <summary>
        /// Returns the UUIDs of the Package versions a Configuration uses, for <see cref="TrakHoundPackageVerificationInformation.InUse"/>. Not called while the Manager is locked
        /// </summary>
        public Func<IEnumerable<string>> InUsePackagesProvider { get; set; }

        /// <summary>
        /// The Packages on disk that were refused (see <see cref="VerificationMode"/>). They aren't in <see cref="Packages"/> and can't be used, but can be uninstalled
        /// </summary>
        public IEnumerable<TrakHoundPackage> RefusedPackages
        {
            get { lock (_lock) return _refusedPackages.Values.ToList(); }
        }

        /// <summary>
        /// Raised each time a Package is checked
        /// </summary>
        public event EventHandler<TrakHoundPackageVerifiedArgs> PackageVerified;


        /// <summary>
        /// Raised when a Package version is installed or loaded (raised in batches after a short delay)
        /// </summary>
        public event EventHandler<TrakHoundPackage> PackageAdded;

        /// <summary>
        /// Raised when a Package version is uninstalled or its package.json is deleted
        /// </summary>
        public event EventHandler<TrakHoundPackageChangedArgs> PackageRemoved;

        /// <summary>
        /// Raised on the caller's thread as soon as a Package version is installed by this Manager (unlike PackageAdded, not for Packages that are only loaded)
        /// </summary>
        public event EventHandler<TrakHoundPackageChangedArgs> PackageInstalled;

        /// <summary>
        /// Raised on the caller's thread as soon as a Package version is uninstalled by this Manager
        /// </summary>
        public event EventHandler<TrakHoundPackageChangedArgs> PackageUninstalled;


        /// <summary>
        /// Create a Manager for the default Package directory
        /// </summary>
        public TrakHoundPackageManager()
        {
            Init();
        }

        /// <summary>
        /// Create a Manager for the specified Package directory
        /// </summary>
        public TrakHoundPackageManager(string path)
        {
            _path = path;

            Init();
        }

        /// <summary>
        /// Create a Manager for the default Package directory that downloads Packages from the specified Package Server (the Organization is not used)
        /// </summary>
        public TrakHoundPackageManager(string organization, string packageServer)
        {
            _client = new TrakHoundPackageClient(packageServer);

            Init();
        }

        /// <summary>
        /// Create a Manager for the specified Package directory that downloads Packages from the specified Package Server (the Organization is not used)
        /// </summary>
        public TrakHoundPackageManager(string organization, string packageServer, string path)
        {
            _client = new TrakHoundPackageClient(packageServer);
            _path = path;

            Init();
        }

        /// <summary>
        /// Create a Manager for the default Package directory that downloads Packages using the Package Client (the Organization is not used)
        /// </summary>
        public TrakHoundPackageManager(string organization, TrakHoundPackageClient packageClient)
        {
            _client = packageClient;

            Init();
        }

        /// <summary>
        /// Create a Manager for the specified Package directory that downloads Packages using the Package Client (the Organization is not used)
        /// </summary>
        public TrakHoundPackageManager(string organization, TrakHoundPackageClient packageClient, string path)
        {
            _client = packageClient;
            _path = path;

            Init();
        }

        /// <summary>
        /// Create the Package directory and start watching it for changes (and start the update timer when a Package Client is set). Called by the constructors
        /// </summary>
        public void Init()
        {
            _notifyDelay.Elapsed += NotifyDelayElapsed;

            if (_client != null)
            {
                _updateTimer = new Timer();
                _updateTimer.Interval = _updateInterval;
                _updateTimer.Elapsed += UpdateTimerElapsed;
                _updateTimer.Enabled = true;
            }

            var dir = TrakHoundPackage.GetDirectory(_path);
            try
            {
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            }
            catch { }

            _fileSystemWatcher = new FileSystemWatcher(dir, TrakHoundPackage.InformationFilename);
            _fileSystemWatcher.IncludeSubdirectories = true;
            _fileSystemWatcher.InternalBufferSize = _fileWatcherBufferSize;
            _fileSystemWatcher.NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime | NotifyFilters.Attributes;
            _fileSystemWatcher.Created += FileSystemWatcherCreated;
            _fileSystemWatcher.Changed += FileSystemWatcherCreated;
            _fileSystemWatcher.Renamed += FileSystemWatcherDeleted;
            _fileSystemWatcher.Deleted += FileSystemWatcherDeleted;
            _fileSystemWatcher.Error += FileSystemWatcherError;
            _fileSystemWatcher.EnableRaisingEvents = true;

            // An install moves the whole Version folder in (from Staging), often into a Package folder created just before. On Linux the watcher only
            // watches a new folder once it sees it, so a Package Information File moved in with it isn't reported. New folders are searched instead
            _directoryWatcher = new FileSystemWatcher(dir);
            _directoryWatcher.IncludeSubdirectories = true;
            _directoryWatcher.InternalBufferSize = _fileWatcherBufferSize;
            _directoryWatcher.NotifyFilter = NotifyFilters.DirectoryName;
            _directoryWatcher.Created += DirectoryWatcherCreated;
            _directoryWatcher.Renamed += DirectoryWatcherCreated;
            _directoryWatcher.Error += FileSystemWatcherError;
            _directoryWatcher.EnableRaisingEvents = true;
        }

        /// <summary>
        /// Stop watching for changes and checking for updates
        /// </summary>
        public void Dispose()
        {
            if (_notifyDelay != null)
            {
                _notifyDelay.Elapsed -= NotifyDelayElapsed;
                _notifyDelay.Dispose();
            }

            if (_updateTimer != null)
            {
                _updateTimer.Elapsed -= UpdateTimerElapsed;
                _updateTimer.Dispose();
            }

            if (_fileSystemWatcher != null)
            {
                _fileSystemWatcher.Created -= FileSystemWatcherCreated;
                _fileSystemWatcher.Changed -= FileSystemWatcherCreated;
                _fileSystemWatcher.Renamed -= FileSystemWatcherDeleted;
                _fileSystemWatcher.Deleted -= FileSystemWatcherDeleted;
                _fileSystemWatcher.Error -= FileSystemWatcherError;
                _fileSystemWatcher.Dispose();
            }

            if (_directoryWatcher != null)
            {
                _directoryWatcher.Created -= DirectoryWatcherCreated;
                _directoryWatcher.Renamed -= DirectoryWatcherCreated;
                _directoryWatcher.Error -= FileSystemWatcherError;
                _directoryWatcher.Dispose();
            }
        }


        /// <summary>
        /// Read the installed Packages from the Package directory and update the packages file
        /// </summary>
        public void Load()
        {
            //_notifyDelay.Enabled = false;
            if (_updateTimer != null) _updateTimer.Enabled = false;
            _fileSystemWatcher.EnableRaisingEvents = false;
            if (_directoryWatcher != null) _directoryWatcher.EnableRaisingEvents = false;

            _packageFileItems.Clear();
            lock (_lock)
            {
                _installedPackages.Clear();
                _refusedPackages.Clear();
                _verificationResults.Clear();
                _alreadyVerified.Clear();
            }

            // Read "trakhound-packages.json"
            var packagesFile = TrakHoundPackagesFile.Read(_path);
            if (packagesFile != null)
            {
                if (!packagesFile.Packages.IsNullOrEmpty())
                {
                    _packageFileItems.AddRange(packagesFile.Packages);
                }
            }

            // Read Installed Packages
            var installedPackages = TrakHoundPackage.GetInstalled(_path)?.ToList();
            if (!installedPackages.IsNullOrEmpty())
            {
                var checks = VerificationMode != TrakHoundPackageVerificationMode.Off ? CheckDirectories(installedPackages) : null;

                foreach (var installedPackage in installedPackages)
                {
                    if (!VerifyInstalled(installedPackage, false, checks)) continue;

                    AddInstalled(installedPackage);

                    lock (_lock)
                    {
                        _alreadyVerified.Add(installedPackage.Uuid);

                        if (!_addedPackages.ContainsKey(installedPackage.Uuid))
                        {
                            _addedPackages.Add(installedPackage.Uuid, installedPackage);
                        }
                    }
                }

                UpdatePackagesFile();

                _notifyDelay.Set();
            }

            //_notifyDelay.Enabled = true;
            if (_updateTimer != null) _updateTimer.Enabled = true;
            _fileSystemWatcher.EnableRaisingEvents = true;
            if (_directoryWatcher != null) _directoryWatcher.EnableRaisingEvents = true;
        }


        #region "Verification"

        /// <summary>
        /// Get the result of the last check of a Package. Returns null if it hasn't been checked
        /// </summary>
        public TrakHoundPackageVerificationResult GetVerificationResult(string packageId, string packageVersion)
        {
            var packageUuid = TrakHoundPackage.GenerateUuid(packageId, packageVersion);
            if (packageUuid == null) return null;

            lock (_lock) return _verificationResults.GetValueOrDefault(packageUuid);
        }

        /// <summary>
        /// Get the Verification Mode in use and the result of the last check of each Package on disk, including those that were refused
        /// </summary>
        public TrakHoundPackageVerificationReport GetVerificationReport()
        {
            // Read before locking, since the provider resolves Versions through this Manager
            HashSet<string> inUse = null;
            try
            {
                var inUseUuids = InUsePackagesProvider?.Invoke();
                if (inUseUuids != null) inUse = new HashSet<string>(inUseUuids.Where(o => o != null));
            }
            catch { } // Reported as not in use

            lock (_lock)
            {
                var packages = _installedPackages.Values.Select(o => TrakHoundPackageVerificationPolicy.CreateInformation(o, _verificationResults.GetValueOrDefault(o.Uuid), false, GetInstalledTime(o), inUse?.Contains(o.Uuid) == true))
                    .Concat(_refusedPackages.Values.Select(o => TrakHoundPackageVerificationPolicy.CreateInformation(o, _verificationResults.GetValueOrDefault(o.Uuid), true, GetInstalledTime(o), inUse?.Contains(o.Uuid) == true)))
                    .OrderBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(o => o.Version, VersionComparer.Default)
                    .ToList();

                return new TrakHoundPackageVerificationReport { Mode = VerificationMode, Packages = packages };
            }
        }

        /// <summary>
        /// When a Package version was installed. The Manager doesn't record it, so it's when its directory was created (an install creates it, and replacing a Version creates it again)
        /// </summary>
        private DateTime? GetInstalledTime(TrakHoundPackage package)
        {
            try
            {
                var directory = !string.IsNullOrEmpty(package.Location) ? package.Location : TrakHoundPackage.GetPackageVersionDirectory(_path, package.Category, package.Id, package.Version);
                if (!string.IsNullOrEmpty(directory) && Directory.Exists(directory)) return Directory.GetCreationTimeUtc(directory);
            }
            catch { }

            return null;
        }

        /// <summary>
        /// Check a Package on disk now, including when the Mode is Off, and apply the result. A Package that fails under Require stops being used, and a refused Package that now passes is used again
        /// </summary>
        /// <returns>The result, or null if the Package isn't installed</returns>
        public TrakHoundPackageVerificationResult Verify(string packageId, string packageVersion)
        {
            var packageUuid = TrakHoundPackage.GenerateUuid(packageId, packageVersion);
            if (packageUuid == null) return null;

            TrakHoundPackage package;
            lock (_lock) package = _installedPackages.GetValueOrDefault(packageUuid) ?? _refusedPackages.GetValueOrDefault(packageUuid);
            if (package == null) return null;

            VerifyNow(package);

            return GetVerificationResult(packageId, packageVersion);
        }

        /// <summary>
        /// Check every Package on disk now (see <see cref="Verify(string, string)"/>), ex. after the Trusted Publishers change
        /// </summary>
        public void VerifyAll()
        {
            List<TrakHoundPackage> packages;
            lock (_lock) packages = _installedPackages.Values.Concat(_refusedPackages.Values).ToList();

            var checks = CheckDirectories(packages);

            foreach (var package in packages) VerifyNow(package, checks);
        }

        /// <summary>
        /// Check a package (.thp) file that isn't installed, ex. before installing it. Nothing is installed or recorded. Refused is set if the Mode in use would refuse it
        /// </summary>
        public TrakHoundPackageVerificationInformation CheckPackageFile(byte[] packageBytes)
        {
            TrakHoundTrustedPublishers trustedPublishers = null;
            try
            {
                trustedPublishers = TrustedPublishersProvider?.Invoke();
            }
            catch { } // No signature is trusted

            var result = TrakHoundPackageVerifier.VerifyArchive(packageBytes, trustedPublishers);
            CheckApproval(result, true);
            var refused = !TrakHoundPackageVerificationPolicy.IsAllowed(VerificationMode, result);

            TrakHoundPackage package = null;
            try
            {
                package = TrakHoundPackage.ReadInformationFromPackage(packageBytes);
            }
            catch { } // Reported by the result

            if (package != null) return TrakHoundPackageVerificationPolicy.CreateInformation(package, result, refused);

            return new TrakHoundPackageVerificationInformation
            {
                State = TrakHoundPackageVerificationPolicy.GetState(result),
                Refused = refused,
                Description = TrakHoundPackageVerificationPolicy.Describe(result),
                Errors = result.Errors
            };
        }

        /// <summary>
        /// Check the Manifests of Packages on a Package Source against the Trusted Publishers. Nothing is installed or recorded
        /// </summary>
        /// <param name="packages">The Packages as listed by the Source</param>
        /// <param name="manifests">The Manifests from the Source. A Package without one is left out of the result</param>
        /// <returns>The result for each Package by UUID</returns>
        public Dictionary<string, TrakHoundPackageManifestCheck> CheckManifests(IEnumerable<TrakHoundPackage> packages, IEnumerable<TrakHoundPackageManifestJson> manifests)
        {
            TrakHoundTrustedPublishers trustedPublishers = null;
            try
            {
                trustedPublishers = TrustedPublishersProvider?.Invoke();
            }
            catch { } // No signature is trusted

            var results = new Dictionary<string, TrakHoundPackageManifestCheck>();

            var packagesByUuid = packages.GroupBy(o => o.Uuid).ToDictionary(o => o.Key, o => o.First());
            foreach (var manifest in manifests.Where(o => o?.Uuid != null))
            {
                var package = packagesByUuid.GetValueOrDefault(manifest.Uuid);
                if (package != null) results[manifest.Uuid] = TrakHoundPackageVerifier.VerifyListedManifest(manifest, package, trustedPublishers);
            }

            return results;
        }

        /// <summary>
        /// Returns true if the Mode in use would refuse to install a Package with this Manifest, whatever its files are. A Trusted Manifest can still be refused if its files don't match
        /// </summary>
        public bool WouldRefuse(TrakHoundPackageManifestCheck check)
        {
            if (check == null || !TrakHoundPackageVerificationPolicy.RequiresTrust(VerificationMode)) return false;
            if (check.State != TrakHoundPackageManifestState.Trusted) return true;

            return VerificationMode == TrakHoundPackageVerificationMode.RequireApproval && !GetApproval(check.Manifest, true).Approved;
        }

        /// <summary>
        /// Get the libraries listed in the SBOM of a Package on disk. Returns null if it isn't installed or doesn't have an SBOM
        /// </summary>
        public IEnumerable<TrakHoundPackageSbomComponent> GetSbom(string packageId, string packageVersion)
        {
            var packageUuid = TrakHoundPackage.GenerateUuid(packageId, packageVersion);
            if (packageUuid == null) return null;

            TrakHoundPackage package;
            lock (_lock) package = _installedPackages.GetValueOrDefault(packageUuid) ?? _refusedPackages.GetValueOrDefault(packageUuid);
            if (package == null) return null;

            var directory = !string.IsNullOrEmpty(package.Location) ? package.Location : TrakHoundPackage.GetPackageVersionDirectory(_path, package.Category, package.Id, package.Version);

            return TrakHoundPackageSbom.Read(directory);
        }

        /// <summary>
        /// Check the files of Packages in parallel, since an Instance can wait minutes for them to be read one at a time. Only the file check runs in parallel: applying each result changes the Manager and raises events, so that stays in order
        /// </summary>
        private DirectoryChecks CheckDirectories(IReadOnlyCollection<TrakHoundPackage> packages)
        {
            var checks = new DirectoryChecks();

            TrakHoundTrustedPublishers trustedPublishers = null;
            try
            {
                trustedPublishers = TrustedPublishersProvider?.Invoke();
            }
            catch (Exception ex)
            {
                // No signature is trusted, so under Require nothing is allowed rather than everything
                checks.TrustedPublishersError = ex.Message;
            }

            var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Clamp(Environment.ProcessorCount, 1, _maxConcurrentChecks) };
            Parallel.ForEach(packages, options, package =>
            {
                checks.Results[package] = TrakHoundPackageVerifier.VerifyDirectory(package.Location, trustedPublishers);
            });

            return checks;
        }

        private void VerifyNow(TrakHoundPackage package, DirectoryChecks checks = null)
        {
            bool wasRefused;
            lock (_lock) wasRefused = _refusedPackages.ContainsKey(package.Uuid);

            if (VerifyInstalled(package, true, checks) && wasRefused)
            {
                // Added now so that it isn't missing from the Packages until the Added event is raised
                AddInstalled(package);

                lock (_lock)
                {
                    _alreadyVerified.Add(package.Uuid);
                    _addedPackages[package.Uuid] = package;
                }

                _notifyDelay.Set();
            }
        }

        /// <summary>
        /// Check an installed Package and record the result. A Package that is refused stops being used. Returns true if it can be used
        /// </summary>
        /// <param name="package">The Package to check</param>
        /// <param name="force">Check even if the Mode is Off</param>
        /// <param name="checks">The results of checking the files, if that's already done</param>
        private bool VerifyInstalled(TrakHoundPackage package, bool force = false, DirectoryChecks checks = null)
        {
            if (VerificationMode == TrakHoundPackageVerificationMode.Off && !force) return true;

            var allowed = checks != null
                ? ApplyResult(package, checks.Results[package], checks.TrustedPublishersError)
                : Verify(package, trustedPublishers => TrakHoundPackageVerifier.VerifyDirectory(package.Location, trustedPublishers));

            if (allowed)
            {
                lock (_lock) _refusedPackages.Remove(package.Uuid);
                return true;
            }

            bool wasInstalled;
            lock (_lock)
            {
                wasInstalled = _installedPackages.ContainsKey(package.Uuid);
                _refusedPackages[package.Uuid] = package;
            }

            // Also clears what Load doesn't, so that the Latest Version falls back to a Version that can be used
            RemoveInstalled(package);

            if (wasInstalled && PackageRemoved != null) PackageRemoved.Invoke(this, new TrakHoundPackageChangedArgs(package.Category, package.Id, package.Version));

            return false;
        }

        private bool Verify(TrakHoundPackage package, Func<TrakHoundTrustedPublishers, TrakHoundPackageVerificationResult> verify, bool install = false)
        {
            TrakHoundTrustedPublishers trustedPublishers = null;
            string trustedPublishersError = null;
            try
            {
                trustedPublishers = TrustedPublishersProvider?.Invoke();
            }
            catch (Exception ex)
            {
                // No signature is trusted, so under Require nothing is allowed rather than everything
                trustedPublishersError = ex.Message;
            }

            return ApplyResult(package, verify(trustedPublishers), trustedPublishersError, install);
        }

        private bool ApplyResult(TrakHoundPackage package, TrakHoundPackageVerificationResult result, string trustedPublishersError, bool install = false)
        {
            CheckApproval(result, install);

            var allowed = TrakHoundPackageVerificationPolicy.IsAllowed(VerificationMode, result);

            lock (_lock) _verificationResults[package.Uuid] = result;

            PackageVerified?.Invoke(this, new TrakHoundPackageVerifiedArgs(package, result, allowed, trustedPublishersError, install));

            return allowed;
        }

        /// <summary>
        /// Under RequireApproval, record whether a Package Manager approved the build
        /// </summary>
        private void CheckApproval(TrakHoundPackageVerificationResult result, bool install)
        {
            if (result == null || VerificationMode != TrakHoundPackageVerificationMode.RequireApproval) return;

            TrakHoundPackageVerificationPolicy.CheckApproval(result, GetApprovals(), install);
        }

        private TrakHoundPackageApproval GetApproval(TrakHoundPackageManifest manifest, bool install)
        {
            if (manifest == null) return new TrakHoundPackageApproval(false, false, null, "Not approved : the Package has no Manifest");

            return (GetApprovals() ?? new TrakHoundPackageApprovals(null, "no Package Source is a TrakHound Package Manager")).Check(manifest.Id, manifest.Version, manifest.GetContentHash(), install, DateTime.UtcNow);
        }

        private TrakHoundPackageApprovals GetApprovals()
        {
            try
            {
                return ApprovalsProvider?.Invoke();
            }
            catch (Exception ex)
            {
                // Nothing is approved rather than everything
                return new TrakHoundPackageApprovals(null, $"the approval lists can't be read : {ex.Message}");
            }
        }

        /// <summary>
        /// Read the attestation lists again before an install under RequireApproval, so that a build approved since the last read can be installed
        /// </summary>
        private async Task RefreshApprovals()
        {
            if (VerificationMode != TrakHoundPackageVerificationMode.RequireApproval || ApprovalsRefresh == null) return;

            try
            {
                await ApprovalsRefresh(System.Threading.CancellationToken.None);
            }
            catch { } // The last lists are used
        }

        #endregion


        /// <summary>
        /// Record an installed Package version (and update the latest Version of the Package, which is a Stable Version if there is one)
        /// </summary>
        private void AddInstalled(TrakHoundPackage package)
        {
            // The installed Packages are read by Modules and the Admin Interface while the File Watcher and Installs update them
            lock (_lock)
            {
                _installedPackages.Remove(package.Uuid);
                _installedPackages.Add(package.Uuid, package);

                var latestPackageUuid = _latestPackages.GetValueOrDefault(package.Id);
                if (latestPackageUuid != null)
                {
                    var existingPackage = _installedPackages.GetValueOrDefault(latestPackageUuid);
                    if (existingPackage == null || VersionComparer.PreferStable.Compare(package.Version, existingPackage.Version) > 0)
                    {
                        _latestPackages.Remove(package.Id);
                        _latestPackages.Add(package.Id, package.Uuid);
                    }
                }
                else
                {
                    _latestPackages.Add(package.Id, package.Uuid);
                }
            }

            _installedPackageUuidsByCategory.Add(package.Category, package.Uuid);
            _installedPackageIdsByCategory.Add(package.Category, package.Id);
            _installedPackageUuidsById.Add(package.Id, package.Uuid);
            _installedPackageVersionsById.Add(package.Id, package.Version);
        }

        /// <summary>
        /// Remove an installed Package version (and reset the latest Version of the Package to the highest remaining Version)
        /// </summary>
        private void RemoveInstalled(TrakHoundPackage package)
        {
            _installedPackageUuidsByCategory.Remove(package.Category, package.Uuid);
            _installedPackageUuidsById.Remove(package.Id, package.Uuid);
            _installedPackageVersionsById.Remove(package.Id, package.Version);

            // Only remove the Package ID from the Category once no Versions remain
            var packageVersions = _installedPackageVersionsById.Get(package.Id);
            if (packageVersions.IsNullOrEmpty())
            {
                _installedPackageIdsByCategory.Remove(package.Category, package.Id);
            }

            lock (_lock)
            {
                var latestUuid = _latestPackages.GetValueOrDefault(package.Id);
                if (latestUuid != null && latestUuid == package.Uuid)
                {
                    _latestPackages.Remove(package.Id);

                    if (!packageVersions.IsNullOrEmpty())
                    {
                        var latestVersion = packageVersions.OrderByDescending(o => o, VersionComparer.PreferStable).FirstOrDefault();
                        latestUuid = TrakHoundPackage.GenerateUuid(package.Id, latestVersion);
                        if (latestUuid != null)
                        {
                            _latestPackages.Add(package.Id, latestUuid);
                        }
                    }
                }

                _installedPackages.Remove(package.Uuid);
            }
        }


        #region "File Watcher"

        private void FileSystemWatcherCreated(object sender, FileSystemEventArgs e) => PackageInformationFileAdded(e.FullPath);

        private void DirectoryWatcherCreated(object sender, FileSystemEventArgs e)
        {
            // The new folder's watch is already in place, so anything moved in after this is reported, and what arrived before is found here
            if (IsStagingPath(e.FullPath)) return;

            try
            {
                if (!Directory.Exists(e.FullPath)) return;

                foreach (var path in Directory.EnumerateFiles(e.FullPath, TrakHoundPackage.InformationFilename, SearchOption.AllDirectories))
                {
                    PackageInformationFileAdded(path);
                }
            }
            catch { } // Removed again before it was searched
        }

        private void PackageInformationFileAdded(string path)
        {
            if (IsStagingPath(path)) return;

            var package = TrakHoundPackage.ReadInformationFromFile(path);
            if (package != null && package.Uuid != null)
            {
                lock (_lock)
                {
                    // Something changed after it was checked
                    _alreadyVerified.Remove(package.Uuid);

                    if (!_addedPackages.ContainsKey(package.Uuid))
                    {
                        _addedPackages.Add(package.Uuid, package);
                    }
                }

                _notifyDelay.Set();
            }
        }

        private void FileSystemWatcherDeleted(object sender, FileSystemEventArgs e)
        {
            // Ignore Staging files (ex. a replaced install being removed)
            if (IsStagingPath(e.FullPath)) return;

            var versionPath = System.IO.Path.GetDirectoryName(e.FullPath);
            var version = System.IO.Path.GetFileName(versionPath);

            var packagePath = System.IO.Path.GetDirectoryName(versionPath);
            var packageId = System.IO.Path.GetFileName(packagePath);

            var categoryPath = System.IO.Path.GetDirectoryName(packagePath);
            var category = System.IO.Path.GetFileName(categoryPath);

            // A Package removed by another process (ex. th packages uninstall) is no longer Installed or the Latest Version
            TrakHoundPackage package;
            var packageUuid = TrakHoundPackage.GenerateUuid(packageId, version);
            lock (_lock)
            {
                package = packageUuid != null ? _installedPackages.GetValueOrDefault(packageUuid) : null;

                if (packageUuid != null)
                {
                    _refusedPackages.Remove(packageUuid);
                    _verificationResults.Remove(packageUuid);
                }
            }
            if (package != null) RemoveInstalled(package);

            if (PackageRemoved != null) PackageRemoved.Invoke(this, new TrakHoundPackageChangedArgs(category, packageId, version));
        }

        private void NotifyDelayElapsed(object sender, EventArgs e)
        {
            var addedPackages = new List<TrakHoundPackage>();

            lock (_lock)
            {
                foreach (var package in _addedPackages)
                {
                    addedPackages.Add(package.Value);
                }

                _addedPackages.Clear();
            }

            foreach (var package in addedPackages)
            {
                // A Package copied in or installed by another process only arrives here, so this is what checks it
                bool verified;
                lock (_lock) verified = _alreadyVerified.Remove(package.Uuid);
                if (!verified && !VerifyInstalled(package)) continue;

                // A Package installed by another process (ex. th packages install) must also update the Latest Version,
                // or a Configuration with packageVersion "*" keeps using the previous Version until the Instance restarts
                AddInstalled(package);

                if (PackageAdded != null) PackageAdded.Invoke(this, package);
            }
        }

        private bool IsStagingPath(string path)
        {
            var watcherPath = _fileSystemWatcher?.Path;
            if (!string.IsNullOrEmpty(watcherPath) && !string.IsNullOrEmpty(path))
            {
                var relativePath = System.IO.Path.GetRelativePath(watcherPath, path);
                var firstSegment = relativePath.Split(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar).FirstOrDefault();
                return firstSegment == TrakHoundPackage.StagingDirectory;
            }

            return false;
        }

        private void FileSystemWatcherError(object sender, ErrorEventArgs e)
        {
            Console.WriteLine("ERROR :: " + e.GetException().Message);
        }

        #endregion

        #region "Updates"

        /// <summary>
        /// Automatically install updates of a Package that match the Version (may contain '*' wildcards). Replaces any configuration previously added by the same Sender
        /// </summary>
        public void AddUpdateConfiguration(string sender, string packageId, string packageVersion)
        {
            if (!string.IsNullOrEmpty(packageId) && !string.IsNullOrEmpty(packageVersion))
            {
                var configuration = new TrakHoundPackageUpdateConfiguration();
                configuration.Id = packageId;
                configuration.Version = packageVersion;

                lock (_lock)
                {
                    _updateConfigurations.Remove(sender);
                    _updateConfigurations.Add(sender, configuration);
                }
            }
        }

        /// <summary>
        /// Remove the update configuration added by the Sender
        /// </summary>
        public void RemoveUpdateConfiguration(string sender)
        {
            if (!string.IsNullOrEmpty(sender))
            {
                lock (_lock)
                {
                    _updateConfigurations.Remove(sender);
                }
            }
        }


        // async void : an exception would end the process, so nothing may escape
        private async void UpdateTimerElapsed(object sender, ElapsedEventArgs e)
        {
            try
            {
                await CheckForUpdates();
            }
            catch { } // Checked again on the next interval
        }

        private async Task CheckForUpdates()
        {
            IEnumerable<TrakHoundPackageUpdateConfiguration> updateConfigurations;
            lock (_lock) updateConfigurations = _updateConfigurations.Values.ToList();

            if (!updateConfigurations.IsNullOrEmpty())
            {
                var queries = new List<TrakHoundPackageQuery>();
                foreach (var updateConfiguration in updateConfigurations)
                {
                    if (updateConfiguration.Id != null)
                    {
                        var installedPackage = GetLatest(updateConfiguration.Id);
                        if (installedPackage != null)
                        {
                            if (!queries.Any(o => o.Id == updateConfiguration.Id))
                            {
                                queries.Add(new TrakHoundPackageQuery(updateConfiguration.Id, installedPackage.Version, updateConfiguration.Version));
                            }
                        }
                    }
                }

                if (!queries.IsNullOrEmpty())
                {
                    var updatedPackages = await _client.QueryUpdates(queries);
                    if (!updatedPackages.IsNullOrEmpty())
                    {
                        queries.Clear();
                        foreach (var updatedPackage in updatedPackages)
                        {
                            queries.Add(new TrakHoundPackageQuery(updatedPackage.Id, updatedPackage.Version));
                        }

                        var packageArchiveBytes = await _client.DownloadArchive(queries);
                        await InstallArchive(packageArchiveBytes);

                        //foreach (var package in updatedPackages)
                        //{
                        //    var packageBytes = await _client.Download(_organization, package.Id, package.Version);
                        //    await Install(packageBytes);
                        //}
                    }
                }
            }
        }

        //private async void UpdateTimerElapsed(object sender, ElapsedEventArgs e)
        //{
        //    IEnumerable<TrakHoundPackageUpdateConfiguration> updateConfigurations;
        //    IEnumerable<TrakHoundPackage> installedPackages;
        //    lock (_lock)
        //    {
        //        updateConfigurations = _updateConfigurations.Values.ToList();
        //        installedPackages = _installedPackages.ToList();
        //    }

        //    if (!updateConfigurations.IsNullOrEmpty() && !installedPackages.IsNullOrEmpty())
        //    {
        //        var queries = new List<TrakHoundPackageQuery>();
        //        foreach (var updateConfiguration in updateConfigurations)
        //        {
        //            var installedPackage = installedPackages.Where(o => o.Id == updateConfiguration.Id)?.OrderByDescending(o => o.Version.ToVersion()).FirstOrDefault();
        //            if (installedPackage != null)
        //            {
        //                if (!queries.Any(o => o.Id == updateConfiguration.Id))
        //                {
        //                    queries.Add(new TrakHoundPackageQuery(updateConfiguration.Id, installedPackage.Version, updateConfiguration.Version));
        //                }
        //            }
        //        }

        //        if (!queries.IsNullOrEmpty())
        //        {
        //            var updatedPackages = await _client.QueryUpdates(queries);
        //            if (!updatedPackages.IsNullOrEmpty())
        //            {
        //                queries.Clear();
        //                foreach (var updatedPackage in updatedPackages)
        //                {
        //                    queries.Add(new TrakHoundPackageQuery(updatedPackage.Id, updatedPackage.Version));
        //                }

        //                var packageArchiveBytes = await _client.DownloadArchive(queries);
        //                await InstallArchive(packageArchiveBytes);

        //                //foreach (var package in updatedPackages)
        //                //{
        //                //    var packageBytes = await _client.Download(_organization, package.Id, package.Version);
        //                //    await Install(packageBytes);
        //                //}
        //            }
        //        }
        //    }
        //}

        #endregion


        /// <summary>
        /// Get the IDs of the installed Packages in a Category
        /// </summary>
        public IEnumerable<string> GetIds(string packageCategory)
        {
            return _installedPackageIdsByCategory.Get(packageCategory);
        }

        /// <summary>
        /// Get every installed Version of the Packages in a Category. Returns null if none are installed
        /// </summary>
        public IEnumerable<TrakHoundPackage> GetByCategory(string packageCategory)
        {
            if (!string.IsNullOrEmpty(packageCategory))
            {
                var packageUuids = _installedPackageUuidsByCategory.Get(packageCategory);
                if (!packageUuids.IsNullOrEmpty())
                {
                    var packages = new List<TrakHoundPackage>();
                    lock (_lock)
                    {
                        foreach (var packageUuid in packageUuids)
                        {
                            var package = _installedPackages.GetValueOrDefault(packageUuid);
                            if (package != null)
                            {
                                packages.Add(package);
                            }
                        }
                    }
                    return packages;
                }
            }

            return null;
        }

        /// <summary>
        /// Get every installed Version of a Package. Returns null if none are installed
        /// </summary>
        public IEnumerable<TrakHoundPackage> GetById(string packageId)
        {
            if (!string.IsNullOrEmpty(packageId))
            {
                var packageUuids = _installedPackageUuidsById.Get(packageId);
                if (!packageUuids.IsNullOrEmpty())
                {
                    var packages = new List<TrakHoundPackage>();
                    lock (_lock)
                    {
                        foreach (var packageUuid in packageUuids)
                        {
                            var package = _installedPackages.GetValueOrDefault(packageUuid);
                            if (package != null)
                            {
                                packages.Add(package);
                            }
                        }
                    }
                    return packages;
                }
            }

            return null;
        }

        /// <summary>
        /// Returns true if an installed Version of the Package matches the Version (may contain '*' wildcards; any Version if null)
        /// </summary>
        public bool IsInstalled(string packageId, string packageVersion = null)
        {
            return IsInstalled(new TrakHoundPackageDependency(packageId, packageVersion));
        }

        /// <summary>
        /// Returns true if an installed Version of the Package satisfies the Dependency (Version and Minimum Version)
        /// </summary>
        public bool IsInstalled(ITrakHoundPackageDependency dependency)
        {
            if (dependency != null && !string.IsNullOrEmpty(dependency.Id))
            {
                lock (_lock) return _installedPackages.Values.Any(o => string.Equals(o.Id, dependency.Id, StringComparison.OrdinalIgnoreCase) && TrakHoundPackageDependency.IsSatisfiedBy(dependency, o.Version));
            }

            return false;
        }

        /// <summary>
        /// Returns a description of why the installed Versions of a Dependency don't satisfy it (ex. "v1.1.0 is installed, below the minimum 1.2.0").
        /// Returns null if an installed Version satisfies the Dependency, or if the Dependency is not installed at all
        /// </summary>
        public string GetDependencyIssue(ITrakHoundPackageDependency dependency)
        {
            if (dependency != null && !string.IsNullOrEmpty(dependency.Id))
            {
                List<string> installedVersions;
                lock (_lock)
                {
                    installedVersions = _installedPackages.Values
                        .Where(o => string.Equals(o.Id, dependency.Id, StringComparison.OrdinalIgnoreCase))
                        .Select(o => o.Version)
                        .OrderByDescending(o => o, VersionComparer.Default)
                        .ToList();
                }

                if (installedVersions.Count < 1 || installedVersions.Any(o => TrakHoundPackageDependency.IsSatisfiedBy(dependency, o))) return null;

                // The highest installed Version that matches the Version (but not the Minimum Version)
                var matchVersion = !string.IsNullOrEmpty(dependency.Version) ? dependency.Version : TrakHoundPackage.WilcardVersion;
                var belowMinimum = installedVersions.FirstOrDefault(o => TrakHoundPackage.MatchVersion(o, matchVersion));
                if (belowMinimum != null)
                {
                    return $"v{belowMinimum} is installed, below the minimum {dependency.MinimumVersion}";
                }

                return $"v{installedVersions.First()} is installed, which doesn't match {matchVersion}";
            }

            return null;
        }

        /// <summary>
        /// Get an installed Package by ID and Version (the latest installed Version if null). Returns null if not installed
        /// </summary>
        public TrakHoundPackage Get(string packageId, string packageVersion = null)
        {
            if (!string.IsNullOrEmpty(packageVersion))
            {
                var packageUuid = TrakHoundPackage.GenerateUuid(packageId, packageVersion);
                if (packageUuid != null)
                {
                    lock (_lock) return _installedPackages.GetValueOrDefault(packageUuid);
                }
            }
            else
            {
                return GetLatest(packageId);
            }

            return null;
        }

        /// <summary>
        /// Get the installed Package that a requested Version resolves to (see <see cref="TrakHoundPackage.SelectVersion"/>). Returns null if none match
        /// </summary>
        public TrakHoundPackage Resolve(string packageId, string packageVersion)
        {
            if (string.IsNullOrEmpty(packageVersion) || packageVersion == TrakHoundPackage.WilcardVersion) return GetLatest(packageId);
            if (!packageVersion.Contains('*')) return Get(packageId, packageVersion);

            var version = TrakHoundPackage.SelectVersion(GetVersions(packageId), packageVersion);
            return version != null ? Get(packageId, version) : null;
        }

        /// <summary>
        /// Get the latest installed Version of each Package in a Category. Returns null if none are installed
        /// </summary>
        public IEnumerable<TrakHoundPackage> GetLatestByCategory(string packageCategory)
        {
            var categoryPackageIds = _installedPackageIdsByCategory.Get(packageCategory);
            if (!categoryPackageIds.IsNullOrEmpty())
            {
                var latestPackages = new List<TrakHoundPackage>();

                foreach (var packageId in categoryPackageIds)
                {
                    var package = GetLatest(packageId);
                    if (package != null)
                    {
                        latestPackages.Add(package);
                    }
                }

                return latestPackages;
            }

            return null;
        }

        /// <summary>
        /// Get the latest installed Version of a Package (the newest Stable Version, or the newest Prerelease if there is no Stable Version). Returns null if not installed
        /// </summary>
        public TrakHoundPackage GetLatest(string packageId)
        {
            if (!string.IsNullOrEmpty(packageId))
            {
                lock (_lock)
                {
                    var latestUuid = _latestPackages.GetValueOrDefault(packageId);
                    if (latestUuid != null)
                    {
                        return _installedPackages.GetValueOrDefault(latestUuid);
                    }
                }
            }

            return null;
        }


        /// <summary>
        /// Get the installed Versions of a Package
        /// </summary>
        public IEnumerable<string> GetVersions(string packageId)
        {
            return _installedPackageVersionsById.Get(packageId);
        }

        /// <summary>
        /// Get the contents of the Readme (.md or .txt) in an installed Package version directory (the latest Version if null), using the default Package directory. Returns null if not found
        /// </summary>
        public string GetReadMe(string packageCategory, string packageId, string packageVersion = null)
        {
            string packageDirectory;
            if (!string.IsNullOrEmpty(packageVersion)) packageDirectory = TrakHoundPackage.GetPackageVersionDirectory(packageCategory, packageId, packageVersion);
            else packageDirectory = TrakHoundPackage.GetLatestPackageDirectory(packageCategory, packageId);

            if (!string.IsNullOrEmpty(packageDirectory))
            {
                var filePaths = new List<string>();
                foreach (var extension in TrakHoundPackage.ReadMeFileExtensions)
                {
                    filePaths.Add(System.IO.Path.Combine(packageDirectory, $"{TrakHoundPackage.ReadMeFilename}{extension}"));
                }

                foreach (var filePath in filePaths)
                {
                    try
                    {
                        if (File.Exists(filePath))
                        {
                            var file = File.ReadAllText(filePath);
                            if (file != null) return file;
                        }
                    }
                    catch { }
                }
            }

            return null;
        }


        /// <summary>
        /// Download (using the Package Client) and install a Package by its UUID, including its Dependencies
        /// </summary>
        public async Task<IEnumerable<TrakHoundPackageInstallResult>> Install(string packageUuid, TrakHoundPackageClient packageClient = null)
        {
            var results = new List<TrakHoundPackageInstallResult>();

            var client = packageClient != null ? packageClient : _client;
            if (client != null && !string.IsNullOrEmpty(packageUuid))
            {
                await RefreshApprovals();

                var packageBytes = await client.Download(packageUuid);
                return await Install(packageBytes, packageClient: packageClient);
            }

            return results;
        }

        /// <summary>
        /// Download (using the Package Client) and install a Package by ID and Version (the latest Version if null), including its Dependencies
        /// </summary>
        public async Task<IEnumerable<TrakHoundPackageInstallResult>> Install(string packageId, string packageVersion = null, TrakHoundPackageClient packageClient = null)
        {
            await RefreshApprovals();

            return await DownloadAndInstall(new TrakHoundPackageDependency(packageId, packageVersion), packageClient, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Install a Package (.thp) file, downloading and installing its Dependencies first (Dependencies that are already installed are skipped).
        /// The Version (if specified) is written to the packages file as the requested Version
        /// </summary>
        public async Task<IEnumerable<TrakHoundPackageInstallResult>> Install(byte[] packageBytes, string version = null, TrakHoundPackageClient packageClient = null)
        {
            await RefreshApprovals();

            return await InstallPackage(packageBytes, version, packageClient, new HashSet<string>(StringComparer.OrdinalIgnoreCase));
        }

        private async Task<IEnumerable<TrakHoundPackageInstallResult>> DownloadAndInstall(ITrakHoundPackageDependency dependency, TrakHoundPackageClient packageClient, HashSet<string> visitedPackageIds)
        {
            var results = new List<TrakHoundPackageInstallResult>();

            var client = packageClient != null ? packageClient : _client;
            if (client != null && dependency != null && !string.IsNullOrEmpty(dependency.Id))
            {
                var packageBytes = await client.Download(dependency.Id, dependency.Version);
                if (packageBytes.IsNullOrEmpty())
                {
                    // Reported (rather than skipped) so the Install doesn't look complete without its Dependency
                    var result = new TrakHoundPackageInstallResult();
                    result.PackageId = dependency.Id;
                    result.Message = $"Package ({dependency.Id}) {TrakHoundPackageDependency.ToVersionString(dependency)} could not be downloaded";
                    results.Add(result);
                    return results;
                }

                // Check that the downloaded Version (the latest that matches the Version) meets the Minimum Version
                if (!string.IsNullOrEmpty(dependency.MinimumVersion) && !packageBytes.IsNullOrEmpty())
                {
                    var information = TrakHoundPackage.ReadInformationFromPackage(packageBytes);
                    if (information != null && !TrakHoundPackageDependency.IsSatisfiedBy(dependency, information.Version))
                    {
                        var result = new TrakHoundPackageInstallResult();
                        result.Message = $"Package ({dependency.Id}) Version {information.Version} does not satisfy the Dependency : {TrakHoundPackageDependency.ToVersionString(dependency)}";
                        results.Add(result);
                        return results;
                    }
                }

                return await InstallPackage(packageBytes, dependency.Version, packageClient, visitedPackageIds);
            }

            return results;
        }

        private async Task<IEnumerable<TrakHoundPackageInstallResult>> InstallPackage(byte[] packageBytes, string version, TrakHoundPackageClient packageClient, HashSet<string> visitedPackageIds)
        {
            var results = new List<TrakHoundPackageInstallResult>();

            if (packageBytes != null && packageBytes.Length > 0)
            {
                var result = new TrakHoundPackageInstallResult();

                // Read Information from Package
                var information = TrakHoundPackage.ReadInformationFromPackage(packageBytes);
                if (information != null && !string.IsNullOrEmpty(information.Uuid) && !string.IsNullOrEmpty(information.Category) && !string.IsNullOrEmpty(information.Id))
                {
                    // Track the Packages in this Install so that circular Dependencies are only installed once
                    visitedPackageIds.Add(information.Id);

                    // Checked first so that a refused Package doesn't bring in its Dependencies
                    if (VerificationMode != TrakHoundPackageVerificationMode.Off)
                    {
                        if (!Verify(information, trustedPublishers => TrakHoundPackageVerifier.VerifyArchive(packageBytes, trustedPublishers), true))
                        {
                            result.PackageId = information.Id;
                            result.PackageVersion = information.Version;
                            result.Message = $"Package ({information.Id}) v{information.Version} was refused : {TrakHoundPackageVerificationPolicy.Describe(GetVerificationResult(information.Id, information.Version))}";
                            results.Add(result);
                            return results;
                        }
                    }

                    var packageDirectory = TrakHoundPackage.GetDirectory(_path);
                    var packagePath = System.IO.Path.Combine(packageDirectory, information.Category, information.Id, information.Version);

                    information.Location = packagePath;

                    if (!information.Dependencies.IsNullOrEmpty())
                    {
                        foreach (var dependency in information.Dependencies)
                        {
                            if (string.IsNullOrEmpty(dependency.Id) || visitedPackageIds.Contains(dependency.Id)) continue;
                            if (IsInstalled(dependency)) continue;

                            results.AddRange(await DownloadAndInstall(dependency, packageClient, visitedPackageIds));
                        }

                        // A Package whose Dependency was refused couldn't run
                        if (TrakHoundPackageVerificationPolicy.RequiresTrust(VerificationMode) && results.Any(o => !o.Success))
                        {
                            result.PackageId = information.Id;
                            result.PackageVersion = information.Version;
                            result.Message = $"Package ({information.Id}) v{information.Version} wasn't installed because a Dependency failed";
                            results.Add(result);
                            return results;
                        }
                    }

                    if (TrakHoundPackage.Install(packageBytes, _path))
                    {
                        lock (_lock) _refusedPackages.Remove(information.Uuid);

                        AddInstalled(information);

                        UpdatePackagesFile(version);

                        lock (_lock)
                        {
                            if (!_addedPackages.ContainsKey(information.Uuid))
                            {
                                _addedPackages.Add(information.Uuid, information);
                            }
                        }

                        _notifyDelay.Set();

                        PackageInstalled?.Invoke(this, new TrakHoundPackageChangedArgs(information.Category, information.Id, information.Version));

                        result.Success = true;
                        result.PackageId = information.Id;
                        result.PackageVersion = information.Version;
                    }
                }

                results.Add(result);
            }

            return results;
        }

        /// <summary>
        /// Install every Package (.thp) file in a zip archive
        /// </summary>
        public async Task<IEnumerable<TrakHoundPackageInstallResult>> InstallArchive(byte[] packageArchiveBytes, TrakHoundPackageClient packageClient = null)
        {
            var results = new List<TrakHoundPackageInstallResult>();

            if (!packageArchiveBytes.IsNullOrEmpty())
            {
                try
                {
                    var tempDir = TrakHoundTemp.CreateDirectory();
                    TrakHoundFiles.Unzip(packageArchiveBytes, tempDir);

                    foreach (var file in Directory.GetFiles(tempDir))
                    {
                        var packageBytes = File.ReadAllBytes(file);
                        var packageResults = await Install(packageBytes);
                        results.AddRange(packageResults);
                    }
                }
                catch { }
            }

            return results;
        }

        /// <summary>
        /// Install every Package (.thp) file in a zip archive file
        /// </summary>
        public async Task<IEnumerable<TrakHoundPackageInstallResult>> InstallArchive(string packageArchivePath, TrakHoundPackageClient packageClient = null)
        {
            var results = new List<TrakHoundPackageInstallResult>();

            if (!string.IsNullOrEmpty(packageArchivePath))
            {
                try
                {
                    var packageArchiveBytes = File.ReadAllBytes(packageArchivePath);
                    if (packageArchiveBytes != null)
                    {
                        var tempDir = TrakHoundTemp.CreateDirectory();
                        TrakHoundFiles.Unzip(packageArchiveBytes, tempDir);

                        foreach (var file in Directory.GetFiles(tempDir))
                        {
                            var packageBytes = File.ReadAllBytes(file);
                            var packageResults = await Install(packageBytes, packageClient: packageClient);
                            results.AddRange(packageResults);
                        }
                    }
                }
                catch { }
            }

            return results;
        }

        /// <summary>
        /// Uninstall a Package version. Returns true if the Package version was installed
        /// </summary>
        public bool Uninstall(string packageId, string packageVersion)
        {
            var packageUuid = TrakHoundPackage.GenerateUuid(packageId, packageVersion);
            if (packageUuid != null)
            {
                TrakHoundPackage package;
                TrakHoundPackage refusedPackage;
                lock (_lock)
                {
                    package = _installedPackages.GetValueOrDefault(packageUuid);
                    refusedPackage = _refusedPackages.GetValueOrDefault(packageUuid);
                    _refusedPackages.Remove(packageUuid);
                    _verificationResults.Remove(packageUuid);
                }

                // A refused Package isn't in use, so there's nothing to unload. It isn't in the packages file either, so it's removed by its directory
                if (refusedPackage != null && package == null)
                {
                    var removed = TrakHoundPackage.RemovePackageVersion(refusedPackage.Location);
                    if (removed) PackageUninstalled?.Invoke(this, new TrakHoundPackageChangedArgs(refusedPackage.Category, refusedPackage.Id, refusedPackage.Version));
                    return removed;
                }

                if (package != null)
                {
                    TrakHoundPackage.Uninstall(packageId, packageVersion, _path);

                    RemoveInstalled(package);

                    UpdatePackagesFile();

                    if (PackageRemoved != null) PackageRemoved.Invoke(this, new TrakHoundPackageChangedArgs(package.Category, package.Id, package.Version));
                    PackageUninstalled?.Invoke(this, new TrakHoundPackageChangedArgs(package.Category, package.Id, package.Version));

                    return true;
                }
            }

            return false;
        }

        //public bool Uninstall(string packageId, string packageVersion)
        //{
        //    if (_installedPackages.Exists(o => o.Id == packageId && o.Version == packageVersion))
        //    {
        //        if (TrakHoundPackage.Uninstall(packageId, packageVersion))
        //        {
        //            _installedPackages.RemoveAll(o => o.Id == packageId && o.Version == packageVersion);

        //            UpdatePackagesFile();

        //            return true;
        //        }
        //    }

        //    return false;
        //}


        private bool UpdatePackagesFile(string version = null)
        {
            List<TrakHoundPackage> installedPackages;
            lock (_lock) installedPackages = _installedPackages.Values.ToList();

            if (!installedPackages.IsNullOrEmpty())
            {
                var items = new List<TrakHoundPackagesFileItem>();
                foreach (var package in installedPackages)
                {
                    var item = new TrakHoundPackagesFileItem();
                    item.Id = package.Id;
                    item.Version = !string.IsNullOrEmpty(version) ? version : package.Version;
                    item.Category = package.Category;
                    item.Installed = package.Version;
                    items.Add(item);
                }

                return TrakHoundPackagesFile.Write(items, path: _path);
            }
            else
            {
                return true;
            }
        }
    }
}
