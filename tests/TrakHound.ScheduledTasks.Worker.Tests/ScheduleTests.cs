// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.ScheduledTasks.Tests
{
    public class ScheduleTests
    {
        private static DateTime Utc(int hour, int minute, int second = 0) => new DateTime(2026, 1, 15, hour, minute, second, DateTimeKind.Utc);


        [Fact]
        public void SixFields_IncludesSeconds()
        {
            var schedule = Schedule.Parse("*/10 * * * * *", "UTC");

            Assert.Equal(Utc(0, 0, 10), schedule.GetNext(Utc(0, 0, 1)));
        }

        [Fact]
        public void FiveFields_IsMinutes()
        {
            var schedule = Schedule.Parse("*/5 * * * *", "UTC");

            Assert.Equal(Utc(0, 5), schedule.GetNext(Utc(0, 0, 1)));
        }

        [Fact]
        public void TimeZone_IsApplied()
        {
            // 08:00 in New York (EST, UTC-5 in January) is 13:00 UTC
            var schedule = Schedule.Parse("0 8 * * *", "America/New_York");

            Assert.Equal(Utc(13, 0), schedule.GetNext(Utc(0, 0)));
        }

        [Fact]
        public void EmptyTimeZone_IsServerLocal()
        {
            var schedule = Schedule.Parse("0 8 * * *", null);

            Assert.Equal(TimeZoneInfo.Local.Id, schedule.TimeZone.Id);
        }

        [Fact]
        public void UnknownTimeZone_Throws()
        {
            var exception = Assert.Throws<ArgumentException>(() => Schedule.Parse("0 8 * * *", "Not/AZone"));
            Assert.Contains("Not/AZone", exception.Message);
        }

        [Fact]
        public void InvalidExpression_Throws()
        {
            Assert.ThrowsAny<Exception>(() => Schedule.Parse("every day", "UTC"));
        }

        [Fact]
        public void MissedOccurrence_IsTheFirstAfterTheLastRun()
        {
            var schedule = Schedule.Parse("0 * * * *", "UTC");

            var missed = schedule.GetMissedOccurrence(Utc(7, 0), Utc(9, 30), TimeSpan.FromMinutes(1));

            Assert.Equal(Utc(8, 0), missed);
        }

        [Fact]
        public void MissedOccurrence_NoneWhenUpToDate()
        {
            var schedule = Schedule.Parse("0 * * * *", "UTC");

            Assert.Null(schedule.GetMissedOccurrence(Utc(9, 0), Utc(9, 30), TimeSpan.FromMinutes(1)));
        }

        [Fact]
        public void MissedOccurrence_RecentOccurrenceIsLeftToTheSchedule()
        {
            var schedule = Schedule.Parse("0 * * * *", "UTC");

            // 09:00 is only 30 seconds ago (inside the grace period)
            Assert.Null(schedule.GetMissedOccurrence(Utc(8, 0), Utc(9, 0, 30), TimeSpan.FromMinutes(1)));
        }
    }
}
