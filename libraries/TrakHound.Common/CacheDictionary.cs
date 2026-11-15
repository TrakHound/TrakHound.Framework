// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound
{
    /// <summary>
    /// A thread-safe Dictionary whose entries expire after a fixed time-to-live and are removed by a background worker
    /// </summary>
    public class CacheDictionary<TKey, TValue> : IDisposable
    {
        /// <summary>
        /// Handler invoked with the Key of an entry that has expired
        /// </summary>
        public delegate void KeyExpirationHandler(TKey key);

        private readonly struct Entry
        {
            public readonly TValue Value;
            public readonly long Expires; // Environment.TickCount64 (ms)

            public Entry(TValue value, long expires)
            {
                Value = value;
                Expires = expires;
            }
        }

        private readonly long _ttl;
        private readonly TimeSpan _expirationInterval;
        private readonly Dictionary<TKey, Entry> _cache = new Dictionary<TKey, Entry>();
        private readonly object _lock = new object();
        private readonly CancellationTokenSource _stop = new CancellationTokenSource();
        private int _disposed;


        /// <summary>
        /// Raised for each Key that is removed by the background worker after it expires
        /// </summary>
        public event KeyExpirationHandler KeyExpired;

        /// <summary>
        /// The number of entries currently stored (may include expired entries that have not been removed yet)
        /// </summary>
        public int Count
        {
            get { lock (_lock) return _cache.Count; }
        }


        /// <summary>
        /// Creates a new CacheDictionary with the time-to-live of each entry (ttl) and the interval the expiration worker runs at (expirationInterval), both in milliseconds
        /// </summary>
        public CacheDictionary(int ttl = 60000, int expirationInterval = 5000)
        {
            _ttl = ttl;
            _expirationInterval = TimeSpan.FromMilliseconds(expirationInterval);

            _ = Task.Run(() => Worker(_stop.Token));
        }

        /// <summary>
        /// Stops the background expiration worker
        /// </summary>
        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) == 0)
            {
                _stop.Cancel();
                _stop.Dispose();
            }
        }


        /// <summary>
        /// Adds or replaces the Value for the specified Key and resets its expiration (null Keys are ignored)
        /// </summary>
        public void Add(TKey key, TValue value)
        {
            if (key != null)
            {
                var expires = Environment.TickCount64 + _ttl;
                lock (_lock) _cache[key] = new Entry(value, expires);
            }
        }

        /// <summary>
        /// Adds or replaces the Value for the specified Key with its own time-to-live (ttl) and resets its expiration (null Keys are ignored)
        /// </summary>
        public void Add(TKey key, TValue value, TimeSpan ttl)
        {
            if (key != null)
            {
                var expires = Environment.TickCount64 + (long)ttl.TotalMilliseconds;
                lock (_lock) _cache[key] = new Entry(value, expires);
            }
        }

        /// <summary>
        /// Gets the Value for the specified Key if it exists and has not expired
        /// </summary>
        public bool TryGet(TKey key, out TValue value)
        {
            if (key != null)
            {
                lock (_lock)
                {
                    if (_cache.TryGetValue(key, out var entry) && entry.Expires > Environment.TickCount64)
                    {
                        value = entry.Value;
                        return true;
                    }
                }
            }

            value = default;
            return false;
        }

        /// <summary>
        /// Gets the Value for the specified Key (default if it does not exist or has expired)
        /// </summary>
        public TValue Get(TKey key) => TryGet(key, out var value) ? value : default;

        /// <summary>
        /// Returns true if the specified Key exists and has not expired
        /// </summary>
        public bool ContainsKey(TKey key) => TryGet(key, out _);

        /// <summary>
        /// Removes the entry for the specified Key without raising KeyExpired
        /// </summary>
        public void Remove(TKey key)
        {
            if (key != null)
            {
                lock (_lock) _cache.Remove(key);
            }
        }


        private async Task Worker(CancellationToken cancellationToken)
        {
            using var timer = new PeriodicTimer(_expirationInterval);

            try
            {
                while (await timer.WaitForNextTickAsync(cancellationToken))
                {
                    List<TKey> expiredKeys = null;
                    var now = Environment.TickCount64;

                    lock (_lock)
                    {
                        foreach (var pair in _cache)
                        {
                            if (pair.Value.Expires <= now)
                            {
                                (expiredKeys ??= new List<TKey>()).Add(pair.Key);
                            }
                        }

                        if (expiredKeys != null)
                        {
                            foreach (var key in expiredKeys) _cache.Remove(key);
                        }
                    }

                    var handler = KeyExpired;
                    if (expiredKeys != null && handler != null)
                    {
                        foreach (var key in expiredKeys)
                        {
                            try { handler.Invoke(key); }
                            catch { } // A faulty subscriber must not stop expiration
                        }
                    }
                }
            }
            catch (OperationCanceledException) { }
        }
    }
}
