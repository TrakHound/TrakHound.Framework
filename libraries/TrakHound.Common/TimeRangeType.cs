// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// The type of a TimeRange, based on the calendar unit it spans
    /// </summary>
    public enum TimeRangeType
    {
        /// <summary>
        /// A custom range that does not match a standard calendar unit
        /// </summary>
        Custom = 0,
        /// <summary>
        /// A single Second
        /// </summary>
        Second = 1,
        /// <summary>
        /// A single Minute
        /// </summary>
        Minute = 2,
        /// <summary>
        /// A single Hour
        /// </summary>
        Hour = 3,
        /// <summary>
        /// A single Day
        /// </summary>
        Day = 4,
        /// <summary>
        /// A single Month
        /// </summary>
        Month = 5,
        /// <summary>
        /// A single Year
        /// </summary>
        Year = 6
    }
}
