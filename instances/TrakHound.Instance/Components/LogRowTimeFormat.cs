// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Components
{
    /// <summary>
    /// How LogRows shows each line's time (the full time is always on hover)
    /// </summary>
    public enum LogRowTimeFormat
    {
        /// <summary>
        /// The local date and time (ex. 2026-10-28 01:27:54.123)
        /// </summary>
        Clock,

        /// <summary>
        /// The time since the first line (ex. +1.497s), for a single run
        /// </summary>
        SinceFirst
    }
}
