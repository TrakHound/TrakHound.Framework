// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Blazor.Services
{
    /// <summary>
    /// Items available to download (from "_download/{key}") for a limited time
    /// </summary>
    public class TrakHoundDownloadService : IDisposable
    {
        private readonly Dictionary<string, TrakHoundDownloadItem> _items = new Dictionary<string, TrakHoundDownloadItem>();
        private readonly Dictionary<string, DateTime> _expirations = new Dictionary<string, DateTime>();
        private readonly object _lock = new object();
        private readonly System.Timers.Timer _retentionTimer;
        private readonly TimeSpan _defaultRetentionTime = TimeSpan.FromMinutes(5);


        public TrakHoundDownloadService()
        {
            _retentionTimer = new System.Timers.Timer();
            _retentionTimer.Interval = 5000;
            _retentionTimer.Elapsed += RetentionTimerElapsed;
            _retentionTimer.Start();
        }

        public void Dispose()
        {
            if (_retentionTimer != null)
            {
                _retentionTimer.Elapsed -= RetentionTimerElapsed;
                _retentionTimer.Dispose();
            }

            lock (_lock)
            {
                foreach (var key in _items.Keys.ToList()) RemoveInternal(key);
            }
        }

        private void RetentionTimerElapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            var now = DateTime.UtcNow;

            lock (_lock)
            {
                // Remove expired items (and their content)
                foreach (var key in _expirations.Where(o => now > o.Value).Select(o => o.Key).ToList())
                {
                    RemoveInternal(key);
                }
            }
        }

        public TrakHoundDownloadItem GetItem(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                lock (_lock) return _items.GetValueOrDefault(key);
            }

            return null;
        }

        public string AddItem(TrakHoundDownloadItem item) => AddItem(item, _defaultRetentionTime);

        /// <summary>
        /// Add an item that can be downloaded until the Retention time has passed
        /// </summary>
        public string AddItem(TrakHoundDownloadItem item, TimeSpan retention)
        {
            if (item != null)
            {
                var key = Guid.NewGuid().ToString();
                item.Key = key;

                lock (_lock)
                {
                    _items.Add(key, item);
                    _expirations.Add(key, DateTime.UtcNow + retention);
                }
                return key;
            }

            return null;
        }

        public void RemoveItem(string key)
        {
            if (string.IsNullOrEmpty(key)) return;
            lock (_lock) RemoveInternal(key);
        }

        private void RemoveInternal(string key)
        {
            if (_items.Remove(key, out var item) && item.DeleteFileWhenRemoved && !string.IsNullOrEmpty(item.FilePath))
            {
                try { if (File.Exists(item.FilePath)) File.Delete(item.FilePath); } catch { }
            }

            _expirations.Remove(key);
        }
    }
}
