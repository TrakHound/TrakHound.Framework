// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// The type of aggregation to apply to a set of values
    /// </summary>
    public enum TrakHoundAggregateType
    {
        /// <summary>
        /// The average (arithmetic mean) of the values
        /// </summary>
        Mean,
        /// <summary>
        /// The middle value of the sorted values
        /// </summary>
        Median,
        /// <summary>
        /// The largest value
        /// </summary>
        Max,
        /// <summary>
        /// The smallest value
        /// </summary>
        Min,
        /// <summary>
        /// The total of the values
        /// </summary>
        Sum,
        /// <summary>
        /// The values rounded up to the nearest whole number
        /// </summary>
        Ceiling,
        /// <summary>
        /// The values rounded down to the nearest whole number
        /// </summary>
        Floor,
    }
}
