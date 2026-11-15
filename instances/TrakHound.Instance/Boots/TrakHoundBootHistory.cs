// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Instances;

namespace TrakHound.Instance.Boots
{
    /// <summary>
    /// The recent Boots of the Instance, kept in a JSON file in the Configuration Profile.
    /// Kept apart from Log History so it still works when Log History is disabled or can't be written.
    /// Two processes can write it at once (an IIS Overlapped Recycle), so each change is made under a Lock File and the file is replaced, not rewritten
    /// </summary>
    public class TrakHoundBootHistory
    {
        public const string Filename = "boots.json";
        public const int DefaultMaxBoots = 100;

        /// <summary>
        /// The most Runtime Starts kept for a Boot (the first Start is always kept, then the most recent Restarts)
        /// </summary>
        public const int MaxStarts = 50;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };
        private static readonly TimeSpan _lockTimeout = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan _retryInterval = TimeSpan.FromMilliseconds(25);

        private readonly string _path;
        private readonly int _maxBoots;
        private readonly object _lock = new object();
        private List<TrakHoundInstanceBoot> _cache;
        private DateTime _cacheLastWrite;


        public string Path => _path;


        public TrakHoundBootHistory(string path, int maxBoots = DefaultMaxBoots)
        {
            _path = path;
            _maxBoots = Math.Max(1, maxBoots);
        }


        /// <summary>
        /// The Boots, newest first
        /// </summary>
        public IReadOnlyList<TrakHoundInstanceBoot> GetBoots()
        {
            lock (_lock)
            {
                try
                {
                    // Read again only when the file has changed (another process may have written it)
                    var lastWrite = File.Exists(_path) ? File.GetLastWriteTimeUtc(_path) : DateTime.MinValue;
                    if (_cache == null || lastWrite != _cacheLastWrite)
                    {
                        _cache = WithRetry(() => Read()).OrderByDescending(o => o.Started).ToList();
                        _cacheLastWrite = lastWrite;
                    }
                }
                catch
                {
                    _cache ??= new List<TrakHoundInstanceBoot>();
                }

                return _cache;
            }
        }

        public TrakHoundInstanceBoot GetBoot(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            return GetBoots().FirstOrDefault(o => o.Id == id);
        }

        /// <summary>
        /// Adds or replaces the Boot with the same ID
        /// </summary>
        public void Save(TrakHoundInstanceBoot boot)
        {
            if (boot == null || string.IsNullOrEmpty(boot.Id)) return;

            Update(boots =>
            {
                var index = boots.FindIndex(o => o.Id == boot.Id);
                if (index >= 0) boots[index] = boot;
                else boots.Add(boot);
            });
        }

        /// <summary>
        /// Reads the Boots, applies the change, trims the oldest, and writes the file (under the Lock File, so changes from another process aren't lost)
        /// </summary>
        public void Update(Action<List<TrakHoundInstanceBoot>> update)
        {
            if (update == null) return;

            lock (_lock)
            {
                var directory = System.IO.Path.GetDirectoryName(_path);
                if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

                using var fileLock = AcquireLock();

                List<TrakHoundInstanceBoot> boots;
                try { boots = Read(); }
                catch { boots = new List<TrakHoundInstanceBoot>(); } // Unreadable (ex. cut short by a Power Loss) : start a new History

                update(boots);

                boots = boots.Where(o => o != null).OrderByDescending(o => o.Started).Take(_maxBoots).OrderBy(o => o.Started).ToList();
                foreach (var boot in boots) TrimStarts(boot);

                // Written to a temporary file then moved over the History, so a reader never sees a partly written file
                var tempPath = _path + ".tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(boots, _jsonOptions));

                // Readers notice a change by its Last Write Time, and two writes close together can get the same one
                var previousWrite = File.GetLastWriteTimeUtc(_path);
                if (File.GetLastWriteTimeUtc(tempPath) <= previousWrite) File.SetLastWriteTimeUtc(tempPath, previousWrite.AddTicks(1));

                File.Move(tempPath, _path, true);

                _cache = null;
            }
        }


        private List<TrakHoundInstanceBoot> Read()
        {
            if (!File.Exists(_path)) return new List<TrakHoundInstanceBoot>();

            var json = File.ReadAllText(_path);
            if (string.IsNullOrWhiteSpace(json)) return new List<TrakHoundInstanceBoot>();

            return JsonSerializer.Deserialize<List<TrakHoundInstanceBoot>>(json, _jsonOptions)?.Where(o => o != null).ToList() ?? new List<TrakHoundInstanceBoot>();
        }

        private static void TrimStarts(TrakHoundInstanceBoot boot)
        {
            if (boot.Starts == null || boot.Starts.Count <= MaxStarts) return;

            // Keep the first Start (part of the Boot) and the most recent Restarts
            boot.Starts.RemoveRange(1, boot.Starts.Count - MaxStarts);
        }

        private FileStream AcquireLock()
        {
            var lockPath = _path + ".lock";
            var deadline = DateTime.UtcNow + _lockTimeout;

            while (true)
            {
                try
                {
                    return new FileStream(lockPath, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    Thread.Sleep(_retryInterval);
                }
            }
        }

        private static T WithRetry<T>(Func<T> read)
        {
            var deadline = DateTime.UtcNow + _lockTimeout;

            while (true)
            {
                try
                {
                    return read();
                }
                catch (IOException) when (DateTime.UtcNow < deadline)
                {
                    // The file is being replaced by a writer
                    Thread.Sleep(_retryInterval);
                }
            }
        }
    }
}
