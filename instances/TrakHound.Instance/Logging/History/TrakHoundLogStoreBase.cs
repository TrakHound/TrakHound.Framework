// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog.Common;
using System.Buffers;
using System.IO.Compression;
using System.Text.RegularExpressions;
using TrakHound.Logging;

namespace TrakHound.Instance.Logging.History
{
    /// <summary>
    /// Storage for Log History
    /// </summary>
    public interface ITrakHoundLogStore : IDisposable
    {
        string Directory { get; }

        /// <summary>
        /// Add a Logger to the Logger Index (so that it can be found by name, including after a restart)
        /// </summary>
        void RegisterLogger(string loggerId, string loggerName);

        void Write(IReadOnlyList<TrakHoundLogRecord> records);

        TrakHoundLogQueryResult Query(TrakHoundLogQuery query);

        TrakHoundLoggerLogCount[] QueryCounts(TrakHoundLogQuery query);

        TrakHoundLoggerInformation[] GetLoggers();

        /// <summary>
        /// Apply Retention, Archiving and Size limits
        /// </summary>
        TrakHoundLogMaintenanceResult Maintain(TrakHoundLogConfiguration configuration);
    }

    public class TrakHoundLogMaintenanceResult
    {
        public int Archived { get; set; }

        public int Deleted { get; set; }

        public int ArchivesDeleted { get; set; }

        public long ActiveSize { get; set; }

        public long ArchiveSize { get; set; }

        public bool HasChanges => Archived > 0 || Deleted > 0 || ArchivesDeleted > 0;

        public override string ToString() => $"Archived = {Archived} : Deleted = {Deleted} : Archives Deleted = {ArchivesDeleted} : Active Size = {ActiveSize / 1048576.0:0.0} MB : Archive Size = {ArchiveSize / 1048576.0:0.0} MB";
    }


    /// <summary>
    /// Logger Index, compressed archive ("_archive/{loggerId}/{yyyy-MM-dd}.log.gz") and maintenance shared by the Log Stores
    /// </summary>
    public abstract class TrakHoundLogStoreBase : ITrakHoundLogStore
    {
        public const string ArchiveDirectoryName = "_archive";

        private static readonly Regex _hashRegex = new Regex("^[0-9a-f]{32}$", RegexOptions.Compiled);

        private readonly Dictionary<string, TrakHoundLoggerInformation> _loggers = new Dictionary<string, TrakHoundLoggerInformation>();
        private readonly object _loggersLock = new object();
        private readonly object _saveLock = new object(); // Saves can come from the Writer and from Maintenance
        private bool _loggersChanged;

        private static readonly TimeSpan _fileIndexLifetime = TimeSpan.FromMinutes(1);
        private readonly Dictionary<string, TrakHoundLogFileInfo[]> _fileIndex = new Dictionary<string, TrakHoundLogFileInfo[]>(); // Logger Directory => Files
        private readonly object _fileIndexLock = new object();
        private long _fileIndexVersion;
        private DateTime _fileIndexExpires;


        public string Directory { get; }

        public string ArchiveDirectory => Path.Combine(Directory, ArchiveDirectoryName);


        protected TrakHoundLogStoreBase(string directory)
        {
            Directory = directory;
            if (!System.IO.Directory.Exists(Directory)) System.IO.Directory.CreateDirectory(Directory);
        }

        public virtual void Dispose()
        {
            SaveLoggerIndex(true);
        }


        #region "Logger Index"

        public void RegisterLogger(string loggerId, string loggerName)
        {
            if (string.IsNullOrEmpty(loggerId)) return;

            lock (_loggersLock)
            {
                if (_loggers.TryGetValue(loggerId, out var existing))
                {
                    // Replace a placeholder name (ex. a legacy hash directory discovered before the Logger was added)
                    if (!string.IsNullOrEmpty(loggerName) && existing.Name != loggerName)
                    {
                        existing.Name = loggerName;
                        _loggersChanged = true;
                    }
                }
                else
                {
                    var now = UnixDateTime.Now;
                    _loggers.Add(loggerId, new TrakHoundLoggerInformation { Id = loggerId, Name = loggerName ?? loggerId, FirstSeen = now, LastSeen = now });
                    _loggersChanged = true;
                }
            }
        }

        protected void UpdateLastSeen(IReadOnlyList<TrakHoundLogRecord> records)
        {
            lock (_loggersLock)
            {
                foreach (var record in records)
                {
                    if (!_loggers.TryGetValue(record.LoggerId, out var logger))
                    {
                        logger = new TrakHoundLoggerInformation { Id = record.LoggerId, Name = record.LoggerName ?? record.LoggerId, FirstSeen = record.Timestamp };
                        _loggers.Add(record.LoggerId, logger);
                        _loggersChanged = true;
                    }

                    if (record.Timestamp > logger.LastSeen) logger.LastSeen = record.Timestamp;
                }
            }
        }

        /// <summary>
        /// Add a Logger found in storage (ex. a directory from a previous run) without changing a known name
        /// </summary>
        protected void DiscoverLogger(string loggerId)
        {
            if (string.IsNullOrEmpty(loggerId)) return;

            lock (_loggersLock)
            {
                if (!_loggers.ContainsKey(loggerId))
                {
                    // Hash IDs can't be reversed (the name is set once the Logger is added).
                    // Nested directories (from earlier versions) are readable Logger paths. Other directories are NLog Logger Names.
                    string name;
                    if (_hashRegex.IsMatch(loggerId)) name = loggerId;
                    else if (loggerId.Contains('/')) name = loggerId;
                    else name = $"{TrakHoundLogHistoryTarget.SystemLoggerDirectory}/{loggerId}";

                    _loggers.Add(loggerId, new TrakHoundLoggerInformation { Id = loggerId, Name = name });
                    _loggersChanged = true;
                }
            }
        }

        protected void SetLoggers(IEnumerable<TrakHoundLoggerInformation> loggers)
        {
            lock (_loggersLock)
            {
                foreach (var logger in loggers)
                {
                    if (!string.IsNullOrEmpty(logger?.Id)) _loggers[logger.Id] = logger;
                }
            }
        }

        public TrakHoundLoggerInformation[] GetLoggers()
        {
            lock (_loggersLock)
            {
                return _loggers.Values.Select(o => new TrakHoundLoggerInformation { Id = o.Id, Name = o.Name, FirstSeen = o.FirstSeen, LastSeen = o.LastSeen }).ToArray();
            }
        }

        protected string GetLoggerName(string loggerId)
        {
            lock (_loggersLock)
            {
                return _loggers.TryGetValue(loggerId, out var logger) ? logger.Name : loggerId;
            }
        }

        /// <summary>
        /// Get the Loggers that match the Query's Patterns (by Name, or by ID for Loggers without a known Name),
        /// and the Loggers whose entries may match by Sender (see <see cref="GetSenderLoggerIds"/>)
        /// </summary>
        protected IReadOnlyList<TrakHoundLoggerInformation> ResolveLoggers(TrakHoundLogQuery query)
        {
            var loggers = GetLoggers();
            if (TrakHoundLoggerPattern.IsMatchAll(query.Patterns)) return loggers;

            return loggers.Where(o => IsLoggerMatch(query, o) || query.Patterns.Any(p => TrakHoundLoggerPattern.CanMatchUnder(p, o.Name))).ToList();
        }

        /// <summary>
        /// The IDs of the resolved Loggers that only match by Sender, so only their entries whose Sender matches are included
        /// (ex. "workers/**/Devices/d-1" matches the Sender "workers/{package}/{id}/Engine/Devices/d-1" of the Logger "workers/{package}/{id}/Engine").
        /// Null if there are none. Live Log Subscriptions match Senders the same way.
        /// </summary>
        protected static IReadOnlySet<string> GetSenderLoggerIds(TrakHoundLogQuery query, IReadOnlyList<TrakHoundLoggerInformation> loggers)
        {
            if (TrakHoundLoggerPattern.IsMatchAll(query.Patterns)) return null;

            var ids = loggers.Where(o => !IsLoggerMatch(query, o)).Select(o => o.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
            return ids.Count > 0 ? ids : null;
        }

        private static bool IsLoggerMatch(TrakHoundLogQuery query, TrakHoundLoggerInformation logger)
        {
            return TrakHoundLoggerPattern.IsMatch(query.Patterns, logger.Name) || query.Patterns.Any(p => string.Equals(p, logger.Id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Save the Logger Index if a Logger was added or renamed (or always when forced, to save Last Seen times)
        /// </summary>
        protected void SaveLoggerIndex(bool force = false)
        {
            TrakHoundLoggerInformation[] loggers;
            lock (_loggersLock)
            {
                if (!_loggersChanged && !force) return;
                _loggersChanged = false;
                loggers = GetLoggers();
            }

            try
            {
                lock (_saveLock) OnSaveLoggers(loggers);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn(ex, "TrakHound Log History : Error Saving Logger Index");
                lock (_loggersLock) _loggersChanged = true;
            }
        }

        protected abstract void OnSaveLoggers(IReadOnlyList<TrakHoundLoggerInformation> loggers);

        #endregion

        #region "Write / Query"

        public abstract void Write(IReadOnlyList<TrakHoundLogRecord> records);

        public TrakHoundLogQueryResult Query(TrakHoundLogQuery query)
        {
            if (query == null) return null;

            var loggers = ResolveLoggers(query);
            if (loggers.Count == 0) return new TrakHoundLogQueryResult { Items = new List<TrakHoundLogItem>() };

            return TrakHoundLogQueryEngine.Query(GetSources(query, loggers), query, GetSenderLoggerIds(query, loggers));
        }

        public virtual TrakHoundLoggerLogCount[] QueryCounts(TrakHoundLogQuery query)
        {
            if (query == null) return null;

            var loggers = ResolveLoggers(query);
            if (loggers.Count == 0) return Array.Empty<TrakHoundLoggerLogCount>();

            return TrakHoundLogQueryEngine.Count(GetSources(query, loggers), query, GetSenderLoggerIds(query, loggers));
        }

        protected abstract IEnumerable<ITrakHoundLogSource> GetSources(TrakHoundLogQuery query, IReadOnlyList<TrakHoundLoggerInformation> loggers);

        /// <summary>
        /// Get the files (in "{directory}/{loggerId}/") for each Logger
        /// </summary>
        protected IEnumerable<ITrakHoundLogSource> GetFileSources(string directory, IReadOnlyList<TrakHoundLoggerInformation> loggers)
        {
            var sources = new List<ITrakHoundLogSource>();

            foreach (var logger in loggers.OrderBy(o => o.Id, StringComparer.Ordinal))
            {
                foreach (var info in GetIndexedFiles(Path.Combine(directory, logger.Id)))
                {
                    sources.Add(new TrakHoundLogFileSource(info, logger.Id, logger.Name));
                }
            }

            return sources;
        }

        /// <summary>
        /// Get the Log files in a Logger directory (cached, as every Query and Count would otherwise list the directory of every Logger)
        /// </summary>
        private TrakHoundLogFileInfo[] GetIndexedFiles(string loggerDirectory)
        {
            long version;
            lock (_fileIndexLock)
            {
                // Expires so that files added outside of the store are also found
                var now = DateTime.UtcNow;
                if (now >= _fileIndexExpires)
                {
                    _fileIndex.Clear();
                    _fileIndexVersion++;
                    _fileIndexExpires = now + _fileIndexLifetime;
                }

                if (_fileIndex.TryGetValue(loggerDirectory, out var cached)) return cached;
                version = _fileIndexVersion;
            }

            var files = Array.Empty<TrakHoundLogFileInfo>();
            if (System.IO.Directory.Exists(loggerDirectory))
            {
                files = System.IO.Directory.EnumerateFiles(loggerDirectory)
                    .OrderBy(o => o, StringComparer.Ordinal)
                    .Select(TrakHoundLogRecordFormat.ParseFileName)
                    .Where(o => o != null)
                    .ToArray();
            }

            lock (_fileIndexLock)
            {
                // Not cached if the files changed while listing
                if (version == _fileIndexVersion) _fileIndex[loggerDirectory] = files;
            }

            return files;
        }

        /// <summary>
        /// Called when Log files are added, moved or deleted
        /// </summary>
        protected void InvalidateFileIndex()
        {
            lock (_fileIndexLock)
            {
                _fileIndex.Clear();
                _fileIndexVersion++;
            }
        }

        /// <summary>
        /// Add each directory that contains Log files as a Logger. The Logger ID is the path relative to the base directory.
        /// </summary>
        protected void DiscoverLoggerDirectories(string directory)
        {
            foreach (var loggerDirectory in GetLoggerDirectories(directory))
            {
                DiscoverLogger(loggerDirectory.LoggerId);
            }
        }

        /// <summary>
        /// Get the directories (recursively, excluding the Archive) that contain Log files, with their Logger ID (relative path using '/')
        /// </summary>
        protected static IEnumerable<(string LoggerId, string Path)> GetLoggerDirectories(string directory)
        {
            var results = new List<(string, string)>();
            if (!System.IO.Directory.Exists(directory)) return results;

            var pending = new Stack<string>();
            foreach (var subDirectory in System.IO.Directory.EnumerateDirectories(directory))
            {
                if (Path.GetFileName(subDirectory) != ArchiveDirectoryName) pending.Push(subDirectory);
            }

            while (pending.Count > 0)
            {
                var current = pending.Pop();
                try
                {
                    if (System.IO.Directory.EnumerateFiles(current).Any(o => TrakHoundLogRecordFormat.ParseFileName(o) != null))
                    {
                        results.Add((Path.GetRelativePath(directory, current).Replace('\\', '/'), current));
                    }

                    foreach (var subDirectory in System.IO.Directory.EnumerateDirectories(current)) pending.Push(subDirectory);
                }
                catch { }
            }

            return results;
        }

        #endregion

        #region "Maintenance"

        public TrakHoundLogMaintenanceResult Maintain(TrakHoundLogConfiguration configuration)
        {
            var result = new TrakHoundLogMaintenanceResult();

            try
            {
                MaintainActive(configuration, result);
                MaintainArchive(configuration, result);
            }
            finally
            {
                InvalidateFileIndex();
            }

            PruneLoggers();
            SaveLoggerIndex(true);

            return result;
        }

        protected abstract void MaintainActive(TrakHoundLogConfiguration configuration, TrakHoundLogMaintenanceResult result);

        /// <summary>
        /// Apply Retention and the Size limit to the Log files in the base directory (one file per Logger per day).
        /// The oldest days are Archived (or Deleted) first, and today's files are never removed by the Size limit.
        /// </summary>
        /// <returns>The size of the remaining active files</returns>
        protected long MaintainActiveFiles(TrakHoundLogConfiguration configuration, TrakHoundLogMaintenanceResult result, long otherActiveSize = 0)
        {
            var compress = configuration.ArchiveModeType == TrakHoundLogArchiveMode.Compress;
            var today = DateTime.UtcNow.Date;

            var files = GetActiveFiles();

            // Retention : Archive (or Delete) days older than the Retention period
            var cutoff = GetRetentionCutoff(configuration);
            if (cutoff.HasValue)
            {
                foreach (var file in files.Where(o => o.Info.Date < cutoff.Value).ToList())
                {
                    if (RemoveActiveFile(file.LoggerId, file.Info.Path, compress, result)) files.Remove(file);
                }
            }

            // Size Limit : Archive (or Delete) the oldest days (never today) until under the limit
            var size = files.Sum(o => o.Size);
            if (configuration.MaxSizeMB > 0)
            {
                var maxSize = configuration.MaxSizeMB * 1048576;
                foreach (var day in files.Where(o => o.Info.Date < today).GroupBy(o => o.Info.Date).OrderBy(o => o.Key).ToList())
                {
                    if (size + otherActiveSize <= maxSize) break;

                    foreach (var file in day)
                    {
                        if (RemoveActiveFile(file.LoggerId, file.Info.Path, compress, result))
                        {
                            size -= file.Size;
                            files.Remove(file);
                        }
                    }
                }
            }

            DeleteEmptyDirectories(Directory);
            return size;
        }

        /// <summary>
        /// Called before an active file is Archived or Deleted (ex. to close it)
        /// </summary>
        protected virtual void OnRemovingFile(string path) { }

        private List<(string LoggerId, TrakHoundLogFileInfo Info, long Size)> GetActiveFiles()
        {
            var files = new List<(string, TrakHoundLogFileInfo, long)>();

            foreach (var loggerDirectory in GetLoggerDirectories(Directory))
            {
                foreach (var file in System.IO.Directory.EnumerateFiles(loggerDirectory.Path))
                {
                    var info = TrakHoundLogRecordFormat.ParseFileName(file);
                    if (info != null && !info.IsCompressed)
                    {
                        files.Add((loggerDirectory.LoggerId, info, new FileInfo(file).Length));
                    }
                }
            }

            return files;
        }

        private bool RemoveActiveFile(string loggerId, string path, bool compress, TrakHoundLogMaintenanceResult result)
        {
            OnRemovingFile(path);

            try
            {
                if (compress)
                {
                    CompressToArchive(loggerId, path);
                    result.Archived++;
                }
                else
                {
                    File.Delete(path);
                    InvalidateFileIndex();
                    result.Deleted++;
                }

                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn(ex, "TrakHound Log History : Error {0} {1}", compress ? "Archiving" : "Deleting", path);
                return false;
            }
        }

        /// <summary>
        /// Remove Loggers that were only discovered from storage (never written to) and no longer have any Log files
        /// </summary>
        protected void PruneLoggers()
        {
            var stored = new HashSet<string>(GetLoggerDirectories(Directory).Concat(GetLoggerDirectories(ArchiveDirectory)).Select(o => o.LoggerId));

            lock (_loggersLock)
            {
                foreach (var logger in _loggers.Values.Where(o => o.LastSeen == 0 && !stored.Contains(o.Id) && !IsLoggerStored(o.Id)).ToList())
                {
                    _loggers.Remove(logger.Id);
                    _loggersChanged = true;
                }
            }
        }

        /// <summary>
        /// Returns true if the store has entries for the Logger other than in Log files (ex. in a database)
        /// </summary>
        protected virtual bool IsLoggerStored(string loggerId) => false;

        /// <summary>
        /// The first UTC day that is kept as active storage (null if there is no Retention limit)
        /// </summary>
        protected static DateTime? GetRetentionCutoff(TrakHoundLogConfiguration configuration)
        {
            if (configuration.RetentionDays <= 0) return null;
            return DateTime.UtcNow.Date.AddDays(-configuration.RetentionDays);
        }

        /// <summary>
        /// Delete Archives older than the Archive Retention period, then the oldest Archives until under the Archive size limit
        /// </summary>
        private void MaintainArchive(TrakHoundLogConfiguration configuration, TrakHoundLogMaintenanceResult result)
        {
            if (!System.IO.Directory.Exists(ArchiveDirectory)) return;

            var files = System.IO.Directory.EnumerateFiles(ArchiveDirectory, "*", SearchOption.AllDirectories)
                .Select(o => (Info: TrakHoundLogRecordFormat.ParseFileName(o), File: new FileInfo(o)))
                .Where(o => o.Info != null)
                .OrderBy(o => o.Info.Date)
                .ToList();

            if (configuration.ArchiveRetentionDays > 0)
            {
                var cutoff = DateTime.UtcNow.Date.AddDays(-configuration.ArchiveRetentionDays);
                foreach (var file in files.Where(o => o.Info.Date < cutoff).ToList())
                {
                    if (TryDelete(file.File.FullName))
                    {
                        result.ArchivesDeleted++;
                        files.Remove(file);
                    }
                }
            }

            var size = files.Sum(o => o.File.Length);
            if (configuration.ArchiveMaxSizeMB > 0)
            {
                var maxSize = configuration.ArchiveMaxSizeMB * 1048576;
                while (size > maxSize && files.Count > 0)
                {
                    var oldest = files[0];
                    files.RemoveAt(0);
                    if (TryDelete(oldest.File.FullName))
                    {
                        result.ArchivesDeleted++;
                        size -= oldest.File.Length;
                    }
                }
            }

            result.ArchiveSize = size;
            DeleteEmptyDirectories(ArchiveDirectory);
        }

        /// <summary>
        /// Get a path in the Archive for the Logger and date that doesn't already exist
        /// </summary>
        protected string GetArchivePath(string loggerId, string sourceFileName)
        {
            var loggerDirectory = Path.Combine(ArchiveDirectory, loggerId);
            if (!System.IO.Directory.Exists(loggerDirectory)) System.IO.Directory.CreateDirectory(loggerDirectory);

            // "{date}[.{level}].log" => "{date}[.{level}][.{part}].log.gz"
            var baseName = sourceFileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase) ? sourceFileName.Substring(0, sourceFileName.Length - 4) : sourceFileName;
            var path = Path.Combine(loggerDirectory, $"{baseName}.log.gz");
            var part = 1;
            while (File.Exists(path))
            {
                path = Path.Combine(loggerDirectory, $"{baseName}.{part++}.log.gz");
            }

            return path;
        }

        /// <summary>
        /// Write records to a new compressed Archive file (written to a temporary file first so that a partial Archive is never read)
        /// </summary>
        protected void WriteArchive(string loggerId, DateTime utcDate, IEnumerable<TrakHoundLogRecord> records)
        {
            var path = GetArchivePath(loggerId, TrakHoundLogRecordFormat.GetFileName(utcDate));
            var tempPath = path + ".tmp";

            using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(fileStream, CompressionLevel.Optimal))
            {
                var buffer = new ArrayBufferWriter<byte>(4096);
                foreach (var record in records)
                {
                    TrakHoundLogRecordFormat.WriteJsonLine(buffer, record);
                    if (buffer.WrittenCount > 65536)
                    {
                        gzip.Write(buffer.WrittenSpan);
                        buffer.Clear();
                    }
                }
                gzip.Write(buffer.WrittenSpan);
            }

            File.Move(tempPath, path);
            InvalidateFileIndex();
        }

        /// <summary>
        /// Compress an existing file into the Archive (then delete the original)
        /// </summary>
        protected void CompressToArchive(string loggerId, string path)
        {
            var archivePath = GetArchivePath(loggerId, Path.GetFileName(path));
            var tempPath = archivePath + ".tmp";

            using (var input = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var output = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var gzip = new GZipStream(output, CompressionLevel.Optimal))
            {
                input.CopyTo(gzip);
            }

            File.Move(tempPath, archivePath);
            InvalidateFileIndex();
            File.Delete(path);
            InvalidateFileIndex();
        }

        protected bool TryDelete(string path)
        {
            try
            {
                File.Delete(path);
                InvalidateFileIndex();
                return true;
            }
            catch (Exception ex)
            {
                InternalLogger.Warn(ex, "TrakHound Log History : Error Deleting {0}", path);
                return false;
            }
        }

        protected static void DeleteEmptyDirectories(string directory)
        {
            try
            {
                foreach (var subDirectory in System.IO.Directory.EnumerateDirectories(directory))
                {
                    if (Path.GetFileName(subDirectory) == ArchiveDirectoryName) continue;

                    DeleteEmptyDirectories(subDirectory);
                    if (!System.IO.Directory.EnumerateFileSystemEntries(subDirectory).Any()) System.IO.Directory.Delete(subDirectory);
                }
            }
            catch { }
        }

        #endregion
    }
}
