// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Pages.Explorer.Services.TimeSeries;

namespace TrakHound.Instance.Routing.Tests
{
    public class TimeSeriesExplorerTests
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);


        [Theory]
        [InlineData(300, 1)]        // 5 minutes at 1 second
        [InlineData(3600, 30)]      // 1 hour at 30 seconds
        [InlineData(86400, 300)]    // 1 day at 5 minutes
        [InlineData(2592000, 21600)] // 30 days at 6 hours
        public void Interval_Keeps_A_Range_Within_The_Limit(int rangeSeconds, int intervalSeconds)
        {
            var interval = TimeSeriesExplorer.GetInterval(TimeSpan.FromSeconds(rangeSeconds));

            Assert.Equal(TimeSpan.FromSeconds(intervalSeconds), interval);
            Assert.True(rangeSeconds / interval.TotalSeconds <= TimeSeriesExplorer.MaxIntervals);
        }

        [Theory]
        [InlineData(86400, 60, 60)]     // 1 day at the selected 1 minute
        [InlineData(86400, 1, 10)]      // 1 day at 1 second is too many, so the next that fits
        [InlineData(300, 3600, 3600)]   // A Window longer than the range is still allowed
        public void Window_Is_The_Selected_One_If_It_Fits(int rangeSeconds, int selectedSeconds, int windowSeconds)
        {
            var window = TimeSeriesExplorer.GetWindow(TimeSpan.FromSeconds(rangeSeconds), TimeSpan.FromSeconds(selectedSeconds));

            Assert.Equal(TimeSpan.FromSeconds(windowSeconds), window);
            Assert.True(rangeSeconds / window.TotalSeconds <= TimeSeriesExplorer.MaxWindowIntervals);
        }

        [Theory]
        [InlineData("8000", 8000L)]
        [InlineData("-5", -5L)]
        [InlineData("8000.0", 8000.0)]
        [InlineData("0.5", 0.5)]
        [InlineData("1e3", 1000.0)]
        [InlineData("true", true)]
        [InlineData("FALSE", false)]
        [InlineData("RUN", "RUN")]
        [InlineData("\"8000\"", "8000")]
        [InlineData("\"true\"", "true")]
        public void Field_Text_Becomes_The_Right_Type(string text, object expected)
        {
            Assert.Equal(expected, TimeSeriesExplorer.ParseFieldValue(text));
        }

        [Fact]
        public void Lines_Are_Name_And_Value()
        {
            Assert.Null(TimeSeriesExplorer.ParseLines("machine=M1\r\n\r\n line = L=1 ", out var values));

            Assert.Equal("M1", values["machine"]);
            Assert.Equal("L=1", values["line"]);
        }

        [Theory]
        [InlineData("machine")]
        [InlineData("=M1")]
        [InlineData("machine=")]
        public void Lines_Without_A_Name_And_Value_Are_Rejected(string text)
        {
            Assert.NotNull(TimeSeriesExplorer.ParseLines(text, out _));
        }

        [Fact]
        public void Typed_Times_Are_Utc()
        {
            Assert.Equal(T0, TimeSeriesExplorer.ParseTime("2026-01-01T00:00:00"));
            Assert.Null(TimeSeriesExplorer.ParseTime("not a time"));
        }

        [Fact]
        public void Line_Scales_To_The_Chart()
        {
            var points = new (DateTimeOffset, double?)[] { (T0, 10.0), (T0.AddMinutes(5), 20.0), (T0.AddMinutes(10), 30.0) };

            var lines = TimeSeriesExplorer.GetLines(points, T0, T0.AddMinutes(10), 1000, 100, out var min, out var max);

            Assert.Equal("0,100 500,50 1000,0", Assert.Single(lines));
            Assert.Equal(10.0, min);
            Assert.Equal(30.0, max);
        }

        [Fact]
        public void A_Null_Value_Breaks_The_Line()
        {
            var points = new (DateTimeOffset, double?)[] { (T0, 1.0), (T0.AddMinutes(1), 2.0), (T0.AddMinutes(2), null), (T0.AddMinutes(3), 3.0), (T0.AddMinutes(4), 4.0) };

            var lines = TimeSeriesExplorer.GetLines(points, T0, T0.AddMinutes(4), 400, 100, out _, out _);

            Assert.Equal(2, lines.Count);
        }

        [Fact]
        public void A_Flat_Line_Sits_In_The_Middle()
        {
            var points = new (DateTimeOffset, double?)[] { (T0, 5.0), (T0.AddMinutes(1), 5.0) };

            var lines = TimeSeriesExplorer.GetLines(points, T0, T0.AddMinutes(1), 100, 100, out _, out _);

            Assert.Equal("0,50 100,50", Assert.Single(lines));
        }

        [Fact]
        public void A_Value_On_Its_Own_Still_Draws()
        {
            var points = new (DateTimeOffset, double?)[] { (T0, 5.0) };

            var lines = TimeSeriesExplorer.GetLines(points, T0, T0.AddMinutes(1), 100, 100, out _, out _);

            Assert.Equal("0,50 0,50", Assert.Single(lines));
        }

        [Fact]
        public void A_Short_Spike_Still_Shows_When_Points_Share_A_Position()
        {
            var points = Enumerable.Range(0, 1000).Select(i => (T0.AddSeconds(i), (double?)(i == 500 ? 100.0 : 1.0))).ToList();

            var lines = TimeSeriesExplorer.GetLines(points, T0, T0.AddSeconds(1000), 10, 100, out _, out var max);

            Assert.Equal(100.0, max);
            Assert.Contains(",0", Assert.Single(lines));
        }

        [Fact]
        public void A_Point_Before_The_Range_Stays_On_The_Chart()
        {
            var points = new (DateTimeOffset, double?)[] { (T0.AddMinutes(-5), 1.0), (T0.AddMinutes(5), 2.0) };

            var lines = TimeSeriesExplorer.GetLines(points, T0, T0.AddMinutes(10), 100, 100, out _, out _);

            Assert.StartsWith("0,", Assert.Single(lines));
        }

        [Fact]
        public void No_Values_Means_No_Lines()
        {
            var lines = TimeSeriesExplorer.GetLines(new (DateTimeOffset, double?)[] { (T0, null) }, T0, T0.AddMinutes(1), 100, 100, out var min, out var max);

            Assert.Empty(lines);
            Assert.Null(min);
            Assert.Null(max);
        }
    }
}
