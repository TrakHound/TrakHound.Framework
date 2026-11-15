// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog;
using System.Runtime.InteropServices;
using System.Text.Json;
using TrakHound.Instance.Configurations;
using TrakHound.Instance.Pages.Packages;
using TrakHound.Instances;
using TrakHound.Packages;

namespace TrakHound.Instance.PackageSources
{
    /// <summary>
    /// Reports the installed Packages (every Version, used or not) to the Package Sources that are TrakHound Package Managers, so they keep a record of each Instance's Packages
    /// for reports and drift detection. Reported when the Instance starts, after Packages change (once per burst of changes), and every PackageInventoryReportInterval Hours.
    /// A failed report is logged and waits for the next one: it never holds up the Instance or a Package install
    /// </summary>
    public class TrakHoundPackageInventoryReporter : BackgroundService
    {
        public static readonly TimeSpan DefaultChangeDelay = TimeSpan.FromSeconds(30);

        private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan _startupPollInterval = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan _settingsPollInterval = TimeSpan.FromMinutes(1); // How soon a changed Interval setting is applied

        private readonly Func<IEnumerable<RemoteConfiguration>> _sources;
        private readonly Func<string, TrakHoundPackageInventory> _createInventory;
        private readonly Func<RemoteConfiguration, TrakHoundPackageInventory, CancellationToken, Task<(int StatusCode, int Received, string Message)>> _send;
        private readonly Func<bool> _isStarted;
        private readonly Func<int> _intervalHours;
        private readonly TimeSpan _changeDelay;
        private readonly Logger _logger;
        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundPackageSourceFailures _failures = new TrakHoundPackageSourceFailures();
        private readonly Dictionary<string, string> _lastReported = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // Source ID => Packages last reported
        private readonly SemaphoreSlim _reportLock = new SemaphoreSlim(1, 1);
        private readonly object _lock = new object();
        private Timer _changeTimer;
        private bool _startupReported;
        private DateTime? _lastReport;


        /// <summary>
        /// Report the Instance's Packages to its Package Manager Sources
        /// </summary>
        public TrakHoundPackageInventoryReporter(ITrakHoundInstance instance, RemotesConfiguration remotesConfiguration, Logger logger = null)
            : this(
                  () => GetPackageManagerSources(remotesConfiguration),
                  reason => CreateInventory(instance, reason),
                  Send,
                  () => instance.Status == TrakHoundInstanceStatus.Started,
                  () => instance.Configuration?.PackageInventoryReportInterval ?? 0,
                  DefaultChangeDelay,
                  logger)
        {
            _instance = instance;

            if (_instance.PackageManager != null)
            {
                _instance.PackageManager.PackageAdded += PackageAdded;
                _instance.PackageManager.PackageRemoved += PackageChanged;
                _instance.PackageManager.PackageInstalled += PackageChanged;
                _instance.PackageManager.PackageUninstalled += PackageChanged;
            }
        }

        /// <param name="sources">The Package Manager Sources to report to (read for each report)</param>
        /// <param name="createInventory">Creates the Inventory for a reason (see <see cref="TrakHoundPackageInventoryReason"/>)</param>
        /// <param name="send">Sends the Inventory to a Source</param>
        /// <param name="isStarted">Returns true once the Instance has loaded its Packages</param>
        /// <param name="intervalHours">The Heartbeat interval in Hours (0 = off), read each time so that a changed Setting applies without a restart</param>
        /// <param name="changeDelay">How long to wait after a change for more, so that a burst of changes is reported once</param>
        public TrakHoundPackageInventoryReporter(
            Func<IEnumerable<RemoteConfiguration>> sources,
            Func<string, TrakHoundPackageInventory> createInventory,
            Func<RemoteConfiguration, TrakHoundPackageInventory, CancellationToken, Task<(int StatusCode, int Received, string Message)>> send,
            Func<bool> isStarted,
            Func<int> intervalHours,
            TimeSpan changeDelay,
            Logger logger = null)
        {
            _sources = sources;
            _createInventory = createInventory;
            _send = send;
            _isStarted = isStarted;
            _intervalHours = intervalHours;
            _changeDelay = changeDelay;
            _logger = logger;
        }

        public override void Dispose()
        {
            if (_instance?.PackageManager != null)
            {
                _instance.PackageManager.PackageAdded -= PackageAdded;
                _instance.PackageManager.PackageRemoved -= PackageChanged;
                _instance.PackageManager.PackageInstalled -= PackageChanged;
                _instance.PackageManager.PackageUninstalled -= PackageChanged;
            }

            lock (_lock)
            {
                _changeTimer?.Dispose();
                _changeTimer = null;
            }

            base.Dispose();
        }


        /// <summary>
        /// The Package Sources that are TrakHound Package Managers
        /// </summary>
        public static IEnumerable<RemoteConfiguration> GetPackageManagerSources(RemotesConfiguration remotesConfiguration)
        {
            return remotesConfiguration?.Remotes?.Where(o => o != null && o.PackageManager && !string.IsNullOrEmpty(o.BaseUrl)).ToList() ?? new List<RemoteConfiguration>();
        }

        /// <summary>
        /// Create the Inventory of an Instance : every Package version on disk (including those that were refused) with its Verification
        /// </summary>
        public static TrakHoundPackageInventory CreateInventory(ITrakHoundInstance instance, string reason)
        {
            var inventory = TrakHoundPackageInventory.Create(instance.PackageManager?.GetVerificationReport(), reason);
            inventory.InstanceId = instance.Id;
            inventory.InstanceName = instance.Configuration?.Name;
            inventory.HostName = Environment.MachineName;
            inventory.FrameworkVersion = instance.Version;
            inventory.OperatingSystem = RuntimeInformation.OSDescription;
            return inventory;
        }

        private static async Task<(int StatusCode, int Received, string Message)> Send(RemoteConfiguration source, TrakHoundPackageInventory inventory, CancellationToken cancellationToken)
        {
            var client = PackagesContext.CreateClient(source);
            if (client == null) return (0, 0, "No Base URL");

            return await client.ReportInventory(inventory, cancellationToken);
        }


        /// <summary>
        /// Report after a change, once no other change follows within the Change Delay. Changes before the Start-up report are left to it
        /// </summary>
        public void NotifyChanged()
        {
            lock (_lock)
            {
                if (!_startupReported) return;

                _changeTimer ??= new Timer(ChangeTimerElapsed);
                _changeTimer.Change(_changeDelay, Timeout.InfiniteTimeSpan);
            }
        }

        /// <summary>
        /// Send the Inventory to each Package Manager Source now. A failure is logged (once for each kind) and never thrown
        /// </summary>
        public async Task Report(string reason, CancellationToken cancellationToken = default)
        {
            var sources = _sources?.Invoke()?.ToList();
            if (sources.IsNullOrEmpty()) return;

            await _reportLock.WaitAsync(cancellationToken);
            try
            {
                lock (_lock) _lastReport = DateTime.UtcNow;

                TrakHoundPackageInventory inventory;
                try
                {
                    inventory = _createInventory(reason);
                }
                catch (Exception ex)
                {
                    _logger?.Warn($"Package Inventory couldn't be created : {ex.Message}");
                    return;
                }

                // A change that leaves the Packages as they were last reported (ex. a Package loaded again) isn't sent
                var packages = JsonSerializer.Serialize(inventory.Packages);

                foreach (var source in sources)
                {
                    var sourceKey = source.Id ?? source.BaseUrl;
                    lock (_lock)
                    {
                        if (reason == TrakHoundPackageInventoryReason.Change && _lastReported.GetValueOrDefault(sourceKey) == packages) continue;
                    }

                    var name = TrakHoundPackageSourceFailures.GetName(source);
                    (int StatusCode, int Received, string Message) result;
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeout.CancelAfter(_requestTimeout);
                        result = await _send(source, inventory, timeout.Token);
                    }
                    catch (Exception ex)
                    {
                        result = (0, 0, ex.Message);
                    }

                    if (result.StatusCode >= 200 && result.StatusCode < 300)
                    {
                        lock (_lock) _lastReported[sourceKey] = packages;

                        if (_failures.Succeeded(source)) _logger?.Info($"Package Inventory reported to Package Manager ({name}) again : {result.Received} Package(s) recorded");
                        else _logger?.Debug($"Package Inventory reported to Package Manager ({name}) : {reason} : {result.Received} Package(s) recorded");
                    }
                    else
                    {
                        // Sent again with the next report, so the Package Manager catches up
                        lock (_lock) _lastReported.Remove(sourceKey);

                        if (_failures.Failed(source, result.StatusCode))
                        {
                            _logger?.Warn($"Package Inventory couldn't be reported to Package Manager ({name}) : {TrakHoundPackageSourceFailures.Describe(result.StatusCode, result.Message)}. It will be sent with the next report");
                        }
                    }
                }
            }
            finally
            {
                _reportLock.Release();
            }
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Returns to the Host straight away, so that start-up doesn't wait for a report
            await Task.Yield();

            try
            {
                // Installed Packages are Loaded when the Instance Starts
                while (!_isStarted()) await Task.Delay(_startupPollInterval, stoppingToken);

                lock (_lock) _startupReported = true;
                await Report(TrakHoundPackageInventoryReason.Startup, stoppingToken);

                while (!stoppingToken.IsCancellationRequested)
                {
                    await Task.Delay(_settingsPollInterval, stoppingToken);

                    var intervalHours = _intervalHours?.Invoke() ?? 0;
                    DateTime? lastReport;
                    lock (_lock) lastReport = _lastReport;

                    if (intervalHours > 0 && (lastReport == null || DateTime.UtcNow - lastReport.Value >= TimeSpan.FromHours(intervalHours)))
                    {
                        await Report(TrakHoundPackageInventoryReason.Heartbeat, stoppingToken);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                _logger?.Warn($"Package Inventory Reporter stopped : {ex.Message}");
            }
        }

        // async void : an exception would end the process, so nothing may escape
        private async void ChangeTimerElapsed(object state)
        {
            try
            {
                await Report(TrakHoundPackageInventoryReason.Change);
            }
            catch { } // Reported with the next change or Heartbeat
        }

        private void PackageAdded(object sender, TrakHoundPackage package) => NotifyChanged();

        private void PackageChanged(object sender, TrakHoundPackageChangedArgs args) => NotifyChanged();
    }
}
