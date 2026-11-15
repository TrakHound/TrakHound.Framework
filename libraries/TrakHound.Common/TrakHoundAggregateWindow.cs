// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound
{
    /// <summary>
    /// An aggregate Value calculated for a Target over a window of time
    /// </summary>
    public class TrakHoundAggregateWindow
    {
        /// <summary>
        /// The Target (ex. Object path or Uuid) that the Value was calculated for
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// The start of the window as Unix time (in nanoseconds)
        /// </summary>
        public long Start { get; set; }

        /// <summary>
        /// The end of the window as Unix time (in nanoseconds)
        /// </summary>
        public long End { get; set; }

        /// <summary>
        /// The aggregate Value for the window
        /// </summary>
        public double Value { get; set; }


        /// <summary>
        /// Creates a new TrakHoundAggregateWindow for the specified Target, window (Unix nanoseconds), and Value
        /// </summary>
        public TrakHoundAggregateWindow(string target, long start, long end, double value)
        {
            Target = target;
            Start = start;
            End = end;
            Value = value;
        }
    }
}
