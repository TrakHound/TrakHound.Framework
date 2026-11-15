// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.


namespace TrakHound.Instance.Components
{
    public class NotificationService : IDisposable
    {
        private readonly List<Notifications.NotificationItem> _notifications = new List<Notifications.NotificationItem>();
        private readonly object _lock = new object();
        private System.Timers.Timer _notificationTimer;


        // A copy, since the timer thread removes expired notifications while the page renders
        public IEnumerable<Notifications.NotificationItem> Notifications
        {
            get
            {
                lock (_lock) return _notifications.ToArray();
            }
        }

        public event EventHandler Updated;


        public NotificationService() 
        {
            _notificationTimer = new System.Timers.Timer();
            _notificationTimer.Interval = 500;
            _notificationTimer.Elapsed += NotificationTimerElapsed;
            _notificationTimer.Start();
        }

        public void Dispose()
        {
            if (_notificationTimer != null)
            {
                _notificationTimer.Elapsed -= NotificationTimerElapsed;
                _notificationTimer.Dispose();
            }
        }

        public void AddNotification(NotificationType type, string message, string details = null, int duration = 5000)
        {
            var notification = new Notifications.NotificationItem();
            notification.Type = type;
            notification.Message = message;
            notification.Details = details;
            notification.Timestamp = DateTime.Now;
            notification.TTL = TimeSpan.FromMilliseconds(duration);
            lock (_lock) _notifications.Add(notification);

            Updated?.Invoke(this, new EventArgs());
        }

        public void Dismiss(string notificationId)
        {
            int removed;
            lock (_lock) removed = _notifications.RemoveAll(o => o.Id == notificationId);

            if (removed > 0) Updated?.Invoke(this, new EventArgs());
        }

        private void NotificationTimerElapsed(object sender, System.Timers.ElapsedEventArgs args)
        {
            int removed;
            lock (_lock)
            {
                var now = DateTime.Now;
                removed = _notifications.RemoveAll(o => (now - o.Timestamp) > o.TTL);
            }

            // Raised outside of the lock so handlers can read Notifications without deadlocking
            if (removed > 0) Updated?.Invoke(this, new EventArgs());
        }
    }
}
