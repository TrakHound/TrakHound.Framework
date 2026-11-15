// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Health.Checks;
using TrakHound.Instances;
using TrakHound.Logging;

namespace TrakHound.Instance.Health
{
    /// <summary>
    /// Evaluates the Health Checks in the background and keeps the latest result, so reading the Health is always cheap.
    /// A Check's Status only changes once the new Status has been seen for <see cref="ConfirmEvaluations"/> evaluations in a row (so brief spikes don't flap).
    /// </summary>
    public class TrakHoundHealthMonitor : IDisposable
    {
        public static readonly TimeSpan EvaluationInterval = TimeSpan.FromSeconds(10);
        public const int ConfirmEvaluations = 2;

        private static readonly TimeSpan _initialDelay = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan _checkTimeout = TimeSpan.FromSeconds(10);
        private static readonly ITrakHoundLogger _logger = new TrakHoundLogger<TrakHoundHealthMonitor>();

        private readonly ITrakHoundInstance _instance;
        private readonly List<ITrakHoundHealthCheck> _checks;
        private readonly DiskHealthCheck _diskCheck;
        private readonly Dictionary<string, HashSet<string>> _knownPackages = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase); // Package ID => Builds (Uuid) seen
        private readonly Dictionary<string, TrakHoundHealthCheckResult> _confirmed = new Dictionary<string, TrakHoundHealthCheckResult>(); // Check ID => Reported Result
        private readonly Dictionary<string, (TrakHoundHealthStatus Status, int Count)> _pending = new Dictionary<string, (TrakHoundHealthStatus, int)>(); // Check ID => Pending Status
        private readonly object _lock = new object();
        private Timer _timer;
        private int _evaluating;
        private TrakHoundInstanceHealth _current;


        /// <summary>
        /// Tracks errors caused by Packages that are not compatible with this version of the Instance
        /// </summary>
        public TrakHoundPackageCompatibilityTracker Compatibility { get; }

        /// <summary>
        /// Counts the Errors logged by each Logger
        /// </summary>
        public TrakHoundLogErrorTracker LogErrors { get; }

        /// <summary>
        /// Raised when the overall Status changes
        /// </summary>
        public event EventHandler<TrakHoundInstanceHealth> StatusChanged;

        /// <summary>
        /// Raised when a Check's reported Status changes (Previous is null for the first evaluation)
        /// </summary>
        public event EventHandler<TrakHoundHealthCheckChange> CheckStatusChanged;

        /// <summary>
        /// Raised after each evaluation
        /// </summary>
        public event EventHandler<TrakHoundInstanceHealth> Updated;


        /// <param name="getPaths">The directories the Instance writes to (Name, Path) for the Disk Check</param>
        public TrakHoundHealthMonitor(ITrakHoundInstance instance, ITrakHoundLogProvider logProvider, Func<IEnumerable<(string Name, string Path)>> getPaths = null)
        {
            _instance = instance;
            Compatibility = new TrakHoundPackageCompatibilityTracker(logProvider);
            LogErrors = new TrakHoundLogErrorTracker(logProvider);
            _diskCheck = new DiskHealthCheck(getPaths);

            _checks = new List<ITrakHoundHealthCheck>
            {
                new RuntimeHealthCheck(),
                new ResourcesHealthCheck(),
                _diskCheck,
                new PackagesHealthCheck(Compatibility),
                new ServicesHealthCheck(),
                new WorkersHealthCheck(),
                new FunctionsHealthCheck(),
                new ApiHealthCheck(),
                new BuffersHealthCheck(),
                new LogsHealthCheck(LogErrors)
            };
        }

        /// <summary>
        /// Include another directory in the Disk Check (ex. the Log History directory)
        /// </summary>
        public void AddDiskPath(string name, string path) => _diskCheck.AddPath(name, path);

        public void Start()
        {
            lock (_lock)
            {
                if (_timer != null) return;

                _timer = new Timer(TimerElapsed, null, _initialDelay, EvaluationInterval);

                // A new version of a Package may fix its compatibility errors
                if (_instance.PackageManager != null) _instance.PackageManager.PackageAdded += PackageAdded;
            }
        }

        private void PackageAdded(object sender, TrakHound.Packages.TrakHoundPackage package)
        {
            if (package == null || string.IsNullOrEmpty(package.Id)) return;

            // The Package Manager also announces every Package shortly after loading them (after their errors may have been logged),
            // so only a different build of an already known Package (ex. an updated version) clears its errors
            bool isNewBuild;
            lock (_knownPackages)
            {
                if (!_knownPackages.TryGetValue(package.Id, out var builds))
                {
                    builds = new HashSet<string>();
                    _knownPackages.Add(package.Id, builds);
                    isNewBuild = false;
                }
                else
                {
                    isNewBuild = !builds.Contains(package.Uuid ?? package.Version ?? "");
                }

                builds.Add(package.Uuid ?? package.Version ?? "");
            }

            if (isNewBuild) Compatibility.Clear(package.Id);
        }

        public void Dispose()
        {
            lock (_lock) _timer?.Dispose();
            if (_instance.PackageManager != null) _instance.PackageManager.PackageAdded -= PackageAdded;
            Compatibility.Dispose();
            LogErrors.Dispose();
        }


        /// <summary>
        /// The latest Health (Unknown until the first evaluation)
        /// </summary>
        public TrakHoundInstanceHealth GetHealth()
        {
            lock (_lock)
            {
                return _current ?? new TrakHoundInstanceHealth
                {
                    InstanceId = _instance.Id,
                    Status = TrakHoundHealthStatus.Unknown,
                    Checked = DateTime.UtcNow,
                    Checks = Array.Empty<TrakHoundHealthCheckResult>()
                };
            }
        }

        /// <summary>
        /// Evaluate all Checks now (also called by the Timer)
        /// </summary>
        public async Task<TrakHoundInstanceHealth> Evaluate()
        {
            var context = new TrakHoundHealthContext { Instance = _instance, Now = DateTime.UtcNow };

            // Checks run in parallel, so one slow Check doesn't delay the others
            var results = await Task.WhenAll(_checks.Select(o => EvaluateCheck(o, context)));

            // Critical and Ignored (read on each evaluation so changes apply without a restart)
            var settings = _instance.Configuration?.Health;
            foreach (var result in results) ApplySettings(result, settings);

            TrakHoundInstanceHealth health;
            TrakHoundHealthStatus? previousStatus;
            var checkChanges = new List<TrakHoundHealthCheckChange>();

            lock (_lock)
            {
                var reported = new List<TrakHoundHealthCheckResult>();
                foreach (var result in results)
                {
                    var previous = _confirmed.GetValueOrDefault(result.Id);
                    var current = Confirm(result, context.Now);
                    reported.Add(current);

                    if (previous == null || previous.Status != current.Status || previous.Ignored != current.Ignored)
                    {
                        checkChanges.Add(new TrakHoundHealthCheckChange { Previous = previous, Current = current });
                    }
                }

                // Ignored Checks are shown but not included
                var known = reported.Where(o => o.Status != TrakHoundHealthStatus.Unknown && !o.Ignored).ToList();
                var status = known.Count > 0 ? known.Max(o => o.Status) : TrakHoundHealthStatus.Unknown;

                previousStatus = _current?.Status;
                var statusChanged = previousStatus == status ? _current.StatusChanged : context.Now;

                health = new TrakHoundInstanceHealth
                {
                    InstanceId = _instance.Id,
                    Status = status,
                    StatusChanged = statusChanged,
                    Checked = context.Now,
                    Checks = reported
                };

                _current = health;
            }

            foreach (var change in checkChanges)
            {
                try { CheckStatusChanged?.Invoke(this, change); } catch { }
            }

            if (previousStatus != health.Status)
            {
                LogStatusChange(previousStatus, health);
                Raise(StatusChanged, health);
            }

            Raise(Updated, health);
            return health;
        }


        private async void TimerElapsed(object state)
        {
            // Skip if the previous evaluation is still running
            if (Interlocked.Exchange(ref _evaluating, 1) == 1) return;

            try
            {
                await Evaluate();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Health Evaluation Error : {ex.Message}");
            }
            finally
            {
                Interlocked.Exchange(ref _evaluating, 0);
            }
        }

        private static async Task<TrakHoundHealthCheckResult> EvaluateCheck(ITrakHoundHealthCheck check, TrakHoundHealthContext context)
        {
            try
            {
                var evaluate = check.Evaluate(context);
                if (await Task.WhenAny(evaluate, Task.Delay(_checkTimeout)) == evaluate)
                {
                    return await evaluate ?? context.Result(check, TrakHoundHealthStatus.Unknown, "No result");
                }

                return context.Result(check, TrakHoundHealthStatus.Unknown, $"The Check did not complete within {_checkTimeout.TotalSeconds:0} seconds");
            }
            catch (Exception ex)
            {
                return context.Result(check, TrakHoundHealthStatus.Unknown, $"The Check failed : {ex.Message}");
            }
        }

        // Set each item's Key and apply the Critical and Ignore settings. A Check's Status follows its items (the most severe that isn't Ignored).
        private static void ApplySettings(TrakHoundHealthCheckResult result, TrakHoundInstanceHealthConfiguration settings)
        {
            var adjusted = false;
            var ignoredCount = 0;

            if (!result.Items.IsNullOrEmpty())
            {
                foreach (var item in result.Items)
                {
                    item.Key = $"{result.Id}/{(!string.IsNullOrEmpty(item.Name) ? item.Name : item.Id)}";
                    if (settings == null) continue;

                    var reference = new TrakHoundHealthCheckItemReference(item.Id, item.Name);
                    if (settings.IsIgnored(result.Id, reference))
                    {
                        item.Ignored = true;
                        if (item.Status > TrakHoundHealthStatus.Healthy) ignoredCount++;
                        adjusted = true;
                    }
                    else if (settings.IsCritical(result.Id, reference))
                    {
                        item.Critical = true;
                        if (item.Status == TrakHoundHealthStatus.Degraded) item.Status = TrakHoundHealthStatus.Unhealthy;
                        adjusted = true;
                    }
                }

                if (adjusted && result.Status != TrakHoundHealthStatus.Unknown)
                {
                    var counted = result.Items.Where(o => !o.Ignored && o.Status != TrakHoundHealthStatus.Unknown).ToList();
                    result.Status = counted.Count > 0 ? counted.Max(o => o.Status) : TrakHoundHealthStatus.Healthy;
                    if (ignoredCount > 0) result.Description += $" ({ignoredCount} ignored)";
                }
            }

            if (settings != null)
            {
                if (settings.IsIgnored(result.Id))
                {
                    result.Ignored = true;
                }
                else if (settings.IsCritical(result.Id))
                {
                    result.Critical = true;
                    if (result.Status == TrakHoundHealthStatus.Degraded) result.Status = TrakHoundHealthStatus.Unhealthy;
                }
            }
        }

        // Returns the Result to report : a new Status is only reported once it has been seen for ConfirmEvaluations evaluations in a row
        private TrakHoundHealthCheckResult Confirm(TrakHoundHealthCheckResult result, DateTime now)
        {
            if (!_confirmed.TryGetValue(result.Id, out var confirmed) || result.Status == confirmed.Status || result.Status == TrakHoundHealthStatus.Unknown || confirmed.Status == TrakHoundHealthStatus.Unknown)
            {
                // First evaluation, same Status (with updated details), or a change to / from Unknown (ex. Metrics disabled) : report now
                result.StatusChanged = confirmed != null && confirmed.Status == result.Status ? confirmed.StatusChanged : now;
                _confirmed[result.Id] = result;
                _pending.Remove(result.Id);
                return result;
            }

            var pending = _pending.TryGetValue(result.Id, out var p) && p.Status == result.Status ? (result.Status, p.Count + 1) : (result.Status, 1);
            if (pending.Item2 >= ConfirmEvaluations)
            {
                result.StatusChanged = now;
                _confirmed[result.Id] = result;
                _pending.Remove(result.Id);
                return result;
            }

            _pending[result.Id] = pending;
            return confirmed;
        }

        private void LogStatusChange(TrakHoundHealthStatus? previous, TrakHoundInstanceHealth health)
        {
            var reasons = health.Checks?
                .Where(o => o.Status == health.Status && !o.Ignored && health.Status > TrakHoundHealthStatus.Healthy)
                .Select(o => $"{o.Title} : {o.Description}");

            var message = $"Instance Health : {previous?.ToString() ?? "-"} => {health.Status}";
            if (!reasons.IsNullOrEmpty()) message += $" ({string.Join(" ; ", reasons)})";

            if (health.Status > TrakHoundHealthStatus.Healthy) _logger.LogWarning(message);
            else _logger.LogInformation(message);
        }

        private static void Raise(EventHandler<TrakHoundInstanceHealth> handler, TrakHoundInstanceHealth health)
        {
            try
            {
                handler?.Invoke(null, health);
            }
            catch { }
        }
    }
}
