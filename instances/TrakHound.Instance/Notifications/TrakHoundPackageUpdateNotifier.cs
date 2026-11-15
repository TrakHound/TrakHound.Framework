// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog;
using TrakHound.Instance.Configurations;
using TrakHound.Instance.Pages.Packages;
using TrakHound.Instances;
using TrakHound.Packages;

namespace TrakHound.Instance.Notifications
{
    /// <summary>
    /// Periodically checks the Package Sources (Remotes) for newer versions of the installed Packages (every PackageUpdateCheckInterval Hours of the Instance Configuration).
    /// The Updates available are summarized in a single Notification that is kept current as Packages are Updated,
    /// and is only shown as unread again when an Update that wasn't already notified becomes available.
    /// </summary>
    internal class TrakHoundPackageUpdateNotifier : BackgroundService
    {
        private const string _keyPrefix = "package-updates:";
        private const int _maxListed = 5;
        private const string _link = "_admin/packages";

        private static readonly TimeSpan _startupDelay = TimeSpan.FromSeconds(30);
        private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan _settingsPollInterval = TimeSpan.FromMinutes(1); // How soon a changed Interval setting is applied

        private readonly ITrakHoundInstance _instance;
        private readonly RemotesConfiguration _remotesConfiguration;
        private readonly TrakHoundInstanceNotificationService _notificationService;
        private readonly Logger _logger;
        private readonly object _lock = new object();

        private record PackageUpdate(TrakHoundPackage Package, string InstalledVersion, string SourceName)
        {
            public string Id => $"{Package.Id}:{Package.Version}";
        }

        private Dictionary<string, PackageUpdate> _updates = new Dictionary<string, PackageUpdate>(StringComparer.OrdinalIgnoreCase); // Package ID => Newest available
        private HashSet<string> _notified = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Updates (Package ID:Version) already notified (or dismissed)


        public TrakHoundPackageUpdateNotifier(ITrakHoundInstance instance, RemotesConfiguration remotesConfiguration, TrakHoundInstanceNotificationService notificationService, Logger logger = null)
        {
            _instance = instance;
            _remotesConfiguration = remotesConfiguration;
            _notificationService = notificationService;
            _logger = logger;

            if (_instance.PackageManager != null)
            {
                _instance.PackageManager.PackageAdded += PackageAdded;
                _instance.PackageManager.PackageRemoved += PackageRemoved;
            }
        }

        public override void Dispose()
        {
            if (_instance.PackageManager != null)
            {
                _instance.PackageManager.PackageAdded -= PackageAdded;
                _instance.PackageManager.PackageRemoved -= PackageRemoved;
            }

            base.Dispose();
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try
            {
                await Task.Delay(_startupDelay, stoppingToken);

                DateTime? lastCheck = null;

                while (!stoppingToken.IsCancellationRequested)
                {
                    // Installed Packages are Loaded when the Instance Starts
                    while (_instance.Status != TrakHoundInstanceStatus.Started)
                    {
                        await Task.Delay(_pollInterval, stoppingToken);
                    }

                    // Read each time so that a change in Settings is applied without a restart
                    var intervalHours = _instance.Configuration?.PackageUpdateCheckInterval ?? 0;
                    if (intervalHours > 0 && (lastCheck == null || DateTime.UtcNow - lastCheck.Value >= TimeSpan.FromHours(intervalHours)))
                    {
                        lastCheck = DateTime.UtcNow;

                        try
                        {
                            await Check();
                        }
                        catch (Exception ex)
                        {
                            _logger?.Warn($"Package Update Check Error : {ex.Message}");
                        }
                    }

                    await Task.Delay(_settingsPollInterval, stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>
        /// Query each Package Source for newer versions of the installed Packages and update the Notification
        /// </summary>
        public async Task Check()
        {
            var packageManager = _instance.PackageManager;
            var sources = _remotesConfiguration?.Remotes?.Where(o => o != null && !string.IsNullOrEmpty(o.BaseUrl)).ToList();
            if (packageManager == null || sources.IsNullOrEmpty()) return;

            var updates = new Dictionary<string, PackageUpdate>(StringComparer.OrdinalIgnoreCase);
            var allSourcesReached = true;

            // A Prerelease is only an Update for a Package that a Configuration includes Prereleases for (ex. "*-*")
            var prereleasePackageIds = PackagesContext.GetPrereleasePackageIds(_instance);

            foreach (var source in sources)
            {
                var (packages, error) = await PackagesContext.QueryAvailable(PackagesContext.CreateClient(source), packageManager, prereleasePackageIds);
                if (packages == null)
                {
                    allSourcesReached = false;
                    _logger?.Debug($"Package Update Check : Source ({source.Name ?? source.BaseUrl}) not available : {error}");
                    continue;
                }

                foreach (var available in packages.Values)
                {
                    var includePrereleases = prereleasePackageIds.Contains(available.Id);
                    var installed = PackagesContext.GetInstalledLatest(packageManager, available.Id, includePrereleases);
                    var update = PackagesContext.GetUpdate(available, installed, includePrereleases);
                    if (update == null) continue;

                    var existing = updates.GetValueOrDefault(update.Id);
                    if (existing == null || PackagesContext.GetUpdate(update, existing.Package, includePrereleases) != null)
                    {
                        updates[update.Id] = new PackageUpdate(update, installed.Version, source.Name ?? source.BaseUrl);
                    }
                }
            }

            _logger?.Debug($"Package Update Check Complete : {updates.Count} Update(s) Available");

            // An offline Source's Updates are unknown, so keep the current Notification rather than replace it with a partial list
            if (!allSourcesReached && (updates.Count < 1 || FindNotification() != null)) return;

            lock (_lock) _updates = updates;
            UpdateNotification(true);
        }


        private TrakHoundInstanceNotification FindNotification()
        {
            return _notificationService.Find(o => o.Key != null && o.Key.StartsWith(_keyPrefix));
        }

        /// <summary>
        /// Add, Update, or Remove the Notification for the current Updates
        /// </summary>
        /// <param name="allowNew">Add a new (unread) Notification if there are Updates that weren't already notified</param>
        private void UpdateNotification(bool allowNew)
        {
            List<PackageUpdate> updates;
            bool hasNew;
            lock (_lock)
            {
                updates = _updates.Values.OrderBy(o => o.Package.Id).ToList();
                hasNew = updates.Any(o => !_notified.Contains(o.Id));
                _notified = updates.Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            }

            var existing = FindNotification();

            if (updates.Count < 1)
            {
                if (existing != null) _notificationService.Remove(o => o.Id == existing.Id);
                return;
            }

            // The Key identifies the set of Updates (a dismissed set isn't notified again)
            var key = _keyPrefix + string.Join(";", updates.Select(o => o.Id)).ToSHA1Hash();
            if (existing?.Key == key) return;

            var (title, message) = Describe(updates);

            if (allowNew && hasNew)
            {
                if (existing != null) _notificationService.Remove(o => o.Id == existing.Id);
                _notificationService.Notify(TrakHoundInstanceNotificationType.Information, TrakHoundInstanceNotificationCategories.Packages, title, message, _link, "View Installed Packages", key);
            }
            else if (existing != null)
            {
                // Nothing new (ex. some of the Packages were Updated) so change the existing Notification without marking it unread
                _notificationService.Update(existing.Id, o =>
                {
                    o.Key = key;
                    o.Title = title;
                    o.Message = message;
                });
            }
        }

        private static (string Title, string Message) Describe(List<PackageUpdate> updates)
        {
            if (updates.Count == 1)
            {
                var update = updates[0];
                return ("Package Update Available", $"{update.Package.Id} v{update.InstalledVersion} → v{update.Package.Version} ({update.SourceName})");
            }

            var lines = updates.Take(_maxListed).Select(o => $"{o.Package.Id} v{o.InstalledVersion} → v{o.Package.Version}").ToList();
            if (updates.Count > _maxListed) lines.Add($"and {updates.Count - _maxListed} more");

            return ($"{updates.Count} Package Updates Available", string.Join("\n", lines));
        }

        private void PackageAdded(object sender, TrakHoundPackage package) => RemoveAppliedUpdate(package?.Id);

        private void PackageRemoved(object sender, TrakHoundPackageChangedArgs args) => RemoveAppliedUpdate(args.PackageId);

        /// <summary>
        /// Remove a Package from the Notification once it is Updated (or Uninstalled)
        /// </summary>
        private void RemoveAppliedUpdate(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return;

            lock (_lock)
            {
                var update = _updates.GetValueOrDefault(packageId);
                if (update == null) return;

                var includePrereleases = PackagesContext.IncludesPrereleases(_instance, packageId);
                var installed = PackagesContext.GetInstalledLatest(_instance.PackageManager, packageId, includePrereleases);
                if (installed != null && PackagesContext.GetUpdate(update.Package, installed, includePrereleases) != null) return; // Still an Update

                _updates = new Dictionary<string, PackageUpdate>(_updates, StringComparer.OrdinalIgnoreCase);
                _updates.Remove(packageId);
            }

            UpdateNotification(false);
        }
    }
}
