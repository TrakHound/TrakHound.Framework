// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TrakHound
{
    /// <summary>
    /// A range of time (From / To) that can be expressed as a standard Year, Month, Day, Hour, Minute, or Second Id (ex. "2024-05-01H13") or as a custom "from:to" range in Unix nanoseconds
    /// </summary>
    public struct TimeRange : IComparable<TimeRange>
    {
        private const long _secondSpan = 1000000000;
        private const long _minuteSpan = _secondSpan * 60;
        private const long _hourSpan = _minuteSpan * 60;
        private const long _daySpan = _hourSpan * 24;
        private const long _weekSpan = _daySpan * 7;
        private const long _monthSpan = _daySpan * 30;
        private const long _yearSpan = _daySpan * 365;

        /// <summary>
        /// Relative expression for the Year containing the reference time
        /// </summary>
        public const string LastYearPattern = "last-year";
        /// <summary>
        /// Relative expression for the Month containing the reference time
        /// </summary>
        public const string LastMonthPattern = "last-month";
        /// <summary>
        /// Relative expression for the Day containing the reference time
        /// </summary>
        public const string LastDayPattern = "last-day";
        /// <summary>
        /// Relative expression for the Hour containing the reference time
        /// </summary>
        public const string LastHourPattern = "last-hour";
        /// <summary>
        /// Relative expression for the Minute containing the reference time
        /// </summary>
        public const string LastMinutePattern = "last-minute";
        /// <summary>
        /// Relative expression for the Second containing the reference time
        /// </summary>
        public const string LastSecondPattern = "last-second";
        private const string _lastExpressionPattern = "last-(.+)";
        private static Regex _lastExpressionRegex = new Regex(_lastExpressionPattern, RegexOptions.Compiled);


        /// <summary>
        /// The supported fixed relative expressions ("last-year", "last-month", etc.)
        /// </summary>
        public static IEnumerable<string> RelativeTimeRanges = new string[]
        {
            LastYearPattern,
            LastMonthPattern, 
            LastDayPattern, 
            LastHourPattern,
            LastMinutePattern,
            LastSecondPattern
        };


        private static DateTime _minimumFrom = new DateTime(1900, 1, 1);

        private const string _yearPattern = "([0-9]{4})";
        private static Regex _yearRegex = new Regex(_yearPattern, RegexOptions.Compiled);
        private const string _yearIdPattern = "([0-9]{4})";
        private static Regex _yearIdRegex = new Regex(_yearIdPattern, RegexOptions.Compiled);
        private const string _yearParsePattern = "^([0-9]{4})(?:Z((?:\\+|\\-)[0-9]{2}\\:[0-9]{2})|Z)?$";
        private static Regex _yearParseRegex = new Regex(_yearParsePattern, RegexOptions.Compiled);

        private const string _monthPattern = "[0-9]{4}-([0-9]{2})";
        private static Regex _monthRegex = new Regex(_monthPattern, RegexOptions.Compiled);
        private const string _monthIdPattern = "([0-9]{4}-[0-9]{2})";
        private static Regex _monthIdRegex = new Regex(_monthIdPattern, RegexOptions.Compiled);
        private const string _monthParsePattern = "^([0-9]{4})-([0-9]{2})(?:Z((?:\\+|\\-)[0-9]{2}:[0-9]{2})|Z)?$";
        private static Regex _monthParseRegex = new Regex(_monthParsePattern, RegexOptions.Compiled);

        private const string _dayPattern = "[0-9]{4}-[0-9]{2}-([0-9]{2})";
        private static Regex _dayRegex = new Regex(_dayPattern, RegexOptions.Compiled);
        private const string _dayIdPattern = "([0-9]{4}-[0-9]{2}-[0-9]{2})";
        private static Regex _dayIdRegex = new Regex(_dayIdPattern, RegexOptions.Compiled);
        private const string _dayParsePattern = "^([0-9]{4})-([0-9]{2})-([0-9]{2})(?:Z((?:\\+|\\-)[0-9]{2}:[0-9]{2})|Z)?$";
        private static Regex _dayParseRegex = new Regex(_dayParsePattern, RegexOptions.Compiled);

        private const string _hourPattern = "[0-9]{4}-[0-9]{2}-[0-9]{2}H([0-9]{2})";
        private static Regex _hourRegex = new Regex(_hourPattern, RegexOptions.Compiled);
        private const string _hourIdPattern = "([0-9]{4}-[0-9]{2}-[0-9]{2}H[0-9]{2})";
        private static Regex _hourIdRegex = new Regex(_hourIdPattern, RegexOptions.Compiled);
        private const string _hourParsePattern = "^([0-9]{4})-([0-9]{2})-([0-9]{2})H([0-9]{2})(?:Z((?:\\+|\\-)[0-9]{2}:[0-9]{2})|Z)?$";
        private static Regex _hourParseRegex = new Regex(_hourParsePattern, RegexOptions.Compiled);

        private const string _minutePattern = "[0-9]{4}-[0-9]{2}-[0-9]{2}H[0-9]{2}M([0-9]{2})";
        private static Regex _minuteRegex = new Regex(_minutePattern, RegexOptions.Compiled);
        private const string _minuteIdPattern = "([0-9]{4}-[0-9]{2}-[0-9]{2}H[0-9]{2}M[0-9]{2})";
        private static Regex _minuteIdRegex = new Regex(_minuteIdPattern, RegexOptions.Compiled);
        private const string _minuteParsePattern = "^([0-9]{4})-([0-9]{2})-([0-9]{2})H([0-9]{2})M([0-9]{2})(?:Z((?:\\+|\\-)[0-9]{2}:[0-9]{2})|Z)?$";
        private static Regex _minuteParseRegex = new Regex(_minuteParsePattern, RegexOptions.Compiled);

        private const string _secondPattern = "[0-9]{4}-[0-9]{2}-[0-9]{2}H[0-9]{2}M[0-9]{2}S([0-9]{2})";
        private static Regex _secondRegex = new Regex(_secondPattern, RegexOptions.Compiled);
        private const string _secondIdPattern = "([0-9]{4}-[0-9]{2}-[0-9]{2}H[0-9]{2}M[0-9]{2}S[0-9]{2})";
        private static Regex _secondIdRegex = new Regex(_secondIdPattern, RegexOptions.Compiled);
        private const string _secondParsePattern = "^([0-9]{4})-([0-9]{2})-([0-9]{2})H([0-9]{2})M([0-9]{2})S([0-9]{2})(?:Z((?:\\+|\\-)[0-9]{2}:[0-9]{2})|Z)?$";
        private static Regex _secondParseRegex = new Regex(_secondParsePattern, RegexOptions.Compiled);

        private const string _customPattern = "([0-9]+):([0-9]+)";
        private static Regex _customRegex = new Regex(_customPattern, RegexOptions.Compiled);


        /// <summary>
        /// The type of the range (Year, Month, Day, Hour, Minute, Second, or Custom if it does not match a standard unit)
        /// </summary>
        public TimeRangeType Type => GetTimeRangeType(this);

        /// <summary>
        /// The start of the range
        /// </summary>
        public DateTime From { get; set; }

        /// <summary>
        /// The end of the range
        /// </summary>
        public DateTime To { get; set; }

        /// <summary>
        /// The length of the range (To - From)
        /// </summary>
        public TimeSpan Duration => To - From;

        /// <summary>
        /// Returns true if From is after 1900-01-01 and To is after From
        /// </summary>
        public bool IsValid => From > _minimumFrom && To > From;


        /// <summary>
        /// Creates a new TimeRange from the specified start and end DateTimes
        /// </summary>
        public TimeRange(DateTime from, DateTime to)
        {
            From = from;
            To = to;
        }

        /// <summary>
        /// Creates a new TimeRange from the specified start and end Unix times (in nanoseconds)
        /// </summary>
        public TimeRange(long from, long to)
        {
            From = from.ToDateTime();
            To = to.ToDateTime();
        }


        /// <summary>
        /// An empty TimeRange (From and To both at the Unix Epoch)
        /// </summary>
        public static TimeRange Zero => new TimeRange(0, 0);



        /// <summary>
        /// Returns the standard Id for the range (ex. "2024-05" for a Month) or "from:to" in Unix nanoseconds for a Custom range
        /// </summary>
        public override string ToString()
        {
            if (IsStandardYear(this)) return GenerateYearId(From);
            else if (IsStandardMonth(this)) return GenerateMonthId(From);
            else if (IsStandardDay(this)) return GenerateDayId(From);
            else if (IsStandardHour(this)) return GenerateHourId(From);
            else if (IsStandardMinute(this)) return GenerateMinuteId(From);
            else if (IsStandardSecond(this)) return GenerateSecondId(From);

            return $"{From.ToUnixTime()}:{To.ToUnixTime()}";
        }

        /// <summary>
        /// Returns true if the expression parses to a valid TimeRange
        /// </summary>
        public static bool Validate(string timeRangeExpression)
        {
            var timeRange = TimeRange.Parse(timeRangeExpression);
            return timeRange.IsValid;
        }

        /// <summary>
        /// Compares TimeRanges by their From time (in UTC)
        /// </summary>
        public int CompareTo(TimeRange timeRange)
        {
            if (this.From.ToUniversalTime() < timeRange.From.ToUniversalTime()) return -1;
            if (this.From.ToUniversalTime() == timeRange.From.ToUniversalTime()) return 0;
            return 1;
        }

        /// <summary>
        /// Gets the type of the TimeRange based on its From and To times
        /// </summary>
        public static TimeRangeType GetTimeRangeType(TimeRange timeRange)
        {
            if (IsStandardYear(timeRange)) return TimeRangeType.Year;
            else if (IsStandardMonth(timeRange)) return TimeRangeType.Month;
            else if (IsStandardDay(timeRange)) return TimeRangeType.Day;
            else if (IsStandardHour(timeRange)) return TimeRangeType.Hour;
            else if (IsStandardMinute(timeRange)) return TimeRangeType.Minute;
            else if (IsStandardSecond(timeRange)) return TimeRangeType.Second;

            return TimeRangeType.Custom;
        }

        /// <summary>
        /// Gets the type that matches the specified span (in nanoseconds), using 30 days for a Month and 365 days for a Year
        /// </summary>
        public static TimeRangeType GetTimeRangeType(long span)
        {
            switch (span)
            {
                case _yearSpan: return TimeRangeType.Year;
                case _monthSpan: return TimeRangeType.Month;
                //case _weekSpan: return TimeRangeType.Week;
                case _daySpan: return TimeRangeType.Day;
                case _hourSpan: return TimeRangeType.Hour;
                case _minuteSpan: return TimeRangeType.Minute;
                case _secondSpan: return TimeRangeType.Second;
            }

            return TimeRangeType.Custom;
        }


        /// <summary>
        /// Gets the TimeRange for a relative expression ("last-year", "last-day", etc. or "last-{TimeSpan}" such as "last-5m") relative to the specified Unix time in nanoseconds (defaults to now). Returns null if the expression is not valid
        /// </summary>
        public static TimeRange? GetRelative(string relativeExpression, long? timestamp = null)
        {
            var ts = timestamp != null ? timestamp.Value.ToDateTime() : DateTime.UtcNow;
            return GetRelative(relativeExpression, ts);
        }

        /// <summary>
        /// Gets the TimeRange for a relative expression ("last-year", "last-day", etc. or "last-{TimeSpan}" such as "last-5m") relative to the specified time (defaults to UtcNow). Returns null if the expression is not valid
        /// </summary>
        public static TimeRange? GetRelative(string relativeExpression, DateTime? timestamp = null)
        {
            if (!string.IsNullOrEmpty(relativeExpression))
            {
                var ts = timestamp ?? DateTime.UtcNow;

                if (relativeExpression == LastYearPattern) return TimeRange.FromYear(ts);
                else if (relativeExpression == LastMonthPattern) return TimeRange.FromMonth(ts);
                else if (relativeExpression == LastDayPattern) return TimeRange.FromDay(ts);
                else if (relativeExpression == LastHourPattern) return TimeRange.FromHour(ts);
                else if (relativeExpression == LastMinutePattern) return TimeRange.FromMinute(ts);
                else if (relativeExpression == LastSecondPattern) return TimeRange.FromSecond(ts);
                else
                {
                    var match = _lastExpressionRegex.Match(relativeExpression);
                    if (match.Success)
                    {
                        var expression = match.Groups[1].Value;
                        var timeSpan = expression.ToTimeSpan();
                        if (timeSpan > TimeSpan.Zero)
                        {
                            return new TimeRange(ts - timeSpan, ts);
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Returns true if this range overlaps the specified range
        /// </summary>
        public bool IsBetween(DateTime from, DateTime to)
        {
            if (to > from && To > From)
            {
                if (From >= from && From < to) return true;
                if (To >= from && To < to) return true;
                if (From <= from && To >= to) return true;
            }

            return false;
        }


        #region "Detect"

        /// <summary>
        /// Returns true if the TimeRange spans a single calendar Year
        /// </summary>
        public static bool IsStandardYear(TimeRange timeRange)
        {
            return timeRange.From.Year != timeRange.To.Year &&
                   timeRange.From.Month == 1 &&
                   timeRange.From.Day == 1 &&
                   timeRange.From.Minute == 0 &&
                   timeRange.From.Second == 0 &&
                   timeRange.To.Month == 1 &&
                   timeRange.To.Day == 1 &&
                   timeRange.To.Minute == 0 &&
                   timeRange.To.Second == 0;
        }

        /// <summary>
        /// Returns true if the TimeRange spans a single calendar Month
        /// </summary>
        public static bool IsStandardMonth(TimeRange timeRange)
        {
            return timeRange.From.Month != timeRange.To.Month &&
                   timeRange.From.Day == 1 &&
                   timeRange.From.Minute == 0 &&
                   timeRange.From.Second == 0 &&
                   timeRange.To.Day == 1 &&
                   timeRange.To.Minute == 0 &&
                   timeRange.To.Second == 0;
        }

        /// <summary>
        /// Returns true if the TimeRange spans a single Day starting on the Hour
        /// </summary>
        public static bool IsStandardDay(TimeRange timeRange)
        {
            if (timeRange.Duration == TimeSpan.FromDays(1))
            {
                return timeRange.From.Minute == 0 &&
                       timeRange.From.Second == 0 &&
                       timeRange.To.Minute == 0 &&
                       timeRange.To.Second == 0;
            }

            return false;
        }

        /// <summary>
        /// Returns true if the TimeRange spans a single Hour starting on the Hour
        /// </summary>
        public static bool IsStandardHour(TimeRange timeRange)
        {
            if (timeRange.Duration == TimeSpan.FromHours(1))
            {
                return timeRange.From.Minute == 0 &&
                       timeRange.From.Second == 0 &&
                       timeRange.To.Minute == 0 &&
                       timeRange.To.Second == 0;

            }

            return false;
        }

        /// <summary>
        /// Returns true if the TimeRange spans a single Minute starting on the Minute
        /// </summary>
        public static bool IsStandardMinute(TimeRange timeRange)
        {
            if (timeRange.Duration == TimeSpan.FromMinutes(1))
            {
                return timeRange.From.Second == 0 && timeRange.To.Second == 0;

            }

            return false;
        }

        /// <summary>
        /// Returns true if the TimeRange spans a single Second starting on the Second
        /// </summary>
        public static bool IsStandardSecond(TimeRange timeRange)
        {
            if (timeRange.Duration == TimeSpan.FromSeconds(1))
            {
                return timeRange.From.Millisecond == 0 && timeRange.To.Millisecond == 0;

            }

            return false;
        }

        #endregion

        #region "Generate"

        /// <summary>
        /// Generates the Hour and Minute Ids for the range between the specified Unix times (in nanoseconds)
        /// </summary>
        public static IEnumerable<string> Generate(long from, long to)
        {
            return Generate(from.ToDateTime(), to.ToDateTime());
        }

        /// <summary>
        /// Generates the Hour and Minute Ids for the range between the specified DateTimes
        /// </summary>
        public static IEnumerable<string> Generate(DateTime from, DateTime to)
        {
            var ids = new List<string>();

            ids.AddRange(GenerateHourIds(from, to));
            ids.AddRange(GenerateMinuteIds(from, to));

            return ids;
        }


        /// <summary>
        /// Splits the range between the specified Unix times (in nanoseconds) into one-Second TimeRanges
        /// </summary>
        public static IEnumerable<TimeRange> GenerateSeconds(long from, long to) => GenerateSeconds(new TimeRange(from, to));

        /// <summary>
        /// Splits the range between the specified DateTimes into one-Second TimeRanges
        /// </summary>
        public static IEnumerable<TimeRange> GenerateSeconds(DateTime from, DateTime to) => GenerateSeconds(new TimeRange(from, to));

        /// <summary>
        /// Splits the TimeRange into one-Second TimeRanges, from the Second containing From through the Second ending at or containing To
        /// </summary>
        public static IEnumerable<TimeRange> GenerateSeconds(TimeRange timeRange)
        {
            var timeRanges = new List<TimeRange>();

            var start = GetFirstStandardSecond(timeRange);
            var last = GetLastStandardSecond(timeRange);
            if (start.IsValid && last.IsValid)
            {
                var prev = start.From;
                var next = start.To;

                while (next <= last.To)
                {
                    timeRanges.Add(new TimeRange(prev, next));

                    prev = next;
                    next = next.AddSeconds(1);
                }
            }

            return timeRanges;
        }


        /// <summary>
        /// Generates the Second Ids for the range between the specified Unix times (in nanoseconds)
        /// </summary>
        public static IEnumerable<string> GenerateSecondIds(long from, long to) => GenerateSecondIds(new TimeRange(from, to));

        /// <summary>
        /// Generates the Second Ids for the range between the specified DateTimes
        /// </summary>
        public static IEnumerable<string> GenerateSecondIds(DateTime from, DateTime to) => GenerateSecondIds(new TimeRange(from, to));

        /// <summary>
        /// Generates the Second Ids for the TimeRange
        /// </summary>
        public static IEnumerable<string> GenerateSecondIds(TimeRange timeRange) => GenerateSeconds(timeRange).Select(o => o.ToString());


        /// <summary>
        /// Splits the range between the specified Unix times (in nanoseconds) into one-Minute TimeRanges
        /// </summary>
        public static IEnumerable<TimeRange> GenerateMinutes(long from, long to) => GenerateMinutes(new TimeRange(from, to));

        /// <summary>
        /// Splits the range between the specified DateTimes into one-Minute TimeRanges
        /// </summary>
        public static IEnumerable<TimeRange> GenerateMinutes(DateTime from, DateTime to) => GenerateMinutes(new TimeRange(from, to));

        /// <summary>
        /// Splits the TimeRange into one-Minute TimeRanges, from the Minute containing From through the Minute ending at or containing To
        /// </summary>
        public static IEnumerable<TimeRange> GenerateMinutes(TimeRange timeRange)
        {
            var timeRanges = new List<TimeRange>();

            var start = GetFirstStandardMinute(timeRange);
            var last = GetLastStandardMinute(timeRange);
            if (start.IsValid && last.IsValid)
            {
                var prev = start.From;
                var next = start.To;

                while (next <= last.To)
                {
                    timeRanges.Add(new TimeRange(prev, next));

                    prev = next;
                    next = next.AddMinutes(1);
                }
            }

            return timeRanges;
        }


        /// <summary>
        /// Generates the Minute Ids for the range between the specified Unix times (in nanoseconds)
        /// </summary>
        public static IEnumerable<string> GenerateMinuteIds(long from, long to) => GenerateMinuteIds(new TimeRange(from, to));

        /// <summary>
        /// Generates the Minute Ids for the range between the specified DateTimes
        /// </summary>
        public static IEnumerable<string> GenerateMinuteIds(DateTime from, DateTime to) => GenerateMinuteIds(new TimeRange(from, to));

        /// <summary>
        /// Generates the Minute Ids for the TimeRange
        /// </summary>
        public static IEnumerable<string> GenerateMinuteIds(TimeRange timeRange) => GenerateMinutes(timeRange).Select(o => o.ToString());



        /// <summary>
        /// Splits the range between the specified Unix times (in nanoseconds) into one-Hour TimeRanges
        /// </summary>
        public static IEnumerable<TimeRange> GenerateHours(long from, long to) => GenerateHours(new TimeRange(from, to));

        /// <summary>
        /// Splits the range between the specified DateTimes into one-Hour TimeRanges
        /// </summary>
        public static IEnumerable<TimeRange> GenerateHours(DateTime from, DateTime to) => GenerateHours(new TimeRange(from, to));

        /// <summary>
        /// Splits the TimeRange into one-Hour TimeRanges, from the Hour containing From through the Hour ending at or containing To
        /// </summary>
        public static IEnumerable<TimeRange> GenerateHours(TimeRange timeRange)
        {
            var timeRanges = new List<TimeRange>();

            var start = GetFirstStandardHour(timeRange);
            var last = GetLastStandardHour(timeRange);
            if (start.IsValid && last.IsValid)
            {
                var prev = start.From;
                var next = start.To;

                while (next <= last.To)
                {
                    timeRanges.Add(new TimeRange(prev, next));

                    prev = next;
                    next = next.AddHours(1);
                }
            }

            return timeRanges;
        }


        /// <summary>
        /// Generates the Hour Ids for the range between the specified Unix times (in nanoseconds)
        /// </summary>
        public static IEnumerable<string> GenerateHourIds(long from, long to) => GenerateHourIds(new TimeRange(from, to));

        /// <summary>
        /// Generates the Hour Ids for the range between the specified DateTimes
        /// </summary>
        public static IEnumerable<string> GenerateHourIds(DateTime from, DateTime to) => GenerateHourIds(new TimeRange(from, to));

        /// <summary>
        /// Generates the Hour Ids for the TimeRange
        /// </summary>
        public static IEnumerable<string> GenerateHourIds(TimeRange timeRange) => GenerateHours(timeRange).Select(o => o.ToString());



        /// <summary>
        /// Generates the Year Id ("yyyy") for the specified Unix time (in nanoseconds) in Local time, including the zone offset
        /// </summary>
        public static string GenerateYearId(long timestamp) => GenerateYearId(timestamp.ToLocalDateTime());

        /// <summary>
        /// Generates the Year Id ("yyyy") for the specified Date
        /// </summary>
        public static string GenerateYearId(Date date) => date.Year.ToString();

        /// <summary>
        /// Generates the Year Id ("yyyy") for the specified DateTime, including the zone offset for Local times
        /// </summary>
        public static string GenerateYearId(DateTime dateTime) => $"{dateTime.Year.ToString()}{GetZoneId(dateTime)}";


        /// <summary>
        /// Generates the Month Id ("yyyy-MM") for the specified Unix time (in nanoseconds) in Local time, including the zone offset
        /// </summary>
        public static string GenerateMonthId(long timestamp) => GenerateMonthId(timestamp.ToLocalDateTime());

        /// <summary>
        /// Generates the Month Id ("yyyy-MM") for the specified Date
        /// </summary>
        public static string GenerateMonthId(Date date) => $"{date.Year}-{date.Month.ToString("00")}";

        /// <summary>
        /// Generates the Month Id ("yyyy-MM") for the specified DateTime, including the zone offset for Local times
        /// </summary>
        public static string GenerateMonthId(DateTime dateTime) => $"{dateTime.Year}-{dateTime.Month.ToString("00")}{GetZoneId(dateTime)}";


        /// <summary>
        /// Generates the Day Id ("yyyy-MM-dd") for the specified Unix time (in nanoseconds) in Local time, including the zone offset
        /// </summary>
        public static string GenerateDayId(long timestamp) => GenerateDayId(timestamp.ToLocalDateTime());

        /// <summary>
        /// Generates the Day Id ("yyyy-MM-dd") for the specified Date
        /// </summary>
        public static string GenerateDayId(Date date) => $"{date.Year}-{date.Month.ToString("00")}-{date.Day.ToString("00")}";

        /// <summary>
        /// Generates the Day Id ("yyyy-MM-dd") for the specified DateTime, including the zone offset for Local times
        /// </summary>
        public static string GenerateDayId(DateTime dateTime) => $"{dateTime.Year}-{dateTime.Month.ToString("00")}-{dateTime.Day.ToString("00")}{GetZoneId(dateTime)}";


        /// <summary>
        /// Generates the Hour Id ("yyyy-MM-ddHhh") for the specified Unix time (in nanoseconds) in Local time, including the zone offset
        /// </summary>
        public static string GenerateHourId(long timestamp) => GenerateHourId(timestamp.ToLocalDateTime());

        /// <summary>
        /// Generates the Hour Id ("yyyy-MM-ddH00") for the first Hour of the specified Date
        /// </summary>
        public static string GenerateHourId(Date date) => $"{date.Year}-{date.Month.ToString("00")}-{date.Day.ToString("00")}H00";

        /// <summary>
        /// Generates the Hour Id ("yyyy-MM-ddHhh") for the specified DateTime, including the zone offset for Local times
        /// </summary>
        public static string GenerateHourId(DateTime dateTime) => $"{dateTime.Year}-{dateTime.Month.ToString("00")}-{dateTime.Day.ToString("00")}H{dateTime.Hour.ToString("00")}{GetZoneId(dateTime)}";


        /// <summary>
        /// Generates the Minute Id ("yyyy-MM-ddHhhMmm") for the specified Unix time (in nanoseconds) in Local time, including the zone offset
        /// </summary>
        public static string GenerateMinuteId(long timestamp) => GenerateMinuteId(timestamp.ToLocalDateTime());

        /// <summary>
        /// Generates the Minute Id ("yyyy-MM-ddH00M00") for the first Minute of the specified Date
        /// </summary>
        public static string GenerateMinuteId(Date date) => $"{date.Year}-{date.Month.ToString("00")}-{date.Day.ToString("00")}H00M00";

        /// <summary>
        /// Generates the Minute Id ("yyyy-MM-ddHhhMmm") for the specified DateTime, including the zone offset for Local times
        /// </summary>
        public static string GenerateMinuteId(DateTime dateTime) => $"{dateTime.Year}-{dateTime.Month.ToString("00")}-{dateTime.Day.ToString("00")}H{dateTime.Hour.ToString("00")}M{dateTime.Minute.ToString("00")}{GetZoneId(dateTime)}";


        /// <summary>
        /// Generates the Second Id ("yyyy-MM-ddHhhMmmSss") for the specified Unix time (in nanoseconds) in Local time, including the zone offset
        /// </summary>
        public static string GenerateSecondId(long timestamp) => GenerateSecondId(timestamp.ToLocalDateTime());

        /// <summary>
        /// Generates the Second Id ("yyyy-MM-ddH00M00S00") for the first Second of the specified Date
        /// </summary>
        public static string GenerateSecondId(Date date) => $"{date.Year}-{date.Month.ToString("00")}-{date.Day.ToString("00")}H00M00S00";

        /// <summary>
        /// Generates the Second Id ("yyyy-MM-ddHhhMmmSss") for the specified DateTime, including the zone offset for Local times
        /// </summary>
        public static string GenerateSecondId(DateTime dateTime) => $"{dateTime.Year}-{dateTime.Month.ToString("00")}-{dateTime.Day.ToString("00")}H{dateTime.Hour.ToString("00")}M{dateTime.Minute.ToString("00")}S{dateTime.Second.ToString("00")}{GetZoneId(dateTime)}";



        /// <summary>
        /// Gets the Week Id ("yyyy:ww") for the specified Unix time (in nanoseconds) in Local time
        /// </summary>
        public static string GetStandardWeek(long timestamp) => GetStandardWeek(timestamp.ToLocalDateTime());

        /// <summary>
        /// Gets the Week Id ("yyyy:ww") for the specified Date
        /// </summary>
        public static string GetStandardWeek(Date date) => $"{date.Year}:{date.Week.ToString("00")}";

        /// <summary>
        /// Gets the Week Id ("yyyy:ww") for the specified DateTime
        /// </summary>
        public static string GetStandardWeek(DateTime dateTime) => $"{dateTime.Year}:{dateTime.GetWeekOfYear().ToString("00")}";

        #endregion

        #region "Get"

        /// <summary>
        /// Gets the TimeRange for the UTC Year containing the specified DateTime
        /// </summary>
        public static TimeRange FromYear(DateTime dateTime)
        {
            var dt = dateTime.ToUniversalTime();
            var timeRange = new TimeRange();
            timeRange.From = new DateTime(dt.Year, 1, 1, 0, 0, 0, dt.Kind);
            timeRange.To = timeRange.From.AddYears(1);
            return timeRange;
        }

        /// <summary>
        /// Gets the TimeRange for the UTC Month containing the specified DateTime
        /// </summary>
        public static TimeRange FromMonth(DateTime dateTime)
        {
            var dt = dateTime.ToUniversalTime();
            var timeRange = new TimeRange();
            timeRange.From = new DateTime(dt.Year, dt.Month, 1, 0, 0, 0, dt.Kind);
            timeRange.To = timeRange.From.AddMonths(1);
            return timeRange;
        }

        /// <summary>
        /// Gets the TimeRange for the UTC Day containing the specified DateTime
        /// </summary>
        public static TimeRange FromDay(DateTime dateTime)
        {
            var dt = dateTime.ToUniversalTime();
            var timeRange = new TimeRange();
            timeRange.From = new DateTime(dt.Year, dt.Month, dt.Day, 0, 0, 0, dt.Kind);
            timeRange.To = timeRange.From.AddDays(1);
            return timeRange;
        }

        /// <summary>
        /// Gets the TimeRange for the UTC Hour containing the specified DateTime
        /// </summary>
        public static TimeRange FromHour(DateTime dateTime)
        {
            var dt = dateTime.ToUniversalTime();
            var timeRange = new TimeRange();
            timeRange.From = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, 0, 0, dt.Kind);
            timeRange.To = timeRange.From.AddHours(1);
            return timeRange;
        }

        /// <summary>
        /// Gets the TimeRange for the UTC Minute containing the specified DateTime
        /// </summary>
        public static TimeRange FromMinute(DateTime dateTime)
        {
            var dt = dateTime.ToUniversalTime();
            var timeRange = new TimeRange();
            timeRange.From = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, 0, dt.Kind);
            timeRange.To = timeRange.From.AddMinutes(1);
            return timeRange;
        }

        /// <summary>
        /// Gets the TimeRange for the UTC Second containing the specified DateTime
        /// </summary>
        public static TimeRange FromSecond(DateTime dateTime)
        {
            var dt = dateTime.ToUniversalTime();
            var timeRange = new TimeRange();
            timeRange.From = new DateTime(dt.Year, dt.Month, dt.Day, dt.Hour, dt.Minute, dt.Second, 0, dt.Kind);
            timeRange.To = timeRange.From.AddSeconds(1);
            return timeRange;
        }


        /// <summary>
        /// Parses a Year, Month, Day, Hour, Minute, or Second Id (with an optional "Z+hh:mm" offset) or a custom "from:to" range in Unix nanoseconds (Zero if the expression is empty or invalid)
        /// </summary>
        public static TimeRange Parse(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                Match match;

                // Second
                match = _secondParseRegex.Match(timeRangeExpression);
                if (match.Success) return ParseSecond(timeRangeExpression);

                // Minute
                match = _minuteParseRegex.Match(timeRangeExpression);
                if (match.Success) return ParseMinute(timeRangeExpression);

                // Hour
                match = _hourParseRegex.Match(timeRangeExpression);
                if (match.Success) return ParseHour(timeRangeExpression);

                // Day
                match = _dayParseRegex.Match(timeRangeExpression);
                if (match.Success) return ParseDay(timeRangeExpression);

                // Month
                match = _monthParseRegex.Match(timeRangeExpression);
                if (match.Success) return ParseMonth(timeRangeExpression);

                // Year
                match = _yearParseRegex.Match(timeRangeExpression);
                if (match.Success) return ParseYear(timeRangeExpression);

                // Custom
                return ParseCustom(timeRangeExpression);
            }

            return Zero;
        }

        /// <summary>
        /// Parses each expression and returns the valid TimeRanges
        /// </summary>
        public static IEnumerable<TimeRange> Parse(IEnumerable<string> timeRangeExpressions)
        {
            var timeRanges = new List<TimeRange>();

            if (!timeRangeExpressions.IsNullOrEmpty())
            {
                foreach (var timeRangeExpression in timeRangeExpressions)
                {
                    var timeRange = Parse(timeRangeExpression);
                    if (timeRange.IsValid)
                    {
                        timeRanges.Add(timeRange);
                    }
                }
            }

            return timeRanges;
        }


        /// <summary>
        /// Parses a Year Id ("yyyy" with an optional "Z+hh:mm" offset) into a TimeRange (Zero if invalid)
        /// </summary>
        public static TimeRange ParseYear(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _yearParseRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    var year = match.Groups[1].Value.ToInt();

                    var offset = TimeSpan.Zero;
                    if (match.Groups.Count > 2)
                    {
                        var offsetStr = match.Groups[2].Value;
                        if (!string.IsNullOrEmpty(offsetStr))
                        {
                            offset = TimeSpan.Parse(offsetStr);
                        }
                    }


                    DateTime from;
                    if (offset != TimeSpan.Zero)
                    {
                        var dateTimeOffset = new DateTimeOffset(new DateTime(year, 1, 1, 0, 0, 0), offset);

                        //from = dateTimeOffset.LocalDateTime;
                        //from = from.Subtract(offset);
                        from = dateTimeOffset.DateTime;
                        from = from.Subtract(offset);
                        from = new DateTime(from.Ticks, DateTimeKind.Utc);
                    }
                    else
                    {
                        from = new DateTime(year, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                    }

                    var to = from.AddYears(1);

                    return new TimeRange(from, to);
                }
            }

            return Zero;
        }

        /// <summary>
        /// Parses a Month Id ("yyyy-MM" with an optional "Z+hh:mm" offset) into a TimeRange (Zero if invalid)
        /// </summary>
        public static TimeRange ParseMonth(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _monthParseRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    var year = match.Groups[1].Value.ToInt();
                    var month = match.Groups[2].Value.ToInt();

                    var offset = TimeSpan.Zero;
                    if (match.Groups.Count > 3)
                    {
                        var offsetStr = match.Groups[3].Value;
                        if (!string.IsNullOrEmpty(offsetStr))
                        {
                            offset = TimeSpan.Parse(offsetStr);
                        }
                    }


                    DateTime from;
                    if (offset != TimeSpan.Zero)
                    {
                        var dateTimeOffset = new DateTimeOffset(new DateTime(year, month, 1, 0, 0, 0), offset);

                        //from = dateTimeOffset.LocalDateTime;
                        //from = from.Subtract(offset);
                        from = dateTimeOffset.DateTime;
                        from = from.Subtract(offset);
                        from = new DateTime(from.Ticks, DateTimeKind.Utc);
                    }
                    else
                    {
                        from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc);
                    }

                    var to = from.AddMonths(1);

                    return new TimeRange(from, to);
                }
            }

            return Zero;
        }

        /// <summary>
        /// Parses a Day Id ("yyyy-MM-dd" with an optional "Z+hh:mm" offset) into a TimeRange (Zero if invalid)
        /// </summary>
        public static TimeRange ParseDay(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _dayParseRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    var year = match.Groups[1].Value.ToInt();
                    var month = match.Groups[2].Value.ToInt();
                    var day = match.Groups[3].Value.ToInt();


                    var offset = TimeSpan.Zero;
                    if (match.Groups.Count > 4)
                    {
                        var offsetStr = match.Groups[4].Value;
                        if (!string.IsNullOrEmpty(offsetStr))
                        {
                            offset = TimeSpan.Parse(offsetStr);
                        }
                    }


                    DateTime from;
                    if (offset != TimeSpan.Zero)
                    {
                        var dateTimeOffset = new DateTimeOffset(new DateTime(year, month, day, 0, 0, 0), offset);

                        //from = dateTimeOffset.LocalDateTime;
                        //from = from.Subtract(offset);
                        from = dateTimeOffset.DateTime;
                        from = from.Subtract(offset);
                        from = new DateTime(from.Ticks, DateTimeKind.Utc);
                    }
                    else
                    {
                        from = new DateTime(year, month, day, 0, 0, 0, DateTimeKind.Utc);
                    }

                    var to = from.AddDays(1);

                    return new TimeRange(from, to);
                }
            }

            return Zero;
        }

        /// <summary>
        /// Parses an Hour Id ("yyyy-MM-ddHhh" with an optional "Z+hh:mm" offset) into a TimeRange (Zero if invalid)
        /// </summary>
        public static TimeRange ParseHour(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _hourParseRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    var year = match.Groups[1].Value.ToInt();
                    var month = match.Groups[2].Value.ToInt();
                    var day = match.Groups[3].Value.ToInt();
                    var hour = match.Groups[4].Value.ToInt();


                    var offset = TimeSpan.Zero;
                    if (match.Groups.Count > 5)
                    {
                        var offsetStr = match.Groups[5].Value;
                        if (!string.IsNullOrEmpty(offsetStr))
                        {
                            offset = TimeSpan.Parse(offsetStr);
                        }
                    }


                    DateTime from;
                    if (offset != TimeSpan.Zero)
                    {
                        var dateTimeOffset = new DateTimeOffset(new DateTime(year, month, day, hour, 0, 0), offset);

                        //from = dateTimeOffset.LocalDateTime;
                        //from = from.Subtract(offset);
                        from = dateTimeOffset.DateTime;
                        from = from.Subtract(offset);
                        from = new DateTime(from.Ticks, DateTimeKind.Utc);
                    }
                    else
                    {
                        from = new DateTime(year, month, day, hour, 0, 0, DateTimeKind.Utc);
                    }

                    var to = from.AddHours(1);

                    return new TimeRange(from, to);
                }
            }

            return Zero;
        }

        /// <summary>
        /// Parses a Minute Id ("yyyy-MM-ddHhhMmm" with an optional "Z+hh:mm" offset) into a TimeRange (Zero if invalid)
        /// </summary>
        public static TimeRange ParseMinute(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _minuteParseRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    var year = match.Groups[1].Value.ToInt();
                    var month = match.Groups[2].Value.ToInt();
                    var day = match.Groups[3].Value.ToInt();
                    var hour = match.Groups[4].Value.ToInt();
                    var minute = match.Groups[5].Value.ToInt();


                    var offset = TimeSpan.Zero;
                    if (match.Groups.Count > 6)
                    {
                        var offsetStr = match.Groups[6].Value;
                        if (!string.IsNullOrEmpty(offsetStr))
                        {
                            offset = TimeSpan.Parse(offsetStr);
                        }
                    }


                    DateTime from;
                    if (offset != TimeSpan.Zero)
                    {
                        var dateTimeOffset = new DateTimeOffset(new DateTime(year, month, day, hour, minute, 0), offset);

                        //from = dateTimeOffset.LocalDateTime;
                        //from = from.Subtract(offset);
                        from = dateTimeOffset.DateTime;
                        from = from.Subtract(offset);
                        from = new DateTime(from.Ticks, DateTimeKind.Utc);
                    }
                    else
                    {
                        from = new DateTime(year, month, day, hour, minute, 0, DateTimeKind.Utc);
                    }

                    var to = from.AddMinutes(1);

                    return new TimeRange(from, to);
                }
            }

            return Zero;
        }

        /// <summary>
        /// Parses a Second Id ("yyyy-MM-ddHhhMmmSss" with an optional "Z+hh:mm" offset) into a TimeRange (Zero if invalid)
        /// </summary>
        public static TimeRange ParseSecond(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _secondParseRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    var year = match.Groups[1].Value.ToInt();
                    var month = match.Groups[2].Value.ToInt();
                    var day = match.Groups[3].Value.ToInt();
                    var hour = match.Groups[4].Value.ToInt();
                    var minute = match.Groups[5].Value.ToInt();
                    var second = match.Groups[6].Value.ToInt();


                    var offset = TimeSpan.Zero;
                    if (match.Groups.Count > 7)
                    {
                        var offsetStr = match.Groups[7].Value;
                        if (!string.IsNullOrEmpty(offsetStr))
                        {
                            offset = TimeSpan.Parse(offsetStr);
                        }
                    }


                    DateTime from;
                    if (offset != TimeSpan.Zero)
                    {
                        var dateTimeOffset = new DateTimeOffset(new DateTime(year, month, day, hour, minute, second, 0), offset);

                        //from = dateTimeOffset.LocalDateTime;
                        //from = from.Subtract(offset);
                        from = dateTimeOffset.DateTime;
                        from = from.Subtract(offset);
                        from = new DateTime(from.Ticks, DateTimeKind.Utc);
                    }
                    else
                    {
                        from = new DateTime(year, month, day, hour, minute, second, 0, DateTimeKind.Utc);
                    }

                    var to = from.AddSeconds(1);

                    return new TimeRange(from, to);
                }
            }

            return Zero;
        }

        /// <summary>
        /// Parses a custom "from:to" range in Unix nanoseconds into a TimeRange (Zero if invalid)
        /// </summary>
        public static TimeRange ParseCustom(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _customRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    var start = match.Groups[1].Value.ToLong();
                    var end = match.Groups[2].Value.ToLong();

                    return new TimeRange(start, end);
                }
            }

            return Zero;
        }


        /// <summary>
        /// Gets the Year from the expression (0 if not found)
        /// </summary>
        public static int GetYear(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _yearRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    return match.Groups[1].Value.ToInt();
                }
            }

            return 0;
        }

        /// <summary>
        /// Gets the Year Id ("yyyy") from the expression (null if not found)
        /// </summary>
        public static string GetYearId(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _yearIdRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return null;
        }


        /// <summary>
        /// Gets the Month from the expression (0 if not found)
        /// </summary>
        public static int GetMonth(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _monthRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    return match.Groups[1].Value.ToInt();
                }
            }

            return 0;
        }

        /// <summary>
        /// Gets the Month Id ("yyyy-MM") from the expression (null if not found)
        /// </summary>
        public static string GetMonthId(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _monthIdRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return null;
        }


        /// <summary>
        /// Gets the Day from the expression (0 if not found)
        /// </summary>
        public static int GetDay(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _dayRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    return match.Groups[1].Value.ToInt();
                }
            }

            return 0;
        }

        /// <summary>
        /// Gets the Day Id ("yyyy-MM-dd") from the expression (null if not found)
        /// </summary>
        public static string GetDayId(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _dayIdRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return null;
        }


        /// <summary>
        /// Gets the Hour from the expression (0 if not found)
        /// </summary>
        public static int GetHour(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _hourRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    return match.Groups[1].Value.ToInt();
                }
            }

            return 0;
        }

        /// <summary>
        /// Gets the Hour Id ("yyyy-MM-ddHhh") from the expression (null if not found)
        /// </summary>
        public static string GetHourId(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _hourIdRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the one-Hour TimeRange containing the From time of the specified TimeRange
        /// </summary>
        public static TimeRange GetFirstStandardHour(TimeRange timeRange)
        {
            var from = new DateTime(timeRange.From.Year, timeRange.From.Month, timeRange.From.Day, timeRange.From.Hour, 0, 0, 0, timeRange.From.Kind);
            var to = from.AddHours(1);

            return new TimeRange(from, to);
        }

        /// <summary>
        /// Gets the last one-Hour TimeRange that ends at or contains the To time of the specified TimeRange
        /// </summary>
        public static TimeRange GetLastStandardHour(TimeRange timeRange)
        {
            var next = new DateTime(timeRange.To.Year, timeRange.To.Month, timeRange.To.Day, timeRange.To.Hour, 0, 0, 0, timeRange.To.Kind);
            DateTime from;
            DateTime to;

            if (next < timeRange.To)
            {
                from = next;
                to = from.AddHours(1);
            }
            else
            {
                to = next;
                from = to.AddHours(-1);
            }

            return new TimeRange(from, to);
        }


        /// <summary>
        /// Gets the Minute from the expression (0 if not found)
        /// </summary>
        public static int GetMinute(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _minuteRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    return match.Groups[1].Value.ToInt();
                }
            }

            return 0;
        }

        /// <summary>
        /// Gets the Minute Id ("yyyy-MM-ddHhhMmm") from the expression (null if not found)
        /// </summary>
        public static string GetMinuteId(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _minuteIdRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the one-Minute TimeRange containing the From time of the specified TimeRange
        /// </summary>
        public static TimeRange GetFirstStandardMinute(TimeRange timeRange)
        {
            var from = new DateTime(timeRange.From.Year, timeRange.From.Month, timeRange.From.Day, timeRange.From.Hour, timeRange.From.Minute, 0, 0, timeRange.From.Kind);
            var to = from.AddMinutes(1);

            return new TimeRange(from, to);
        }

        /// <summary>
        /// Gets the last one-Minute TimeRange that ends at or contains the To time of the specified TimeRange
        /// </summary>
        public static TimeRange GetLastStandardMinute(TimeRange timeRange)
        {
            var next = new DateTime(timeRange.To.Year, timeRange.To.Month, timeRange.To.Day, timeRange.To.Hour, timeRange.To.Minute, 0, 0, timeRange.To.Kind);
            DateTime from;
            DateTime to;

            if (next < timeRange.To)
            {
                from = next;
                to = from.AddMinutes(1);
            }
            else
            {
                to = next;
                from = to.AddMinutes(-1);
            }

            return new TimeRange(from, to);
        }


        /// <summary>
        /// Gets the Second from the expression (0 if not found)
        /// </summary>
        public static int GetSecond(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _secondRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    return match.Groups[1].Value.ToInt();
                }
            }

            return 0;
        }

        /// <summary>
        /// Gets the Second Id ("yyyy-MM-ddHhhMmmSss") from the expression (null if not found)
        /// </summary>
        public static string GetSecondId(string timeRangeExpression)
        {
            if (!string.IsNullOrEmpty(timeRangeExpression))
            {
                var match = _secondIdRegex.Match(timeRangeExpression);
                if (match.Success)
                {
                    return match.Groups[1].Value;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the one-Second TimeRange containing the From time of the specified TimeRange
        /// </summary>
        public static TimeRange GetFirstStandardSecond(TimeRange timeRange)
        {
            var from = new DateTime(timeRange.From.Year, timeRange.From.Month, timeRange.From.Day, timeRange.From.Hour, timeRange.From.Minute, timeRange.From.Second, 0, timeRange.From.Kind);
            var to = from.AddSeconds(1);

            return new TimeRange(from, to);
        }

        /// <summary>
        /// Gets the last one-Second TimeRange that ends at or contains the To time of the specified TimeRange
        /// </summary>
        public static TimeRange GetLastStandardSecond(TimeRange timeRange)
        {
            var next = new DateTime(timeRange.To.Year, timeRange.To.Month, timeRange.To.Day, timeRange.To.Hour, timeRange.To.Minute, timeRange.To.Second, 0, timeRange.To.Kind);
            DateTime from;
            DateTime to;

            if (next < timeRange.To)
            {
                from = next;
                to = from.AddSeconds(1);
            }
            else
            {
                to = next;
                from = to.AddSeconds(-1);
            }

            return new TimeRange(from, to);
        }


        //public static TimeRange GetFirstStandardHour(TimeRange timeRange)
        //{
        //    var next = new DateTime(timeRange.From.Year, timeRange.From.Month, timeRange.From.Day, timeRange.From.Hour, 0, 0, 0, timeRange.From.Kind);

        //    while (next < timeRange.From)
        //    {
        //        next = next.AddHours(1);
        //    }

        //    if (next < timeRange.To) return new TimeRange(next, next.AddHours(1));

        //    return Zero;
        //}

        //public static TimeRange GetLastStandardHour(TimeRange timeRange)
        //{
        //    var prev = new DateTime(timeRange.To.Year, timeRange.To.Month, timeRange.To.Day, timeRange.To.Hour, 0, 0, 0, timeRange.To.Kind);

        //    while (prev > timeRange.To)
        //    {
        //        prev = prev.AddHours(-1);
        //    }

        //    if (prev < timeRange.To) return new TimeRange(prev, prev.AddHours(1));

        //    return Zero;
        //}


        /// <summary>
        /// Gets the zone offset suffix ("Z+hh:mm") for a Local DateTime (empty for UTC or a zero offset)
        /// </summary>
        public static string GetZoneId(DateTime dateTime)
        {
            var offset = TimeSpan.Zero;
            if (dateTime.Kind == DateTimeKind.Local) offset = TimeZoneInfo.Local.GetUtcOffset(dateTime);
            if (offset != TimeSpan.Zero)
            {
                var offsetHour = offset.Hours;
                var offsetMinutes = offset.Minutes;
                var symbol = "";
                if (offset > TimeSpan.Zero) symbol = "+";
                if (offset < TimeSpan.Zero) symbol = "-";

                return $"Z{symbol}{Math.Abs(offsetHour).ToString("00")}:{offsetMinutes.ToString("00")}";
            }

            return "";
        }

        #endregion
       

        // Don't use Day of Week (it isn't a range of Time) instead each day of week should be a metric 

        //public static string GetStandardDayOfWeek(long timestamp) => GetStandardDayOfWeek(timestamp.ToLocalDateTime());

        //public static string GetStandardDayOfWeek(Date date) => $"{date.Year}::{date.DayOfWeek}";

        //public static string GetStandardDayOfWeek(DateTime dateTime) => $"{dateTime.Year}::{(int)dateTime.DayOfWeek}";
    }
}
