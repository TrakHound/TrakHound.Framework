// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.Instance.Audit
{
    public static class TrakHoundAuditActions
    {
        public const string Baseline = "Baseline";
        public const string Added = "Added";
        public const string Changed = "Changed";
        public const string Removed = "Removed";
    }

    public static class TrakHoundAuditSources
    {
        public const string Admin = "Admin";
        public const string App = "App";
        public const string SystemApi = "SystemApi";
        public const string Module = "Module";

        /// <summary>
        /// Made by the Instance itself, with no caller (ex. a Package Update)
        /// </summary>
        public const string Instance = "Instance";

        /// <summary>
        /// Found by comparing what's on disk, so who made the change isn't known (ex. a file edited directly, or a Deployment installed while Stopped)
        /// </summary>
        public const string Detected = "Detected";
    }

    public class TrakHoundAuditRecord
    {
        public long Sequence { get; set; }
        public DateTime Timestamp { get; set; }
        public string Action { get; set; }

        /// <summary>
        /// What changed (ex. "package/function/My.Package/1.0.0" or "configuration/worker/my-worker.yaml")
        /// </summary>
        public string Target { get; set; }

        public string Source { get; set; }
        public string User { get; set; }
        public string ClientAddress { get; set; }

        /// <summary>
        /// SHA-256 of the file before and after the change. Contents aren't stored since Configuration files can hold Secrets
        /// </summary>
        public string PreviousContentHash { get; set; }
        public string ContentHash { get; set; }

        public string PreviousHash { get; set; }

        /// <summary>
        /// SHA-256 of this Record (without this property), which includes the Hash of the Record before it
        /// </summary>
        public string Hash { get; set; }
    }

    public class TrakHoundAuditVerification
    {
        public bool Valid { get; set; }
        public long Records { get; set; }

        /// <summary>
        /// The line of the file where the Hash Chain first breaks
        /// </summary>
        public long? FailedLine { get; set; }

        public string Message { get; set; }
    }


    /// <summary>
    /// An append only file of changes (JSON Lines) where each Record includes the Hash of the Record before it, so editing or removing a Record
    /// breaks the chain. Kept apart from Log History so it isn't affected by Log settings or Retention.
    /// Two processes can write it at once (an IIS Overlapped Recycle), so each change is made under a Lock File.
    /// </summary>
    public class TrakHoundAuditLog
    {
        public const string Filename = "audit.jsonl";
        public const string SnapshotFilename = "audit.snapshot.json";
        public const string LockFilename = "audit.lock";

        private const int _tailLength = 65536;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        private static readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan _retryInterval = TimeSpan.FromMilliseconds(25);

        private readonly string _path;
        private readonly string _snapshotPath;
        private readonly string _lockPath;
        private readonly object _lock = new object();


        public string Path => _path;


        public TrakHoundAuditLog(string directory)
        {
            _path = System.IO.Path.Combine(directory, Filename);
            _snapshotPath = System.IO.Path.Combine(directory, SnapshotFilename);
            _lockPath = System.IO.Path.Combine(directory, LockFilename);
        }


        /// <summary>
        /// Under the Lock File : gives the Snapshot of the last known state (Target => Content Hash) to the change, and whether there was one,
        /// appends the Records it returns, and saves the Snapshot it updated
        /// </summary>
        public void Update(Func<Dictionary<string, string>, bool, IEnumerable<TrakHoundAuditRecord>> change)
        {
            lock (_lock)
            {
                var directory = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                using var fileLock = AcquireLock();

                var snapshot = ReadSnapshot();
                var exists = snapshot != null;
                snapshot ??= new Dictionary<string, string>();

                var records = change(snapshot, exists)?.ToList();
                if (!records.IsNullOrEmpty()) Append(records);

                var tempPath = _snapshotPath + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(snapshot));
                File.Move(tempPath, _snapshotPath, true);
            }
        }

        /// <summary>
        /// The most recent Records, newest first, optionally only those with a Target or User containing the search text
        /// </summary>
        public IReadOnlyList<TrakHoundAuditRecord> GetRecords(int take = 100, string search = null)
        {
            var records = new Queue<TrakHoundAuditRecord>();
            if (take < 1) return records.ToList();

            foreach (var line in ReadLines())
            {
                var record = Parse(line);
                if (record == null || !Matches(record, search)) continue;

                records.Enqueue(record);
                if (records.Count > take) records.Dequeue();
            }

            return records.Reverse().ToList();
        }

        /// <summary>
        /// Checks the Hash Chain from the first Record. Removing Records from the end, or replacing the whole file, can't be detected
        /// from the file alone (it takes a copy kept somewhere else)
        /// </summary>
        public TrakHoundAuditVerification Verify()
        {
            var result = new TrakHoundAuditVerification { Valid = true };

            string previousHash = null;
            long lineNumber = 0;

            foreach (var line in ReadLines())
            {
                lineNumber++;

                var record = Parse(line);
                string problem = null;
                if (record == null) problem = "the line can't be read";
                else if (record.Sequence != lineNumber) problem = $"Sequence {record.Sequence} was expected to be {lineNumber}";
                else if (record.PreviousHash != previousHash) problem = "it doesn't follow the Record before it";
                else if (record.Hash != ComputeHash(record)) problem = "its contents don't match its Hash";

                if (problem != null)
                {
                    result.Valid = false;
                    result.FailedLine = lineNumber;
                    result.Message = $"Line {lineNumber} failed : {problem}";
                    return result;
                }

                previousHash = record.Hash;
                result.Records++;
            }

            result.Message = $"{result.Records} Record(s) verified";
            return result;
        }

        public static string HashFile(string path)
        {
            try
            {
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                return Convert.ToHexStringLower(SHA256.HashData(stream));
            }
            catch (FileNotFoundException) { return null; }
            catch (DirectoryNotFoundException) { return null; }
        }


        private void Append(List<TrakHoundAuditRecord> records)
        {
            var (last, endsWithNewLine) = ReadLast();

            var text = new StringBuilder();
            if (!endsWithNewLine) text.Append('\n'); // Cut short by a Power Loss (Verify reports the line)

            foreach (var record in records)
            {
                record.Sequence = (last?.Sequence ?? 0) + 1;
                record.PreviousHash = last?.Hash;
                record.Hash = ComputeHash(record);

                text.Append(JsonSerializer.Serialize(record, _jsonOptions));
                text.Append('\n');

                last = record;
            }

            using var stream = new FileStream(_path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
            var bytes = new UTF8Encoding(false).GetBytes(text.ToString());
            stream.Write(bytes);
            stream.Flush(true);
        }

        private (TrakHoundAuditRecord Record, bool EndsWithNewLine) ReadLast()
        {
            if (!File.Exists(_path)) return (null, true);

            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            if (stream.Length == 0) return (null, true);

            var start = Math.Max(0, stream.Length - _tailLength);
            stream.Seek(start, SeekOrigin.Begin);

            var buffer = new byte[stream.Length - start];
            stream.ReadExactly(buffer);

            var text = Encoding.UTF8.GetString(buffer);
            var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            // The first line may be only the end of a line when the file is longer than what was read
            var first = start > 0 ? 1 : 0;
            for (var i = lines.Length - 1; i >= first; i--)
            {
                var record = Parse(lines[i]);
                if (record != null) return (record, text.EndsWith('\n'));
            }

            return (null, text.EndsWith('\n'));
        }

        private IEnumerable<string> ReadLines()
        {
            if (!File.Exists(_path)) yield break;

            using var stream = new FileStream(_path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using var reader = new StreamReader(stream, Encoding.UTF8);

            string line;
            while ((line = reader.ReadLine()) != null)
            {
                if (!string.IsNullOrEmpty(line)) yield return line;
            }
        }

        private Dictionary<string, string> ReadSnapshot()
        {
            try
            {
                if (!File.Exists(_snapshotPath)) return null;
                return JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(_snapshotPath));
            }
            catch (JsonException)
            {
                return null; // Unreadable : starts again from a new Baseline, which is recorded
            }
        }

        private static TrakHoundAuditRecord Parse(string line)
        {
            try { return JsonSerializer.Deserialize<TrakHoundAuditRecord>(line, _jsonOptions); }
            catch (JsonException) { return null; }
        }

        private static string ComputeHash(TrakHoundAuditRecord record)
        {
            var hash = record.Hash;
            record.Hash = null;
            try
            {
                var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(record, _jsonOptions));
                return Convert.ToHexStringLower(SHA256.HashData(bytes));
            }
            finally
            {
                record.Hash = hash;
            }
        }

        private static bool Matches(TrakHoundAuditRecord record, string search)
        {
            if (string.IsNullOrEmpty(search)) return true;
            return (record.Target?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false) || (record.User?.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false);
        }

        private FileStream AcquireLock()
        {
            var deadline = DateTime.UtcNow + _lockTimeout;

            while (true)
            {
                try
                {
                    return new FileStream(_lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(_retryInterval);
                }
            }
        }
    }
}
