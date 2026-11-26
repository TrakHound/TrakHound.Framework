// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Volumes;

namespace TrakHound.Packages.Dev.Api
{
    /// <summary>
    /// A Package kept as a file in the Volume
    /// </summary>
    public class DevPackageEntry
    {
        /// <summary>
        /// The Organization (the name of the directory the file is in, under the packages directory)
        /// </summary>
        public string Organization { get; init; }

        /// <summary>
        /// The path of the file in the Volume, with '/' separators (ex. packages/Dev/Contoso.Hello/1.0.0.thp)
        /// </summary>
        public string Path { get; init; }

        /// <summary>
        /// The Package information read from the file
        /// </summary>
        public TrakHoundPackage Package { get; init; }

        /// <summary>
        /// The Version as a Package Server lists it : Major.Minor.Patch with any Prerelease Label (ex. 1.2.0 for 1.2, or 1.2.0-dev.1)
        /// </summary>
        public string Version { get; init; }

        public string Uuid => Package.Uuid;

        public string Id => Package.Id;


        /// <summary>
        /// The information returned by the Api (with the listed Version)
        /// </summary>
        public TrakHoundPackageJson ToJson()
        {
            var json = new TrakHoundPackageJson(Package);
            json.Version = Version;
            return json;
        }
    }

    public enum DevPackageAddStatus
    {
        Added,
        Replaced,
        Exists,
        Invalid,
        Error
    }

    public class DevPackageAddResult
    {
        public DevPackageAddStatus Status { get; init; }

        public DevPackageEntry Entry { get; init; }

        public string Message { get; init; }

        /// <summary>
        /// Older Versions deleted to keep the newest (keepVersions)
        /// </summary>
        public IReadOnlyList<DevPackageEntry> Removed { get; init; } = Array.Empty<DevPackageEntry>();
    }


    /// <summary>
    /// Keeps Package files (.thp) in a Volume, as packages/{organization}/{id}/{version}.thp, and indexes them in memory from their
    /// information. The files are the only record, so a file copied in by hand is listed too (it's found when the directory is next read)
    /// and moving to another Package Server only means uploading the files
    /// </summary>
    public class DevPackageStore
    {
        public const string RootDirectory = "packages";

        private readonly ITrakHoundVolume _volume;
        private readonly TimeSpan _rescanInterval;
        private readonly Func<DateTime> _now;
        private readonly object _lock = new object();
        private readonly SemaphoreSlim _scanLock = new SemaphoreSlim(1, 1);
        private readonly SemaphoreSlim _writeLock = new SemaphoreSlim(1, 1);

        // By Path (case-insensitive, so the same file isn't indexed twice on Windows)
        private Dictionary<string, DevPackageEntry> _entries = new Dictionary<string, DevPackageEntry>(StringComparer.OrdinalIgnoreCase);

        // Files that couldn't be read as a Package, so they aren't opened on every scan
        private HashSet<string> _unreadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private DateTime _lastScan = DateTime.MinValue;


        /// <param name="volume">The Volume the files are kept in</param>
        /// <param name="rescanInterval">How long the index is used before the directory is read again for files added or removed by hand</param>
        /// <param name="now">The current time (UTC), for tests</param>
        public DevPackageStore(ITrakHoundVolume volume, TimeSpan? rescanInterval = null, Func<DateTime> now = null)
        {
            _volume = volume ?? throw new ArgumentNullException(nameof(volume));
            _rescanInterval = rescanInterval ?? TimeSpan.FromSeconds(2);
            _now = now ?? (() => DateTime.UtcNow);
        }


        #region "Read"

        /// <summary>
        /// The Organizations that have Packages, in order
        /// </summary>
        public async Task<IReadOnlyList<string>> GetOrganizations()
        {
            var entries = await GetEntries();
            return entries.Select(o => o.Organization).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(o => o, StringComparer.OrdinalIgnoreCase).ToList();
        }

        /// <summary>
        /// The Organization with the casing it's stored with (an exact match first), so Organizations match case-insensitively. The Organization unchanged if it has no Packages
        /// </summary>
        public async Task<string> ResolveOrganization(string organization)
        {
            if (string.IsNullOrEmpty(organization)) return organization;

            var organizations = await GetOrganizations();
            return organizations.FirstOrDefault(o => o == organization)
                ?? organizations.FirstOrDefault(o => string.Equals(o, organization, StringComparison.OrdinalIgnoreCase))
                ?? organization;
        }

        /// <summary>
        /// Every Package version in the Organization
        /// </summary>
        public async Task<IReadOnlyList<DevPackageEntry>> GetPackages(string organization)
        {
            var entries = await GetEntries();
            return entries.Where(o => IsOrganization(o, organization)).ToList();
        }

        /// <summary>
        /// The Package version with the UUID. If more than one file has it, the most recently built
        /// </summary>
        public async Task<DevPackageEntry> Find(string organization, string uuid)
        {
            if (string.IsNullOrEmpty(uuid)) return null;

            return (await GetPackages(organization))
                .Where(o => string.Equals(o.Uuid, uuid, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(o => o.Package.BuildDate)
                .FirstOrDefault();
        }

        /// <summary>
        /// Every version of a Package (one entry per Version, the most recently built if a Version is in more than one file)
        /// </summary>
        public async Task<IReadOnlyList<DevPackageEntry>> GetVersions(string organization, string id)
        {
            if (string.IsNullOrEmpty(id)) return Array.Empty<DevPackageEntry>();

            return (await GetPackages(organization))
                .Where(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase))
                .GroupBy(o => o.Version, StringComparer.OrdinalIgnoreCase)
                .Select(o => o.OrderByDescending(p => p.Package.BuildDate).First())
                .OrderBy(o => o.Version, TrakHound.Extensions.VersionComparer.Default)
                .ToList();
        }

        /// <summary>
        /// A version of a Package : an exact Version (ex. 1.2.3 or 1.2.3-dev.1), or a Version with wildcards (ex. 1.*, * or *-*) selected the way an Instance selects one
        /// </summary>
        public async Task<DevPackageEntry> FindVersion(string organization, string id, string version)
        {
            if (string.IsNullOrEmpty(version)) return null;

            var versions = await GetVersions(organization, id);

            var selected = version.Contains('*') ? TrakHoundPackage.SelectVersion(versions.Select(o => o.Version), version) : version;
            if (string.IsNullOrEmpty(selected)) return null;

            return versions.FirstOrDefault(o => string.Equals(o.Version, selected, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// The most recently built version of each Package in the Organization (what the Packages Api lists as the latest)
        /// </summary>
        public async Task<IReadOnlyList<DevPackageEntry>> GetLatest(string organization)
        {
            return (await GetPackages(organization))
                .GroupBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
                .Select(o => o.OrderByDescending(p => p.Package.BuildDate).ThenByDescending(p => p.Version, TrakHound.Extensions.VersionComparer.Default).First())
                .OrderBy(o => o.Id, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// Open the Package file. The caller disposes the Stream. Null if the file can't be read
        /// </summary>
        public Task<Stream> OpenRead(DevPackageEntry entry)
        {
            if (entry == null) return Task.FromResult<Stream>(null);
            return _volume.ReadStream(entry.Path);
        }

        /// <summary>
        /// The Manifest and signature of a Package, as the exact bytes in the file. Null if the file can't be read
        /// </summary>
        public async Task<TrakHoundPackageManifestJson> ReadManifest(DevPackageEntry entry)
        {
            if (entry == null) return null;

            using (var stream = await _volume.ReadStream(entry.Path))
            {
                if (stream == null) return null;

                var manifest = new TrakHoundPackageManifestJson { Uuid = entry.Uuid };
                try
                {
                    (manifest.Manifest, manifest.Signature) = TrakHoundPackageManifest.ReadFromArchive(stream);
                }
                catch (InvalidDataException ex)
                {
                    manifest.Error = ex.Message;
                }

                return manifest;
            }
        }

        #endregion

        #region "Write"

        /// <summary>
        /// Add a Package file. The Organization, ID and Version come from the file (they're the path it's kept at), never the caller.
        /// A Version that's already stored is refused unless allowOverwrite is true. With keepVersions above 0, older Versions of the Package are deleted
        /// so only that many are kept
        /// </summary>
        public async Task<DevPackageAddResult> Add(string organization, byte[] packageBytes, bool allowOverwrite = false, int keepVersions = 0)
        {
            if (!TrakHoundPackage.IsValidPathSegment(organization)) return Invalid($"Invalid Organization ({organization})");
            if (packageBytes == null || packageBytes.Length < 1) return Invalid("The Package file is empty");

            var package = TrakHoundPackage.ReadInformationFromPackage(packageBytes);
            if (package == null) return Invalid("Not a Package file, or its information (package.json) can't be read");

            var version = NormalizeVersion(package.Version);
            if (version == null) return Invalid($"Invalid Package Version ({package.Version})");

            // Used as the file name : checked again in its normalized form
            if (!TrakHoundPackage.IsValidPathSegment(package.Id) || !TrakHoundPackage.IsValidPathSegment(version)) return Invalid($"Invalid Package ID or Version ({package.Id} v{package.Version})");

            await _writeLock.WaitAsync();
            try
            {
                organization = await ResolveOrganization(organization);

                var existing = await Find(organization, package.Uuid);
                if (existing != null && !allowOverwrite)
                {
                    return new DevPackageAddResult { Status = DevPackageAddStatus.Exists, Entry = existing, Message = $"{package.Id} v{version} is already stored. Upload a new Version (ex. pack it with a new --version-suffix), delete this one first, or set the Api's allowOverwrite Parameter" };
                }

                var path = $"{RootDirectory}/{organization}/{package.Id}/{version}{TrakHoundPackage.FileExtension}";

                bool written;
                using (var stream = new MemoryStream(packageBytes, false))
                {
                    // Written to a temporary file and moved into place by the Volume, so a failed write never leaves part of a Package
                    written = await _volume.WriteStream(path, stream);
                }
                if (!written) return new DevPackageAddResult { Status = DevPackageAddStatus.Error, Message = $"{path} could not be written to the Volume" };

                var entry = new DevPackageEntry { Organization = organization, Path = path, Package = package, Version = version };

                // Every other file with this Version (ex. one copied in by hand under another name) is replaced too
                var replaced = (await GetPackages(organization)).Where(o => string.Equals(o.Uuid, package.Uuid, StringComparison.OrdinalIgnoreCase) && !SamePath(o.Path, path)).ToList();
                foreach (var other in replaced) await DeleteFile(other);

                lock (_lock)
                {
                    var entries = new Dictionary<string, DevPackageEntry>(_entries, StringComparer.OrdinalIgnoreCase);
                    entries[path] = entry;
                    _entries = entries;

                    var unreadable = new HashSet<string>(_unreadable, StringComparer.OrdinalIgnoreCase);
                    unreadable.Remove(path);
                    _unreadable = unreadable;
                }

                var removed = keepVersions > 0 ? await Trim(organization, package.Id, keepVersions, entry) : Array.Empty<DevPackageEntry>();

                return new DevPackageAddResult { Status = existing != null ? DevPackageAddStatus.Replaced : DevPackageAddStatus.Added, Entry = entry, Removed = removed };
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Delete a Package version (every file that has the UUID). Returns the number of files deleted
        /// </summary>
        public async Task<int> Delete(string organization, string uuid)
        {
            await _writeLock.WaitAsync();
            try
            {
                var entries = (await GetPackages(organization)).Where(o => string.Equals(o.Uuid, uuid, StringComparison.OrdinalIgnoreCase)).ToList();
                var deleted = 0;
                foreach (var entry in entries)
                {
                    if (await DeleteFile(entry)) deleted++;
                }
                return deleted;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        /// <summary>
        /// Delete every version of a Package. Returns the number of files deleted
        /// </summary>
        public async Task<int> DeleteAll(string organization, string id)
        {
            await _writeLock.WaitAsync();
            try
            {
                var entries = (await GetPackages(organization)).Where(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();
                var deleted = 0;
                foreach (var entry in entries)
                {
                    if (await DeleteFile(entry)) deleted++;
                }
                return deleted;
            }
            finally
            {
                _writeLock.Release();
            }
        }

        // Delete the oldest Versions (in Semantic Versioning order) beyond the newest keepVersions. The Version just added is always kept
        private async Task<IReadOnlyList<DevPackageEntry>> Trim(string organization, string id, int keepVersions, DevPackageEntry added)
        {
            var entries = (await GetPackages(organization)).Where(o => string.Equals(o.Id, id, StringComparison.OrdinalIgnoreCase)).ToList();

            var keep = new HashSet<string>(
                entries.Select(o => o.Version).Distinct(StringComparer.OrdinalIgnoreCase).OrderByDescending(o => o, TrakHound.Extensions.VersionComparer.Default).Take(keepVersions),
                StringComparer.OrdinalIgnoreCase);
            keep.Add(added.Version);

            var removed = new List<DevPackageEntry>();
            foreach (var entry in entries.Where(o => !keep.Contains(o.Version)))
            {
                if (await DeleteFile(entry)) removed.Add(entry);
            }

            return removed;
        }

        private async Task<bool> DeleteFile(DevPackageEntry entry)
        {
            var deleted = await _volume.DeleteFile(entry.Path);
            if (deleted)
            {
                lock (_lock)
                {
                    var entries = new Dictionary<string, DevPackageEntry>(_entries, StringComparer.OrdinalIgnoreCase);
                    entries.Remove(entry.Path);
                    _entries = entries;
                }

                // An empty Package directory is removed (DeleteDirectory only removes empty directories)
                var directory = GetDirectory(entry.Path);
                if (!string.IsNullOrEmpty(directory) && !SamePath(directory, $"{RootDirectory}/{entry.Organization}"))
                {
                    await _volume.DeleteDirectory(directory);
                }
            }

            return deleted;
        }

        #endregion

        #region "Index"

        /// <summary>
        /// Read the directory now (normally it's read at most every rescanInterval)
        /// </summary>
        public Task Refresh() => Scan(true);

        private async Task<IReadOnlyCollection<DevPackageEntry>> GetEntries()
        {
            await Scan(false);
            return _entries.Values;
        }

        private async Task Scan(bool force)
        {
            if (!force && _now() - _lastScan < _rescanInterval) return;

            await _scanLock.WaitAsync();
            try
            {
                // Another caller may have read it while this one waited
                if (!force && _now() - _lastScan < _rescanInterval) return;

                var current = _entries;
                var unreadable = _unreadable;

                var files = (await _volume.ListFiles(RootDirectory, true) ?? Array.Empty<string>())
                    .Select(NormalizePath)
                    .Where(o => o.EndsWith(TrakHoundPackage.FileExtension, StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var entries = new Dictionary<string, DevPackageEntry>(StringComparer.OrdinalIgnoreCase);
                var stillUnreadable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var path in files)
                {
                    // packages/{organization}/... : a file directly in the packages directory has no Organization
                    var segments = path.Split('/');
                    if (segments.Length < 3 || !TrakHoundPackage.IsValidPathSegment(segments[1])) continue;

                    if (current.TryGetValue(path, out var existing))
                    {
                        entries[path] = existing;
                        continue;
                    }

                    if (unreadable.Contains(path))
                    {
                        stillUnreadable.Add(path);
                        continue;
                    }

                    var entry = await ReadEntry(segments[1], path);
                    if (entry != null) entries[path] = entry;
                    else stillUnreadable.Add(path);
                }

                lock (_lock)
                {
                    // Keep what was added or removed while the directory was read
                    foreach (var added in _entries.Where(o => !current.ContainsKey(o.Key))) entries[added.Key] = added.Value;
                    foreach (var removed in current.Where(o => !_entries.ContainsKey(o.Key))) entries.Remove(removed.Key);

                    _entries = entries;
                    _unreadable = stillUnreadable;
                }

                _lastScan = _now();
            }
            finally
            {
                _scanLock.Release();
            }
        }

        private async Task<DevPackageEntry> ReadEntry(string organization, string path)
        {
            try
            {
                using (var stream = await _volume.ReadStream(path))
                {
                    if (stream == null) return null;

                    var package = TrakHoundPackage.ReadInformationFromStream(stream);
                    var version = package != null ? NormalizeVersion(package.Version) : null;
                    if (version == null) return null;

                    return new DevPackageEntry { Organization = organization, Path = path, Package = package, Version = version };
                }
            }
            catch
            {
                // Ex. a file that's still being copied in : it's read again when it changes name, or after the Api restarts
                return null;
            }
        }

        #endregion

        #region "Versions"

        /// <summary>
        /// The Version as a Package Server lists it : Major.Minor.Patch with any Prerelease Label and without Build Metadata (ex. 1.2 is 1.2.0,
        /// 1.2.0-dev.1+abc is 1.2.0-dev.1). Null if it can't be read
        /// </summary>
        public static string NormalizeVersion(string version)
        {
            var baseVersion = version.ToBaseVersion();
            if (baseVersion == null) return null;

            var normalized = new Version(baseVersion.Major, baseVersion.Minor, Math.Max(baseVersion.Build, 0)).ToString();
            var label = GetPrereleaseLabel(version);
            return !string.IsNullOrEmpty(label) ? $"{normalized}-{label}" : normalized;
        }

        private static string GetPrereleaseLabel(string version)
        {
            if (string.IsNullOrEmpty(version)) return null;

            var buildIndex = version.IndexOf('+');
            if (buildIndex >= 0) version = version.Substring(0, buildIndex);

            var labelIndex = version.IndexOf('-');
            return labelIndex >= 0 && labelIndex < version.Length - 1 ? version.Substring(labelIndex + 1) : null;
        }

        #endregion

        private static bool IsOrganization(DevPackageEntry entry, string organization) => string.Equals(entry.Organization, organization, StringComparison.OrdinalIgnoreCase);

        private static bool SamePath(string a, string b) => string.Equals(NormalizePath(a), NormalizePath(b), StringComparison.OrdinalIgnoreCase);

        private static string NormalizePath(string path) => path?.Replace('\\', '/').Trim('/');

        private static string GetDirectory(string path)
        {
            var index = path.LastIndexOf('/');
            return index > 0 ? path.Substring(0, index) : null;
        }

        private static DevPackageAddResult Invalid(string message) => new DevPackageAddResult { Status = DevPackageAddStatus.Invalid, Message = message };
    }
}
