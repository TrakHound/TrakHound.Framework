// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Notifications;

namespace TrakHound.Instance
{
    /// <summary>
    /// Admin Interface Notifications about what happened when the Instance started (the previous run, Startup Deployments, the Admin User)
    /// </summary>
    public static class TrakHoundStartupNotifications
    {
        /// <summary>
        /// Add a Notification if the previous run of the Instance didn't Shut Down Cleanly.
        /// Repeated Unclean Shutdowns update the same Notification (with a Count) instead of adding one per run.
        /// </summary>
        public static void NotifyPreviousRun(TrakHoundInstanceNotificationService notificationService, string instanceId)
        {
            const string keyPrefix = "unclean-shutdown:";

            var previousRun = TrakHoundInstanceLifetimeMonitor.PreviousRun;
            if (previousRun == null) return;

            // The Key identifies the run, so a dismissed Notification doesn't block later Unclean Shutdowns
            var key = $"{keyPrefix}{previousRun.ProcessId}:{previousRun.Started}";
            var message = $"The Instance (PID {previousRun.ProcessId}, Started {previousRun.Started}) stopped unexpectedly around {previousRun.LastModified:yyyy-MM-dd HH:mm:ss}. Reason : {previousRun.Reason}";

            var existing = notificationService.Find(o => o.Key != null && o.Key.StartsWith(keyPrefix));
            if (existing != null)
            {
                if (existing.Key == key) return;

                notificationService.Update(existing.Id, o =>
                {
                    o.Count = Math.Max(o.Count, 1) + 1;
                    o.Key = key;
                    o.Title = $"Instance did not Shut Down Cleanly ({o.Count} times)";
                    o.Message = message;
                    o.Timestamp = DateTime.UtcNow;
                    o.Read = false;
                });
            }
            else
            {
                notificationService.Notify(
                    TrakHoundInstanceNotificationType.Warning,
                    TrakHoundInstanceNotificationCategories.Instance,
                    "Previous Run did not Shut Down Cleanly",
                    message,
                    GetLogsLink(instanceId),
                    "View Logs",
                    key
                    );
            }
        }

        /// <summary>
        /// Add a Notification for each Deployment installed (or that failed) when the Instance started
        /// </summary>
        public static void NotifyStartupDeployments(TrakHoundInstanceNotificationService notificationService, TrakHoundStartupDeployments startupDeployments, string instanceId)
        {
            foreach (var record in startupDeployments.Results)
            {
                var name = !string.IsNullOrEmpty(record.ProfileId) ? $"{record.ProfileId} v{record.Version}" : record.Filename;
                var key = $"startup-deployment:{record.Hash}";

                if (record.Success)
                {
                    notificationService.Notify(
                        TrakHoundInstanceNotificationType.Success,
                        TrakHoundInstanceNotificationCategories.Configuration,
                        $"Deployment {name} Installed",
                        $"{record.Filename} was installed when the Instance started. Backup : {record.BackupPath}",
                        "_admin/deployments/install",
                        "View Deployments",
                        key
                        );
                }
                else
                {
                    var rollback = record.Restored ? "The previous configuration and packages were restored from the Backup." : !string.IsNullOrEmpty(record.BackupPath) ? $"The Backup could not be restored : {record.BackupPath}" : "Nothing was changed.";

                    notificationService.Notify(
                        TrakHoundInstanceNotificationType.Error,
                        TrakHoundInstanceNotificationCategories.Configuration,
                        $"Deployment {name} Failed",
                        $"{record.Filename} could not be installed when the Instance started : {record.Errors.FirstOrDefault()}. {rollback}",
                        GetLogsLink(instanceId),
                        "View Logs",
                        key
                        );
                }
            }
        }

        /// <summary>
        /// Add a Notification if an Admin User Seed file (or Password file) was used when the Instance started
        /// </summary>
        public static void NotifyStartupAdminUser(TrakHoundInstanceNotificationService notificationService, TrakHoundStartupAdminUser startupAdminUser, string instanceId)
        {
            const string key = "startup-admin-user";

            // A Password file is set on every start (ex. in a container), so only a problem with it is a Notification
            var fromPasswordFile = startupAdminUser.Source == TrakHoundStartupAdminUser.SourceType.PasswordFile;
            if (fromPasswordFile && startupAdminUser.Result == TrakHoundStartupAdminUser.ResultType.Ignored) return;

            switch (startupAdminUser.Result)
            {
                case TrakHoundStartupAdminUser.ResultType.Created:
                    notificationService.Notify(
                        TrakHoundInstanceNotificationType.Success,
                        TrakHoundInstanceNotificationCategories.Configuration,
                        "Admin User Created",
                        fromPasswordFile
                            ? $"The Admin User '{startupAdminUser.Username}' was created from {TrakHoundStartupAdminUser.PasswordFileEnvironmentVariable} when the Instance started."
                            : $"The Admin User '{startupAdminUser.Username}' was created by Setup when the Instance started.",
                        key: key
                        );
                    break;

                case TrakHoundStartupAdminUser.ResultType.Ignored:
                    notificationService.Notify(
                        TrakHoundInstanceNotificationType.Warning,
                        TrakHoundInstanceNotificationCategories.Configuration,
                        "Setup Admin User Ignored",
                        "The Admin User from Setup was not created because an Admin User already exists. The existing Admin User was not changed.",
                        key: key
                        );
                    break;

                case TrakHoundStartupAdminUser.ResultType.Invalid:
                    notificationService.Notify(
                        TrakHoundInstanceNotificationType.Error,
                        TrakHoundInstanceNotificationCategories.Configuration,
                        "Setup Admin User Failed",
                        fromPasswordFile
                            ? $"The Admin User could not be created from {TrakHoundStartupAdminUser.PasswordFileEnvironmentVariable} : {startupAdminUser.Message}"
                            : $"The Admin User from Setup could not be created : {startupAdminUser.Message}",
                        GetLogsLink(instanceId),
                        "View Logs",
                        key
                        );
                    break;
            }
        }

        private static string GetLogsLink(string instanceId) => $"_admin/explorer/{instanceId}/diagnostics/logs/view";
    }
}
