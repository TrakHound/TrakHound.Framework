// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;

namespace TrakHound.Instance.Health.Checks
{
    /// <summary>
    /// Free space on the drives the Instance writes to (Configuration, Packages, Volumes, Logs, ...).
    /// Each drive is checked once (listing the directories on it).
    /// </summary>
    public class DiskHealthCheck : ITrakHoundHealthCheck
    {
        private const double _degradedPercent = 10;
        private const double _unhealthyPercent = 5;
        private const long _degradedBytes = 2L * 1024 * 1024 * 1024; // 2 GB
        private const long _unhealthyBytes = 500L * 1024 * 1024; // 500 MB

        private readonly Func<IEnumerable<(string Name, string Path)>> _getPaths;
        private readonly List<(string Name, string Path)> _additionalPaths = new List<(string, string)>();
        private readonly object _lock = new object();

        // Path => Drive, built from DriveInfo.GetDrives() only when the paths change (IsReady can block on
        // disconnected network drives). Rebuilt periodically to pick up new mounts, and after a Drive can't be read
        private static readonly TimeSpan _driveMapRefreshInterval = TimeSpan.FromMinutes(5);
        private Dictionary<string, DriveInfo> _driveMap;
        private string _driveMapKey;
        private DateTime _driveMapUpdated;
        private bool _driveMapInvalid;


        public string Id => "disk";

        public string Title => "Disk";


        public DiskHealthCheck(Func<IEnumerable<(string Name, string Path)>> getPaths)
        {
            _getPaths = getPaths;
        }

        /// <summary>
        /// Include another directory (ex. the Log History directory)
        /// </summary>
        public void AddPath(string name, string path)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(path)) return;
            lock (_lock) _additionalPaths.Add((name, path));
        }


        public Task<TrakHoundHealthCheckResult> Evaluate(TrakHoundHealthContext context)
        {
            List<(string Name, string Path)> paths;
            lock (_lock) paths = (_getPaths?.Invoke() ?? Enumerable.Empty<(string, string)>()).Concat(_additionalPaths).Where(o => !string.IsNullOrEmpty(o.Path)).ToList();

            var driveMap = GetDriveMap(paths.Select(o => o.Path));

            // Group the directories by Drive
            var drives = new Dictionary<string, (DriveInfo Drive, List<string> Names)>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in paths)
            {
                var drive = driveMap.GetValueOrDefault(path.Path);
                if (drive == null) continue;

                if (!drives.TryGetValue(drive.Name, out var entry))
                {
                    entry = (drive, new List<string>());
                    drives.Add(drive.Name, entry);
                }

                if (!entry.Names.Contains(path.Name)) entry.Names.Add(path.Name);
            }

            if (drives.Count == 0)
            {
                return Task.FromResult(context.Result(this, TrakHoundHealthStatus.Unknown, "The drives could not be read"));
            }

            var items = new List<TrakHoundHealthCheckItem>();
            foreach (var (drive, names) in drives.Values)
            {
                try
                {
                    var free = drive.AvailableFreeSpace;
                    var total = drive.TotalSize;
                    var percent = total > 0 ? (double)free / total * 100 : 100;

                    var status = TrakHoundHealthStatus.Healthy;
                    if (percent < _unhealthyPercent || free < _unhealthyBytes) status = TrakHoundHealthStatus.Unhealthy;
                    else if (percent < _degradedPercent || free < _degradedBytes) status = TrakHoundHealthStatus.Degraded;

                    items.Add(new TrakHoundHealthCheckItem
                    {
                        Id = drive.Name,
                        Name = drive.Name,
                        Status = status,
                        Description = $"{FormatBytes(free)} free of {FormatBytes(total)} ({percent:0.#}%) · {string.Join(", ", names)}"
                    });
                }
                catch (Exception ex)
                {
                    // The Drive may have been removed or disconnected, so map the paths again on the next run
                    lock (_lock) _driveMapInvalid = true;

                    items.Add(new TrakHoundHealthCheckItem { Id = drive.Name, Name = drive.Name, Status = TrakHoundHealthStatus.Unknown, Description = $"Could not be read : {ex.Message} · {string.Join(", ", names)}" });
                }
            }

            var known = items.Where(o => o.Status != TrakHoundHealthStatus.Unknown).ToList();
            var overall = known.Count > 0 ? known.Max(o => o.Status) : TrakHoundHealthStatus.Unknown;

            var description = overall > TrakHoundHealthStatus.Healthy
                ? string.Join(" ; ", items.Where(o => o.Status == overall).Select(o => $"{o.Name} {o.Description.Split(" · ")[0]}"))
                : string.Join(" · ", items.Select(o => $"{o.Name} {o.Description.Split(" (")[0]}"));

            return Task.FromResult(context.Result(this, overall, description, null, items));
        }


        private Dictionary<string, DriveInfo> GetDriveMap(IEnumerable<string> paths)
        {
            var distinctPaths = paths.Distinct().ToList();
            var key = string.Join("\n", distinctPaths);

            lock (_lock)
            {
                if (_driveMap != null && !_driveMapInvalid && _driveMapKey == key && DateTime.UtcNow - _driveMapUpdated < _driveMapRefreshInterval)
                {
                    return _driveMap;
                }
            }

            // Read the Drives (and IsReady) once for all of the paths
            DriveInfo[] readyDrives;
            try
            {
                readyDrives = DriveInfo.GetDrives().Where(o => o.IsReady).ToArray();
            }
            catch
            {
                readyDrives = Array.Empty<DriveInfo>();
            }

            var driveMap = new Dictionary<string, DriveInfo>();
            foreach (var path in distinctPaths)
            {
                driveMap[path] = GetDrive(readyDrives, path);
            }

            lock (_lock)
            {
                _driveMap = driveMap;
                _driveMapKey = key;
                _driveMapUpdated = DateTime.UtcNow;
                _driveMapInvalid = false;
            }

            return driveMap;
        }

        private static DriveInfo GetDrive(IEnumerable<DriveInfo> readyDrives, string path)
        {
            try
            {
                var fullPath = Path.GetFullPath(path);

                // The Drive with the longest matching Root (ex. a mount point on Linux)
                return readyDrives
                    .Where(o => fullPath.StartsWith(o.RootDirectory.FullName, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                    .OrderByDescending(o => o.RootDirectory.FullName.Length)
                    .FirstOrDefault();
            }
            catch
            {
                return null;
            }
        }

        private static string FormatBytes(double value)
        {
            if (value >= 1024L * 1024 * 1024 * 1024) return $"{value / (1024L * 1024 * 1024 * 1024):0.#} TB";
            if (value >= 1024 * 1024 * 1024) return $"{value / (1024 * 1024 * 1024):0.#} GB";
            return $"{value / (1024 * 1024):0} MB";
        }
    }
}
