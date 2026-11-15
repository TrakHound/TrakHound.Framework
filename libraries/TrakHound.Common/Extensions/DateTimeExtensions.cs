// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Globalization;

namespace TrakHound
{
    /// <summary>
    /// Extension methods for DateTime and TimeSpan
    /// </summary>
    public static class DateTimeExtensions
    {
        /// <summary>
        /// Formats the DateTime as an ISO 8601 string (round-trip "o" format)
        /// </summary>
        public static string ToISO8601String(this DateTime dateTime)
        {
            return dateTime.ToString("o");
        }

        /// <summary>
        /// Formats the TimeSpan as "hh:mm:ss", prefixed with the number of Days if one or more (ex. "2 Days 01:30:00").
        /// If showMilliseconds is true, a TimeSpan under one second is formatted in milliseconds (ex. "250ms")
        /// </summary>
        public static string ToFormattedString(this TimeSpan ts, bool showMilliseconds = false)
        {
            if (ts.Days > 1) return string.Format("{0:%d} Days {0:hh\\:mm\\:ss}", ts);
            else if (ts.Days > 0) return string.Format("{0:%d} Day {0:hh\\:mm\\:ss}", ts);
            else if (showMilliseconds && ts > TimeSpan.Zero && ts.TotalSeconds < 1) return $"{ts.TotalMilliseconds}ms";
            else return ts.ToString(@"hh\:mm\:ss");
        }

        /// <summary>
        /// Formats the TimeSpan using only its largest whole unit (ex. "3d", "5h", "12m", "40s")
        /// </summary>
        public static string ToSimpleFormattedString(this TimeSpan ts)
        {
            if (ts.Days > 0) return string.Format("{0:%d}d", ts);
            else if (ts.TotalHours >= 1) return string.Format("{0:%h}h", ts);
            else if (ts.TotalMinutes >= 1) return string.Format("{0:%m}m", ts);
            else if (ts.TotalMinutes < 1) return string.Format("{0:%s}s", ts);
            //else if (ts.TotalMinutes < 1) return string.Format("<1m", ts);
            //else if (ts.TotalSeconds < 10) return "<30s";
            else return "Just Now";
        }

        /// <summary>
        /// Formats the TimeSpan using its largest unit, with more precision for short durations (ex. "3d", "12m", "4.25s", "120ms", "0.125ms")
        /// </summary>
        public static string ToDetailedFormattedString(this TimeSpan ts)
        {
            if (ts.Days > 0) return string.Format("{0:%d}d", ts);
            else if (ts.TotalHours >= 1) return string.Format("{0:%h}h", ts);
            else if (ts.TotalMinutes >= 1) return string.Format("{0:%m}m", ts);
            else if (ts.TotalSeconds >= 10) return string.Format("{0:%s}s", ts);
            else if (ts.TotalSeconds >= 1) return string.Format("{0:%s\\.ff}s", ts);
            else if (ts.TotalMilliseconds > 1) return $"{Math.Round(ts.TotalMilliseconds, 0)}ms";
            else return $"{Math.Round((double)ts.Ticks / 10000, 3)}ms";
        }

        /// <summary>
        /// Gets the week number of the year (en-US calendar, weeks starting on Sunday, week 1 contains January 1st)
        /// </summary>
        public static int GetWeekOfYear(this DateTime dateTime)
        {
            var ci = new CultureInfo("en-US");
            return ci.Calendar.GetWeekOfYear(dateTime, CalendarWeekRule.FirstDay, DayOfWeek.Sunday);
        }
    }
}
