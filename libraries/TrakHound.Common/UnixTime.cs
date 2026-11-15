// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound
{
    /// <summary>
    /// Helpers for the current Unix time
    /// </summary>
    public static class UnixDateTime
    {
        /// <summary>
        /// The current UTC time as Unix time (in nanoseconds)
        /// </summary>
        public static long Now => DateTime.UtcNow.ToUnixTime();

        /// <summary>
        /// Gets the time elapsed since the specified Unix time (in nanoseconds)
        /// </summary>
        public static TimeSpan Ago(long unixNanoseconds) => TimeSpan.FromTicks(Now / 100 - unixNanoseconds / 100); // Ticks so very old times can't overflow
    }


    /// <summary>
    /// Extension methods for converting between DateTime and Unix time (in nanoseconds unless noted)
    /// </summary>
    public static class UnixTimeExtensions
    {
        /// <summary>
        /// The Unix Epoch (1970-01-01T00:00:00Z)
        /// </summary>
        public static readonly DateTime EpochTime = DateTime.UnixEpoch;

        private const long NanosecondsPerTick = 100;
        private static readonly long _maxUnixTicks = DateTime.MaxValue.Ticks - EpochTime.Ticks;
        private static readonly long _minUnixTicks = DateTime.MinValue.Ticks - EpochTime.Ticks;
        private static readonly DateTime _maxUtc = DateTime.SpecifyKind(DateTime.MaxValue, DateTimeKind.Utc);
        private static readonly DateTime _minUtc = DateTime.SpecifyKind(DateTime.MinValue, DateTimeKind.Utc);


        /// <summary>
        /// Converts the DateTime to Unix time in nanoseconds (clamped to the range of a long).
        /// Local times are converted to UTC first; Unspecified times are treated as UTC
        /// </summary>
        public static long ToUnixTime(this DateTime d)
        {
            var ticks = GetUnixTicks(d);
            if (ticks > long.MaxValue / NanosecondsPerTick) return long.MaxValue;
            if (ticks < long.MinValue / NanosecondsPerTick) return long.MinValue;
            return ticks * NanosecondsPerTick;
        }

        /// <summary>
        /// Converts the DateTime to Unix time in milliseconds (rounded down).
        /// Local times are converted to UTC first; Unspecified times are treated as UTC
        /// </summary>
        public static long ToUnixTimeMilliseconds(this DateTime d) => FloorDivide(GetUnixTicks(d), TimeSpan.TicksPerMillisecond);

        /// <summary>
        /// Converts the DateTime to Unix time in seconds (rounded down).
        /// Local times are converted to UTC first; Unspecified times are treated as UTC
        /// </summary>
        public static long ToUnixTimeSeconds(this DateTime d) => FloorDivide(GetUnixTicks(d), TimeSpan.TicksPerSecond);

        private static long GetUnixTicks(DateTime d)
        {
            if (d.Kind == DateTimeKind.Local) d = d.ToUniversalTime();
            return d.Ticks - EpochTime.Ticks;
        }

        // Rounds toward negative infinity (like DateTimeOffset.ToUnixTimeSeconds) so times before the Epoch round down
        private static long FloorDivide(long value, long divisor)
        {
            var quotient = value / divisor;
            if (value % divisor < 0) quotient--;
            return quotient;
        }


        /// <summary>
        /// Converts the Unix time (in nanoseconds) to a UTC DateTime
        /// </summary>
        public static DateTime ToDateTime(this long unixNanoseconds) => FromUnixTime(unixNanoseconds);

        /// <summary>
        /// Converts the Unix time (in nanoseconds) to a Local DateTime
        /// </summary>
        public static DateTime ToLocalDateTime(this long unixNanoseconds) => FromUnixTime(unixNanoseconds).ToLocalTime();

        // Every long nanosecond value maps to a valid DateTime (~1677–2262), so no range check is needed
        /// <summary>
        /// Converts the Unix time (in nanoseconds) to a UTC DateTime. Sub-tick nanoseconds are truncated toward zero
        /// (ex. -150 is 1 tick before the Epoch), which doesn't affect values created by ToUnixTime (always a multiple of 100)
        /// </summary>
        public static DateTime FromUnixTime(long unixNanoseconds) => EpochTime.AddTicks(unixNanoseconds / NanosecondsPerTick);

        /// <summary>
        /// Converts the Unix time (in seconds) to a UTC DateTime (clamped to the DateTime range, as UTC)
        /// </summary>
        public static DateTime FromUnixTimeSeconds(long unixSeconds) => FromUnixUnits(unixSeconds, TimeSpan.TicksPerSecond);

        /// <summary>
        /// Converts the Unix time (in milliseconds) to a UTC DateTime (clamped to the DateTime range, as UTC)
        /// </summary>
        public static DateTime FromUnixTimeMilliseconds(long unixMilliseconds) => FromUnixUnits(unixMilliseconds, TimeSpan.TicksPerMillisecond);


        private static DateTime FromUnixUnits(long value, long ticksPerUnit)
        {
            if (value > _maxUnixTicks / ticksPerUnit) return _maxUtc;
            if (value < _minUnixTicks / ticksPerUnit) return _minUtc;
            return EpochTime.AddTicks(value * ticksPerUnit);
        }
    }
}
