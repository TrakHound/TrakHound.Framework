// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;
using TrakHound.InfluxDb.Service;
using TrakHound.Services;
using TrakHound.Services.TimeSeries;
using Xunit.Abstractions;

namespace TrakHound.TimeSeries.Conformance.Tests
{
    /// <summary>
    /// Runs when TRAKHOUND_INFLUXDB_TEST_URL names an InfluxDB 3 server (with TRAKHOUND_INFLUXDB_TEST_TOKEN if it uses authentication).
    /// Each test uses its own Database and drops it afterward. InfluxDB 3 Core allows few Databases, so use a development server
    /// </summary>
    [Collection("InfluxDb")]
    public class InfluxDbConformanceTests : TimeSeriesServiceConformanceTests, IAsyncLifetime
    {
        private readonly InfluxDbTestServer _server = new InfluxDbTestServer();
        private InfluxDbTimeSeriesService _service;

        protected override ITrakHoundTimeSeriesService Service => _service ?? throw new SkipException($"Set {InfluxDbTestServer.Prefix}URL to run the InfluxDB tests");

        // InfluxDB 3 can't delete Points
        protected override bool SupportsDelete => false;

        public Task InitializeAsync()
        {
            if (_server.Available) _service = _server.CreateService();
            return Task.CompletedTask;
        }

        protected override async Task Reset()
        {
            if (_service == null) throw new SkipException($"Set {InfluxDbTestServer.Prefix}URL to run the InfluxDB tests");

            await _server.DropDatabase();
            _service.Dispose();
            _service = _server.CreateService();
        }

        public async Task DisposeAsync()
        {
            _service?.Dispose();
            await _server.DropDatabase();
        }
    }


    /// <summary>
    /// What only applies to InfluxDB. The load test also needs TRAKHOUND_INFLUXDB_TEST_LOAD set to the number of Points to write
    /// </summary>
    [Collection("InfluxDb")]
    public class InfluxDbServerTests : IAsyncLifetime
    {
        private const string _measurement = "server_spindle";

        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        private readonly ITestOutputHelper _output;
        private readonly InfluxDbTestServer _server = new InfluxDbTestServer();
        private InfluxDbTimeSeriesService _service;


        public InfluxDbServerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        public Task InitializeAsync()
        {
            if (_server.Available) _service = _server.CreateService();
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            _service?.Dispose();
            await _server.DropDatabase();
        }

        private void RequireServer() => Skip.If(_service == null, $"Set {InfluxDbTestServer.Prefix}URL to run the InfluxDB tests");


        [SkippableFact]
        public async Task Delete_Returns_405_So_A_Router_Tries_The_Next_Target()
        {
            RequireServer();

            var response = await _service.Delete(_measurement, null, T0, T0.AddDays(1));

            Assert.Equal(405, response.StatusCode);
        }

        [SkippableFact]
        public async Task A_Tag_Value_Line_Protocol_Cant_Write_Is_A_Bad_Request()
        {
            RequireServer();

            foreach (var value in new[] { "line\nbreak", "ends with\\" })
            {
                var response = await _service.Write(new[] { new TrakHoundTimeSeriesPoint { Measurement = _measurement, Tags = new() { ["machine"] = value }, Timestamp = T0, Fields = new() { ["speed"] = 1.0 } } });
                Assert.Equal(400, response.StatusCode);
            }
        }

        [SkippableFact]
        public async Task A_Series_With_Fewer_Tags_Is_Not_Mixed_With_One_That_Has_More()
        {
            RequireServer();

            var written = await _service.Write(new[]
            {
                new TrakHoundTimeSeriesPoint { Measurement = _measurement, Tags = new() { ["machine"] = "M1" }, Timestamp = T0, Fields = new() { ["speed"] = 1.0 } },
                new TrakHoundTimeSeriesPoint { Measurement = _measurement, Tags = new() { ["machine"] = "M1", ["line"] = "L1" }, Timestamp = T0, Fields = new() { ["speed"] = 2.0 } }
            });
            Assert.True(written.Success, written.Message);

            var read = await _service.Read(new TrakHoundTimeSeriesQuery { Measurement = _measurement, Tags = new() { ["machine"] = new[] { "M1" } }, Start = T0, Stop = T0.AddMinutes(1) });

            Assert.Equal(2, read.Result.Length);
            Assert.All(read.Result, o => Assert.Single(o.Rows));
        }

        [SkippableFact]
        public async Task A_Server_That_Cant_Be_Reached_Is_Not_Available()
        {
            var configuration = new TrakHoundServiceConfiguration { Id = "influxdb-tests", Name = "influxdb-tests" };
            configuration.SetParameter("url", "http://127.0.0.1:1");
            using var service = new InfluxDbTimeSeriesService(configuration);

            Assert.Equal(503, (await service.ListMeasurements()).StatusCode);
            Assert.False((await service.CheckAvailability()).Result.IsAvailable);
        }

        [SkippableTheory]
        [InlineData(1, 5, 200)]  // The last Point before start, and 4 in the range
        [InlineData(1, 4, 400)]
        [InlineData(2, 10, 200)] // Two Fields share the limit
        [InlineData(2, 9, 400)]
        public async Task Time_Weighted_Functions_Stop_At_The_Point_Limit(int fieldCount, int limit, int expected)
        {
            RequireServer();

            var points = Enumerable.Range(0, 5).Select(i => new TrakHoundTimeSeriesPoint
            {
                Measurement = _measurement,
                Tags = new() { ["machine"] = "M1" },
                Timestamp = T0.AddMinutes(i),
                Fields = new() { ["speed"] = 1.5 * i, ["load"] = 2.5 * i }
            });
            Assert.True((await _service.Write(points)).Success);

            using var limited = _server.CreateService(limit);
            var response = await limited.Aggregate(new TrakHoundTimeSeriesAggregateQuery
            {
                Measurement = _measurement,
                Fields = new[] { "speed", "load" }.Take(fieldCount).ToArray(),
                Start = T0.AddSeconds(30),
                Stop = T0.AddMinutes(10),
                Interval = TimeSpan.FromMinutes(10),
                Functions = new[] { TrakHoundTimeSeriesAggregate.TimeWeightedMean }
            });

            Assert.Equal(expected, response.StatusCode);
            if (expected == 400) Assert.Contains("maxTimeWeightedPoints", response.Message);
        }

        [SkippableFact]
        public async Task The_Limit_Does_Not_Apply_To_Other_Functions()
        {
            RequireServer();

            var points = Enumerable.Range(0, 5).Select(i => new TrakHoundTimeSeriesPoint { Measurement = _measurement, Tags = new() { ["machine"] = "M1" }, Timestamp = T0.AddMinutes(i), Fields = new() { ["speed"] = 1.5 * i } });
            Assert.True((await _service.Write(points)).Success);

            using var limited = _server.CreateService(1);
            var response = await limited.Aggregate(new TrakHoundTimeSeriesAggregateQuery { Measurement = _measurement, Fields = new[] { "speed" }, Start = T0, Stop = T0.AddMinutes(10), Interval = TimeSpan.FromMinutes(10), Functions = new[] { TrakHoundTimeSeriesAggregate.Mean } });

            Assert.True(response.Success, response.Message);
        }

        [SkippableFact]
        public async Task Load()
        {
            RequireServer();

            var setting = Environment.GetEnvironmentVariable($"{InfluxDbTestServer.Prefix}LOAD");
            Skip.If(string.IsNullOrEmpty(setting), $"Set {InfluxDbTestServer.Prefix}LOAD to the number of Points to write");

            const int seriesCount = 100;
            const int batchSize = 5000;
            const int writers = 4;

            var perSeries = int.Parse(setting) / seriesCount;
            var step = TimeSpan.FromMinutes(5);

            var timer = Stopwatch.StartNew();
            await Task.WhenAll(Enumerable.Range(0, writers).Select(writer => Task.Run(async () =>
            {
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
                            var response = await _service.Write(batch);
                            Assert.True(response.Success, response.Message);
                            batch.Clear();
                        }
                    }
                }

                if (batch.Count > 0)
                {
                    var response = await _service.Write(batch);
                    Assert.True(response.Success, response.Message);
                }
            })));

            var written = perSeries * seriesCount;
            _output.WriteLine($"Write : {written:N0} Points ({seriesCount} Series, 5 Fields, {writers} writers, batches of {batchSize:N0}) in {timer.Elapsed.TotalSeconds:N1} s = {written / timer.Elapsed.TotalSeconds:N0} Points/s");

            var stop = T0 + step * perSeries;

            timer.Restart();
            var read = await _service.Read(new TrakHoundTimeSeriesQuery { Measurement = _measurement, Tags = new() { ["machine"] = new[] { "M1" } }, Start = T0, Stop = T0.AddDays(1), Fields = new[] { "speed" } });
            Assert.True(read.Success, read.Message);
            _output.WriteLine($"Read 1 Series, 1 day ({read.Result[0].Rows.Length} Points) in {timer.ElapsedMilliseconds} ms");

            timer.Restart();
            var latest = await _service.ReadLatest(_measurement, null, new[] { "speed" });
            Assert.True(latest.Success, latest.Message);
            _output.WriteLine($"ReadLatest {latest.Result.Length} Series in {timer.ElapsedMilliseconds} ms");

            foreach (var function in new[] { TrakHoundTimeSeriesAggregate.Mean, TrakHoundTimeSeriesAggregate.TimeWeightedMean })
            {
                timer.Restart();
                var aggregate = await _service.Aggregate(new TrakHoundTimeSeriesAggregateQuery
                {
                    Measurement = _measurement,
                    Tags = new() { ["machine"] = new[] { "M1" } },
                    Fields = new[] { "speed" },
                    Start = T0,
                    Stop = stop,
                    Interval = TimeSpan.FromHours(1),
                    Functions = new[] { function }
                });
                Assert.True(aggregate.Success, aggregate.Message);
                _output.WriteLine($"{function} 1 Series, whole range, hourly ({aggregate.Result[0].Rows.Length} Rows) in {timer.ElapsedMilliseconds} ms");
            }

            timer.Restart();
            var all = await _service.Aggregate(new TrakHoundTimeSeriesAggregateQuery
            {
                Measurement = _measurement,
                Fields = new[] { "state" },
                Start = T0,
                Stop = stop,
                Interval = TimeSpan.FromDays(1),
                Functions = new[] { TrakHoundTimeSeriesAggregate.DurationInState },
                State = "RUN"
            });
            Assert.True(all.Success, all.Message);
            _output.WriteLine($"DurationInState {all.Result.Length} Series, whole range, daily in {timer.ElapsedMilliseconds} ms");
        }
    }


    internal class InfluxDbTestServer
    {
        public const string Prefix = "TRAKHOUND_INFLUXDB_TEST_";

        private static readonly string _url = Environment.GetEnvironmentVariable($"{Prefix}URL");
        private static readonly string _token = Environment.GetEnvironmentVariable($"{Prefix}TOKEN");

        private string _database = NewDatabase();


        public bool Available => !string.IsNullOrEmpty(_url);


        private static string NewDatabase() => $"conformance_{Guid.NewGuid():N}";

        public InfluxDbTimeSeriesService CreateService(int? maxTimeWeightedPoints = null)
        {
            var configuration = new TrakHoundServiceConfiguration { Id = "influxdb-tests", Name = "influxdb-tests" };
            configuration.SetParameter("url", _url);
            configuration.SetParameter("database", _database);
            if (maxTimeWeightedPoints != null) configuration.SetParameter("maxTimeWeightedPoints", maxTimeWeightedPoints.ToString());
            if (!string.IsNullOrEmpty(_token)) configuration.SetParameter("token", _token);
            return new InfluxDbTimeSeriesService(configuration);
        }

        /// <summary>
        /// Drops the Database and moves on to a new name, so nothing from the dropped one can be seen
        /// </summary>
        public async Task DropDatabase()
        {
            if (!Available) return;

            using var client = new HttpClient();
            if (!string.IsNullOrEmpty(_token)) client.DefaultRequestHeaders.Add("Authorization", $"Bearer {_token}");

            // A Database that was never written to doesn't exist, which is fine here
            using var response = await client.DeleteAsync($"{_url.TrimEnd('/')}/api/v3/configure/database?db={_database}");
            _database = NewDatabase();
        }
    }
}
