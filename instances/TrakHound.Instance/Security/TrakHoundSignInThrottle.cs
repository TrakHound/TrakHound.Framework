// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// Slows down password guessing: after too many failed sign ins from one client address (or for one username), sign in is locked
    /// for a while, and each further lockout is twice as long. Used by the Admin Login and Identity Provider sign in.
    /// </summary>
    /// <remarks>
    /// The username limit is higher than the address limit, so one client can't easily lock out a user it doesn't control
    /// (the Admin especially), while guessing from many addresses is still limited. Kept in memory, so a restart clears it.
    /// </remarks>
    public sealed class TrakHoundSignInThrottle
    {
        public const int DefaultAddressLimit = 5;
        public const int DefaultUsernameLimit = 20;
        public static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(15);
        public static readonly TimeSpan DefaultLockout = TimeSpan.FromMinutes(1);
        public static readonly TimeSpan DefaultMaxLockout = TimeSpan.FromMinutes(15);

        // Bounds memory when many addresses or usernames fail (the oldest entries are dropped)
        private const int _maxEntries = 10000;

        private readonly Dictionary<string, Entry> _entries = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);
        private readonly Func<DateTime> _now;
        private readonly object _lock = new object();


        public int AddressLimit { get; init; } = DefaultAddressLimit;

        public int UsernameLimit { get; init; } = DefaultUsernameLimit;

        /// <summary>
        /// Failures are counted within this period (counting starts again after it)
        /// </summary>
        public TimeSpan Window { get; init; } = DefaultWindow;

        public TimeSpan Lockout { get; init; } = DefaultLockout;

        public TimeSpan MaxLockout { get; init; } = DefaultMaxLockout;


        class Entry
        {
            public int Failures;
            public DateTime WindowStart;
            public DateTime LastFailure;
            public DateTime LockedUntil;
            public int Lockouts;
        }


        public TrakHoundSignInThrottle(Func<DateTime> now = null)
        {
            _now = now ?? (() => DateTime.UtcNow);
        }


        /// <summary>
        /// How long until sign in is allowed again for the address and username (null when it is allowed now)
        /// </summary>
        /// <param name="scope">What is being signed in to (ex. "admin", or an Identity Provider ID)</param>
        public TimeSpan? GetLockout(string scope, string address, string username)
        {
            var now = _now();
            lock (_lock)
            {
                var remaining = Max(GetRemaining(AddressKey(scope, address), now), GetRemaining(UsernameKey(scope, username), now));
                return remaining > TimeSpan.Zero ? remaining : null;
            }
        }

        /// <summary>
        /// Record a failed sign in (wrong username or password)
        /// </summary>
        public void RecordFailure(string scope, string address, string username)
        {
            var now = _now();
            lock (_lock)
            {
                if (_entries.Count >= _maxEntries) RemoveOldest();

                RecordFailure(AddressKey(scope, address), AddressLimit, now);
                RecordFailure(UsernameKey(scope, username), UsernameLimit, now);
            }
        }

        /// <summary>
        /// Record a successful sign in (clears the failures for the address and username)
        /// </summary>
        public void RecordSuccess(string scope, string address, string username)
        {
            lock (_lock)
            {
                var addressKey = AddressKey(scope, address);
                var usernameKey = UsernameKey(scope, username);
                if (addressKey != null) _entries.Remove(addressKey);
                if (usernameKey != null) _entries.Remove(usernameKey);
            }
        }


        private void RecordFailure(string key, int limit, DateTime now)
        {
            if (key == null) return;

            if (!_entries.TryGetValue(key, out var entry))
            {
                entry = new Entry { WindowStart = now };
                _entries[key] = entry;
            }

            // A quiet period as long as the longest lockout forgives earlier lockouts
            if (now - entry.LastFailure > MaxLockout + Window) entry.Lockouts = 0;
            if (now - entry.WindowStart > Window)
            {
                entry.WindowStart = now;
                entry.Failures = 0;
            }

            entry.LastFailure = now;
            entry.Failures++;

            if (entry.Failures >= limit)
            {
                var lockout = TimeSpan.FromTicks(Math.Min(MaxLockout.Ticks, Lockout.Ticks * (1L << Math.Min(entry.Lockouts, 20))));
                entry.LockedUntil = now + lockout;
                entry.Lockouts++;
                entry.Failures = 0;
                entry.WindowStart = now;
            }
        }

        private TimeSpan GetRemaining(string key, DateTime now)
        {
            if (key != null && _entries.TryGetValue(key, out var entry) && entry.LockedUntil > now) return entry.LockedUntil - now;
            return TimeSpan.Zero;
        }

        private void RemoveOldest()
        {
            foreach (var key in _entries.OrderBy(o => o.Value.LastFailure).Take(_maxEntries / 10).Select(o => o.Key).ToList())
            {
                _entries.Remove(key);
            }
        }

        private static TimeSpan Max(TimeSpan a, TimeSpan b) => a > b ? a : b;

        private static string AddressKey(string scope, string address) => !string.IsNullOrEmpty(address) ? $"address:{scope}:{address}" : null;

        private static string UsernameKey(string scope, string username) => !string.IsNullOrEmpty(username) ? $"username:{scope}:{username.Trim()}" : null;


        /// <summary>
        /// A message for the client ("Try again in 3 minutes")
        /// </summary>
        public static string GetMessage(TimeSpan lockout)
        {
            var minutes = (int)Math.Ceiling(lockout.TotalMinutes);
            return $"Too many failed sign in attempts. Try again in {minutes} minute{(minutes == 1 ? "" : "s")}.";
        }
    }
}
