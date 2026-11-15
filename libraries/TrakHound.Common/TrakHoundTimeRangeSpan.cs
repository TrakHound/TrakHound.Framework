// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound
{
    /// <summary>
    /// A span of time for a Target
    /// </summary>
    public struct TrakHoundTimeRangeSpan
    {
        /// <summary>
        /// The Target (ex. Object path or Uuid)
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// The length of the span (in nanoseconds)
        /// </summary>
        public long Span { get; set; }


        /// <summary>
        /// Creates a new TrakHoundTimeRangeSpan for the specified Target and TimeSpan
        /// </summary>
        public TrakHoundTimeRangeSpan(string target, TimeSpan span)
        {
            Target = target;
            Span = (long)span.TotalNanoseconds;
        }

        /// <summary>
        /// Creates a new TrakHoundTimeRangeSpan for the specified Target and span (stored as-is in the Span property, which is in nanoseconds)
        /// </summary>
        public TrakHoundTimeRangeSpan(string target, long spanTicks) 
        { 
            Target = target;
            Span = spanTicks;
        }

        /// <summary>
        /// Converts the TimeSpan to nanoseconds
        /// </summary>
        public static long Get(TimeSpan span)
        {
            return (long)span.TotalNanoseconds;
        }
    }
}
