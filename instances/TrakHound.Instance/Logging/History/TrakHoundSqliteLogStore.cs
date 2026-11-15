// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Data.Sqlite;
using NLog.Common;
using System.Text;
using TrakHound.Logging;

namespace TrakHound.Instance.Logging.History
{
    /// <summary>
    /// Stores Log History in a SQLite database ("{directory}/logs.db") indexed by time and Logger,
    /// with a trigram full-text index for case-insensitive substring search.
    /// Entries older than the Retention period are deleted or exported to the compressed Archive (which remains queryable).
    /// </summary>
    public class TrakHoundSqliteLogStore : TrakHoundLogStoreBase
    {
        public const string DatabaseFilename = "logs.db";

        private const int _ftsMinimumSearchLength = 3; // Trigram tokenizer
        private const int _deleteChunkSize = 5000; // Rows deleted per lock so that Writes can continue during Maintenance

        private readonly string _connectionString;
        private readonly SqliteConnection _writeConnection;
        private readonly Dictionary<string, long> _loggerKeys = new Dictionary<string, long>(); // Logger ID => Key
        private readonly object _writeLock = new object();
        private readonly bool _ftsEnabled;


        public string DatabasePath { get; }


        public TrakHoundSqliteLogStore(string directory) : base(directory)
        {
            DatabasePath = Path.Combine(Directory, DatabaseFilename);
            _connectionString = new SqliteConnectionStringBuilder { DataSource = DatabasePath, Pooling = true }.ToString();

            _writeConnection = new SqliteConnection(_connectionString);
            _writeConnection.Open();

            Execute(_writeConnection, "PRAGMA auto_vacuum = INCREMENTAL;"); // Only applies to a new database (before tables are created)
            Execute(_writeConnection, "PRAGMA journal_mode = WAL;");
            Execute(_writeConnection, "PRAGMA synchronous = NORMAL;");

            Execute(_writeConnection, @"
                CREATE TABLE IF NOT EXISTS loggers (
                    key INTEGER PRIMARY KEY,
                    id TEXT NOT NULL UNIQUE,
                    name TEXT NOT NULL,
                    first_seen INTEGER NOT NULL DEFAULT 0,
                    last_seen INTEGER NOT NULL DEFAULT 0
                );
                CREATE TABLE IF NOT EXISTS entries (
                    id INTEGER PRIMARY KEY,
                    ts INTEGER NOT NULL,
                    level INTEGER NOT NULL,
                    logger INTEGER NOT NULL,
                    sender TEXT,
                    code TEXT,
                    message TEXT,
                    exception TEXT
                );
                CREATE INDEX IF NOT EXISTS ix_entries_ts ON entries (ts);
                CREATE INDEX IF NOT EXISTS ix_entries_logger_ts_level ON entries (logger, ts, level);
                DROP INDEX IF EXISTS ix_entries_logger_ts;"); // Replaced by ix_entries_logger_ts_level (Level included so that Counts are read from the index only)

            _ftsEnabled = CreateFullTextIndex();

            LoadLoggers();

            // Log files in the same directory (legacy NLog files, or from the File provider) remain queryable
            DiscoverLoggerDirectories(Directory);
            DiscoverLoggerDirectories(ArchiveDirectory);
            PruneLoggers();
        }

        public override void Dispose()
        {
            base.Dispose();

            lock (_writeLock)
            {
                try
                {
                    Execute(_writeConnection, "PRAGMA wal_checkpoint(TRUNCATE);");
                    _writeConnection.Dispose();
                }
                catch { }
            }

            SqliteConnection.ClearAllPools();
        }


        private bool CreateFullTextIndex()
        {
            try
            {
                // External content FTS5 table kept in sync by triggers
                Execute(_writeConnection, @"
                    CREATE VIRTUAL TABLE IF NOT EXISTS entries_fts USING fts5 (message, exception, content = 'entries', content_rowid = 'id', tokenize = 'trigram');
                    CREATE TRIGGER IF NOT EXISTS entries_ai AFTER INSERT ON entries BEGIN
                        INSERT INTO entries_fts (rowid, message, exception) VALUES (new.id, new.message, new.exception);
                    END;
                    CREATE TRIGGER IF NOT EXISTS entries_ad AFTER DELETE ON entries BEGIN
                        INSERT INTO entries_fts (entries_fts, rowid, message, exception) VALUES ('delete', old.id, old.message, old.exception);
                    END;");

                return true;
            }
            catch (Exception ex)
            {
                // Search falls back to LIKE
                InternalLogger.Warn(ex, "TrakHound Log History : SQLite Full-Text Search not available");
                return false;
            }
        }

        private void LoadLoggers()
        {
            var loggers = new List<TrakHoundLoggerInformation>();

            using (var command = _writeConnection.CreateCommand())
            {
                command.CommandText = "SELECT key, id, name, first_seen, last_seen FROM loggers;";
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var id = reader.GetString(1);
                        _loggerKeys[id] = reader.GetInt64(0);
                        loggers.Add(new TrakHoundLoggerInformation { Id = id, Name = reader.GetString(2), FirstSeen = reader.GetInt64(3), LastSeen = reader.GetInt64(4) });
                    }
                }
            }

            SetLoggers(loggers);
        }

        protected override void OnSaveLoggers(IReadOnlyList<TrakHoundLoggerInformation> loggers)
        {
            lock (_writeLock)
            {
                using (var transaction = _writeConnection.BeginTransaction())
                using (var command = _writeConnection.CreateCommand())
                {
                    command.Transaction = transaction;
                    command.CommandText = @"
                        INSERT INTO loggers (id, name, first_seen, last_seen) VALUES ($id, $name, $first, $last)
                        ON CONFLICT (id) DO UPDATE SET name = excluded.name, first_seen = excluded.first_seen, last_seen = MAX(last_seen, excluded.last_seen);";

                    var id = command.Parameters.Add("$id", SqliteType.Text);
                    var name = command.Parameters.Add("$name", SqliteType.Text);
                    var first = command.Parameters.Add("$first", SqliteType.Integer);
                    var last = command.Parameters.Add("$last", SqliteType.Integer);

                    foreach (var logger in loggers)
                    {
                        id.Value = logger.Id;
                        name.Value = logger.Name ?? logger.Id;
                        first.Value = logger.FirstSeen;
                        last.Value = logger.LastSeen;
                        command.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
            }
        }

        private long GetLoggerKey(string loggerId, string loggerName, long timestamp)
        {
            if (_loggerKeys.TryGetValue(loggerId, out var key)) return key;

            using (var command = _writeConnection.CreateCommand())
            {
                command.CommandText = @"
                    INSERT INTO loggers (id, name, first_seen, last_seen) VALUES ($id, $name, $ts, $ts) ON CONFLICT (id) DO NOTHING;
                    SELECT key FROM loggers WHERE id = $id;";
                command.Parameters.AddWithValue("$id", loggerId);
                command.Parameters.AddWithValue("$name", loggerName ?? loggerId);
                command.Parameters.AddWithValue("$ts", timestamp);

                key = (long)command.ExecuteScalar();
                _loggerKeys[loggerId] = key;
                return key;
            }
        }


        public override void Write(IReadOnlyList<TrakHoundLogRecord> records)
        {
            if (records == null || records.Count == 0) return;

            lock (_writeLock)
            {
                try
                {
                    using (var transaction = _writeConnection.BeginTransaction())
                    using (var command = _writeConnection.CreateCommand())
                    {
                        command.Transaction = transaction;
                        command.CommandText = "INSERT INTO entries (ts, level, logger, sender, code, message, exception) VALUES ($ts, $level, $logger, $sender, $code, $message, $exception);";

                        var ts = command.Parameters.Add("$ts", SqliteType.Integer);
                        var level = command.Parameters.Add("$level", SqliteType.Integer);
                        var logger = command.Parameters.Add("$logger", SqliteType.Integer);
                        var sender = command.Parameters.Add("$sender", SqliteType.Text);
                        var code = command.Parameters.Add("$code", SqliteType.Text);
                        var message = command.Parameters.Add("$message", SqliteType.Text);
                        var exception = command.Parameters.Add("$exception", SqliteType.Text);

                        foreach (var record in records)
                        {
                            ts.Value = record.Timestamp;
                            level.Value = (int)record.Level;
                            logger.Value = GetLoggerKey(record.LoggerId, record.LoggerName, record.Timestamp);
                            sender.Value = record.Sender != null && record.Sender != record.LoggerName ? record.Sender : DBNull.Value; // Same as the Logger Name in most entries
                            code.Value = (object)record.Code ?? DBNull.Value;
                            message.Value = (object)record.Message ?? DBNull.Value;
                            exception.Value = (object)record.Exception ?? DBNull.Value;
                            command.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                }
                catch (Exception ex)
                {
                    InternalLogger.Error(ex, "TrakHound Log History : Error Writing to {0}", DatabasePath);
                }
            }

            UpdateLastSeen(records);
            SaveLoggerIndex();
        }


        protected override IEnumerable<ITrakHoundLogSource> GetSources(TrakHoundLogQuery query, IReadOnlyList<TrakHoundLoggerInformation> loggers)
        {
            var sources = GetFileSources(ArchiveDirectory, loggers).Concat(GetFileSources(Directory, loggers)).ToList();
            sources.Add(new TrakHoundSqliteLogSource(this, GetLoggerKeys(query, loggers)));
            return sources;
        }

        public override TrakHoundLoggerLogCount[] QueryCounts(TrakHoundLogQuery query)
        {
            if (query == null) return null;

            var loggers = ResolveLoggers(query);
            if (loggers.Count == 0) return Array.Empty<TrakHoundLoggerLogCount>();

            var counts = new Dictionary<string, TrakHoundLoggerLogCount>();
            var senderLoggerIds = GetSenderLoggerIds(query, loggers);
            var senderLoggers = senderLoggerIds != null ? loggers.Where(o => senderLoggerIds.Contains(o.Id)).ToList() : new List<TrakHoundLoggerInformation>();
            var countedLoggers = senderLoggerIds != null ? loggers.Where(o => !senderLoggerIds.Contains(o.Id)).ToList() : loggers;

            // Database (counted by SQL). The Loggers that only match by Sender are counted below, since their Senders are matched outside of SQL.
            var filter = new TrakHoundLogFilter(query, TrakHoundLogQueryEngine.GetFrom(query), TrakHoundLogQueryEngine.GetTo(query));
            if (countedLoggers.Count > 0)
            {
                using (var connection = OpenReadConnection())
                using (var command = connection.CreateCommand())
                {
                    var where = BuildWhere(command, filter, GetLoggerKeys(query, countedLoggers));
                    command.CommandText = $"SELECT l.id, l.name, e.level, COUNT(*) FROM entries e JOIN loggers l ON l.key = e.logger WHERE {where} GROUP BY e.logger, e.level;";

                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var loggerId = reader.GetString(0);
                            if (!counts.TryGetValue(loggerId, out var count))
                            {
                                count = new TrakHoundLoggerLogCount { LoggerId = loggerId, LoggerName = reader.GetString(1) };
                                counts.Add(loggerId, count);
                            }
                            count.Counts[(TrakHoundLogLevel)reader.GetInt32(2)] = reader.GetInt64(3);
                        }
                    }
                }
            }

            // Database entries of the Loggers that only match by Sender
            if (senderLoggers.Count > 0)
            {
                var senderSource = new TrakHoundSqliteLogSource(this, GetLoggerKeys(query, senderLoggers));
                TrakHoundLogQueryEngine.AddCounts(counts, TrakHoundLogQueryEngine.Count(new[] { senderSource }, query, senderLoggerIds));
            }

            // Archive and Log files
            TrakHoundLogQueryEngine.AddCounts(counts, TrakHoundLogQueryEngine.Count(GetFileSources(ArchiveDirectory, loggers).Concat(GetFileSources(Directory, loggers)), query, senderLoggerIds));

            return counts.Values.OrderBy(o => o.LoggerName ?? o.LoggerId).ToArray();
        }

        /// <summary>
        /// The Logger Keys to filter by (null for all Loggers)
        /// </summary>
        private IReadOnlyList<long> GetLoggerKeys(TrakHoundLogQuery query, IReadOnlyList<TrakHoundLoggerInformation> loggers)
        {
            if (TrakHoundLoggerPattern.IsMatchAll(query.Patterns)) return null;

            lock (_writeLock)
            {
                return loggers.Select(o => _loggerKeys.TryGetValue(o.Id, out var key) ? key : -1).Where(o => o >= 0).ToList();
            }
        }

        internal SqliteConnection OpenReadConnection()
        {
            var connection = new SqliteConnection(_connectionString);
            connection.Open();
            return connection;
        }

        internal string BuildWhere(SqliteCommand command, TrakHoundLogFilter filter, IReadOnlyList<long> loggerKeys)
        {
            var where = new StringBuilder("e.ts >= $from AND e.ts <= $to");
            command.Parameters.AddWithValue("$from", filter.From);
            command.Parameters.AddWithValue("$to", filter.To);

            if (loggerKeys != null)
            {
                // Logger Keys are integers from the database (not user input)
                where.Append(loggerKeys.Count > 0 ? $" AND e.logger IN ({string.Join(',', loggerKeys)})" : " AND 0");
            }

            var levels = Enum.GetValues<TrakHoundLogLevel>().Where(filter.Query.IsLevelMatch).Select(o => (int)o).ToList();
            if (levels.Count < Enum.GetValues<TrakHoundLogLevel>().Length)
            {
                where.Append(levels.Count > 0 ? $" AND e.level IN ({string.Join(',', levels)})" : " AND 0");
            }

            if (!string.IsNullOrEmpty(filter.Query.Code))
            {
                where.Append(" AND e.code = $code COLLATE NOCASE");
                command.Parameters.AddWithValue("$code", filter.Query.Code);
            }

            if (!string.IsNullOrEmpty(filter.Query.Sender))
            {
                // The Sender is only stored when it differs from the Logger Name (both queries that use this join the loggers table)
                where.Append(" AND COALESCE(e.sender, l.name) = $sender COLLATE NOCASE");
                command.Parameters.AddWithValue("$sender", filter.Query.Sender);
            }

            var search = filter.Query.Search;
            if (!string.IsNullOrEmpty(search))
            {
                if (IsFullTextSearch(filter))
                {
                    // Quoted as a single phrase (substring match with the trigram tokenizer)
                    where.Append(" AND e.id IN (SELECT rowid FROM entries_fts WHERE entries_fts MATCH $search)");
                    command.Parameters.AddWithValue("$search", "\"" + search.Replace("\"", "\"\"") + "\"");
                }
                else
                {
                    where.Append(" AND (e.message LIKE $search ESCAPE '\\' OR e.exception LIKE $search ESCAPE '\\')");
                    command.Parameters.AddWithValue("$search", "%" + search.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%");
                }
            }

            return where.ToString();
        }

        internal bool IsFullTextSearch(TrakHoundLogFilter filter)
        {
            var search = filter.Query.Search;
            return _ftsEnabled && !string.IsNullOrEmpty(search) && search.Length >= _ftsMinimumSearchLength;
        }

        internal static TrakHoundLogRecord ReadRecord(SqliteDataReader reader)
        {
            // Columns : ts, level, logger id, logger name, sender, code, message, exception
            var record = new TrakHoundLogRecord();
            record.Timestamp = reader.GetInt64(0);
            record.Level = (TrakHoundLogLevel)reader.GetInt32(1);
            record.LoggerId = reader.GetString(2);
            record.LoggerName = reader.GetString(3);
            record.Sender = reader.IsDBNull(4) ? record.LoggerName : reader.GetString(4);
            record.Code = reader.IsDBNull(5) ? null : reader.GetString(5);
            record.Message = reader.IsDBNull(6) ? null : reader.GetString(6);
            record.Exception = reader.IsDBNull(7) ? null : reader.GetString(7);
            return record;
        }

        internal const string RecordColumns = "e.ts, e.level, l.id, l.name, e.sender, e.code, e.message, e.exception";


        protected override void MaintainActive(TrakHoundLogConfiguration configuration, TrakHoundLogMaintenanceResult result)
        {
            var compress = configuration.ArchiveModeType == TrakHoundLogArchiveMode.Compress;
            var today = DateTime.UtcNow.Date;
            var changed = false;

            // Retention : Archive (or Delete) days older than the Retention period
            var cutoff = GetRetentionCutoff(configuration);
            if (cutoff.HasValue)
            {
                DateTime? day;
                while ((day = GetOldestDay()).HasValue && day.Value < cutoff.Value)
                {
                    RemoveDay(day.Value, compress, result);
                    changed = true;
                }
            }

            // Size Limit : Archive (or Delete) the oldest days (never today) until under the limit
            if (configuration.MaxSizeMB > 0)
            {
                var maxSize = configuration.MaxSizeMB * 1048576;
                DateTime? day;
                // The size excludes free pages, so it drops as each day is removed (compacted once below)
                while (GetDatabaseSize() > maxSize && (day = GetOldestDay()).HasValue && day.Value < today)
                {
                    RemoveDay(day.Value, compress, result);
                    changed = true;
                }
            }

            if (changed) Vacuum();

            // Log files in the directory (legacy NLog files, or from the File provider)
            var databaseSize = GetDatabaseSize();
            result.ActiveSize = databaseSize + MaintainActiveFiles(configuration, result, databaseSize);
        }

        protected override bool IsLoggerStored(string loggerId)
        {
            lock (_writeLock)
            {
                return _loggerKeys.ContainsKey(loggerId);
            }
        }

        private DateTime? GetOldestDay()
        {
            using (var connection = OpenReadConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT MIN(ts) FROM entries;";
                var value = command.ExecuteScalar();
                return value is long ts ? ts.ToDateTime().Date : null;
            }
        }

        private void RemoveDay(DateTime utcDay, bool compress, TrakHoundLogMaintenanceResult result)
        {
            var from = DateTime.SpecifyKind(utcDay, DateTimeKind.Utc).ToUnixTime();
            var to = DateTime.SpecifyKind(utcDay.AddDays(1), DateTimeKind.Utc).ToUnixTime();

            if (compress)
            {
                // Export one Archive file per Logger for the day
                using (var connection = OpenReadConnection())
                using (var command = connection.CreateCommand())
                {
                    command.CommandText = $"SELECT {RecordColumns} FROM entries e JOIN loggers l ON l.key = e.logger WHERE e.ts >= $from AND e.ts < $to ORDER BY e.logger, e.ts, e.id;";
                    command.Parameters.AddWithValue("$from", from);
                    command.Parameters.AddWithValue("$to", to);

                    using (var reader = command.ExecuteReader())
                    {
                        var hasRow = reader.Read();

                        // Streamed from the reader (a day for a Logger can be too large to hold in memory)
                        IEnumerable<TrakHoundLogRecord> ReadLoggerRecords(string loggerId)
                        {
                            while (hasRow && reader.GetString(2) == loggerId)
                            {
                                yield return ReadRecord(reader);
                                hasRow = reader.Read();
                            }
                        }

                        while (hasRow)
                        {
                            var loggerId = reader.GetString(2);
                            WriteArchive(loggerId, utcDay, ReadLoggerRecords(loggerId));
                            result.Archived++;

                            // Always move on to the next Logger (even if the Archive didn't read every record)
                            while (hasRow && reader.GetString(2) == loggerId) hasRow = reader.Read();
                        }
                    }
                }
            }

            // Deleted in chunks (each with its own lock) so that Writes aren't blocked for the whole day
            var deleted = 0;
            int chunk;
            do
            {
                lock (_writeLock)
                {
                    using (var command = _writeConnection.CreateCommand())
                    {
                        command.CommandText = $"DELETE FROM entries WHERE id IN (SELECT id FROM entries WHERE ts >= $from AND ts < $to LIMIT {_deleteChunkSize});";
                        command.Parameters.AddWithValue("$from", from);
                        command.Parameters.AddWithValue("$to", to);
                        chunk = command.ExecuteNonQuery();
                    }
                }
                deleted += chunk;
            }
            while (chunk > 0);

            if (!compress) result.Deleted += deleted;
        }

        private long GetDatabaseSize()
        {
            using (var connection = OpenReadConnection())
            using (var command = connection.CreateCommand())
            {
                command.CommandText = "SELECT (page_count - freelist_count) * page_size FROM pragma_page_count(), pragma_freelist_count(), pragma_page_size();";
                return Convert.ToInt64(command.ExecuteScalar());
            }
        }

        private void Vacuum()
        {
            lock (_writeLock)
            {
                try
                {
                    Execute(_writeConnection, "PRAGMA incremental_vacuum;");
                    Execute(_writeConnection, "PRAGMA wal_checkpoint(TRUNCATE);");
                }
                catch (Exception ex)
                {
                    InternalLogger.Warn(ex, "TrakHound Log History : Error Compacting {0}", DatabasePath);
                }
            }
        }

        private static void Execute(SqliteConnection connection, string sql)
        {
            using (var command = connection.CreateCommand())
            {
                command.CommandText = sql;
                command.ExecuteNonQuery();
            }
        }
    }


    /// <summary>
    /// Reads Log History entries from the SQLite database (in time order, using the time / Logger indexes)
    /// </summary>
    public class TrakHoundSqliteLogSource : ITrakHoundLogSource
    {
        private readonly TrakHoundSqliteLogStore _store;
        private readonly IReadOnlyList<long> _loggerKeys;

        public long MinTimestamp => long.MinValue;

        public long MaxTimestamp => long.MaxValue;


        public TrakHoundSqliteLogSource(TrakHoundSqliteLogStore store, IReadOnlyList<long> loggerKeys)
        {
            _store = store;
            _loggerKeys = loggerKeys;
        }


        public IEnumerable<TrakHoundLogRecord> Read(TrakHoundLogFilter filter, bool descending)
        {
            using (var connection = _store.OpenReadConnection())
            using (var command = connection.CreateCommand())
            {
                var where = _store.BuildWhere(command, filter, _loggerKeys);
                var direction = descending ? "DESC" : "ASC";
                command.CommandText = $"SELECT {TrakHoundSqliteLogStore.RecordColumns} FROM entries e JOIN loggers l ON l.key = e.logger WHERE {where} ORDER BY e.ts {direction}, e.id {direction};";

                // Streamed : only the rows needed for the page are read
                using (var reader = command.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        // Senders are matched to Patterns here (not in SQL), for the Loggers that only match by Sender
                        var record = TrakHoundSqliteLogStore.ReadRecord(reader);
                        if (filter.IsSenderMatch(record)) yield return record;
                    }
                }
            }
        }
    }
}
