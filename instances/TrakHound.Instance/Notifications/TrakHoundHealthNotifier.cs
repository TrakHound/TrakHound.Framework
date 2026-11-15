// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Health;
using TrakHound.Instances;

namespace TrakHound.Instance.Notifications
{
    /// <summary>
    /// Adds a Notification when a Health Check stays Degraded or Unhealthy, and updates it to Recovered when the Check is Healthy again.
    /// Brief problems are not notified : a Degraded Check must last <see cref="_degradedHoldTime"/> (and not be within <see cref="_startupGracePeriod"/> of the Instance Starting).
    /// Unhealthy is notified as soon as it is reported (the Health Monitor already confirms it over consecutive evaluations).
    /// Only one Notification is open per Check (its Status and message are updated if it changes while open).
    /// Open Notifications from a previous run of the Instance are picked up again (and Recovered if the Check is now Healthy).
    /// </summary>
    internal class TrakHoundHealthNotifier : IDisposable
    {
        private const string _keyPrefix = "health:";

        private static readonly TimeSpan _degradedHoldTime = TimeSpan.FromSeconds(60);
        private static readonly TimeSpan _startupGracePeriod = TimeSpan.FromMinutes(2);
        private static readonly TimeSpan _shortProblem = TimeSpan.FromMinutes(5); // An unread Notification for a problem shorter than this is removed when it Recovers

        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundInstanceNotificationService _notificationService;
        private readonly Dictionary<string, OpenNotification> _open = new Dictionary<string, OpenNotification>(); // Check ID => Notification
        private readonly object _lock = new object();
        private DateTime? _started; // When the Instance was first seen Started


        private class OpenNotification
        {
            public string NotificationId;
            public TrakHoundHealthStatus Status;
            public DateTime Opened;
        }


        public TrakHoundHealthNotifier(ITrakHoundInstance instance, TrakHoundInstanceNotificationService notificationService)
        {
            _instance = instance;
            _notificationService = notificationService;

            AdoptOpenNotifications();

            if (_instance.Health != null) _instance.Health.Updated += HealthUpdated;
        }

        public void Dispose()
        {
            if (_instance.Health != null) _instance.Health.Updated -= HealthUpdated;
        }


        // Raised after each evaluation, so a problem is notified once it has lasted long enough (not only when its Status changes)
        private void HealthUpdated(object sender, TrakHoundInstanceHealth health)
        {
            try
            {
                var now = health?.Checked ?? DateTime.UtcNow;
                if (_started == null && _instance.Status == TrakHoundInstanceStatus.Started) _started = now;

                if (health?.Checks == null) return;

                foreach (var check in health.Checks)
                {
                    if (check != null && !string.IsNullOrEmpty(check.Id)) Evaluate(check, now);
                }
            }
            catch { } // Never affect the Health evaluation
        }

        private void Evaluate(TrakHoundHealthCheckResult check, DateTime now)
        {
            var isProblem = !check.Ignored && check.Status >= TrakHoundHealthStatus.Degraded;

            OpenNotification open;
            lock (_lock) open = _open.GetValueOrDefault(check.Id);

            if (isProblem)
            {
                if (open != null)
                {
                    if (open.Status == check.Status) return;

                    // Still a problem, with a different Status (ex. Degraded => Unhealthy)
                    _notificationService.Update(open.NotificationId, o =>
                    {
                        o.Type = GetType(check.Status);
                        o.Title = GetTitle(check);
                        o.Message = check.Description;
                        o.Read = false;
                    });

                    lock (_lock) open.Status = check.Status;
                }
                else if (ShouldNotify(check, now))
                {
                    var opened = check.StatusChanged ?? now;
                    var notification = _notificationService.Notify(
                        GetType(check.Status),
                        TrakHoundInstanceNotificationCategories.Health,
                        GetTitle(check),
                        check.Description,
                        GetLink(),
                        "View Health",
                        $"{_keyPrefix}{check.Id}:{opened.Ticks}"
                        );

                    if (notification != null)
                    {
                        lock (_lock) _open[check.Id] = new OpenNotification { NotificationId = notification.Id, Status = check.Status, Opened = opened };
                    }
                }
            }
            else if (open != null && (check.Status == TrakHoundHealthStatus.Healthy || check.Ignored))
            {
                // Recovered (Unknown, ex. still being checked, leaves the Notification open)
                var notification = _notificationService.Find(o => o.Id == open.NotificationId);
                if (notification != null && !notification.Read && now - open.Opened < _shortProblem)
                {
                    // Short and never seen, so not worth keeping
                    _notificationService.Remove(o => o.Id == open.NotificationId);
                }
                else
                {
                    // Recovering is good news, so the Read state is left as it is
                    var reason = check.Ignored ? "is now Ignored" : "Recovered";
                    _notificationService.Update(open.NotificationId, o =>
                    {
                        o.Type = TrakHoundInstanceNotificationType.Success;
                        o.Title = $"{check.Title} {reason}";
                        o.Message = $"Was {open.Status} from {FormatTimestamp(open.Opened)} to {FormatTimestamp(now)}. {o.Message}";
                    });
                }

                lock (_lock) _open.Remove(check.Id);
            }
        }

        private bool ShouldNotify(TrakHoundHealthCheckResult check, DateTime now)
        {
            if (check.Status >= TrakHoundHealthStatus.Unhealthy) return true;

            // Degraded : not while Starting (ex. Workers and APIs still loading) and only once it has lasted
            if (_started == null || now - _started.Value < _startupGracePeriod) return false;
            return now - (check.StatusChanged ?? now) >= _degradedHoldTime;
        }

        // Open Health Notifications from before the Instance was restarted
        private void AdoptOpenNotifications()
        {
            var checkIds = new[] { "runtime", "resources", "disk", "packages", "services", "workers", "functions", "api", "buffers", "logs" };

            foreach (var checkId in checkIds)
            {
                var notification = _notificationService.Find(o =>
                    o.Category == TrakHoundInstanceNotificationCategories.Health &&
                    o.Type != TrakHoundInstanceNotificationType.Success &&
                    o.Key != null && o.Key.StartsWith($"{_keyPrefix}{checkId}:"));

                if (notification != null)
                {
                    var status = notification.Type == TrakHoundInstanceNotificationType.Error ? TrakHoundHealthStatus.Unhealthy : TrakHoundHealthStatus.Degraded;
                    lock (_lock) _open[checkId] = new OpenNotification { NotificationId = notification.Id, Status = status, Opened = notification.Timestamp };
                }
            }
        }

        private static TrakHoundInstanceNotificationType GetType(TrakHoundHealthStatus status)
        {
            return status == TrakHoundHealthStatus.Unhealthy ? TrakHoundInstanceNotificationType.Error : TrakHoundInstanceNotificationType.Warning;
        }

        private static string GetTitle(TrakHoundHealthCheckResult check) => $"{check.Title} {check.Status}";

        private string GetLink() => $"_admin/explorer/{_instance.Id}/diagnostics/health";

        private static string FormatTimestamp(DateTime timestamp)
        {
            var local = timestamp.ToLocalTime();
            return local.Date == DateTime.Now.Date ? local.ToString("HH:mm:ss") : local.ToString("yyyy-MM-dd HH:mm:ss");
        }
    }
}
