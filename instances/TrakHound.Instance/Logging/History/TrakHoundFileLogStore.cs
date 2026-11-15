// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog.Common;
using System.Buffers;
using System.Text.Json;
using TrakHound.Logging;

namespace TrakHound.Instance.Logging.History
{
    /// <summary>
    /// Stores Log History as JSON Lines files : "{directory}/{loggerId}/{yyyy-MM-dd}.log" (one file per Logger per UTC day).
    /// Legacy NLog files ("{yyyy-MM-dd}.{level}.log") in the same directories are also read.
    /// </summary>
    public class TrakHoundFileLogStore : TrakHoundLogStoreBase
    {
        private const string _loggerIndexFilename = "loggers.json";
        private static readonly TimeSpan _writerIdleTimeout = TimeSpan.FromMinutes(1);

        private readonly Dictionary<string, (FileStream Stream, DateTime LastUsed)> _writers = new Dictionary<string, (FileStream, DateTime)>();
        private readonly ArrayBufferWriter<byte> _buffer = new ArrayBufferWriter<byte>(65536);
        private readonly object _writeLock = new object();


        public TrakHoundFileLogStore(string directory) : base(directory)
        {
            LoadLoggerIndex();
            DiscoverLoggerDirectories(Directory);
            DiscoverLoggerDirectories(ArchiveDirectory);
            PruneLoggers();
        }

        public override void Dispose()
        {
            lock (_writeLock)
            {
                foreach (var writer in _writers.Values)
                {
                    try { writer.Stream.Dispose(); } catch { }
                }
                _writers.Clear();
            }

            base.Dispose();
        }


        public override void Write(IReadOnlyList<TrakHoundLogRecord> records)
        {
            if (records == null || records.Count == 0) return;

            lock (_writeLock)
            {
                var now = DateTime.UtcNow;

                // Records are grouped by file (in order) so each file is written once per batch
                foreach (var group in records.GroupBy(o => (o.LoggerId, Date: o.Timestamp.ToDateTime().Date)))
                {
                    _buffer.Clear();
                    foreach (var record in group)
                    {
                        TrakHoundLogRecordFormat.WriteJsonLine(_buffer, record);
                    }

                    var path = Path.Combine(Directory, group.Key.LoggerId, TrakHoundLogRecordFormat.GetFileName(group.Key.Date));
                    try
                    {
                        var stream = GetWriter(path, now);
                        stream.Write(_buffer.WrittenSpan);
                        stream.Flush();
                    }
                    catch (Exception ex)
                    {
                        InternalLogger.Error(ex, "TrakHound Log History : Error Writing {0}", path);
                        CloseWriter(path);
                    }
                }

                CloseIdleWriters(now);
            }

            UpdateLastSeen(records);
            SaveLoggerIndex();
        }

        private FileStream GetWriter(string path, DateTime now)
        {
            if (_writers.TryGetValue(path, out var writer))
            {
                _writers[path] = (writer.Stream, now);
                return writer.Stream;
            }

            var directory = Path.GetDirectoryName(path);
            if (!System.IO.Directory.Exists(directory)) System.IO.Directory.CreateDirectory(directory);

            // Shared so that Queries can read (and Maintenance can delete) while the file is open
            var isNew = !File.Exists(path);
            var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete);
            _writers[path] = (stream, now);

            if (isNew) InvalidateFileIndex(); // So that Queries find the new file
            return stream;
        }

        private void CloseWriter(string path)
        {
            if (_writers.Remove(path, out var writer))
            {
                try { writer.Stream.Dispose(); } catch { }
            }
        }

        private void CloseIdleWriters(DateTime now)
        {
            foreach (var path in _writers.Where(o => now - o.Value.LastUsed > _writerIdleTimeout).Select(o => o.Key).ToList())
            {
                CloseWriter(path);
            }
        }


        protected override IEnumerable<ITrakHoundLogSource> GetSources(TrakHoundLogQuery query, IReadOnlyList<TrakHoundLoggerInformation> loggers)
        {
            return GetFileSources(ArchiveDirectory, loggers).Concat(GetFileSources(Directory, loggers));
        }


        protected override void MaintainActive(TrakHoundLogConfiguration configuration, TrakHoundLogMaintenanceResult result)
        {
            result.ActiveSize = MaintainActiveFiles(configuration, result);
        }

        protected override void OnRemovingFile(string path)
        {
            lock (_writeLock)
            {
                // A file from a previous day can still be open (ex. just after midnight)
                CloseWriter(path);
            }
        }


        private void LoadLoggerIndex()
        {
            var path = Path.Combine(Directory, _loggerIndexFilename);
            if (!File.Exists(path)) return;

            try
            {
                var loggers = JsonSerializer.Deserialize<List<TrakHoundLoggerInformation>>(File.ReadAllText(path));
                if (loggers != null) SetLoggers(loggers);
            }
            catch (Exception ex)
            {
                InternalLogger.Warn(ex, "TrakHound Log History : Error Reading Logger Index {0}", path);
            }
        }

        protected override void OnSaveLoggers(IReadOnlyList<TrakHoundLoggerInformation> loggers)
        {
            var path = Path.Combine(Directory, _loggerIndexFilename);
            var tempPath = path + ".tmp";

            File.WriteAllText(tempPath, JsonSerializer.Serialize(loggers.OrderBy(o => o.Name), new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tempPath, path, true);
        }
    }
}
