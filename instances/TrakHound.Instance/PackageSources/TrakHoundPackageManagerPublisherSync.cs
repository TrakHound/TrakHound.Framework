// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog;
using TrakHound.Instance.Configurations;
using TrakHound.Instance.Pages.Packages;
using TrakHound.Packages;

namespace TrakHound.Instance.PackageSources
{
    /// <summary>
    /// Reads the Trusted Publishers of the Package Sources that are TrakHound Package Managers (when the Instance starts and every hour) and keeps them in the
    /// Configuration Profile ("security/package-manager-publishers.json"), so that the Packages they verified are trusted in addition to the Profile's own Trusted Publishers.
    /// The last copy that was read is kept when a Package Manager can't be reached
    /// </summary>
    public class TrakHoundPackageManagerPublisherSync : BackgroundService
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

        private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(60);

        private readonly string _profilePath;
        private readonly Func<IEnumerable<RemoteConfiguration>> _sources;
        private readonly Func<RemoteConfiguration, CancellationToken, Task<(int StatusCode, TrakHoundTrustedPublishers Publishers, string Message)>> _fetch;
        private readonly TimeSpan _interval;
        private readonly Logger _logger;
        private readonly TrakHoundPackageSourceFailures _failures = new TrakHoundPackageSourceFailures();
        private readonly SemaphoreSlim _syncLock = new SemaphoreSlim(1, 1);
        private readonly object _lock = new object();
        private TrakHoundPackageManagerPublishers _cache;


        /// <summary>
        /// Raised when the synced publishers change (ex. so that the installed Packages are checked again)
        /// </summary>
        public event EventHandler Changed;


        public TrakHoundPackageManagerPublisherSync(string profilePath, RemotesConfiguration remotesConfiguration, Logger logger = null)
            : this(profilePath, () => TrakHoundPackageInventoryReporter.GetPackageManagerSources(remotesConfiguration), Fetch, DefaultInterval, logger) { }

        /// <param name="profilePath">The Configuration Profile directory the publishers are kept in</param>
        /// <param name="sources">The Package Manager Sources (read each time, so a Source that is removed or no longer a Package Manager stops being trusted straight away)</param>
        /// <param name="fetch">Reads the Trusted Publishers of a Source</param>
        /// <param name="interval">How often the publishers are read</param>
        public TrakHoundPackageManagerPublisherSync(
            string profilePath,
            Func<IEnumerable<RemoteConfiguration>> sources,
            Func<RemoteConfiguration, CancellationToken, Task<(int StatusCode, TrakHoundTrustedPublishers Publishers, string Message)>> fetch,
            TimeSpan interval,
            Logger logger = null)
        {
            _profilePath = profilePath;
            _sources = sources;
            _fetch = fetch;
            _interval = interval;
            _logger = logger;

            try
            {
                _cache = TrakHoundPackageManagerPublishers.Read(profilePath);
            }
            catch (Exception ex)
            {
                // Read again from the Package Managers, which replaces the file
                _logger?.Warn($"Package Manager Publishers file can't be read, so its publishers aren't trusted until they are read again : {ex.Message}");
                _cache = new TrakHoundPackageManagerPublishers();
            }
        }

        private static async Task<(int StatusCode, TrakHoundTrustedPublishers Publishers, string Message)> Fetch(RemoteConfiguration source, CancellationToken cancellationToken)
        {
            var client = PackagesContext.CreateClient(source);
            if (client == null) return (0, null, "No Base URL");

            return await client.GetTrustedPublishers(cancellationToken);
        }


        /// <summary>
        /// The publishers to trust : those of the Sources that are currently Package Managers
        /// </summary>
        public IEnumerable<TrakHoundTrustedPublisher> GetPublishers()
        {
            var sourceIds = GetSourceIds();
            lock (_lock) return _cache.GetPublishers(sourceIds);
        }

        /// <summary>
        /// The publishers of each Package Manager Source that is currently configured, for display
        /// </summary>
        public IEnumerable<TrakHoundPackageManagerPublisherSource> GetSources()
        {
            var sourceIds = new HashSet<string>(GetSourceIds(), StringComparer.OrdinalIgnoreCase);
            lock (_lock) return _cache.Sources.Where(o => o.SourceId != null && sourceIds.Contains(o.SourceId)).ToList();
        }

        /// <summary>
        /// Read the Trusted Publishers of each Package Manager Source now. A Source that fails keeps its last copy. Never throws
        /// </summary>
        public async Task Sync(CancellationToken cancellationToken = default)
        {
            await _syncLock.WaitAsync(cancellationToken);
            try
            {
                var sources = _sources?.Invoke()?.ToList() ?? new List<RemoteConfiguration>();
                var changed = false;
                var save = false;

                foreach (var source in sources)
                {
                    var name = TrakHoundPackageSourceFailures.GetName(source);
                    (int StatusCode, TrakHoundTrustedPublishers Publishers, string Message) result;
                    try
                    {
                        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                        timeout.CancelAfter(_requestTimeout);
                        result = await _fetch(source, timeout.Token);
                    }
                    catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                    {
                        result = (0, null, ex.Message);
                    }

                    if (result.StatusCode == 200 && result.Publishers?.Publishers != null)
                    {
                        lock (_lock) changed |= _cache.Set(source.Id ?? source.BaseUrl, source.Name, result.Publishers.Publishers, DateTime.UtcNow);
                        save = true;

                        if (_failures.Succeeded(source)) _logger?.Info($"Trusted Publishers read from Package Manager ({name}) again : {result.Publishers.Publishers.Count} Publisher(s)");
                    }
                    else if (_failures.Failed(source, result.StatusCode))
                    {
                        _logger?.Warn($"Trusted Publishers couldn't be read from Package Manager ({name}) : {TrakHoundPackageSourceFailures.Describe(result.StatusCode, result.Message)}. The last copy that was read is still trusted");
                    }
                }

                // Sources that were removed, or are no longer Package Managers
                lock (_lock)
                {
                    if (_cache.RemoveOtherSources(sources.Select(o => o.Id ?? o.BaseUrl)))
                    {
                        changed = true;
                        save = true;
                    }
                }

                if (save && !string.IsNullOrEmpty(_profilePath))
                {
                    try
                    {
                        lock (_lock) _cache.Save(_profilePath);
                    }
                    catch (Exception ex)
                    {
                        _logger?.Warn($"Package Manager Publishers file couldn't be saved : {ex.Message}");
                    }
                }

                if (changed)
                {
                    _logger?.Info("Package Manager Trusted Publishers changed");
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            finally
            {
                _syncLock.Release();
            }
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Returns to the Host straight away, so that start-up doesn't wait for the Package Managers (the last copy is used until then)
            await Task.Yield();

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await Sync(stoppingToken);
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        _logger?.Warn($"Package Manager Trusted Publishers Sync Error : {ex.Message}");
                    }

                    await Task.Delay(_interval, stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
        }


        private List<string> GetSourceIds()
        {
            try
            {
                return _sources?.Invoke()?.Select(o => o.Id ?? o.BaseUrl).Where(o => o != null).ToList() ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }
    }
}
