// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Threading.Tasks;

namespace TrakHound
{
    /// <summary>
    /// A HashSet that is periodically written to disk by the PersistentHashSetManager
    /// </summary>
    public interface IPersistentHashSet
    {
        /// <summary>
        /// The unique Identifier of the HashSet (also used as the file name)
        /// </summary>
        string Value { get; }

        /// <summary>
        /// Writes the HashSet to disk if it has changed since the last Write
        /// </summary>
        Task Write();   
    }


    /// <summary>
    /// A thread-safe HashSet that is persisted to a GZip compressed file and can be recovered after a restart
    /// </summary>
    public class PersistentHashSet<TValue> : IPersistentHashSet
    {
        private const string _defaultBaseDirectoryName = "cache";
        private static readonly string _defaultBaseDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _defaultBaseDirectoryName);

        private readonly string _value;
        private readonly string _baseDirectory;
        private readonly Func<TValue, TValue, bool> _comparer;
        private readonly Func<string, TValue> _fromConverter;
        private readonly Func<TValue, string> _toConverter;
        private readonly HashSet<TValue> _entries = new HashSet<TValue>();
        private readonly object _lock = new object();
        private bool _isChanged;


        /// <inheritdoc/>
        public string Value => _value;


        /// <summary>
        /// Creates a new PersistentHashSet and registers it with the PersistentHashSetManager. The baseDirectory defaults to "cache" in the AppDomain BaseDirectory (relative paths are resolved against it), and the converters parse and format Values when reading and writing the file
        /// </summary>
        public PersistentHashSet(
            string id,
            string baseDirectory = null,
            Func<TValue, TValue, bool> comparer = null,
            Func<string, TValue> fromConverter = null,
            Func<TValue, string> toConverter = null
            )
        {
            _value = id;
            _comparer = comparer != null ? comparer : DefaultComparer;
            _fromConverter = fromConverter != null ? fromConverter : DefaultFromConverter;
            _toConverter = toConverter != null ? toConverter : DefaultToConverter;

            if (!string.IsNullOrEmpty(baseDirectory))
            {
                _baseDirectory = baseDirectory;
                if (!Path.IsPathRooted(_baseDirectory)) _baseDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, _baseDirectory);
            }
            else
            {
                _baseDirectory = _defaultBaseDirectory;
            }

            PersistentHashSetManager.AddHashSet(_value, this);
        }

        private static bool DefaultComparer(TValue existingValue, TValue newValue)
        {
            return EqualityComparer<TValue>.Default.Equals(existingValue, newValue);
        }

        private static TValue DefaultFromConverter(string inputValue)
        {
            try
            {
                return (TValue)Convert.ChangeType(inputValue, typeof(TValue));
            }
            catch { }

            return default;
        }

        private static string DefaultToConverter(TValue inputValue)
        {
            return inputValue?.ToString();
        }


        /// <summary>
        /// Returns true if the HashSet contains the specified Value
        /// </summary>
        public bool Contains(TValue value)
        { 
            if (value != null)
            {
                lock (_lock)
                {
                    return _entries.Contains(value);
                }
            }

            return false;
        }

        /// <summary>
        /// Adds the Value to the HashSet and marks it as changed
        /// </summary>
        public void Add(TValue value)
        {
            if (value != null)
            {
                lock (_lock)
                {
                    if (_entries.Contains(value))
                    {
                        _entries.Add(value);
                        _isChanged = true;
                    }
                }
            }
        }

        /// <summary>
        /// Removes the Value from the HashSet (returns true if the Value was found)
        /// </summary>
        public bool Remove(TValue value)
        {
            if (value != null)
            {
                lock (_lock)
                {
                    return _entries.Remove(value);
                }
            }

            return false;
        }


        /// <summary>
        /// Reads the Values previously written to disk and adds them to the HashSet
        /// </summary>
        public void Recover()
        {
            if (_value != null)
            {
                var pagePath = Path.Combine(_baseDirectory, _value);
                if (File.Exists(pagePath))
                {
                    var contentLines = ReadFile(pagePath);
                    if (contentLines != null)
                    {
                        foreach (var line in contentLines)
                        {
                            var delimeter = "|-|";
                            var i = line.IndexOf(delimeter);
                            if (i > 0 && i + 1 < line.Length)
                            {
                                Add(_fromConverter(line.Substring(i + delimeter.Length)));
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Writes the Values to the GZip compressed file (merged with the Values already in the file) if the HashSet has changed since the last Write
        /// </summary>
        public async Task Write()
        {
            HashSet<TValue> inputEntries = null;
            var write = false;

            lock (_lock)
            {
                if (_isChanged && _value != null)
                {
                    _isChanged = false;
                    inputEntries = new HashSet<TValue>(_entries);
                    write = true;
                }
            }

            if (write)
            {
                if (!Directory.Exists(_baseDirectory)) Directory.CreateDirectory(_baseDirectory);

                var pagePath = Path.Combine(_baseDirectory, _value);

                var writeEntries = new HashSet<TValue>();

                if (File.Exists(pagePath))
                {
                    var contentLines = ReadFile(pagePath);
                    if (contentLines != null)
                    {
                        foreach (var line in contentLines)
                        {
                            var i = line.IndexOf(':');
                            if (i > 0 && i + 1 < line.Length)
                            {
                                writeEntries.Add(_fromConverter(line.Substring(i + 1)));
                            }
                        }
                    }
                }

                // Add New Entries
                foreach (var entry in inputEntries)
                {
                    writeEntries.Add(entry);
                }

                // Create Write Data
                var writeLines = new List<string>();
                foreach (var entry in writeEntries)
                {
                    writeLines.Add(_toConverter(entry));
                }
                var writeContent = string.Join("\r\n", writeLines);


                await WriteFile(pagePath, writeContent);
            }
        }

        private string[] ReadFile(string path)
        {
            try
            {
                using (var fileStream = File.Open(path, FileMode.Open, FileAccess.Read))
                {
                    using (var readStream = new MemoryStream())
                    using (var gzipStream = new GZipStream(fileStream, CompressionMode.Decompress))
                    {
                        gzipStream.CopyTo(readStream);

                        var bytes = readStream.ToArray();

                        var s = System.Text.Encoding.UTF8.GetString(bytes);
                        if (!string.IsNullOrEmpty(s))
                        {
                            return s.Split(Environment.NewLine);
                        }
                    }
                }
            }
            catch { }

            return null;
        }

        private async Task<bool> WriteFile(string path, string content)
        {
            try
            {
                var inputBytes = System.Text.Encoding.UTF8.GetBytes(content);

                using (var stream = new MemoryStream())
                {
                    using (var gzipStream = new GZipStream(stream, CompressionMode.Compress))
                    {
                        await gzipStream.WriteAsync(inputBytes, 0, inputBytes.Length);
                        await gzipStream.FlushAsync();
                    }

                    var compressedBytes = stream.ToArray();
                    await File.WriteAllBytesAsync(path, compressedBytes);
                    return true;
                }
            }
            catch { }

            return false;
        }
    }

    /// <summary>
    /// Writes all registered PersistentHashSets to disk on an interval (default 30 seconds) and when the process exits
    /// </summary>
    public static class PersistentHashSetManager
    {
        private const int _defaultWriteInterval = 30000; // 30 Seconds

        private static readonly ListDictionary<string, IPersistentHashSet> _hashSets = new ListDictionary<string, IPersistentHashSet>();
        private static readonly List<string> _changed = new List<string>();
        private static readonly System.Timers.Timer _writeTimer;
        private static readonly object _lock = new object();


        static PersistentHashSetManager()
        {
            // Write to Disk at interval
            _writeTimer = new System.Timers.Timer();
            _writeTimer.Interval = _defaultWriteInterval;
            _writeTimer.Elapsed += (s, args) => _ = WriteAll(); // Timer events can't be awaited
            _writeTimer.Start();

            AppDomain.CurrentDomain.ProcessExit += Dispose;
        }

        /// <summary>
        /// Sets the interval (in milliseconds) that the HashSets are written to disk at
        /// </summary>
        public static void SetWriteInterval(int writeInterval)
        {
            if (writeInterval > 0)
            {
                _writeTimer.Stop();
                _writeTimer.Interval = writeInterval;
                _writeTimer.Start();
            }
        }

        /// <summary>
        /// Stops the write timer and writes all HashSets to disk (called when the process exits)
        /// </summary>
        public static async void Dispose(object sender, EventArgs args)
        {
            // async void : an exception would end the process, so nothing may escape
            try
            {
                if (_writeTimer != null) _writeTimer.Dispose();

                await WriteAll();
            }
            catch { }
        }


        /// <summary>
        /// Registers a HashSet to be written to disk (ignored if a HashSet with the same Id is already registered)
        /// </summary>
        public static void AddHashSet(string id, IPersistentHashSet hashSet)
        {
            if (id != null && hashSet != null)
            {
                lock (_lock)
                {
                    if (!_hashSets.ContainsKey(id)) _hashSets.Add(id, hashSet);
                }
            }
        }

        /// <summary>
        /// Gets all of the registered HashSets
        /// </summary>
        public static IEnumerable<IPersistentHashSet> Get()
        {
            var hashSets = new List<IPersistentHashSet>();

            lock (_lock)
            {
                foreach (var hashSet in _hashSets.Values)
                {
                    hashSets.Add(hashSet);
                }
            }

            return hashSets;
        }


        /// <summary>
        /// Writes each registered HashSet to disk (only HashSets that have changed are written)
        /// </summary>
        public static async Task WriteAll()
        {
            var hashSets = Get();
            foreach (var hashSet in hashSets)
            {
                await hashSet.Write();
            }
        }
    }
}
