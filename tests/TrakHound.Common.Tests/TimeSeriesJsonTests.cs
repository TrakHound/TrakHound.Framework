// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Services.TimeSeries;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// Time Series Values keep their types through the JSON of the System API and Clients. A Field keeps one type, so a double that came back as a long would be rejected
    /// </summary>
    public class TimeSeriesJsonTests
    {
        private static T RoundTrip<T>(T value) => JsonSerializer.Deserialize<T>(Json.Convert(value), Json.SharedOptions);


        [Fact]
        public void Point_Fields_Keep_Their_Types()
        {
            var point = RoundTrip(new TrakHoundTimeSeriesPoint
            {
                Measurement = "spindle",
                Tags = new() { ["Machine"] = "M1" },
                Timestamp = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Fields = new() { ["wholeDouble"] = 8000.0, ["fraction"] = 0.5, ["large"] = 1e30, ["long"] = 8000L, ["on"] = true, ["off"] = false, ["state"] = "RUN" },
                Quality = TrakHoundTimeSeriesQuality.Bad
            });

            Assert.Equal(8000.0, Assert.IsType<double>(point.Fields["wholeDouble"]));
            Assert.Equal(0.5, Assert.IsType<double>(point.Fields["fraction"]));
            Assert.Equal(1e30, Assert.IsType<double>(point.Fields["large"]));
            Assert.Equal(8000L, Assert.IsType<long>(point.Fields["long"]));
            Assert.Equal(true, point.Fields["on"]);
            Assert.Equal(false, point.Fields["off"]);
            Assert.Equal("RUN", point.Fields["state"]);
            Assert.Equal(TrakHoundTimeSeriesQuality.Bad, point.Quality);
            Assert.Equal("M1", point.Tags["Machine"]);
        }

        [Fact]
        public void Field_And_Tag_Names_Keep_Their_Case()
        {
            var json = Json.Convert(new TrakHoundTimeSeriesPoint { Measurement = "m", Tags = new() { ["Machine"] = "M1" }, Fields = new() { ["SpindleSpeed"] = 1.0 } });

            Assert.Contains("\"Machine\"", json);
            Assert.Contains("\"SpindleSpeed\"", json);
        }

        [Fact]
        public void Query_Tags_Can_Be_One_Value_Or_Several()
        {
            var query = Json.Convert<TrakHoundTimeSeriesQuery>("{\"measurement\":\"m\",\"tags\":{\"machine\":[\"M1\",\"M2\"],\"line\":\"L1\"}}");

            Assert.Equal(new[] { "M1", "M2" }, query.Tags["machine"]);
            Assert.Equal(new[] { "L1" }, query.Tags["line"]);

            var read = RoundTrip(query);
            Assert.Equal(new[] { "M1", "M2" }, read.Tags["machine"]);
        }

        [Fact]
        public void Point_Keeps_A_Null_Value_And_Field_Qualities()
        {
            var point = RoundTrip(new TrakHoundTimeSeriesPoint
            {
                Measurement = "m",
                Fields = new() { ["speed"] = null, ["load"] = 1.0 },
                Qualities = new() { ["speed"] = TrakHoundTimeSeriesQuality.Bad }
            });

            Assert.True(point.Fields.ContainsKey("speed"));
            Assert.Null(point.Fields["speed"]);
            Assert.Equal(TrakHoundTimeSeriesQuality.Bad, point.GetQuality("speed"));
            Assert.Equal(TrakHoundTimeSeriesQuality.Good, point.GetQuality("load"));
        }

        [Fact]
        public void Latest_Result_Keeps_The_Type_Of_A_Value_And_A_Null_Value()
        {
            var timestamp = new DateTimeOffset(2026, 1, 1, 8, 30, 15, 123, TimeSpan.Zero);

            var result = RoundTrip(new TrakHoundTimeSeriesLatestResult
            {
                Measurement = "m",
                Values = new[]
                {
                    new TrakHoundTimeSeriesLatestValue { Field = "speed", Timestamp = timestamp, Value = 8000.0, Quality = TrakHoundTimeSeriesQuality.Uncertain },
                    new TrakHoundTimeSeriesLatestValue { Field = "load", Timestamp = timestamp, Quality = TrakHoundTimeSeriesQuality.Bad }
                }
            });

            Assert.Equal(8000.0, Assert.IsType<double>(result.Values[0].Value));
            Assert.Equal(TrakHoundTimeSeriesQuality.Uncertain, result.Values[0].Quality);
            Assert.Equal(timestamp, result.Values[0].Timestamp);
            Assert.Null(result.Values[1].Value);
            Assert.Equal(TrakHoundTimeSeriesQuality.Bad, result.Values[1].Quality);
        }

        [Fact]
        public void Result_Rows_Keep_Their_Types_And_Timestamps()
        {
            var timestamp = new DateTimeOffset(2026, 1, 1, 8, 30, 15, 123, TimeSpan.Zero);

            var result = RoundTrip(new TrakHoundTimeSeriesResult
            {
                Measurement = "spindle",
                Tags = new() { ["machine"] = "M1" },
                Columns = new[] { "timestamp", "quality", "speed", "parts", "state", "missing" },
                Rows = new[] { new object[] { timestamp, "Good", 8000.0, 5L, "2026-01-01T00:00:00Z", null } }
            });

            var row = Assert.Single(result.Rows);
            Assert.Equal(timestamp, Assert.IsType<DateTimeOffset>(row[0]));
            Assert.Equal("Good", row[1]);
            Assert.Equal(8000.0, Assert.IsType<double>(row[2]));
            Assert.Equal(5L, Assert.IsType<long>(row[3]));

            // Only the first Column is a Timestamp. A Field that looks like one stays a string
            Assert.IsType<string>(row[4]);
            Assert.Null(row[5]);
        }

        [Fact]
        public void Aggregate_Query_Keeps_Its_Settings()
        {
            var query = RoundTrip(new TrakHoundTimeSeriesAggregateQuery
            {
                Measurement = "spindle",
                Fields = new[] { "state" },
                Start = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                Stop = new DateTimeOffset(2026, 1, 2, 0, 0, 0, TimeSpan.Zero),
                Interval = TimeSpan.FromMinutes(5),
                Functions = new[] { TrakHoundTimeSeriesAggregate.DurationInState, TrakHoundTimeSeriesAggregate.Count },
                Fill = TrakHoundTimeSeriesFill.Previous,
                State = 1.0,
                IncludeBadQuality = true
            });

            Assert.Equal(TimeSpan.FromMinutes(5), query.Interval);
            Assert.Equal(new[] { TrakHoundTimeSeriesAggregate.DurationInState, TrakHoundTimeSeriesAggregate.Count }, query.Functions);
            Assert.Equal(TrakHoundTimeSeriesFill.Previous, query.Fill);
            Assert.Equal(1.0, Assert.IsType<double>(query.State));
            Assert.True(query.IncludeBadQuality);
        }

        [Fact]
        public void Field_Types_Are_Written_As_Names()
        {
            var json = Json.Convert(new TrakHoundTimeSeriesField { Name = "speed", Type = TrakHoundTimeSeriesFieldType.Double });

            Assert.Contains("\"Double\"", json);
            Assert.Equal(TrakHoundTimeSeriesFieldType.Double, RoundTrip(new TrakHoundTimeSeriesField { Type = TrakHoundTimeSeriesFieldType.Double }).Type);
        }

        [Fact]
        public void A_Field_That_Is_An_Object_Is_Rejected()
        {
            var point = JsonSerializer.Deserialize<TrakHoundTimeSeriesPoint>("{\"measurement\":\"m\",\"timestamp\":\"2026-01-01T00:00:00Z\",\"fields\":{\"a\":{\"b\":1}}}", Json.SharedOptions);

            Assert.NotNull(TrakHoundTimeSeriesValidation.ValidatePoint(point));
        }

        [Fact]
        public void A_Field_With_An_Invalid_Value_Does_Not_Break_The_Json()
        {
            var json = Json.Convert(new TrakHoundTimeSeriesPoint { Measurement = "m", Fields = new() { ["a"] = double.NaN } });

            Assert.NotNull(JsonDocument.Parse(json));
        }
    }
}
