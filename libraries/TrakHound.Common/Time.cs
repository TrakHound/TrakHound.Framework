// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound
{
    /// <summary>
    /// A time of day (Hour, Minute, Second) without a date component
    /// </summary>
    public struct Time
    {
        /// <summary>
        /// The Hour of the day (0-23)
        /// </summary>
        public int Hour { get; set; }

        /// <summary>
        /// The Minute of the Hour (0-59)
        /// </summary>
        public int Minute { get; set; }

        /// <summary>
        /// The Second of the Minute (0-59)
        /// </summary>
        public int Second { get; set; }


        /// <summary>
        /// Creates a new Time from the specified Hour, Minute, and Second
        /// </summary>
        public Time(int hour, int minute, int second)
        {
            Hour = hour;
            Minute = minute;
            Second = second;
        }

        /// <summary>
        /// Creates a new Time from the time of day of the specified DateTime
        /// </summary>
        public Time(DateTime ts)
        {
            Hour = ts.Hour;
            Minute = ts.Minute;
            Second = ts.Second;
        }

        /// <summary>
        /// Creates a new Time by parsing the specified string (defaults to 00:00:00 if the string cannot be parsed)
        /// </summary>
        public Time(string s)
        {
            Hour = 0;
            Minute = 0;
            Second = 0;

            if (!string.IsNullOrEmpty(s))
            {
                if (DateTime.TryParse(s, out DateTime ts))
                {
                    Hour = ts.Hour;
                    Minute = ts.Minute;
                    Second = ts.Second;
                }
            }
        }


        /// <summary>
        /// Returns the Time formatted using the current culture's short time pattern ("t")
        /// </summary>
        public override string ToString()
        {
            var m = DateTime.MinValue;
            var d = new DateTime(m.Year, m.Month, m.Day, Hour, Minute, Second);

            return d.ToString("t");
        }
    }
}
