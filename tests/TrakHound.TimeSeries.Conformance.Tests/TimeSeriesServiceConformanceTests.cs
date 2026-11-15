// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services.TimeSeries;

namespace TrakHound.TimeSeries.Conformance.Tests
{
    /// <summary>
    /// What every Time Series Service must do. A Service returns 405 for an operation it doesn't support, and has to pass the rest of these
    /// </summary>
    public abstract class TimeSeriesServiceConformanceTests
    {
        public const string Measurement = "conformance_spindle";

        // A fixed time in the past, so the current time doesn't limit how long a Value holds
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        protected abstract ITrakHoundTimeSeriesService Service { get; }

        /// <summary>
        /// False skips the Delete tests, for a database that can't delete Points. The Service has to return 405 instead
        /// </summary>
        protected virtual bool SupportsDelete => true;

        /// <summary>
        /// Removes what earlier tests wrote
        /// </summary>
        protected abstract Task Reset();


        private static DateTimeOffset At(int minutes) => T0.AddMinutes(minutes);

        private static Dictionary<string, string> Tags(string machine = "M1") => new() { ["machine"] = machine };

        private static Dictionary<string, string[]> Filter(params string[] machines) => new() { ["machine"] = machines.Length > 0 ? machines : new[] { "M1" } };

        private static TrakHoundTimeSeriesPoint Point(int minutes, string field, object value, string machine = "M1", TrakHoundTimeSeriesQuality quality = TrakHoundTimeSeriesQuality.Good) => new()
        {
            Measurement = Measurement,
            Tags = Tags(machine),
            Timestamp = At(minutes),
            Fields = new() { [field] = value },
            Quality = quality
        };

        private async Task Write(params TrakHoundTimeSeriesPoint[] points)
        {
            await Reset();
            var response = await Service.Write(points);
            Assert.True(response.Success, response.Message);
        }

        private async Task<TrakHoundTimeSeriesResult[]> Read(TrakHoundTimeSeriesQuery query)
        {
            var response = await Service.Read(query);
            Assert.True(response.Success, response.Message);
            return response.Result;
        }

        private async Task<TrakHoundTimeSeriesResult> Aggregate(int minutes, TrakHoundTimeSeriesFill fill, object state, bool includeBad, string field, params TrakHoundTimeSeriesAggregate[] functions)
        {
            var response = await Service.Aggregate(new TrakHoundTimeSeriesAggregateQuery
            {
                Measurement = Measurement,
                Tags = Filter(),
                Fields = new[] { field },
                Start = At(0),
                Stop = At(minutes),
                Interval = TimeSpan.FromMinutes(10),
                Functions = functions,
                Fill = fill,
                State = state,
                IncludeBadQuality = includeBad
            });

            Assert.True(response.Success, response.Message);
            return Assert.Single(response.Result);
        }

        private static TrakHoundTimeSeriesQuery Range(int start, int stop, params string[] fields) => new()
        {
            Measurement = Measurement,
            Tags = Filter(),
            Fields = fields.Length > 0 ? fields : null,
            Start = At(start),
            Stop = At(stop)
        };

        private static object Value(TrakHoundTimeSeriesResult result, int row, string column) => result.Rows[row][Array.IndexOf(result.Columns, column)];

        private static double Number(TrakHoundTimeSeriesResult result, int row, string column) => Convert.ToDouble(Value(result, row, column));

        private static string Column(string field, TrakHoundTimeSeriesAggregate function) => TrakHoundTimeSeriesAggregateQuery.GetColumn(field, function);

        private static object Quality(TrakHoundTimeSeriesResult result, int row, string field) => Value(result, row, TrakHoundTimeSeriesResult.GetQualityColumn(field));


        [SkippableFact]
        public async Task Writes_And_Reads_Back_Each_Type()
        {
            await Write(new TrakHoundTimeSeriesPoint
            {
                Measurement = Measurement,
                Tags = Tags(),
                Timestamp = At(0),
                Fields = new() { ["speed"] = 8000.5, ["parts"] = 12L, ["active"] = true, ["state"] = "RUN" },
                Quality = TrakHoundTimeSeriesQuality.Uncertain
            });

            var result = Assert.Single(await Read(Range(0, 1)));

            Assert.Equal(Measurement, result.Measurement);
            Assert.Equal("M1", result.Tags["machine"]);
            Assert.Equal(TrakHoundTimeSeriesResult.TimestampColumn, result.Columns[0]);
            Assert.Equal(At(0), Assert.IsType<DateTimeOffset>(Value(result, 0, TrakHoundTimeSeriesResult.TimestampColumn)));
            Assert.Equal("Uncertain", Quality(result, 0, "speed"));
            Assert.Equal("Uncertain", Quality(result, 0, "state"));
            Assert.Equal(8000.5, Value(result, 0, "speed"));
            Assert.Equal(12L, Value(result, 0, "parts"));
            Assert.Equal(true, Value(result, 0, "active"));
            Assert.Equal("RUN", Value(result, 0, "state"));
        }

        [SkippableFact]
        public async Task Timestamps_Keep_Milliseconds_And_Return_As_Utc()
        {
            var timestamp = new DateTimeOffset(2026, 1, 1, 8, 30, 15, 123, TimeSpan.FromHours(5));
            await Write(new TrakHoundTimeSeriesPoint { Measurement = Measurement, Tags = Tags(), Timestamp = timestamp, Fields = new() { ["speed"] = 1.0 } });

            var result = Assert.Single(await Read(new TrakHoundTimeSeriesQuery { Measurement = Measurement, Start = timestamp, Stop = timestamp.AddSeconds(1) }));
            var read = (DateTimeOffset)result.Rows[0][0];

            Assert.Equal(timestamp, read);
            Assert.Equal(TimeSpan.Zero, read.Offset);
        }

        [SkippableFact]
        public async Task Rewriting_A_Point_Replaces_Only_The_Fields_It_Supplies_And_Their_Quality()
        {
            await Write(new TrakHoundTimeSeriesPoint { Measurement = Measurement, Tags = Tags(), Timestamp = At(0), Fields = new() { ["speed"] = 1.0, ["load"] = 2.0 } });

            Assert.True((await Service.Write(new[] { Point(0, "speed", 5.0, quality: TrakHoundTimeSeriesQuality.Bad) })).Success);

            var result = Assert.Single(await Read(Range(0, 1)));
            Assert.Single(result.Rows);
            Assert.Equal(5.0, Value(result, 0, "speed"));
            Assert.Equal(2.0, Value(result, 0, "load"));
            Assert.Equal("Bad", Quality(result, 0, "speed"));
            Assert.Equal("Good", Quality(result, 0, "load"));
        }

        [SkippableFact]
        public async Task Retrying_A_Batch_Does_Not_Duplicate_Points()
        {
            var points = new[] { Point(0, "speed", 1.0), Point(1, "speed", 2.0) };
            await Write(points);

            Assert.True((await Service.Write(points)).Success);

            Assert.Equal(2, Assert.Single(await Read(Range(0, 10))).Rows.Length);
        }

        [SkippableFact]
        public async Task Range_Includes_Start_And_Excludes_Stop()
        {
            await Write(Point(0, "speed", 1.0), Point(1, "speed", 2.0), Point(2, "speed", 3.0));

            var result = Assert.Single(await Read(Range(1, 2)));

            Assert.Single(result.Rows);
            Assert.Equal(2.0, Value(result, 0, "speed"));
        }

        [SkippableFact]
        public async Task Tags_Select_The_Series_And_Each_Series_Has_A_Result()
        {
            await Write(Point(0, "speed", 1.0, "M1"), Point(0, "speed", 2.0, "M2"));

            var one = Assert.Single(await Read(Range(0, 1)));
            Assert.Equal(1.0, Value(one, 0, "speed"));

            var all = await Read(new TrakHoundTimeSeriesQuery { Measurement = Measurement, Start = At(0), Stop = At(1) });
            Assert.Equal(new[] { "M1", "M2" }, all.Select(o => o.Tags["machine"]).OrderBy(o => o));

            var series = await Service.ListSeries(Measurement);
            Assert.Equal(new[] { "M1", "M2" }, series.Result.Select(o => o.Tags["machine"]).OrderBy(o => o));

            var filtered = await Service.ListSeries(Measurement, Tags("M2"));
            Assert.Equal("M2", Assert.Single(filtered.Result).Tags["machine"]);

            Assert.Contains(Measurement, (await Service.ListMeasurements()).Result);
        }

        [SkippableFact]
        public async Task Take_Applies_To_Each_Series_And_Descending_Returns_The_Newest()
        {
            await Write(Point(0, "speed", 1.0, "M1"), Point(1, "speed", 2.0, "M1"), Point(0, "speed", 3.0, "M2"), Point(1, "speed", 4.0, "M2"));

            var results = await Read(new TrakHoundTimeSeriesQuery { Measurement = Measurement, Start = At(0), Stop = At(5), Take = 1, Descending = true });

            Assert.Equal(2, results.Length);
            Assert.All(results, o => Assert.Equal(At(1), (DateTimeOffset)Assert.Single(o.Rows)[0]));
        }

        [SkippableFact]
        public async Task Include_Previous_Adds_The_Last_Point_Before_Start_Without_Counting_Toward_Take()
        {
            await Write(Point(0, "state", "IDLE"), Point(5, "state", "RUN"), Point(20, "state", "STOP"), Point(21, "state", "RUN"));

            var query = Range(10, 30, "state");
            query.IncludePrevious = true;
            query.Take = 1;
            var result = Assert.Single(await Read(query));

            Assert.Equal(2, result.Rows.Length);
            Assert.Equal(At(5), (DateTimeOffset)result.Rows[0][0]);
            Assert.Equal(At(20), (DateTimeOffset)result.Rows[1][0]);

            // A Series with nothing in the range still has its starting Value
            query = Range(30, 40, "state");
            query.IncludePrevious = true;
            result = Assert.Single(await Read(query));
            Assert.Equal("RUN", Assert.Single(result.Rows)[1]);
        }

        [SkippableFact]
        public async Task Read_Latest_Returns_The_Newest_Value_Of_Each_Field()
        {
            await Write(Point(0, "speed", 1.0), Point(1, "speed", 2.0, quality: TrakHoundTimeSeriesQuality.Uncertain), Point(2, "load", 3.0));

            var response = await Service.ReadLatest(Measurement, Filter(), null);
            var result = Assert.Single(response.Result);

            Assert.Equal(new[] { "load", "speed" }, result.Values.Select(o => o.Field));
            Assert.Equal(At(2), result.Values[0].Timestamp);
            Assert.Equal(3.0, result.Values[0].Value);
            Assert.Equal(At(1), result.Values[1].Timestamp);
            Assert.Equal(2.0, result.Values[1].Value);
            Assert.Equal(TrakHoundTimeSeriesQuality.Uncertain, result.Values[1].Quality);

            var one = Assert.Single((await Service.ReadLatest(Measurement, Filter(), new[] { "speed" })).Result);
            Assert.Equal("speed", Assert.Single(one.Values).Field);
        }

        [SkippableFact]
        public async Task Read_Latest_Returns_A_Field_That_Has_No_Value()
        {
            await Write(Point(0, "speed", 1.0), Point(1, "speed", null, quality: TrakHoundTimeSeriesQuality.Bad));

            var value = Assert.Single(Assert.Single((await Service.ReadLatest(Measurement, Filter(), null)).Result).Values);

            Assert.Equal(At(1), value.Timestamp);
            Assert.Null(value.Value);
            Assert.Equal(TrakHoundTimeSeriesQuality.Bad, value.Quality);
        }

        [SkippableFact]
        public async Task A_Query_Can_Select_Several_Values_Of_A_Tag()
        {
            await Write(Point(0, "speed", 1.0, "M1"), Point(0, "speed", 2.0, "M2"), Point(0, "speed", 3.0, "M3"));

            var query = Range(0, 1);
            query.Tags = Filter("M1", "M3");
            Assert.Equal(new[] { "M1", "M3" }, (await Read(query)).Select(o => o.Tags["machine"]).OrderBy(o => o));

            var latest = await Service.ReadLatest(Measurement, Filter("M2", "M3"), new[] { "speed" });
            Assert.Equal(new[] { "M2", "M3" }, latest.Result.Select(o => o.Tags["machine"]).OrderBy(o => o));

            var aggregate = await Service.Aggregate(new TrakHoundTimeSeriesAggregateQuery
            {
                Measurement = Measurement,
                Tags = Filter("M1", "M2"),
                Fields = new[] { "speed" },
                Start = At(0),
                Stop = At(1),
                Interval = TimeSpan.FromMinutes(1),
                Functions = new[] { TrakHoundTimeSeriesAggregate.Sum }
            });
            Assert.Equal(2, aggregate.Result.Length);

            query.Tags = new() { ["machine"] = Array.Empty<string>() };
            Assert.Equal(400, (await Service.Read(query)).StatusCode);
        }

        [SkippableFact]
        public async Task Each_Field_Has_Its_Own_Quality()
        {
            await Write(new TrakHoundTimeSeriesPoint
            {
                Measurement = Measurement,
                Tags = Tags(),
                Timestamp = At(0),
                Fields = new() { ["speed"] = 1.0, ["load"] = 2.0 },
                Quality = TrakHoundTimeSeriesQuality.Good,
                Qualities = new() { ["load"] = TrakHoundTimeSeriesQuality.Bad }
            });

            // Another Point at the same time only changes its own Field
            Assert.True((await Service.Write(new[] { Point(0, "temp", 3.0, quality: TrakHoundTimeSeriesQuality.Uncertain) })).Success);

            var result = Assert.Single(await Read(Range(0, 1)));

            Assert.Equal("Good", Quality(result, 0, "speed"));
            Assert.Equal("Bad", Quality(result, 0, "load"));
            Assert.Equal("Uncertain", Quality(result, 0, "temp"));

            // Only load is Bad, so only load is skipped
            var sum = await Service.Aggregate(new TrakHoundTimeSeriesAggregateQuery
            {
                Measurement = Measurement,
                Fields = new[] { "speed", "load" },
                Start = At(0),
                Stop = At(1),
                Interval = TimeSpan.FromMinutes(1),
                Functions = new[] { TrakHoundTimeSeriesAggregate.Count }
            });
            var row = Assert.Single(sum.Result);
            Assert.Equal(1L, Convert.ToInt64(Value(row, 0, Column("speed", TrakHoundTimeSeriesAggregate.Count))));
            Assert.Equal(0L, Convert.ToInt64(Value(row, 0, Column("load", TrakHoundTimeSeriesAggregate.Count))));
        }

        [SkippableFact]
        public async Task A_Bad_Field_Can_Be_Recorded_Without_A_Value()
        {
            await Write(Point(0, "speed", 1.0), Point(1, "speed", null, quality: TrakHoundTimeSeriesQuality.Bad), Point(2, "speed", 3.0));

            var result = Assert.Single(await Read(Range(0, 5)));

            Assert.Equal(3, result.Rows.Length);
            Assert.Null(Value(result, 1, "speed"));
            Assert.Equal("Bad", Quality(result, 1, "speed"));

            // A Row for another Field's Point has neither
            Assert.True((await Service.Write(new[] { Point(3, "load", 5.0) })).Success);
            var sparse = Assert.Single(await Read(Range(0, 5)));
            Assert.Null(Value(sparse, 3, "speed"));
            Assert.Null(Quality(sparse, 3, "speed"));
        }

        [SkippableFact]
        public async Task A_Field_Without_A_Value_Must_Be_Bad()
        {
            await Reset();

            Assert.Equal(400, (await Service.Write(new[] { Point(0, "speed", null) })).StatusCode);
            Assert.Equal(400, (await Service.Write(new[] { Point(0, "speed", null, quality: TrakHoundTimeSeriesQuality.Uncertain) })).StatusCode);

            var extra = Point(0, "speed", 1.0);
            extra.Qualities = new() { ["load"] = TrakHoundTimeSeriesQuality.Bad };
            Assert.Equal(400, (await Service.Write(new[] { extra })).StatusCode);
        }

        [SkippableFact]
        public async Task Writing_No_Value_Clears_A_Value_At_The_Same_Time()
        {
            await Write(Point(0, "speed", 1.0));

            Assert.True((await Service.Write(new[] { Point(0, "speed", null, quality: TrakHoundTimeSeriesQuality.Bad) })).Success);

            var result = Assert.Single(await Read(Range(0, 1, "speed")));
            Assert.Null(Value(result, 0, "speed"));
            Assert.Equal("Bad", Quality(result, 0, "speed"));

            // The Value can be written again
            Assert.True((await Service.Write(new[] { Point(0, "speed", 2.0) })).Success);
            result = Assert.Single(await Read(Range(0, 1, "speed")));
            Assert.Equal(2.0, Value(result, 0, "speed"));
            Assert.Equal("Good", Quality(result, 0, "speed"));
        }

        [SkippableFact]
        public async Task A_New_Field_Without_A_Value_Takes_Its_Type_From_Its_First_Value()
        {
            await Write(Point(0, "speed", null, quality: TrakHoundTimeSeriesQuality.Bad));

            var field = Assert.Single((await Service.ListFields(Measurement)).Result);
            Assert.Equal("speed", field.Name);
            Assert.Null(field.Type);

            var read = Assert.Single(await Read(Range(0, 1)));
            Assert.Null(Value(read, 0, "speed"));
            Assert.Equal("Bad", Quality(read, 0, "speed"));

            var aggregate = await Service.Aggregate(new TrakHoundTimeSeriesAggregateQuery { Measurement = Measurement, Fields = new[] { "speed" }, Start = At(0), Stop = At(1), Interval = TimeSpan.FromMinutes(1), Functions = new[] { TrakHoundTimeSeriesAggregate.Mean } });
            Assert.True(aggregate.Success, aggregate.Message);

            Assert.True((await Service.Write(new[] { Point(1, "speed", 2.5) })).Success);
            Assert.Equal(TrakHoundTimeSeriesFieldType.Double, Assert.Single((await Service.ListFields(Measurement)).Result).Type);
            Assert.Equal(400, (await Service.Write(new[] { Point(2, "speed", "fast") })).StatusCode);
        }

        [SkippableTheory]
        [InlineData(false)]
        [InlineData(true)]
        public async Task A_Field_Without_A_Value_Is_Never_Calculated_And_Ends_The_Value_Before_It(bool includeBad)
        {
            // 10 for minutes 0-2, nothing known for 2-6, 40 for 6-10: (10 * 2 + 40 * 4) / 6 = 30
            await Write(Point(0, "speed", 10.0), Point(2, "speed", null, quality: TrakHoundTimeSeriesQuality.Bad), Point(6, "speed", 40.0));

            var result = await Aggregate(10, TrakHoundTimeSeriesFill.None, null, includeBad, "speed", TrakHoundTimeSeriesAggregate.TimeWeightedMean, TrakHoundTimeSeriesAggregate.Count, TrakHoundTimeSeriesAggregate.Mean);

            Assert.Equal(30.0, Number(result, 0, Column("speed", TrakHoundTimeSeriesAggregate.TimeWeightedMean)), 6);
            Assert.Equal(2.0, Number(result, 0, Column("speed", TrakHoundTimeSeriesAggregate.Count)));
            Assert.Equal(25.0, Number(result, 0, Column("speed", TrakHoundTimeSeriesAggregate.Mean)));
        }

        [SkippableFact]
        public async Task Has_More_Says_Whether_Take_Left_Out_Points_Of_A_Series()
        {
            await Write(Point(0, "speed", 1.0, "M1"), Point(1, "speed", 2.0, "M1"), Point(0, "speed", 3.0, "M2"), Point(5, "speed", 4.0, "M3"));

            var query = new TrakHoundTimeSeriesQuery { Measurement = Measurement, Start = At(0), Stop = At(5), Take = 1 };
            var results = (await Read(query)).OrderBy(o => o.Tags["machine"]).ToArray();

            Assert.Equal(2, results.Length);
            Assert.True(results[0].HasMore);
            Assert.Equal(At(0), (DateTimeOffset)Assert.Single(results[0].Rows)[0]);
            Assert.False(results[1].HasMore);

            query.Descending = true;
            var newest = (await Read(query)).OrderBy(o => o.Tags["machine"]).First();
            Assert.True(newest.HasMore);
            Assert.Equal(At(1), (DateTimeOffset)Assert.Single(newest.Rows)[0]);

            // The Point before Start doesn't count
            query = new TrakHoundTimeSeriesQuery { Measurement = Measurement, Tags = Filter("M1"), Start = At(1), Stop = At(5), Take = 1, IncludePrevious = true };
            var previous = Assert.Single(await Read(query));
            Assert.False(previous.HasMore);
            Assert.Equal(2, previous.Rows.Length);

            foreach (var descending in new[] { false, true })
            {
                query = new TrakHoundTimeSeriesQuery { Measurement = Measurement, Tags = Filter("M1"), Start = At(0), Stop = At(5), Take = 1, Descending = descending };
                Assert.True(Assert.Single(await Read(query)).HasMore);
            }
        }

        [SkippableFact]
        public async Task A_Field_Name_Can_Have_Up_To_60_Characters()
        {
            await Reset();

            var name = new string('f', 60);
            Assert.True((await Service.Write(new[] { Point(0, name, 1.0) })).Success);
            Assert.Equal(1.0, Value(Assert.Single(await Read(Range(0, 1, name))), 0, name));

            Assert.Equal(400, (await Service.Write(new[] { Point(0, name + "f", 1.0) })).StatusCode);
        }

        [SkippableFact]
        public async Task Nothing_Found_Is_An_Empty_Result()
        {
            await Reset();

            var read = await Service.Read(new TrakHoundTimeSeriesQuery { Measurement = "conformance_missing", Start = At(0), Stop = At(1) });
            Assert.True(read.Success);
            Assert.Empty(read.Result);

            var series = await Service.ListSeries("conformance_missing");
            Assert.True(series.Success);
            Assert.Empty(series.Result);
        }

        [SkippableFact]
        public async Task Standard_Aggregates_Group_By_Interval()
        {
            await Write(Point(1, "speed", 2.0), Point(2, "speed", 4.0), Point(3, "speed", 9.0), Point(12, "speed", 10.0));

            var result = await Aggregate(20, TrakHoundTimeSeriesFill.None, null, false, "speed",
                TrakHoundTimeSeriesAggregate.Mean,
                TrakHoundTimeSeriesAggregate.Min,
                TrakHoundTimeSeriesAggregate.Max,
                TrakHoundTimeSeriesAggregate.Sum,
                TrakHoundTimeSeriesAggregate.Count,
                TrakHoundTimeSeriesAggregate.First,
                TrakHoundTimeSeriesAggregate.Last);

            Assert.Equal(2, result.Rows.Length);
            Assert.Equal(At(0), (DateTimeOffset)result.Rows[0][0]);
            Assert.Equal(At(10), (DateTimeOffset)result.Rows[1][0]);
            Assert.Equal(5.0, Number(result, 0, Column("speed", TrakHoundTimeSeriesAggregate.Mean)));
            Assert.Equal(2.0, Number(result, 0, Column("speed", TrakHoundTimeSeriesAggregate.Min)));
            Assert.Equal(9.0, Number(result, 0, Column("speed", TrakHoundTimeSeriesAggregate.Max)));
            Assert.Equal(15.0, Number(result, 0, Column("speed", TrakHoundTimeSeriesAggregate.Sum)));
            Assert.Equal(3.0, Number(result, 0, Column("speed", TrakHoundTimeSeriesAggregate.Count)));
            Assert.Equal(2.0, Number(result, 0, Column("speed", TrakHoundTimeSeriesAggregate.First)));
            Assert.Equal(9.0, Number(result, 0, Column("speed", TrakHoundTimeSeriesAggregate.Last)));
            Assert.Equal(10.0, Number(result, 1, Column("speed", TrakHoundTimeSeriesAggregate.Mean)));
        }

        [SkippableFact]
        public async Task Intervals_Are_Aligned_To_The_Unix_Epoch()
        {
            await Write(Point(4, "speed", 1.0));

            // A range that starts in the middle of an Interval returns that Interval's start
            var response = await Service.Aggregate(new TrakHoundTimeSeriesAggregateQuery
            {
                Measurement = Measurement,
                Fields = new[] { "speed" },
                Start = At(3),
                Stop = At(5),
                Interval = TimeSpan.FromMinutes(10),
                Functions = new[] { TrakHoundTimeSeriesAggregate.Count }
            });

            var result = Assert.Single(response.Result);
            Assert.Equal(At(0), (DateTimeOffset)Assert.Single(result.Rows)[0]);
        }

        [SkippableFact]
        public async Task Fill_Decides_What_Empty_Intervals_Return()
        {
            await Write(Point(1, "speed", 2.0), Point(21, "speed", 4.0));

            var none = await Aggregate(30, TrakHoundTimeSeriesFill.None, null, false, "speed", TrakHoundTimeSeriesAggregate.Mean, TrakHoundTimeSeriesAggregate.Count);
            Assert.Equal(2, none.Rows.Length);

            var nulls = await Aggregate(30, TrakHoundTimeSeriesFill.Null, null, false, "speed", TrakHoundTimeSeriesAggregate.Mean, TrakHoundTimeSeriesAggregate.Count);
            Assert.Equal(3, nulls.Rows.Length);
            Assert.Null(Value(nulls, 1, Column("speed", TrakHoundTimeSeriesAggregate.Mean)));
            Assert.Equal(0.0, Number(nulls, 1, Column("speed", TrakHoundTimeSeriesAggregate.Count)));

            var previous = await Aggregate(30, TrakHoundTimeSeriesFill.Previous, null, false, "speed", TrakHoundTimeSeriesAggregate.Mean, TrakHoundTimeSeriesAggregate.Count);
            Assert.Equal(3, previous.Rows.Length);
            Assert.Equal(2.0, Number(previous, 1, Column("speed", TrakHoundTimeSeriesAggregate.Mean)));
            Assert.Equal(0.0, Number(previous, 1, Column("speed", TrakHoundTimeSeriesAggregate.Count)));
        }

        [SkippableFact]
        public async Task Time_Weighted_Mean_Holds_Each_Value_Until_The_Next_Point()
        {
            // Holds 10 from before the range until minute 2, then 40 until minute 10: (10 * 2 + 40 * 8) / 10 = 34
            // The next Interval holds 40 for all 10 minutes
            await Write(Point(-5, "speed", 10.0), Point(2, "speed", 40.0));

            var result = await Aggregate(20, TrakHoundTimeSeriesFill.None, null, false, "speed", TrakHoundTimeSeriesAggregate.TimeWeightedMean);
            var column = Column("speed", TrakHoundTimeSeriesAggregate.TimeWeightedMean);

            Assert.Equal(2, result.Rows.Length);
            Assert.Equal(34.0, Number(result, 0, column), 6);
            Assert.Equal(40.0, Number(result, 1, column), 6);
        }

        [SkippableFact]
        public async Task Duration_In_State_Counts_The_Seconds_A_Value_Held()
        {
            // RUN from before the range until minute 3, STOP until minute 7, RUN until minute 10 and on through the next Interval
            await Write(Point(-5, "state", "RUN"), Point(3, "state", "STOP"), Point(7, "state", "RUN"));

            var result = await Aggregate(20, TrakHoundTimeSeriesFill.None, "RUN", false, "state", TrakHoundTimeSeriesAggregate.DurationInState);
            var column = Column("state", TrakHoundTimeSeriesAggregate.DurationInState);

            Assert.Equal(6 * 60.0, Number(result, 0, column), 6);
            Assert.Equal(10 * 60.0, Number(result, 1, column), 6);
        }

        [SkippableFact]
        public async Task Bad_Points_Are_Skipped_Unless_Included()
        {
            await Write(Point(1, "speed", 2.0), Point(2, "speed", 100.0, quality: TrakHoundTimeSeriesQuality.Bad), Point(3, "speed", 4.0));

            var column = Column("speed", TrakHoundTimeSeriesAggregate.Mean);

            var skipped = await Aggregate(10, TrakHoundTimeSeriesFill.None, null, false, "speed", TrakHoundTimeSeriesAggregate.Mean);
            Assert.Equal(3.0, Number(skipped, 0, column));

            var included = await Aggregate(10, TrakHoundTimeSeriesFill.None, null, true, "speed", TrakHoundTimeSeriesAggregate.Mean);
            Assert.Equal(106.0 / 3, Number(included, 0, column), 6);
        }

        [SkippableFact]
        public async Task Time_After_A_Bad_Point_Is_Not_Counted()
        {
            // 10 for minutes 0-2, unknown for 2-6, 40 for 6-10: (10 * 2 + 40 * 4) / 6 = 30
            await Write(Point(0, "speed", 10.0), Point(2, "speed", 0.0, quality: TrakHoundTimeSeriesQuality.Bad), Point(6, "speed", 40.0));

            var result = await Aggregate(10, TrakHoundTimeSeriesFill.None, null, false, "speed", TrakHoundTimeSeriesAggregate.TimeWeightedMean);

            Assert.Equal(30.0, Number(result, 0, Column("speed", TrakHoundTimeSeriesAggregate.TimeWeightedMean)), 6);
        }

        [SkippableFact]
        public async Task A_Field_Keeps_Its_Type()
        {
            await Write(Point(0, "speed", 1.0));

            var response = await Service.Write(new[] { Point(1, "speed", "fast") });

            Assert.Equal(400, response.StatusCode);
            Assert.Contains("is a Double", response.Message);
        }

        [SkippableFact]
        public async Task Numeric_Functions_Do_Not_Run_On_Strings()
        {
            await Write(Point(0, "state", "RUN"));

            var response = await Service.Aggregate(new TrakHoundTimeSeriesAggregateQuery
            {
                Measurement = Measurement,
                Fields = new[] { "state" },
                Start = At(0),
                Stop = At(10),
                Interval = TimeSpan.FromMinutes(10),
                Functions = new[] { TrakHoundTimeSeriesAggregate.Mean }
            });

            Assert.Equal(400, response.StatusCode);
        }

        [SkippableTheory]
        [InlineData("1starts_with_a_number")]
        [InlineData("has space")]
        [InlineData("has\"quote")]
        [InlineData("semi;colon")]
        [InlineData("")]
        public async Task Invalid_Names_Are_Rejected(string name)
        {
            await Reset();

            var point = Point(0, "speed", 1.0);
            point.Measurement = name;
            Assert.Equal(400, (await Service.Write(new[] { point })).StatusCode);

            Assert.Equal(400, (await Service.Write(new[] { Point(0, name, 1.0) })).StatusCode);
            Assert.Equal(400, (await Service.Read(new TrakHoundTimeSeriesQuery { Measurement = name, Start = At(0), Stop = At(1) })).StatusCode);
            Assert.Equal(400, (await Service.ListSeries(name)).StatusCode);
        }

        [SkippableTheory]
        [InlineData("time")]
        [InlineData("timestamp")]
        [InlineData("quality")]
        public async Task Reserved_Field_Names_Are_Rejected(string name)
        {
            await Reset();

            Assert.Equal(400, (await Service.Write(new[] { Point(0, name, 1.0) })).StatusCode);
        }

        [SkippableFact]
        public async Task Invalid_Values_And_Queries_Are_Rejected()
        {
            await Reset();

            Assert.Equal(400, (await Service.Write(new[] { Point(0, "speed", double.NaN) })).StatusCode);
            Assert.Equal(400, (await Service.Write(new[] { Point(0, "speed", DateTime.UtcNow) })).StatusCode);
            Assert.Equal(400, (await Service.Read(Range(5, 5))).StatusCode);

            var query = Range(0, 5);
            query.Take = 0;
            Assert.Equal(400, (await Service.Read(query)).StatusCode);

            var noInterval = new TrakHoundTimeSeriesAggregateQuery { Measurement = Measurement, Fields = new[] { "speed" }, Start = At(0), Stop = At(5), Functions = new[] { TrakHoundTimeSeriesAggregate.Mean } };
            Assert.Equal(400, (await Service.Aggregate(noInterval)).StatusCode);

            var noState = new TrakHoundTimeSeriesAggregateQuery { Measurement = Measurement, Fields = new[] { "state" }, Start = At(0), Stop = At(5), Interval = TimeSpan.FromMinutes(1), Functions = new[] { TrakHoundTimeSeriesAggregate.DurationInState } };
            Assert.Equal(400, (await Service.Aggregate(noState)).StatusCode);
        }

        [SkippableFact]
        public async Task Delete_Removes_The_Range_Of_The_Selected_Series()
        {
            Skip.IfNot(SupportsDelete, "This Service can't delete Points");

            await Write(Point(0, "speed", 1.0, "M1"), Point(1, "speed", 2.0, "M1"), Point(2, "speed", 3.0, "M1"), Point(1, "speed", 4.0, "M2"));

            Assert.True((await Service.Delete(Measurement, Tags("M1"), At(1), At(2))).Success);

            var m1 = Assert.Single(await Read(Range(0, 5)));
            Assert.Equal(new[] { At(0), At(2) }, m1.Rows.Select(o => (DateTimeOffset)o[0]));

            var m2 = Assert.Single(await Read(new TrakHoundTimeSeriesQuery { Measurement = Measurement, Tags = Filter("M2"), Start = At(0), Stop = At(5) }));
            Assert.Single(m2.Rows);
        }

        [SkippableFact]
        public async Task Deleting_Every_Point_Removes_The_Series_And_Measurement_From_Lists()
        {
            Skip.IfNot(SupportsDelete, "This Service can't delete Points");

            await Write(Point(0, "speed", 1.0, "M1"), Point(0, "speed", 2.0, "M2"));

            Assert.True((await Service.Delete(Measurement, Tags("M1"), At(0), At(1))).Success);
            Assert.Equal("M2", Assert.Single((await Service.ListSeries(Measurement)).Result).Tags["machine"]);

            Assert.True((await Service.Delete(Measurement, null, At(0), At(1))).Success);
            Assert.Empty((await Service.ListSeries(Measurement)).Result);
            Assert.DoesNotContain(Measurement, (await Service.ListMeasurements()).Result);

            // The Series can be written again
            Assert.True((await Service.Write(new[] { Point(5, "speed", 3.0, "M1") })).Success);
            Assert.Equal(3.0, Value(Assert.Single(await Read(Range(0, 10))), 0, "speed"));
        }

        [SkippableFact]
        public async Task New_Fields_Can_Be_Added_To_A_Measurement()
        {
            await Write(Point(0, "speed", 1.0));

            Assert.True((await Service.Write(new[] { Point(1, "load", 5L) })).Success);

            var result = Assert.Single(await Read(Range(0, 5)));
            Assert.Equal(2, result.Rows.Length);
            Assert.Equal(1.0, Value(result, 0, "speed"));
            Assert.Null(Value(result, 0, "load"));
            Assert.Equal(5L, Value(result, 1, "load"));
        }

        [SkippableFact]
        public async Task Same_Point_Twice_In_A_Batch_Merges()
        {
            await Write(Point(0, "speed", 1.0), Point(0, "load", 2.0), Point(0, "speed", 3.0));

            var result = Assert.Single(await Read(Range(0, 1)));
            Assert.Single(result.Rows);
            Assert.Equal(3.0, Value(result, 0, "speed"));
            Assert.Equal(2.0, Value(result, 0, "load"));
        }

        [SkippableFact]
        public async Task Long_Fields_Stay_Long_In_Aggregates()
        {
            await Write(Point(1, "parts", 2L), Point(2, "parts", 3L));

            var result = await Aggregate(10, TrakHoundTimeSeriesFill.None, null, false, "parts", TrakHoundTimeSeriesAggregate.Sum, TrakHoundTimeSeriesAggregate.Max, TrakHoundTimeSeriesAggregate.Count);

            Assert.Equal(5L, Value(result, 0, Column("parts", TrakHoundTimeSeriesAggregate.Sum)));
            Assert.Equal(3L, Value(result, 0, Column("parts", TrakHoundTimeSeriesAggregate.Max)));
            Assert.Equal(2L, Value(result, 0, Column("parts", TrakHoundTimeSeriesAggregate.Count)));
        }

        [SkippableFact]
        public async Task Values_Do_Not_Hold_Past_The_Current_Time()
        {
            await Reset();

            var now = DateTimeOffset.UtcNow;
            Assert.True((await Service.Write(new[] { new TrakHoundTimeSeriesPoint { Measurement = Measurement, Tags = Tags(), Timestamp = now.AddMinutes(-1), Fields = new() { ["state"] = "RUN" } } })).Success);

            var response = await Service.Aggregate(new TrakHoundTimeSeriesAggregateQuery
            {
                Measurement = Measurement,
                Fields = new[] { "state" },
                Start = now.AddMinutes(-1),
                Stop = now.AddHours(1),
                Interval = TimeSpan.FromDays(1),
                Functions = new[] { TrakHoundTimeSeriesAggregate.DurationInState },
                State = "RUN"
            });

            var result = Assert.Single(response.Result);
            var seconds = result.Rows.Sum(o => Convert.ToDouble(o[1]));
            Assert.InRange(seconds, 59, 120);
        }

        [SkippableFact]
        public async Task A_Tag_That_Looks_Like_Sql_Stays_Data()
        {
            await Reset();

            var point = Point(0, "speed", 1.0);
            point.Tags = new() { ["machine"] = "M1'; drop table x; --" };
            Assert.True((await Service.Write(new[] { point })).Success);

            var result = Assert.Single(await Read(new TrakHoundTimeSeriesQuery { Measurement = Measurement, Tags = TrakHoundTimeSeriesQuery.GetTagFilter(point.Tags), Start = At(0), Stop = At(1) }));
            Assert.Equal(point.Tags["machine"], result.Tags["machine"]);
        }

        [SkippableFact]
        public async Task Special_Characters_In_Values_Are_Kept()
        {
            await Reset();

            var point = Point(0, "note", "a \"quoted\" value\\ with, commas = and\nlines");
            point.Tags = new() { ["machine"] = "line 1,cell=2" };
            Assert.True((await Service.Write(new[] { point })).Success);

            var result = Assert.Single(await Read(new TrakHoundTimeSeriesQuery { Measurement = Measurement, Tags = TrakHoundTimeSeriesQuery.GetTagFilter(point.Tags), Start = At(0), Stop = At(1) }));
            Assert.Equal(point.Tags["machine"], result.Tags["machine"]);
            Assert.Equal(point.Fields["note"], Value(result, 0, "note"));
        }

        [SkippableFact]
        public async Task Field_Names_That_Are_Sql_Keywords_Work()
        {
            await Write(Point(0, "select", 1.0), Point(0, "order", 2.0));

            var result = Assert.Single(await Read(Range(0, 1, "select", "order")));

            Assert.Equal(1.0, Value(result, 0, "select"));
            Assert.Equal(2.0, Value(result, 0, "order"));
        }

        [SkippableFact]
        public async Task Read_Asking_For_A_Field_That_Does_Not_Exist_Returns_Null_For_It()
        {
            await Write(Point(0, "speed", 1.0));

            var result = Assert.Single(await Read(Range(0, 1, "speed", "missing")));

            Assert.Equal(1.0, Value(result, 0, "speed"));
            Assert.Null(Value(result, 0, "missing"));
        }

        [SkippableFact]
        public async Task A_Whole_Number_Can_Be_Written_To_A_Double_Field()
        {
            await Write(Point(0, "speed", 1.5));

            var response = await Service.Write(new[] { Point(1, "speed", 8000L), Point(2, "speed", 7) });
            Assert.True(response.Success, response.Message);

            var result = Assert.Single(await Read(Range(0, 5, "speed")));
            Assert.Equal(8000.0, Assert.IsType<double>(Value(result, 1, "speed")));
            Assert.Equal(7.0, Assert.IsType<double>(Value(result, 2, "speed")));
        }

        [SkippableTheory]
        [InlineData(true)]
        [InlineData(false)]
        public async Task A_New_Field_Written_With_Both_Kinds_Of_Number_Is_A_Double(bool wholeFirst)
        {
            var points = wholeFirst
                ? new[] { Point(0, "speed", 8000L), Point(1, "speed", 0.5) }
                : new[] { Point(0, "speed", 0.5), Point(1, "speed", 8000L) };

            await Write(points);

            var result = Assert.Single(await Read(Range(0, 5, "speed")));
            Assert.All(result.Rows, row => Assert.IsType<double>(row[1]));
        }

        [SkippableFact]
        public async Task A_Double_Can_Not_Be_Written_To_A_Long_Field()
        {
            await Write(Point(0, "parts", 5L));

            Assert.Equal(400, (await Service.Write(new[] { Point(1, "parts", 0.5) })).StatusCode);
            Assert.Equal(400, (await Service.Write(new[] { Point(1, "parts", 5.0) })).StatusCode);
        }

        [SkippableFact]
        public async Task A_Whole_Number_Too_Large_For_A_Double_Is_Rejected_Instead_Of_Changed()
        {
            await Write(Point(0, "speed", 1.5));

            Assert.Equal(400, (await Service.Write(new[] { Point(1, "speed", 9007199254740993L) })).StatusCode);
            Assert.True((await Service.Write(new[] { Point(1, "speed", 9007199254740992L) })).Success);
        }

        [SkippableFact]
        public async Task A_Whole_Number_Can_Be_The_State_Of_A_Double_Field()
        {
            await Write(Point(0, "mode", 1.0), Point(4, "mode", 2.0));

            var result = await Aggregate(10, TrakHoundTimeSeriesFill.None, 1L, false, "mode", TrakHoundTimeSeriesAggregate.DurationInState);

            Assert.Equal(4 * 60.0, Number(result, 0, Column("mode", TrakHoundTimeSeriesAggregate.DurationInState)), 6);
        }

        [SkippableFact]
        public async Task List_Fields_Returns_Every_Field_And_Its_Type_However_Sparse_The_Points_Are()
        {
            await Write(new TrakHoundTimeSeriesPoint
            {
                Measurement = Measurement,
                Tags = Tags(),
                Timestamp = At(0),
                Fields = new() { ["speed"] = 1.5, ["parts"] = 5L, ["active"] = true, ["state"] = "RUN" }
            }, Point(1, "speed", 2.5));

            var fields = (await Service.ListFields(Measurement)).Result.ToList();

            Assert.Equal(new[] { "active", "parts", "speed", "state" }, fields.Select(o => o.Name));
            Assert.Equal(TrakHoundTimeSeriesFieldType.Bool, fields[0].Type);
            Assert.Equal(TrakHoundTimeSeriesFieldType.Long, fields[1].Type);
            Assert.Equal(TrakHoundTimeSeriesFieldType.Double, fields[2].Type);
            Assert.Equal(TrakHoundTimeSeriesFieldType.String, fields[3].Type);

            var missing = await Service.ListFields("conformance_missing");
            Assert.True(missing.Success);
            Assert.Empty(missing.Result);

            Assert.Equal(400, (await Service.ListFields("not valid")).StatusCode);
        }
    }
}
