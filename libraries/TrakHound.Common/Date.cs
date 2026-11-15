// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Globalization;

namespace TrakHound
{
    /// <summary>
    /// A calendar Date (Year, Month, Day) without a time component
    /// </summary>
    public struct Date
    {
        /// <summary>
        /// The Year (1-9999)
        /// </summary>
        public int Year { get; }

        /// <summary>
        /// The Month of the Year (1-12)
        /// </summary>
        public int Month { get; }

        /// <summary>
        /// The Day of the Month (1-31)
        /// </summary>
        public int Day { get; }

        /// <summary>
        /// The Week of the Year, with weeks starting on Monday (0 if the Date is not valid)
        /// </summary>
        public int Week { get; }

        /// <summary>
        /// The Day of the Week
        /// </summary>
        public DayOfWeek DayOfWeek { get; }


        /// <summary>
        /// Creates a new Date from the specified Year, Month, and Day
        /// </summary>
        public Date(int year, int month, int day)
        {
            Year = year;
            Month = month;
            Day = day;
            Week = 0;
            DayOfWeek = 0;

            if (year > 0 && month > 0 && day > 0)
            {
                var ts = new DateTime(year, month, day);
                Calendar cal = new CultureInfo("en-US").Calendar;
                Week = cal.GetWeekOfYear(ts, CalendarWeekRule.FirstDay, DayOfWeek.Monday);

                DayOfWeek = ts.DayOfWeek;
            }
        }

        /// <summary>
        /// Creates a new Date from the Date component of the specified DateTime
        /// </summary>
        public Date(DateTime ts)
        {
            Year = ts.Year;
            Month = ts.Month;
            Day = ts.Day;

            Calendar cal = new CultureInfo("en-US").Calendar;
            Week = cal.GetWeekOfYear(ts, CalendarWeekRule.FirstDay, DayOfWeek.Monday);

            DayOfWeek = ts.DayOfWeek;
        }

        /// <summary>
        /// Creates a new Date by parsing the specified string (defaults to 0001-01-01 if the string cannot be parsed)
        /// </summary>
        public Date(string s)
        {
            Year = 1;
            Month = 1;
            Day = 1;
            Week = 0;
            DayOfWeek = 0;

            if (!string.IsNullOrEmpty(s))
            {
                if (DateTime.TryParse(s, out DateTime ts))
                {
                    Year = ts.Year;
                    Month = ts.Month;
                    Day = ts.Day;

                    Calendar cal = new CultureInfo("en-US").Calendar;
                    Week = cal.GetWeekOfYear(ts, CalendarWeekRule.FirstDay, DayOfWeek.Monday);

                    DayOfWeek = ts.DayOfWeek;
                }
            }
        }

        /// <summary>
        /// Returns the Date formatted as "Year-Month-Day" (without zero padding)
        /// </summary>
        public override string ToString()
        {
            return $"{Year}-{Month}-{Day}";
        }
    }

    /// <summary>
    /// Extension methods for converting to a Date
    /// </summary>
    public static class DateExtensions
    {
        /// <summary>
        /// Gets the Date component of the DateTime
        /// </summary>
        public static Date ToDate(this DateTime dateTime)
        {
            return new Date(dateTime.Year, dateTime.Month, dateTime.Day);
        }
    }
}
