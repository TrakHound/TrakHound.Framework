// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound
{
    /// <summary>
    /// A time segment (From / To) in Unix time used to split a time range into fixed length increments aligned to the start of each Hour
    /// </summary>
    public class UnixTimeSegment
    {
        /// <summary>
        /// The start of the Segment as Unix time (in nanoseconds)
        /// </summary>
        public long From { get; set; }

        /// <summary>
        /// The end of the Segment as Unix time (in nanoseconds)
        /// </summary>
        public long To { get; set; }


        /// <summary>
        /// Creates a new UnixTimeSegment from the specified start and end Unix times (in nanoseconds)
        /// </summary>
        public UnixTimeSegment(long from, long to)
        {
            From = from;
            To = to;
        }


        /// <summary>
        /// Splits the range between the specified Unix times (in nanoseconds) into Segments of the specified increment (in seconds)
        /// </summary>
        public static List<UnixTimeSegment> GetSegments(long from, long to, int increment)
        {
            var objs = new List<UnixTimeSegment>();

            var bottom = GetSegmentBottom(from, increment);
            var top = GetSegmentTop(to, increment);
            if (GetSegmentBottom(to, increment) == to) top = top - (increment * 1000);
            var ts = from;

            while (ts < top)
            {
                var b = GetSegmentBottom(ts, increment);
                var t = GetSegmentTop(ts, increment);

                if (b < top)
                {
                    var seg = new UnixTimeSegment(b, t);
                    objs.Add(seg);
                }

                ts = t;
            }

            return objs;
        }

        /// <summary>
        /// Gets the start of the Segment containing the specified Unix time (in nanoseconds) for the specified increment (in seconds), with Segments aligned to the start of the Hour
        /// </summary>
        public static long GetSegmentBottom(long uts, int increment)
        {
            var ts = uts.ToDateTime();

            var y = ts.Year;
            var m = ts.Month;
            var d = ts.Day;
            var h = ts.Hour;
            var mm = ts.Minute;
            var s = ts.Second;
            var ms = ts.Millisecond;

            var b = new DateTime(y, m, d, h, 0, 0);
            var seg = b.AddSeconds(increment);
            while (ts >= seg)
            {
                seg = seg.AddSeconds(increment);
            }

            seg = seg.AddSeconds(increment * -1);

            return seg.ToUnixTime();
        }

        /// <summary>
        /// Gets the end of the Segment containing the specified Unix time (in nanoseconds) for the specified increment (in seconds), with Segments aligned to the start of the Hour
        /// </summary>
        public static long GetSegmentTop(long uts, int increment)
        {
            var ts = uts.ToDateTime();

            var y = ts.Year;
            var m = ts.Month;
            var d = ts.Day;
            var h = ts.Hour;
            var mm = ts.Minute;
            var s = ts.Second;
            var ms = ts.Millisecond;

            var b = new DateTime(y, m, d, h, 0, 0);
            b = b.AddHours(1);

            var seg = b.AddSeconds(increment * -1);
            while (ts < seg)
            {
                seg = seg.AddSeconds(increment * -1);
            }

            seg = seg.AddSeconds(increment);

            return seg.ToUnixTime();
        }
    }
}
