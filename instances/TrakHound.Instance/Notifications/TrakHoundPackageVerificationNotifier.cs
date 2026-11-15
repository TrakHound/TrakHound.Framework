// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Packages;

namespace TrakHound.Instance.Notifications
{
    /// <summary>
    /// Summarizes the Packages that were changed or refused in a single Notification, kept current as Packages are checked.
    /// Unsigned Packages aren't included unless they were refused, since they are normal unless the Require Mode is set
    /// </summary>
    internal class TrakHoundPackageVerificationNotifier : IDisposable
    {
        private const string _keyPrefix = "package-verification:";
        private const string _link = "_admin/packages";
        private const int _maxListed = 5;
        private const int _updateDelay = 2000; // Packages are checked one at a time, so wait for the rest

        private record Failure(string Id, string Version, string Description, bool Refused)
        {
            public string Text => $"{Id} v{Version} : {Description}";
        }

        private readonly TrakHoundPackageManager _packageManager;
        private readonly TrakHoundInstanceNotificationService _notificationService;
        private readonly DelayEvent _delay = new DelayEvent(_updateDelay);
        private readonly Dictionary<string, Failure> _failures = new Dictionary<string, Failure>(); // PackageUuid => Failure
        private readonly object _lock = new object();


        public TrakHoundPackageVerificationNotifier(TrakHoundPackageManager packageManager, TrakHoundInstanceNotificationService notificationService)
        {
            _packageManager = packageManager;
            _notificationService = notificationService;

            _delay.Elapsed += DelayElapsed;

            if (_packageManager != null)
            {
                _packageManager.PackageVerified += PackageVerified;
                _packageManager.PackageRemoved += PackageRemoved;
            }
        }

        public void Dispose()
        {
            if (_packageManager != null)
            {
                _packageManager.PackageVerified -= PackageVerified;
                _packageManager.PackageRemoved -= PackageRemoved;
            }

            _delay.Elapsed -= DelayElapsed;
            _delay.Dispose();
        }


        private void PackageVerified(object sender, TrakHoundPackageVerifiedArgs args)
        {
            var failed = !args.Allowed || args.Result.Status == TrakHoundPackageVerificationStatus.Invalid;

            lock (_lock)
            {
                if (failed) _failures[args.Package.Uuid] = new Failure(args.Package.Id, args.Package.Version, TrakHoundPackageVerificationPolicy.Describe(args.Result), !args.Allowed);
                else _failures.Remove(args.Package.Uuid);
            }

            _delay.Set();
        }

        private void PackageRemoved(object sender, TrakHoundPackageChangedArgs args)
        {
            // A Package that was refused while running is also removed, and stays a Failure
            if (_packageManager.RefusedPackages.Any(o => o.Id == args.PackageId && o.Version == args.PackageVersion)) return;

            var packageUuid = TrakHoundPackage.GenerateUuid(args.PackageId, args.PackageVersion);
            if (packageUuid != null)
            {
                lock (_lock) _failures.Remove(packageUuid);
                _delay.Set();
            }
        }

        private void DelayElapsed(object sender, EventArgs args)
        {
            List<Failure> failures;
            lock (_lock) failures = _failures.Values.OrderBy(o => o.Id, StringComparer.OrdinalIgnoreCase).ToList();

            var existing = _notificationService.Find(o => o.Key != null && o.Key.StartsWith(_keyPrefix));

            if (failures.Count < 1)
            {
                if (existing != null) _notificationService.Remove(o => o.Id == existing.Id);
                return;
            }

            // The Key identifies the set of Failures (a dismissed set isn't notified again)
            var key = _keyPrefix + string.Join(";", failures.Select(o => o.Text)).ToSHA1Hash();
            if (existing?.Key == key) return;

            if (existing != null) _notificationService.Remove(o => o.Id == existing.Id);

            var refusedCount = failures.Count(o => o.Refused);
            var title = refusedCount > 0
                ? $"{refusedCount} Package{(refusedCount == 1 ? " was" : "s were")} Refused"
                : $"{failures.Count} Package{(failures.Count == 1 ? " has" : "s have")} Changed";

            var message = string.Join(Environment.NewLine, failures.Take(_maxListed).Select(o => o.Text));
            if (failures.Count > _maxListed) message += $"{Environment.NewLine}(and {failures.Count - _maxListed} more)";

            var type = refusedCount > 0 ? TrakHoundInstanceNotificationType.Error : TrakHoundInstanceNotificationType.Warning;
            _notificationService.Notify(type, TrakHoundInstanceNotificationCategories.Packages, title, message, _link, "View Installed Packages", key);
        }
    }
}
