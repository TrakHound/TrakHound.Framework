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
    /// A Dictionary that is periodically written to disk by the PersistentDictionaryManager
    /// </summary>
    public interface IPersistentDictionary
    {
        /// <summary>
        /// The unique Identifier of the Dictionary (also used as the file name)
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The number of Keys added to the Dictionary
        /// </summary>
        long KeyCount { get; }

        /// <summary>
        /// Writes the Dictionary to disk if it has changed since the last Write
        /// </summary>
        Task Write();   
    }


    /// <summary>
    /// A thread-safe Dictionary that is persisted to a GZip compressed file and can be recovered after a restart
    /// </summary>
    public class PersistentDictionary<TKey, TValue> : IPersistentDictionary
    {
        private const string _defaultBaseDirectoryName = "cache";
        // Read when used (not when the type is loaded), so it follows the Data Directory once a host has set it
        private static string _defaultBaseDirectory => TrakHoundDataDirectory.GetPath(_defaultBaseDirectoryName);

        private readonly string _id;
        private readonly string _baseDirectory;
        private readonly Func<TValue, TValue, bool> _comparer;
        private readonly Func<string, TKey> _keyConverter;
        private readonly Func<string, TValue> _valueConverter;
        private readonly Dictionary<TKey, TValue> _entries = new Dictionary<TKey, TValue>();
        private readonly object _lock = new object();
        private long _keyCount;
        private bool _isChanged;


        /// <inheritdoc/>
        public string Id => _id;

        /// <inheritdoc/>
        public long KeyCount => _keyCount;


        /// <summary>
        /// Creates a new PersistentDictionary and registers it with the PersistentDictionaryManager. The baseDirectory defaults to "cache" in the AppDomain BaseDirectory (relative paths are resolved against it), the comparer determines if a Value has changed, and the converters parse Keys and Values when recovering
        /// </summary>
        public PersistentDictionary(
            string id,
            string baseDirectory = null,
            Func<TValue, TValue, bool> comparer = null,
            Func<string, TKey> keyConverter = null,
            Func<string, TValue> valueConverter = null
            )
        {
            _id = id;
            _comparer = comparer != null ? comparer : DefaultComparer;
            _keyConverter = keyConverter != null ? keyConverter : DefaultKeyConverter;
            _valueConverter = valueConverter != null ? valueConverter : DefaultValueConverter;

            if (!string.IsNullOrEmpty(baseDirectory))
            {
                _baseDirectory = baseDirectory;
                if (!Path.IsPathRooted(_baseDirectory)) _baseDirectory = TrakHoundDataDirectory.GetPath(_baseDirectory);
            }
            else
            {
                _baseDirectory = _defaultBaseDirectory;
            }

            PersistentDictionaryManager.AddDictionary(_id, this);
        }

        private static bool DefaultComparer(TValue existingValue, TValue newValue)
        {
            return EqualityComparer<TValue>.Default.Equals(existingValue, newValue);
        }

        private static TKey DefaultKeyConverter(string inputKey)
        {
            try
            {
                return (TKey)Convert.ChangeType(inputKey, typeof(TKey));
            }
            catch { }

            return default;
        }

        private static TValue DefaultValueConverter(string inputValue)
        {
            try
            {
                return (TValue)Convert.ChangeType(inputValue, typeof(TValue));
            }
            catch { }

            return default;
        }


        /// <summary>
        /// Returns true if the specified Key exists
        /// </summary>
        public bool ContainsKey(TKey key)
        { 
            if (key != null)
            {
                lock (_lock)
                {
                    return _entries.ContainsKey(key);
                }
            }

            return false;
        }

        /// <summary>
        /// Adds or updates the Value for the specified Key (marked as changed only if the comparer reports the Value is different)
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            if (key != null)
            {
                lock (_lock)
                {
                    if (_entries.ContainsKey(key))
                    {
                        var existingValue = _entries[key];
                        if (_comparer == null || !_comparer(existingValue, value))
                        {
                            _entries[key] = value;
                            _isChanged = true;
                        }
                    }
                    else
                    {
                        _entries.Add(key, value);
                        _keyCount++;
                        _isChanged = true;
                    }
                }
            }
        }

        /// <summary>
        /// Gets the Value for the specified Key (default if the Key is not found)
        /// </summary>
        public TValue Get(TKey key)
        {
            if (key != null)
            {
                lock (_lock)
                {
                    if (_entries.ContainsKey(key))
                    {
                        return _entries[key];
                    }
                }
            }

            return default;
        }

        /// <summary>
        /// Removes the specified Key (returns true if the Key was found)
        /// </summary>
        public bool Remove(TKey key)
        {
            if (key != null)
            {
                lock (_lock)
                {
                    return _entries.Remove(key);
                }
            }

            return false;
        }


        /// <summary>
        /// Reads the Keys and Values previously written to disk and adds them to the Dictionary
        /// </summary>
        public void Recover()
        {
            if (_id != null)
            {
                var pagePath = Path.Combine(_baseDirectory, _id);
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
                                var key = _keyConverter(line.Substring(0, i));
                                var value = _valueConverter(line.Substring(i + delimeter.Length));

                                //Console.WriteLine($"PersistentDictionary : {_id} : Recover() : Key = {key} : Value = {value}"); // DEBUG

                                Add(key, value);
                            }
                        }
                    }
                }
            }
        }

        /// <summary>
        /// Writes all entries to the GZip compressed file if the Dictionary has changed since the last Write
        /// </summary>
        public async Task Write()
        {
            Dictionary<TKey, TValue> inputEntries = null;
            var write = false;

            lock (_lock)
            {
                if (_isChanged && _id != null)
                {
                    _isChanged = false;
                    inputEntries = new Dictionary<TKey, TValue>(_entries);
                    write = true;
                }
            }

            if (write)
            {
                //Console.WriteLine($"PersistentDictionary : {_id} : Write() : write = {write}"); // DEBUG

                if (!inputEntries.IsNullOrEmpty())
                {
                    //Console.WriteLine($"PersistentDictionary : {_id} : Write() : inputEntries.Count() = {inputEntries.Count}"); // DEBUG
                }

                if (!Directory.Exists(_baseDirectory)) Directory.CreateDirectory(_baseDirectory);

                var pagePath = Path.Combine(_baseDirectory, _id);

                var writeEntries = new Dictionary<TKey, TValue>();

                //if (File.Exists(pagePath))
                //{
                //    var contentLines = ReadFile(pagePath);
                //    if (contentLines != null)
                //    {
                //        foreach (var line in contentLines)
                //        {
                //            var i = line.IndexOf(':');
                //            if (i > 0 && i + 1 < line.Length)
                //            {
                //                var key = _keyConverter(line.Substring(0, i));
                //                var value = _valueConverter(line.Substring(i + 1));

                //                writeEntries.Add(key, value);
                //            }
                //        }
                //    }
                //}

                // Add New Entries
                foreach (var entry in inputEntries)
                {
                    writeEntries.Remove(entry.Key);
                    writeEntries.Add(entry.Key, entry.Value);

                    //Console.WriteLine($"PersistentDictionary : {_id} : Write() : Key = {entry.Key} : Value = {entry.Value}"); // DEBUG
                }

                // Create Write Data
                var writeLines = new List<string>();
                foreach (var entry in writeEntries)
                {
                    writeLines.Add($"{entry.Key}|-|{entry.Value}");
                }
                var writeContent = string.Join("\r\n", writeLines);



                //Console.WriteLine($"PersistentDictionary : {_id} : WriteFile()"); // DEBUG



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
    /// Writes all registered PersistentDictionaries to disk on an interval (default 30 seconds) and when the process exits
    /// </summary>
    public static class PersistentDictionaryManager
    {
        private const int _defaultWriteInterval = 30000; // 30 Seconds

        private static readonly ListDictionary<string, IPersistentDictionary> _dictionaries = new ListDictionary<string, IPersistentDictionary>();
        private static readonly List<string> _changed = new List<string>();
        private static readonly System.Timers.Timer _writeTimer;
        private static readonly object _lock = new object();


        static PersistentDictionaryManager()
        {
            // Write to Disk at interval
            _writeTimer = new System.Timers.Timer();
            _writeTimer.Interval = _defaultWriteInterval;
            _writeTimer.Elapsed += (s, args) => _ = WriteAll(); // Timer events can't be awaited
            _writeTimer.Start();

            AppDomain.CurrentDomain.ProcessExit += Dispose;
        }

        /// <summary>
        /// Sets the interval (in milliseconds) that the Dictionaries are written to disk at
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
        /// Stops the write timer and writes all Dictionaries to disk (called when the process exits)
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
        /// Registers a Dictionary to be written to disk (ignored if a Dictionary with the same Id is already registered)
        /// </summary>
        public static void AddDictionary(string id, IPersistentDictionary dictionary)
        {
            if (id != null && dictionary != null)
            {
                lock (_lock)
                {
                    if (!_dictionaries.ContainsKey(id)) _dictionaries.Add(id, dictionary);
                }
            }
        }

        /// <summary>
        /// Gets all of the registered Dictionaries
        /// </summary>
        public static IEnumerable<IPersistentDictionary> Get()
        {
            var dictionaries = new List<IPersistentDictionary>();

            lock (_lock)
            {
                foreach (var dictionary in _dictionaries.Values)
                {
                    dictionaries.Add(dictionary);
                }
            }

            return dictionaries;
        }


        /// <summary>
        /// Writes each registered Dictionary to disk (only Dictionaries that have changed are written)
        /// </summary>
        public static async Task WriteAll()
        {
            var dictionaries = Get();
            foreach (var dictionary in dictionaries)
            {
                //Console.WriteLine($"PersistentDictionary : WriteAll() : {dictionary.Id}");


                await dictionary.Write();
            }
        }
    }
}
