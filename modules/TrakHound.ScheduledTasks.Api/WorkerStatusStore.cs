// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace TrakHound.ScheduledTasks
{
    /// <summary>
    /// The latest Worker Status from each Scheduled Tasks Worker (kept in memory : Workers report every few seconds)
    /// </summary>
    internal static class WorkerStatusStore
    {
        // A Worker reports at least every 10 seconds, so one that hasn't for this long is probably stopped
        internal static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

        // Forget Workers that haven't reported for this long (ex. a removed Worker)
        private static readonly TimeSpan _forgetAfter = TimeSpan.FromHours(1);

        private static readonly ConcurrentDictionary<string, WorkerStatus> _statuses = new ConcurrentDictionary<string, WorkerStatus>();


        public static void Set(WorkerStatus status)
        {
            if (status == null) return;

            status.Received = DateTime.UtcNow;
            _statuses[!string.IsNullOrEmpty(status.WorkerId) ? status.WorkerId : "default"] = status;
        }

        /// <summary>
        /// The latest Status of each Worker (with Stale set for Workers that stopped reporting)
        /// </summary>
        public static List<WorkerStatus> GetAll()
        {
            var now = DateTime.UtcNow;

            foreach (var entry in _statuses.Where(o => now - o.Value.Received > _forgetAfter).ToList())
            {
                _statuses.TryRemove(entry.Key, out _);
            }

            var statuses = _statuses.Values.OrderBy(o => o.WorkerId).ToList();
            foreach (var status in statuses) status.Stale = now - status.Received > StaleAfter;
            return statuses;
        }
    }
}
