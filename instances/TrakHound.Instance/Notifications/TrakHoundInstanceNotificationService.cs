// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace TrakHound.Instance.Notifications
{
    /// <summary>
    /// Notifications for the Admin Interface (ex. the previous run didn't Shut Down Cleanly, a Package Update is available, a Configuration was Added).
    /// Notifications (and dismissed Keys) are saved to a file in the Configuration directory so they are kept between runs.
    /// </summary>
    public class TrakHoundInstanceNotificationService
    {
        public const string Filename = "notifications.json";

        private const int _maxNotifications = 200;
        private const int _maxDismissedKeys = 1000;

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };

        private readonly string _path;
        private readonly Logger _logger;
        private readonly object _lock = new object();
        private readonly object _saveLock = new object();
        private readonly List<TrakHoundInstanceNotification> _notifications = new List<TrakHoundInstanceNotification>();
        private readonly List<string> _dismissedKeys = new List<string>(); // Oldest first


        private class NotificationsFile
        {
            [JsonPropertyName("notifications")]
            public List<TrakHoundInstanceNotification> Notifications { get; set; }

            [JsonPropertyName("dismissed")]
            public List<string> Dismissed { get; set; }
        }


        /// <summary>
        /// Newest first
        /// </summary>
        public IEnumerable<TrakHoundInstanceNotification> Notifications
        {
            get { lock (_lock) return _notifications.OrderByDescending(o => o.Timestamp).ToList(); }
        }

        public int UnreadCount
        {
            get { lock (_lock) return _notifications.Count(o => !o.Read); }
        }

        /// <summary>
        /// Raised when a Notification is Added, Removed or Read
        /// </summary>
        public event Action Changed;


        public TrakHoundInstanceNotificationService(string configurationPath, Logger logger = null)
        {
            _path = !string.IsNullOrEmpty(configurationPath) ? Path.Combine(configurationPath, Filename) : null;
            _logger = logger;

            Load();
        }


        /// <summary>
        /// Add a Notification. If a Key is given, nothing is added if a Notification with the Key already exists or was dismissed.
        /// </summary>
        /// <returns>The Notification added (null if not added)</returns>
        public TrakHoundInstanceNotification Notify(
            TrakHoundInstanceNotificationType type,
            string category,
            string title,
            string message = null,
            string link = null,
            string linkText = null,
            string key = null
            )
        {
            if (string.IsNullOrEmpty(title)) return null;

            var notification = new TrakHoundInstanceNotification
            {
                Id = Guid.NewGuid().ToString(),
                Key = key,
                Type = type,
                Category = category,
                Title = title,
                Message = message,
                Link = link,
                LinkText = linkText,
                Timestamp = DateTime.UtcNow
            };

            lock (_lock)
            {
                if (!string.IsNullOrEmpty(key))
                {
                    if (_dismissedKeys.Contains(key) || _notifications.Any(o => o.Key == key)) return null;
                }

                _notifications.Add(notification);

                // Remove the oldest Notifications over the limit
                if (_notifications.Count > _maxNotifications)
                {
                    var oldest = _notifications.OrderBy(o => o.Timestamp).Take(_notifications.Count - _maxNotifications).ToList();
                    foreach (var item in oldest) _notifications.Remove(item);
                }
            }

            _logger?.Debug($"Notification Added : {category} : {title}{(!string.IsNullOrEmpty(message) ? $" : {message}" : "")}");

            OnChanged();
            return notification;
        }

        /// <summary>
        /// Returns the first Notification that matches (null if none)
        /// </summary>
        public TrakHoundInstanceNotification Find(Func<TrakHoundInstanceNotification, bool> predicate)
        {
            if (predicate == null) return null;
            lock (_lock) return _notifications.FirstOrDefault(predicate);
        }

        /// <summary>
        /// Change a Notification in place (ex. its Message), keeping its Read state and Timestamp
        /// </summary>
        public void Update(string notificationId, Action<TrakHoundInstanceNotification> update)
        {
            if (string.IsNullOrEmpty(notificationId) || update == null) return;

            bool updated = false;
            lock (_lock)
            {
                var notification = _notifications.FirstOrDefault(o => o.Id == notificationId);
                if (notification != null)
                {
                    update(notification);
                    updated = true;
                }
            }

            if (updated) OnChanged();
        }

        /// <summary>
        /// Remove the Notifications that match (without marking their Keys as dismissed, ex. an update notification that no longer applies)
        /// </summary>
        public void Remove(Func<TrakHoundInstanceNotification, bool> predicate)
        {
            if (predicate == null) return;

            int removed;
            lock (_lock) removed = _notifications.RemoveAll(o => predicate(o));

            if (removed > 0) OnChanged();
        }

        /// <summary>
        /// Remove a Notification (its Key won't be notified again)
        /// </summary>
        public void Dismiss(string notificationId)
        {
            if (string.IsNullOrEmpty(notificationId)) return;

            bool removed;
            lock (_lock)
            {
                var notification = _notifications.FirstOrDefault(o => o.Id == notificationId);
                removed = notification != null && _notifications.Remove(notification);
                if (removed) AddDismissedKey(notification.Key);
            }

            if (removed) OnChanged();
        }

        /// <summary>
        /// Remove all Notifications (their Keys won't be notified again)
        /// </summary>
        public void DismissAll()
        {
            lock (_lock)
            {
                if (_notifications.Count < 1) return;

                foreach (var notification in _notifications) AddDismissedKey(notification.Key);
                _notifications.Clear();
            }

            OnChanged();
        }

        public void MarkRead(string notificationId)
        {
            if (string.IsNullOrEmpty(notificationId)) return;

            bool changed = false;
            lock (_lock)
            {
                var notification = _notifications.FirstOrDefault(o => o.Id == notificationId);
                if (notification != null && !notification.Read)
                {
                    notification.Read = true;
                    changed = true;
                }
            }

            if (changed) OnChanged();
        }

        public void MarkAllRead()
        {
            bool changed = false;
            lock (_lock)
            {
                foreach (var notification in _notifications.Where(o => !o.Read))
                {
                    notification.Read = true;
                    changed = true;
                }
            }

            if (changed) OnChanged();
        }


        private void AddDismissedKey(string key)
        {
            if (string.IsNullOrEmpty(key) || _dismissedKeys.Contains(key)) return;

            _dismissedKeys.Add(key);
            if (_dismissedKeys.Count > _maxDismissedKeys) _dismissedKeys.RemoveRange(0, _dismissedKeys.Count - _maxDismissedKeys);
        }

        private void OnChanged()
        {
            Save();

            try { Changed?.Invoke(); }
            catch (Exception ex)
            {
                _logger?.Debug($"Notifications Changed Handler Error : {ex.Message}");
            }
        }

        private void Load()
        {
            if (string.IsNullOrEmpty(_path) || !File.Exists(_path)) return;

            try
            {
                var file = JsonSerializer.Deserialize<NotificationsFile>(File.ReadAllText(_path), _jsonOptions);
                if (file != null)
                {
                    lock (_lock)
                    {
                        if (file.Notifications != null) _notifications.AddRange(file.Notifications.Where(o => o != null && !string.IsNullOrEmpty(o.Id)));
                        if (file.Dismissed != null) _dismissedKeys.AddRange(file.Dismissed.Where(o => !string.IsNullOrEmpty(o)));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger?.Warn($"Error Reading Notifications File ({_path}) : {ex.Message}");
            }
        }

        private void Save()
        {
            if (string.IsNullOrEmpty(_path)) return;

            try
            {
                // Serialized and written under one lock so an older state can't overwrite a newer one
                lock (_saveLock)
                {
                    string json;
                    lock (_lock)
                    {
                        json = JsonSerializer.Serialize(new NotificationsFile { Notifications = _notifications, Dismissed = _dismissedKeys }, _jsonOptions);
                    }

                    var directory = Path.GetDirectoryName(_path);
                    if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

                    // Write to a Temp file first so the file isn't left partially written
                    var tempPath = _path + ".tmp";
                    File.WriteAllText(tempPath, json);
                    File.Move(tempPath, _path, true);
                }
            }
            catch (Exception ex)
            {
                _logger?.Debug($"Error Writing Notifications File ({_path}) : {ex.Message}");
            }
        }
    }
}
