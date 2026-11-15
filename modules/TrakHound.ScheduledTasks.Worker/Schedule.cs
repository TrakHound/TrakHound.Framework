// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Cron;

namespace TrakHound.ScheduledTasks
{
    /// <summary>
    /// A Scheduled Task's CRON expression in its Time Zone (6 fields = with seconds)
    /// </summary>
    internal class Schedule
    {
        private readonly CronExpression _expression;


        public string Expression { get; }

        public TimeZoneInfo TimeZone { get; }


        private Schedule(string expression, CronExpression cronExpression, TimeZoneInfo timeZone)
        {
            Expression = expression;
            _expression = cronExpression;
            TimeZone = timeZone;
        }

        /// <summary>
        /// Parses the CRON expression and finds the Time Zone (IANA or Windows ID, the server's local Time Zone when empty)
        /// </summary>
        public static Schedule Parse(string expression, string timeZoneId = null)
        {
            if (string.IsNullOrWhiteSpace(expression)) throw new ArgumentException("Schedule is required");

            var trimmed = expression.Trim();
            var format = trimmed.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length > 5 ? CronFormat.IncludeSeconds : CronFormat.Standard;
            var cronExpression = CronExpression.Parse(trimmed, format);

            return new Schedule(trimmed, cronExpression, GetTimeZone(timeZoneId));
        }

        public static TimeZoneInfo GetTimeZone(string timeZoneId)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId)) return TimeZoneInfo.Local;

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(timeZoneId.Trim());
            }
            catch (TimeZoneNotFoundException)
            {
                throw new ArgumentException($"Time Zone Not Found : {timeZoneId}");
            }
            catch (InvalidTimeZoneException)
            {
                throw new ArgumentException($"Invalid Time Zone : {timeZoneId}");
            }
        }

        /// <summary>
        /// The next occurrence after fromUtc (UTC), or null if there are no more occurrences
        /// </summary>
        public DateTime? GetNext(DateTime fromUtc)
        {
            return _expression.GetNextOccurrence(DateTime.SpecifyKind(fromUtc, DateTimeKind.Utc), TimeZone);
        }

        /// <summary>
        /// The first occurrence after the last Run that is at least gracePeriod old (null when none was missed).
        /// Occurrences more recent than the grace period are left to the normal schedule
        /// </summary>
        public DateTime? GetMissedOccurrence(DateTime lastRunUtc, DateTime nowUtc, TimeSpan gracePeriod)
        {
            var next = GetNext(lastRunUtc);
            if (next != null && next.Value <= nowUtc - gracePeriod) return next;
            return null;
        }
    }
}
