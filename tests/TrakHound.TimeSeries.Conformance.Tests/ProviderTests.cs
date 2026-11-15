// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Npgsql;
using TrakHound.Postgres.Service;
using TrakHound.Services;
using TrakHound.Services.TimeSeries;
using TrakHound.Timescale.Service;
using TrakHound.Testing.Services;

namespace TrakHound.TimeSeries.Conformance.Tests
{
    /// <summary>
    /// The memory Service always runs, since it needs no setup
    /// </summary>
    public class MemoryConformanceTests : TimeSeriesServiceConformanceTests, IDisposable
    {
        private TrakHoundMemoryTimeSeriesService _service = new TrakHoundMemoryTimeSeriesService();

        protected override ITrakHoundTimeSeriesService Service => _service;

        protected override Task Reset()
        {
            _service.Dispose();
            _service = new TrakHoundMemoryTimeSeriesService();
            return Task.CompletedTask;
        }

        public void Dispose() => _service.Dispose();
    }


    /// <summary>
    /// Runs when TRAKHOUND_TIMESCALE_TEST_SERVER names a TimescaleDB server (with TRAKHOUND_TIMESCALE_TEST_PORT, _DATABASE, _USER and _PASSWORD).
    /// Measurements are created in the trakhound_conformance_ts Schema, which is dropped first, so use a development database
    /// </summary>
    [Collection("Timescale")]
    public class TimescaleConformanceTests : TimeSeriesServiceConformanceTests, IDisposable
    {
        private const string _schema = "trakhound_conformance_ts";

        private TimescaleTimeSeriesService _service;

        protected override ITrakHoundTimeSeriesService Service => _service ?? throw new SkipException($"Set {TimescaleTestServer.Prefix}SERVER to run the TimescaleDB tests");

        public TimescaleConformanceTests()
        {
            var configuration = TimescaleTestServer.Configuration(_schema);
            if (configuration != null) _service = new TimescaleTimeSeriesService(configuration);
        }

        protected override async Task Reset()
        {
            if (_service == null) throw new SkipException($"Set {TimescaleTestServer.Prefix}SERVER to run the TimescaleDB tests");

            await using var connection = new NpgsqlConnection(PostgresConnectionString.Create(TimescaleTestServer.Configuration(_schema)));
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand($"drop schema if exists \"{_schema}\" cascade;", connection);
            await command.ExecuteNonQueryAsync();

            _service.Dispose();
            _service = new TimescaleTimeSeriesService(TimescaleTestServer.Configuration(_schema));
        }

        public void Dispose() => _service?.Dispose();
    }


    internal static class TimescaleTestServer
    {
        public const string Prefix = "TRAKHOUND_TIMESCALE_TEST_";

        public static TrakHoundServiceConfiguration Configuration(string schema)
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable($"{Prefix}SERVER"))) return null;

            var configuration = new TrakHoundServiceConfiguration { Id = "timescale-tests", Name = "timescale-tests" };
            configuration.SetParameter("server", Environment.GetEnvironmentVariable($"{Prefix}SERVER"));
            configuration.SetParameter("port", Environment.GetEnvironmentVariable($"{Prefix}PORT") ?? "0");
            configuration.SetParameter("database", Environment.GetEnvironmentVariable($"{Prefix}DATABASE"));
            configuration.SetParameter("user", Environment.GetEnvironmentVariable($"{Prefix}USER"));
            configuration.SetParameter("password", Environment.GetEnvironmentVariable($"{Prefix}PASSWORD"));
            configuration.SetParameter("schema", schema);
            return configuration;
        }
    }
}
