// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog;
using TrakHound.Instance.Configurations;
using TrakHound.Instance.Pages.Packages;
using TrakHound.Packages;

namespace TrakHound.Instance.PackageSources
{
    /// <summary>
    /// What an Instance knows about the server key and attestation list of a TrakHound Package Manager Source, for the Admin Interface
    /// </summary>
    public class TrakHoundPackageManagerApprovalStatus
    {
        public string SourceId { get; init; }

        public string SourceName { get; init; }

        /// <summary>
        /// The pinned server key. Null until one is read
        /// </summary>
        public TrakHoundPackageManagerPinnedKey PinnedKey { get; init; }

        /// <summary>
        /// The ID of a different key the Package Manager offered, which is refused until the pinned key is cleared
        /// </summary>
        public string RefusedKeyId { get; init; }

        /// <summary>
        /// The last valid list (expired or not). Null if there's none
        /// </summary>
        public TrakHoundPackageAttestationList List { get; init; }

        /// <summary>
        /// When the last valid list was read (UTC)
        /// </summary>
        public DateTime? ListReceived { get; init; }

        public bool ListExpired { get; init; }

        /// <summary>
        /// Why the last attempt to read the key or the list failed. Null if it succeeded
        /// </summary>
        public string Error { get; init; }

        /// <summary>
        /// When the key and list were last read (or tried) (UTC)
        /// </summary>
        public DateTime? LastAttempt { get; init; }
    }

    /// <summary>
    /// Reads the server key and the signed attestation list of each Package Source that is a TrakHound Package Manager (when the Instance starts, every hour,
    /// and before a Package is installed), for the RequireApproval Verification Mode. The first key read from a Source is pinned ("security/package-manager-key.json"),
    /// and a different key is refused until an administrator clears it. The last valid list is kept ("security/package-manager-attestations.json"),
    /// so an Instance that starts without its Package Manager still has it. A list that expires keeps the Packages on it working, but nothing new is installed
    /// </summary>
    public class TrakHoundPackageManagerApprovalSync : BackgroundService
    {
        public static readonly TimeSpan DefaultInterval = TimeSpan.FromHours(1);

        private const string _notificationKeyPrefix = "package-manager-approval:";

        private static readonly TimeSpan _requestTimeout = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan _installRefreshInterval = TimeSpan.FromMinutes(1);

        private readonly string _profilePath;
        private readonly Func<IEnumerable<RemoteConfiguration>> _sources;
        private readonly Func<RemoteConfiguration, CancellationToken, Task<(int StatusCode, TrakHoundPackageManagerServerKey Key, string Message)>> _fetchKey;
        private readonly Func<RemoteConfiguration, CancellationToken, Task<(int StatusCode, TrakHoundPackageAttestationEnvelope Envelope, string Message)>> _fetchList;
        private readonly TimeSpan _interval;
        private readonly Logger _logger;
        private readonly Func<DateTime> _now;
        private readonly TrakHoundPackageSourceFailures _keyFailures = new TrakHoundPackageSourceFailures();
        private readonly TrakHoundPackageSourceFailures _listFailures = new TrakHoundPackageSourceFailures();
        private readonly SemaphoreSlim _syncLock = new SemaphoreSlim(1, 1);
        private readonly object _lock = new object();
        private readonly Dictionary<string, string> _refusedKeyIds = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // Source ID => Key ID
        private readonly Dictionary<string, string> _errors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // Source ID => Error
        private readonly Dictionary<string, string> _loggedErrors = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // Source ID => Error last logged
        private TrakHoundPackageManagerKeys _keys;
        private TrakHoundPackageManagerAttestations _attestations;
        private string _filesError;
        private (string SourceIds, TrakHoundPackageApprovals Approvals) _approvals;
        private DateTime? _lastAttempt;


        /// <summary>
        /// Raised when the lists change (a new list, a Source removed, or a pinned key cleared), so that the installed Packages are checked again
        /// </summary>
        public event EventHandler Changed;

        /// <summary>
        /// Raised with a problem an administrator should see (ex. a different server key, or an expired list) : (Kind, Key, Title, Message), where the Key starts with the Kind
        /// and changes with the problem (so a new one is shown even if the last was dismissed), or (Kind, null, null, null) when it's resolved
        /// </summary>
        public event Action<string, string, string, string> Problem;


        public TrakHoundPackageManagerApprovalSync(string profilePath, RemotesConfiguration remotesConfiguration, Logger logger = null)
            : this(profilePath, () => TrakHoundPackageInventoryReporter.GetPackageManagerSources(remotesConfiguration), FetchKey, FetchList, DefaultInterval, logger) { }

        /// <param name="profilePath">The Configuration Profile directory the key and the lists are kept in</param>
        /// <param name="sources">The Package Manager Sources (read each time, so a Source that is removed or no longer a Package Manager stops approving straight away)</param>
        /// <param name="fetchKey">Reads the server key of a Source</param>
        /// <param name="fetchList">Reads the attestation list of a Source</param>
        /// <param name="interval">How often the key and lists are read</param>
        /// <param name="now">The current time (UTC), for tests</param>
        public TrakHoundPackageManagerApprovalSync(
            string profilePath,
            Func<IEnumerable<RemoteConfiguration>> sources,
            Func<RemoteConfiguration, CancellationToken, Task<(int StatusCode, TrakHoundPackageManagerServerKey Key, string Message)>> fetchKey,
            Func<RemoteConfiguration, CancellationToken, Task<(int StatusCode, TrakHoundPackageAttestationEnvelope Envelope, string Message)>> fetchList,
            TimeSpan interval,
            Logger logger = null,
            Func<DateTime> now = null)
        {
            _profilePath = profilePath;
            _sources = sources;
            _fetchKey = fetchKey;
            _fetchList = fetchList;
            _interval = interval;
            _logger = logger;
            _now = now ?? (() => DateTime.UtcNow);

            try
            {
                _keys = TrakHoundPackageManagerKeys.Read(profilePath);
            }
            catch (Exception ex)
            {
                // Not replaced (that would pin whatever key is read next) : an administrator has to fix or delete the file
                _filesError = $"the Package Manager Key file can't be read ({ex.Message})";
                _logger?.Error($"Package Manager Key file can't be read, so no Package is approved until it is fixed or deleted : {ex.Message}");
                _keys = null;
            }

            try
            {
                _attestations = TrakHoundPackageManagerAttestations.Read(profilePath);
            }
            catch (Exception ex)
            {
                // Read again from the Package Managers, which replaces the file
                _logger?.Warn($"Package Manager Attestations file can't be read, so no Package is approved until a list is read again : {ex.Message}");
                _attestations = new TrakHoundPackageManagerAttestations();
            }
        }

        private static async Task<(int StatusCode, TrakHoundPackageManagerServerKey Key, string Message)> FetchKey(RemoteConfiguration source, CancellationToken cancellationToken)
        {
            var client = PackagesContext.CreateClient(source);
            if (client == null) return (0, null, "No Base URL");

            return await client.GetServerKey(cancellationToken);
        }

        private static async Task<(int StatusCode, TrakHoundPackageAttestationEnvelope Envelope, string Message)> FetchList(RemoteConfiguration source, CancellationToken cancellationToken)
        {
            var client = PackagesContext.CreateClient(source);
            if (client == null) return (0, null, "No Base URL");

            return await client.GetAttestations(cancellationToken);
        }


        /// <summary>
        /// The lists of the Sources that are currently Package Managers, for checking Packages
        /// </summary>
        public TrakHoundPackageApprovals GetApprovals()
        {
            var sourceIds = GetSourceIds();
            var signature = string.Join("\n", sourceIds.OrderBy(o => o, StringComparer.OrdinalIgnoreCase));

            lock (_lock)
            {
                if (_approvals.Approvals != null && _approvals.SourceIds == signature) return _approvals.Approvals;

                TrakHoundPackageApprovals approvals;
                if (_keys == null) approvals = new TrakHoundPackageApprovals(null, _filesError);
                else if (sourceIds.Count < 1) approvals = new TrakHoundPackageApprovals(null, "no Package Source is a TrakHound Package Manager");
                else
                {
                    // Signatures are checked again here, so that a list is only used with the key that is pinned now
                    approvals = TrakHoundPackageManagerApprovalFiles.CreateApprovals(_keys, _attestations, sourceIds, _now());
                    if (!approvals.HasList) approvals = new TrakHoundPackageApprovals(null, GetNoListReason(sourceIds));
                }

                _approvals = (signature, approvals);
                return approvals;
            }
        }

        /// <summary>
        /// The key and list of each Package Manager Source that is currently configured, for display
        /// </summary>
        public IEnumerable<TrakHoundPackageManagerApprovalStatus> GetStatus()
        {
            var sources = GetSources();
            var now = _now();

            lock (_lock)
            {
                return sources.Select(source =>
                {
                    var sourceId = GetSourceId(source);
                    var pinned = _keys?.Get(sourceId);
                    var cached = _attestations.Get(sourceId);
                    var list = cached != null ? TrakHoundPackageAttestationVerifier.Read(cached.Envelope, pinned, now, true).List : null;

                    return new TrakHoundPackageManagerApprovalStatus
                    {
                        SourceId = sourceId,
                        SourceName = TrakHoundPackageSourceFailures.GetName(source),
                        PinnedKey = pinned,
                        RefusedKeyId = _refusedKeyIds.GetValueOrDefault(sourceId),
                        List = list,
                        ListReceived = list != null ? cached.Received : null,
                        ListExpired = list != null && list.Expires.ToUniversalTime() <= now,
                        Error = _keys == null ? _filesError : _errors.GetValueOrDefault(sourceId),
                        LastAttempt = _lastAttempt
                    };
                }).ToList();
            }
        }

        /// <summary>
        /// Clear the pinned key of a Source (and the list it signed), so that the next key read from it is pinned. Every Package of the Source is refused until a new list is read
        /// </summary>
        public bool ClearPinnedKey(string sourceId)
        {
            if (string.IsNullOrEmpty(sourceId)) return false;

            bool cleared;
            lock (_lock)
            {
                if (_keys == null) return false;

                cleared = _keys.Clear(sourceId);
                _attestations.Remove(sourceId);
                _refusedKeyIds.Remove(sourceId);
                _errors.Remove(sourceId);
                _approvals = default;

                if (cleared) SaveFiles(true, true);
            }

            if (cleared)
            {
                _logger?.Warn($"Package Manager server key cleared for Source ({sourceId}). The next key it sends is pinned");
                Problem?.Invoke(GetNotificationKey(sourceId, "key"), null, null, null);
                Changed?.Invoke(this, EventArgs.Empty);
            }

            return cleared;
        }

        /// <summary>
        /// Read the lists again before a Package is installed, unless they were read in the last minute. Never throws
        /// </summary>
        public async Task RefreshBeforeInstall(CancellationToken cancellationToken = default)
        {
            DateTime? lastAttempt;
            lock (_lock) lastAttempt = _lastAttempt;

            if (lastAttempt != null && _now() - lastAttempt.Value < _installRefreshInterval) return;

            try
            {
                await Sync(false, cancellationToken);
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                _logger?.Warn($"Package Manager Attestations couldn't be read before the install : {ex.Message}");
            }
        }

        /// <summary>
        /// Read the server key and list of each Package Manager Source now. A Source that fails keeps its last list. Never throws
        /// </summary>
        public Task Sync(CancellationToken cancellationToken = default) => Sync(true, cancellationToken);

        private async Task Sync(bool reportExpiry, CancellationToken cancellationToken)
        {
            await _syncLock.WaitAsync(cancellationToken);
            try
            {
                var sources = _sources?.Invoke()?.ToList() ?? new List<RemoteConfiguration>();
                var changed = false;
                var saveKeys = false;
                var saveLists = false;

                foreach (var source in sources)
                {
                    var (listChanged, keyPinned, listSaved) = await SyncSource(source, cancellationToken);
                    changed |= listChanged;
                    saveKeys |= keyPinned;
                    saveLists |= listSaved;

                    if (reportExpiry) ReportExpiry(source);
                }

                // Sources that were removed, or are no longer Package Managers. Their pinned keys are kept, so adding the Source back doesn't trust a different key
                lock (_lock)
                {
                    if (_attestations.RemoveOtherSources(sources.Select(GetSourceId)))
                    {
                        changed = true;
                        saveLists = true;
                    }

                    _approvals = default; // Built again, so the reasons shown for a missing list are current
                    _lastAttempt = _now();

                    SaveFiles(saveKeys, saveLists);
                }

                if (changed)
                {
                    _logger?.Info("Package Manager Attestations changed");
                    Changed?.Invoke(this, EventArgs.Empty);
                }
            }
            finally
            {
                _syncLock.Release();
            }
        }

        private async Task<(bool Changed, bool KeyPinned, bool ListSaved)> SyncSource(RemoteConfiguration source, CancellationToken cancellationToken)
        {
            var sourceId = GetSourceId(source);
            var name = TrakHoundPackageSourceFailures.GetName(source);
            var keyPinned = false;

            lock (_lock)
            {
                if (_keys == null) return (false, false, false); // The key file is damaged, so nothing is pinned or trusted
            }

            // Server key
            var keyResult = await Fetch(() => _fetchKey(source, CreateTimeout(cancellationToken)), cancellationToken);
            if (keyResult.StatusCode == 200 && keyResult.Value != null)
            {
                _keyFailures.Succeeded(source);

                var keyError = TrakHoundPackageAttestationVerifier.CheckServerKey(keyResult.Value);
                if (keyError != null)
                {
                    SetError(source, $"The server key it sent can't be used : {keyError}", true);
                    return (false, false, false);
                }

                TrakHoundPackageManagerPinnedKey pinned;
                lock (_lock) pinned = _keys.Get(sourceId);

                if (pinned == null)
                {
                    lock (_lock)
                    {
                        _keys.Pin(sourceId, name, keyResult.Value, _now());
                        _refusedKeyIds.Remove(sourceId);
                    }

                    keyPinned = true;
                    _logger?.Info($"Package Manager ({name}) server key pinned : {keyResult.Value.KeyId} (server {keyResult.Value.ServerId}). Confirm the Key ID with the Package Manager's");
                }
                else if (!pinned.IsSameKey(keyResult.Value))
                {
                    bool firstTime;
                    lock (_lock) firstTime = _refusedKeyIds.GetValueOrDefault(sourceId) != keyResult.Value.KeyId;
                    lock (_lock) _refusedKeyIds[sourceId] = keyResult.Value.KeyId;

                    var message = $"The Package Manager ({name}) sent a different server key ({keyResult.Value.KeyId}) than the one pinned ({pinned.KeyId}), so its attestation lists aren't read. If the Package Manager's key was replaced, clear the pinned key in Trusted Publishers";
                    SetError(source, message, false);
                    if (firstTime)
                    {
                        _logger?.Error(message);
                        Problem?.Invoke(GetNotificationKey(sourceId, "key"), $"{GetNotificationKey(sourceId, "key")}:{keyResult.Value.KeyId}", "Package Manager Server Key Changed", message);
                    }

                    // The last list (signed by the pinned key) is still used
                    return (false, false, false);
                }
                else
                {
                    bool wasRefused;
                    lock (_lock) wasRefused = _refusedKeyIds.Remove(sourceId);
                    if (wasRefused) Problem?.Invoke(GetNotificationKey(sourceId, "key"), null, null, null);
                }
            }
            else if (_keyFailures.Failed(source, keyResult.StatusCode))
            {
                _logger?.Warn($"Server key couldn't be read from Package Manager ({name}) : {TrakHoundPackageSourceFailures.Describe(keyResult.StatusCode, keyResult.Message)}");
            }

            TrakHoundPackageManagerPinnedKey pinnedKey;
            lock (_lock) pinnedKey = _keys.Get(sourceId);
            if (pinnedKey == null)
            {
                SetError(source, $"The server key hasn't been read : {TrakHoundPackageSourceFailures.Describe(keyResult.StatusCode, keyResult.Message)}", false);
                return (false, keyPinned, false);
            }

            // Attestation list
            var listResult = await Fetch(() => _fetchList(source, CreateTimeout(cancellationToken)), cancellationToken);
            if (listResult.StatusCode != 200 || listResult.Value == null)
            {
                var message = $"The attestation list couldn't be read : {TrakHoundPackageSourceFailures.Describe(listResult.StatusCode, listResult.Message)}";
                SetError(source, message, false);
                if (_listFailures.Failed(source, listResult.StatusCode)) _logger?.Warn($"Attestation list couldn't be read from Package Manager ({name}) : {TrakHoundPackageSourceFailures.Describe(listResult.StatusCode, listResult.Message)}. The last valid list is still used");

                return (false, keyPinned, false);
            }

            if (_listFailures.Succeeded(source)) _logger?.Info($"Attestation list read from Package Manager ({name}) again");

            var (list, error) = TrakHoundPackageAttestationVerifier.Read(listResult.Value, pinnedKey, _now());
            if (list == null)
            {
                SetError(source, $"The attestation list it sent was refused : {error}", true);
                return (false, keyPinned, false);
            }

            bool listChanged;
            lock (_lock)
            {
                var existing = _attestations.Get(sourceId);
                listChanged = existing?.Envelope?.Payload != listResult.Value.Payload;

                // Only the newest list is kept, so a replayed older one doesn't bring back a revoked build
                if (existing?.Envelope != null)
                {
                    var existingList = TrakHoundPackageAttestationVerifier.Read(existing.Envelope, pinnedKey, _now(), true).List;
                    if (existingList != null && existingList.Issued.ToUniversalTime() > list.Issued.ToUniversalTime())
                    {
                        _errors.Remove(sourceId);
                        return (false, keyPinned, false);
                    }
                }

                _attestations.Set(sourceId, name, listResult.Value, _now());
                _errors.Remove(sourceId);
                _loggedErrors.Remove(sourceId);
            }

            if (listChanged) _logger?.Info($"Attestation list read from Package Manager ({name}) : {list.Packages.Count} build(s) in Channel ({list.Channel}), valid until {list.Expires.ToUniversalTime():u}");

            return (listChanged, keyPinned, true);
        }

        /// <summary>
        /// Report a Source whose list has expired, on every scheduled read until a new list arrives
        /// </summary>
        private void ReportExpiry(RemoteConfiguration source)
        {
            var sourceId = GetSourceId(source);
            var name = TrakHoundPackageSourceFailures.GetName(source);
            var now = _now();

            TrakHoundPackageAttestationList list;
            lock (_lock)
            {
                var cached = _attestations.Get(sourceId);
                list = cached != null ? TrakHoundPackageAttestationVerifier.Read(cached.Envelope, _keys?.Get(sourceId), now, true).List : null;
            }

            var key = GetNotificationKey(sourceId, "expired");
            if (list != null && list.Expires.ToUniversalTime() <= now)
            {
                var message = $"The attestation list of Package Manager ({name}) expired at {list.Expires.ToUniversalTime():u}. The Packages on it keep working, but nothing new is installed until a new list is read";
                _logger?.Warn(message);
                Problem?.Invoke(key, $"{key}:{list.Expires.ToUniversalTime().Ticks}", "Package Manager Approvals Expired", message);
            }
            else
            {
                Problem?.Invoke(key, null, null, null);
            }
        }


        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Returns to the Host straight away, so that start-up doesn't wait for the Package Managers (the last list is used until then)
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
                        _logger?.Warn($"Package Manager Attestations Sync Error : {ex.Message}");
                    }

                    await Task.Delay(_interval, stoppingToken);
                }
            }
            catch (OperationCanceledException) { }
        }


        private static async Task<(int StatusCode, T Value, string Message)> Fetch<T>(Func<Task<(int, T, string)>> fetch, CancellationToken cancellationToken)
        {
            try
            {
                return await fetch();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                return (0, default, ex.Message);
            }
        }

        private static CancellationToken CreateTimeout(CancellationToken cancellationToken)
        {
            // Not disposed : it's short-lived and only cancels the one request
            var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(_requestTimeout);
            return timeout.Token;
        }

        private void SetError(RemoteConfiguration source, string error, bool log)
        {
            var sourceId = GetSourceId(source);
            bool logIt;
            lock (_lock)
            {
                _errors[sourceId] = error;
                logIt = log && _loggedErrors.GetValueOrDefault(sourceId) != error;
                if (logIt) _loggedErrors[sourceId] = error;
            }

            if (logIt) _logger?.Error($"Package Manager ({TrakHoundPackageSourceFailures.GetName(source)}) : {error}");
        }

        private string GetNoListReason(IEnumerable<string> sourceIds)
        {
            var reasons = sourceIds.Select(o => _errors.GetValueOrDefault(o) ?? (_keys?.Get(o) == null ? "the server key hasn't been read" : "no attestation list has been read")).Distinct().ToList();
            return string.Join("; ", reasons);
        }

        private void SaveFiles(bool keys, bool lists)
        {
            if (string.IsNullOrEmpty(_profilePath)) return;

            try
            {
                if (keys) _keys?.Save(_profilePath);
            }
            catch (Exception ex)
            {
                _logger?.Error($"Package Manager Key file couldn't be saved : {ex.Message}");
            }

            try
            {
                if (lists) _attestations.Save(_profilePath);
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Package Manager Attestations file couldn't be saved : {ex.Message}");
            }
        }

        private static string GetSourceId(RemoteConfiguration source) => source.Id ?? source.BaseUrl;

        private static string GetNotificationKey(string sourceId, string kind) => $"{_notificationKeyPrefix}{kind}:{sourceId}";

        private List<RemoteConfiguration> GetSources()
        {
            try
            {
                return _sources?.Invoke()?.Where(o => o != null && GetSourceId(o) != null).ToList() ?? new List<RemoteConfiguration>();
            }
            catch
            {
                return new List<RemoteConfiguration>();
            }
        }

        private List<string> GetSourceIds() => GetSources().Select(GetSourceId).ToList();
    }
}
