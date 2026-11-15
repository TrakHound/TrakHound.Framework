// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Services
{
    public class TrakHoundFileCache
    {
        private const int _ttl = 3600000;
        private const int _interval = 60000;
        private readonly CacheDictionary<string, CacheEntry> _files = new CacheDictionary<string, CacheEntry>(_ttl, _interval);


        private class CacheEntry
        {
            public byte[] Contents { get; set; }
            public DateTime LastWriteTime { get; set; }
        }


        public void Add(string path, byte[] contents)
        {
            if (!string.IsNullOrEmpty(path) && !contents.IsNullOrEmpty())
            {
                _files.Add(path, new CacheEntry { Contents = contents, LastWriteTime = GetLastWriteTime(path) });
            }
        }

        /// <summary>
        /// Get the cached contents of a file. Returns null if not cached or if the file has changed since it was cached.
        /// </summary>
        public byte[] Get(string path)
        {
            if (!string.IsNullOrEmpty(path) && _files.TryGet(path, out var entry))
            {
                // Ignore the cached contents if the file has been replaced (ex. a Package rebuilt with the same version)
                if (entry.LastWriteTime == GetLastWriteTime(path))
                {
                    return entry.Contents;
                }
            }

            return null;
        }

        private static DateTime GetLastWriteTime(string path)
        {
            try
            {
                return File.GetLastWriteTimeUtc(path);
            }
            catch
            {
                return DateTime.MinValue;
            }
        }
    }
}
