// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Logging;
using TrakHound.Packages;

namespace TrakHound.Instance.Audit
{
    /// <summary>
    /// Records changes to Packages and Configuration files in the Audit Log. Changes made through the Package Manager or Configuration Profile are
    /// recorded as they're made, with who made them (see TrakHoundAuditContext). Anything else is found by comparing what's on disk with the last
    /// known state, at startup and when files change, and is recorded as Detected. A failure to record is logged and never stops the change.
    /// </summary>
    public sealed class TrakHoundAuditMonitor : IDisposable
    {
        public const string PackagePrefix = "package/";
        public const string ConfigurationPrefix = "configuration/";

        private const int _compareDelay = 5000;
        private const string _securityDirectory = "security";

        private static readonly string[] _excludedFilenames = { "admin-sessions-revoked.json" };

        private readonly TrakHoundAuditLog _log;
        private readonly TrakHoundPackageManager _packageManager;
        private readonly TrakHoundConfigurationProfile _configurationProfile;
        private readonly string _packagesPath;
        private readonly string _configurationPath;
        private readonly ITrakHoundLogger _logger;
        private readonly List<FileSystemWatcher> _watchers = new List<FileSystemWatcher>();
        private Timer _compareTimer;


        public TrakHoundAuditLog Log => _log;


        public TrakHoundAuditMonitor(TrakHoundAuditLog log, string configurationPath, string packagesPath, TrakHoundPackageManager packageManager = null, TrakHoundConfigurationProfile configurationProfile = null, ITrakHoundLogger logger = null)
        {
            _log = log;
            _configurationPath = configurationPath;
            _packagesPath = packagesPath;
            _packageManager = packageManager;
            _configurationProfile = configurationProfile;
            _logger = logger;
        }


        public void Start()
        {
            if (_packageManager != null)
            {
                _packageManager.PackageInstalled += PackageChanged;
                _packageManager.PackageUninstalled += PackageChanged;
            }
            if (_configurationProfile != null)
            {
                _configurationProfile.ConfigurationFileChanged += ConfigurationFileChanged;
            }

            Compare();

            _compareTimer = new Timer(_ => Compare(), null, Timeout.Infinite, Timeout.Infinite);
            AddWatcher(_configurationPath);
            AddWatcher(_packagesPath);
        }

        public void Dispose()
        {
            if (_packageManager != null)
            {
                _packageManager.PackageInstalled -= PackageChanged;
                _packageManager.PackageUninstalled -= PackageChanged;
            }
            if (_configurationProfile != null)
            {
                _configurationProfile.ConfigurationFileChanged -= ConfigurationFileChanged;
            }

            foreach (var watcher in _watchers) watcher.Dispose();
            _watchers.Clear();

            _compareTimer?.Dispose();
        }


        /// <summary>
        /// Records a change to a file in the Configuration directory that was made without the Configuration Profile (ex. the Instance Configuration).
        /// Call it from the code making the change, so that it's recorded with who made it
        /// </summary>
        public void RecordFile(string path)
        {
            var target = GetConfigurationTarget(path);
            if (target != null && IsConfigurationFile(path)) Record(target, TrakHoundAuditLog.HashFile(path));
        }

        /// <summary>
        /// Compares what's on disk with the last known state and records the differences as Detected.
        /// The first time there's nothing to compare with, so one Baseline Record is added
        /// </summary>
        public void Compare()
        {
            Run(() => _log.Update((snapshot, exists) =>
            {
                var current = Scan();
                var records = new List<TrakHoundAuditRecord>();

                if (!exists)
                {
                    records.Add(new TrakHoundAuditRecord { Timestamp = DateTime.UtcNow, Action = TrakHoundAuditActions.Baseline, Source = TrakHoundAuditSources.Detected, Target = $"{current.Count} file(s)" });
                }
                else
                {
                    foreach (var entry in current.OrderBy(o => o.Key, StringComparer.Ordinal))
                    {
                        var previous = snapshot.GetValueOrDefault(entry.Key);
                        if (previous != entry.Value) records.Add(CreateRecord(entry.Key, previous, entry.Value, TrakHoundAuditSources.Detected, null, null));
                    }

                    foreach (var entry in snapshot.Where(o => !current.ContainsKey(o.Key)).OrderBy(o => o.Key, StringComparer.Ordinal))
                    {
                        records.Add(CreateRecord(entry.Key, entry.Value, null, TrakHoundAuditSources.Detected, null, null));
                    }
                }

                snapshot.Clear();
                foreach (var entry in current) snapshot[entry.Key] = entry.Value;

                return records;
            }));
        }


        private void PackageChanged(object sender, TrakHoundPackageChangedArgs args)
        {
            if (string.IsNullOrEmpty(_packagesPath)) return;

            var directory = TrakHoundPackage.GetPackageVersionDirectory(_packagesPath, args.PackageCategory, args.PackageId, args.PackageVersion);
            if (directory == null) return;

            var target = PackagePrefix + GetRelativePath(_packagesPath, directory);
            Record(target, TrakHoundAuditLog.HashFile(Path.Combine(directory, TrakHoundPackage.InformationFilename)));
        }

        private void ConfigurationFileChanged(object sender, string path) => RecordFile(path);

        private void Record(string target, string contentHash)
        {
            var context = TrakHoundAuditContext.Current;

            Run(() => _log.Update((snapshot, exists) =>
            {
                var previous = snapshot.GetValueOrDefault(target);
                if (previous == contentHash) return null;

                if (contentHash != null) snapshot[target] = contentHash;
                else snapshot.Remove(target);

                return new[] { CreateRecord(target, previous, contentHash, context?.Source ?? TrakHoundAuditSources.Instance, context?.User, context?.ClientAddress) };
            }));
        }

        private static TrakHoundAuditRecord CreateRecord(string target, string previous, string current, string source, string user, string clientAddress)
        {
            return new TrakHoundAuditRecord
            {
                Timestamp = DateTime.UtcNow,
                Action = previous == null ? TrakHoundAuditActions.Added : current == null ? TrakHoundAuditActions.Removed : TrakHoundAuditActions.Changed,
                Target = target,
                Source = source,
                User = user,
                ClientAddress = clientAddress,
                PreviousContentHash = previous,
                ContentHash = current
            };
        }

        private Dictionary<string, string> Scan()
        {
            var results = new Dictionary<string, string>();

            if (Directory.Exists(_configurationPath))
            {
                foreach (var path in Directory.EnumerateFiles(_configurationPath, "*", SearchOption.AllDirectories))
                {
                    if (!IsConfigurationFile(path)) continue;

                    var hash = TrakHoundAuditLog.HashFile(path);
                    if (hash != null) results[GetConfigurationTarget(path)] = hash;
                }
            }

            // {category}/{id}/{version}/package.json (a Package's own files can include other files with the same name)
            if (!string.IsNullOrEmpty(_packagesPath) && Directory.Exists(_packagesPath))
            {
                foreach (var category in Directory.EnumerateDirectories(_packagesPath))
                foreach (var package in Directory.EnumerateDirectories(category))
                foreach (var version in Directory.EnumerateDirectories(package))
                {
                    var hash = TrakHoundAuditLog.HashFile(Path.Combine(version, TrakHoundPackage.InformationFilename));
                    if (hash != null) results[PackagePrefix + GetRelativePath(_packagesPath, version)] = hash;
                }
            }

            return results;
        }

        /// <summary>
        /// The .yaml Configuration files, and the .json files that hold settings in the security directory (ex. Trusted Publishers).
        /// Other .json files in the Configuration directory are state written by the Instance (ex. the Boots)
        /// </summary>
        private bool IsConfigurationFile(string path)
        {
            var filename = Path.GetFileName(path);
            if (_excludedFilenames.Contains(filename, StringComparer.OrdinalIgnoreCase)) return false;

            var extension = Path.GetExtension(filename);
            if (extension.Equals(".yaml", StringComparison.OrdinalIgnoreCase)) return true;
            if (!extension.Equals(".json", StringComparison.OrdinalIgnoreCase)) return false;

            var relative = Path.GetRelativePath(_configurationPath, path);
            return relative.StartsWith(_securityDirectory + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && Path.GetDirectoryName(relative).Equals(_securityDirectory, StringComparison.OrdinalIgnoreCase);
        }

        private string GetConfigurationTarget(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;

            var relative = Path.GetRelativePath(_configurationPath, path);
            if (relative.StartsWith("..") || Path.IsPathRooted(relative)) return null;

            return ConfigurationPrefix + relative.Replace('\\', '/');
        }

        private static string GetRelativePath(string basePath, string path) => Path.GetRelativePath(basePath, path).Replace('\\', '/');

        private void AddWatcher(string path)
        {
            if (string.IsNullOrEmpty(path) || !Directory.Exists(path)) return;

            try
            {
                var watcher = new FileSystemWatcher(path) { IncludeSubdirectories = true };
                watcher.Created += FileChanged;
                watcher.Changed += FileChanged;
                watcher.Deleted += FileChanged;
                watcher.Renamed += FileChanged;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Audit Log can't watch \"{path}\" : {ex.Message}");
            }
        }

        // Waits for changes to finish (and for changes being made through the Instance to be recorded with who made them)
        private void FileChanged(object sender, FileSystemEventArgs e)
        {
            var filename = Path.GetFileName(e.FullPath);
            if (filename.StartsWith("audit.", StringComparison.OrdinalIgnoreCase)) return;

            _compareTimer?.Change(_compareDelay, Timeout.Infinite);
        }

        private void Run(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Audit Log could not be written : {ex.Message}");
            }
        }
    }
}
