// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Npgsql;
using System.Diagnostics;
using TrakHound.Postgres.Service;
using TrakHound.Services.TimeSeries;
using TrakHound.Timescale.Service;
using Xunit.Abstractions;

namespace TrakHound.TimeSeries.Conformance.Tests
{
    /// <summary>
    /// What only a real server can show: Hypertables, compressed Chunks and writers that race. Runs when TRAKHOUND_TIMESCALE_TEST_SERVER is set.
    /// The load test also needs TRAKHOUND_TIMESCALE_TEST_LOAD set to the number of Points to write
    /// </summary>
    [Collection("Timescale")]
    public class TimescaleServerTests : IAsyncLifetime
    {
        private const string _schema = "trakhound_server_ts";
        private const string _measurement = "server_spindle";

        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly ITestOutputHelper _output;
        private TimescaleTimeSeriesService _service;


        public TimescaleServerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public async Task InitializeAsync()
        {
            if (TimescaleTestServer.Configuration(_schema) == null) return;

            await Execute($"drop schema if exists \"{_schema}\" cascade;");
            _service = CreateService();
        }

        public Task DisposeAsync()
        {
            _service?.Dispose();
            return Task.CompletedTask;
        }


        private static TimescaleTimeSeriesService CreateService() => new TimescaleTimeSeriesService(TimescaleTestServer.Configuration(_schema));

        private void RequireServer() => Skip.If(_service == null, $"Set {TimescaleTestServer.Prefix}SERVER to run the TimescaleDB tests");

        private static async Task<object> Execute(string sql)
        {
            await using var connection = new NpgsqlConnection(PostgresConnectionString.Create(TimescaleTestServer.Configuration(_schema)));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection) { CommandTimeout = 600 };
            return await command.ExecuteScalarAsync();
        }

        private static Task CompressChunks() => Execute($"select count(compress_chunk(c, if_not_compressed => true)) from show_chunks('\"{_schema}\".\"{_measurement}\"') c;");

        private static Task<object> CompressedChunks() => Execute($"select count(*) from timescaledb_information.chunks where hypertable_schema = '{_schema}' and hypertable_name = '{_measurement}' and is_compressed;");

        private static TrakHoundTimeSeriesPoint Point(string machine, DateTimeOffset timestamp, string field, object value) => new()
        {
            Measurement = _measurement,
            Tags = new() { ["machine"] = machine },
            Timestamp = timestamp,
            Fields = new() { [field] = value }
        };

        private async Task Write(params TrakHoundTimeSeriesPoint[] points)
        {
            var response = await _service.Write(points);
            Assert.True(response.Success, response.Message);
        }

        private async Task<TrakHoundTimeSeriesResult[]> Read(DateTimeOffset start, DateTimeOffset stop, string machine = "M1")
        {
            var response = await _service.Read(new TrakHoundTimeSeriesQuery { Measurement = _measurement, Tags = new() { ["machine"] = new[] { machine } }, Start = start, Stop = stop });
            Assert.True(response.Success, response.Message);
            return response.Result;
        }


        [SkippableFact]
        public async Task A_Measurement_Is_A_Hypertable_With_A_Compression_Policy()
        {
            RequireServer();

            await Write(Point("M1", T0, "speed", 1.0));

            Assert.Equal(1L, await Execute($"select count(*) from timescaledb_information.hypertables where hypertable_schema = '{_schema}' and hypertable_name = '{_measurement}' and compression_enabled;"));
            Assert.Equal(1L, await Execute($"select count(*) from timescaledb_information.jobs where hypertable_schema = '{_schema}' and hypertable_name = '{_measurement}' and proc_name = 'policy_compression';"));
        }

        [SkippableFact]
        public async Task Compressed_Chunks_Can_Be_Read_Rewritten_Extended_And_Deleted()
        {
            RequireServer();

            await Write(Point("M1", T0, "speed", 1.0), Point("M1", T0.AddMinutes(1), "speed", 2.0), Point("M1", T0.AddMinutes(2), "speed", 3.0));
            await CompressChunks();
            Assert.True((long)await CompressedChunks() > 0);

            // Read
            var result = Assert.Single(await Read(T0, T0.AddMinutes(5)));
            Assert.Equal(3, result.Rows.Length);

            // Aggregate
            var aggregate = await _service.Aggregate(new TrakHoundTimeSeriesAggregateQuery
            {
                Measurement = _measurement,
                Fields = new[] { "speed" },
                Start = T0,
                Stop = T0.AddMinutes(10),
                Interval = TimeSpan.FromMinutes(10),
                Functions = new[] { TrakHoundTimeSeriesAggregate.Mean, TrakHoundTimeSeriesAggregate.TimeWeightedMean }
            });
            Assert.True(aggregate.Success, aggregate.Message);
            Assert.Equal(2.0, Convert.ToDouble(Assert.Single(aggregate.Result).Rows[0][1]), 6);

            // Rewrite an existing Point, which has to keep its other Fields
            await Write(new TrakHoundTimeSeriesPoint { Measurement = _measurement, Tags = new() { ["machine"] = "M1" }, Timestamp = T0, Fields = new() { ["speed"] = 10.0 }, Quality = TrakHoundTimeSeriesQuality.Uncertain });
            result = Assert.Single(await Read(T0, T0.AddMinutes(1)));
            Assert.Equal(10.0, result.Rows[0][Array.IndexOf(result.Columns, "speed")]);
            Assert.Equal("Uncertain", result.Rows[0][Array.IndexOf(result.Columns, TrakHoundTimeSeriesResult.GetQualityColumn("speed"))]);

            // A new Field on a compressed Hypertable
            await Write(Point("M1", T0, "load", 7L));
            result = Assert.Single(await Read(T0, T0.AddMinutes(1)));
            Assert.Equal(7L, result.Rows[0][Array.IndexOf(result.Columns, "load")]);
            Assert.Equal(10.0, result.Rows[0][Array.IndexOf(result.Columns, "speed")]);

            // Delete
            Assert.True((await _service.Delete(_measurement, new Dictionary<string, string> { ["machine"] = "M1" }, T0.AddMinutes(1), T0.AddMinutes(2))).Success);
            Assert.Equal(2, Assert.Single(await Read(T0, T0.AddMinutes(5))).Rows.Length);
        }

        [SkippableFact]
        public async Task Services_Racing_To_Create_The_Same_Measurement_And_Fields_All_Succeed()
        {
            RequireServer();

            var services = Enumerable.Range(0, 8).Select(_ => CreateService()).ToList();
            try
            {
                var tasks = services.Select((service, i) => service.Write(new[]
                {
                    Point($"M{i}", T0.AddMinutes(i), "speed", 1.0),
                    Point($"M{i}", T0.AddMinutes(i), $"field_{i % 2}", 2.0)
                }));

                var responses = await Task.WhenAll(tasks);
                Assert.All(responses, o => Assert.True(o.Success, o.Message));

                var series = await _service.ListSeries(_measurement);
                Assert.Equal(8, series.Result.Count());
            }
            finally
            {
                foreach (var service in services) service.Dispose();
            }
        }

        [SkippableFact]
        public async Task Racing_Writers_Of_Different_Types_Leave_One_Type_And_A_Bad_Request()
        {
            RequireServer();

            await Write(Point("M1", T0, "speed", 1.0));

            var services = Enumerable.Range(0, 2).Select(_ => CreateService()).ToList();
            try
            {
                var responses = await Task.WhenAll(
                    services[0].Write(new[] { Point("M1", T0.AddMinutes(1), "mixed", 1.0) }),
                    services[1].Write(new[] { Point("M1", T0.AddMinutes(2), "mixed", "text") })
                    );

                Assert.Single(responses, o => o.Success);
                Assert.Single(responses, o => o.StatusCode == 400);
            }
            finally
            {
                foreach (var service in services) service.Dispose();
            }
        }

        [SkippableFact]
        public async Task A_Service_Recovers_When_Its_Cached_Tables_Are_Dropped()
        {
            RequireServer();

            await Write(Point("M1", T0, "speed", 1.0));

            await Execute($"drop schema \"{_schema}\" cascade;");

            await Write(Point("M1", T0, "speed", 2.0));
            Assert.Equal(2.0, Assert.Single(await Read(T0, T0.AddMinutes(1))).Rows[0][1]);
        }

        [SkippableFact]
        public async Task A_Second_Service_Sees_Fields_Another_One_Added()
        {
            RequireServer();

            using var other = CreateService();

            await Write(Point("M1", T0, "speed", 1.0));
            Assert.True((await other.Read(new TrakHoundTimeSeriesQuery { Measurement = _measurement, Start = T0, Stop = T0.AddMinutes(1) })).Success);

            await Write(Point("M1", T0.AddMinutes(1), "load", 2.0));

            // Reads and Aggregates through the other Service return the new Field
            var read = await other.Read(new TrakHoundTimeSeriesQuery { Measurement = _measurement, Fields = new[] { "load" }, Start = T0, Stop = T0.AddMinutes(5) });
            Assert.Equal(2.0, Assert.Single(read.Result).Rows[0][1]);
            var all = await other.Read(new TrakHoundTimeSeriesQuery { Measurement = _measurement, Start = T0, Stop = T0.AddMinutes(5) });
            Assert.Contains("load", Assert.Single(all.Result).Columns);

            // The other Service has its Columns cached without "load", and still has to reject the wrong type
            var conflict = await other.Write(new[] { Point("M1", T0.AddMinutes(2), "load", "text") });
            Assert.Equal(400, conflict.StatusCode);

            var written = await other.Write(new[] { Point("M1", T0.AddMinutes(2), "load", 3.0) });
            Assert.True(written.Success, written.Message);
        }

        [SkippableFact]
        public async Task Load()
        {
            RequireServer();

            var setting = Environment.GetEnvironmentVariable($"{TimescaleTestServer.Prefix}LOAD");
            Skip.If(string.IsNullOrEmpty(setting), $"Set {TimescaleTestServer.Prefix}LOAD to the number of Points to write");

            const int seriesCount = 100;
            const int batchSize = 5000;
            const int writers = 4;

            var total = int.Parse(setting);
            var perSeries = total / seriesCount;
            var step = TimeSpan.FromMinutes(5);

            await Write(Point("M0", T0.AddYears(-1), "speed", 0.0));

            // Each writer has its own Series, the way separate Workers would
            var timer = Stopwatch.StartNew();
            var fieldsAdded = 0;
            using (var done = new CancellationTokenSource())
            {
                // New Fields keep arriving during the writes
                var adder = Task.Run(async () =>
                {
                    while (!done.IsCancellationRequested)
                    {
                        var response = await _service.Write(new[] { Point("M0", T0.AddYears(-1).AddMinutes(fieldsAdded + 1), $"late_{fieldsAdded}", 1.0) });
                        Assert.True(response.Success, response.Message);
                        fieldsAdded++;
                        await Task.Delay(TimeSpan.FromSeconds(1));
                    }
                });

                await Task.WhenAll(Enumerable.Range(0, writers).Select(writer => Task.Run(async () =>
                {
                    using var service = CreateService();

                    var batch = new List<TrakHoundTimeSeriesPoint>(batchSize);
                    for (var i = 0; i < perSeries; i++)
                    {
                        for (var s = writer; s < seriesCount; s += writers)
                        {
                            batch.Add(new TrakHoundTimeSeriesPoint
                            {
                                Measurement = _measurement,
                                Tags = new() { ["machine"] = $"M{s}" },
                                Timestamp = T0 + step * i,
                                Fields = new() { ["speed"] = 8000.0 + i % 100, ["load"] = 50.0 + i % 10, ["temperature"] = 20.0 + i % 5, ["parts"] = (long)i, ["state"] = i % 10 == 0 ? "STOP" : "RUN" }
                            });

                            if (batch.Count >= batchSize)
                            {
                                var response = await service.Write(batch);
                                Assert.True(response.Success, response.Message);
                                batch.Clear();
                            }
                        }
                    }

                    if (batch.Count > 0)
                    {
                        var response = await service.Write(batch);
                        Assert.True(response.Success, response.Message);
                    }
                })));

                done.Cancel();
                await adder;
            }

            var written = perSeries * seriesCount;
            var writeTime = timer.Elapsed;
            _output.WriteLine($"Write : {written:N0} Points ({seriesCount} Series, 5 Fields, {writers} writers, batches of {batchSize:N0}) in {writeTime.TotalSeconds:N1} s = {written / writeTime.TotalSeconds:N0} Points/s. {fieldsAdded} new Fields added during the writes");

            Assert.Equal((long)written, await Execute($"select count(*) from \"{_schema}\".\"{_measurement}\" where \"time\" >= '{T0:O}';"));

            var range = step * perSeries;
            var stop = T0 + range;
            await Measure("uncompressed", T0, stop);

            timer.Restart();
            await CompressChunks();
            _output.WriteLine($"Compress : {await CompressedChunks()} Chunks in {timer.Elapsed.TotalSeconds:N1} s. Table size {await Execute($"select pg_size_pretty(hypertable_size('\"{_schema}\".\"{_measurement}\"'));")}");

            await Measure("compressed", T0, stop);
        }

        private async Task Measure(string name, DateTimeOffset start, DateTimeOffset stop)
        {
            var timer = Stopwatch.StartNew();
            var read = await _service.Read(new TrakHoundTimeSeriesQuery { Measurement = _measurement, Tags = new() { ["machine"] = new[] { "M1" } }, Start = start, Stop = start.AddDays(1), Fields = new[] { "speed" } });
            Assert.True(read.Success, read.Message);
            _output.WriteLine($"{name} : Read 1 Series, 1 day ({read.Result[0].Rows.Length} Points) in {timer.ElapsedMilliseconds} ms");

            timer.Restart();
            var latest = await _service.ReadLatest(_measurement, null, new[] { "speed" });
            Assert.True(latest.Success, latest.Message);
            _output.WriteLine($"{name} : ReadLatest {latest.Result.Length} Series in {timer.ElapsedMilliseconds} ms");

            foreach (var function in new[] { TrakHoundTimeSeriesAggregate.Mean, TrakHoundTimeSeriesAggregate.TimeWeightedMean })
            {
                timer.Restart();
                var aggregate = await _service.Aggregate(new TrakHoundTimeSeriesAggregateQuery
                {
                    Measurement = _measurement,
                    Tags = new() { ["machine"] = new[] { "M1" } },
                    Fields = new[] { "speed" },
                    Start = start,
                    Stop = stop,
                    Interval = TimeSpan.FromHours(1),
                    Functions = new[] { function }
                });
                Assert.True(aggregate.Success, aggregate.Message);
                _output.WriteLine($"{name} : {function} 1 Series, whole range, hourly ({aggregate.Result[0].Rows.Length} Rows) in {timer.ElapsedMilliseconds} ms");
            }

            timer.Restart();
            var all = await _service.Aggregate(new TrakHoundTimeSeriesAggregateQuery
            {
                Measurement = _measurement,
                Fields = new[] { "state" },
                Start = start,
                Stop = stop,
                Interval = TimeSpan.FromDays(1),
                Functions = new[] { TrakHoundTimeSeriesAggregate.DurationInState },
                State = "RUN"
            });
            Assert.True(all.Success, all.Message);
            _output.WriteLine($"{name} : DurationInState {all.Result.Length} Series, whole range, daily in {timer.ElapsedMilliseconds} ms");
        }
    }
}
