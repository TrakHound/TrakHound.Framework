// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.ScheduledTasks
{
    /// <summary>
    /// What happens when a Scheduled Task is due while its previous Run is still in progress
    /// </summary>
    internal enum OverlapPolicy
    {
        /// <summary>
        /// Skip the occurrence (shows as Missed in the Functions Summary)
        /// </summary>
        Skip,

        /// <summary>
        /// Run once more after the current Run finishes (further occurrences are skipped)
        /// </summary>
        Queue,

        /// <summary>
        /// Run alongside the current Run
        /// </summary>
        Concurrent
    }
}
