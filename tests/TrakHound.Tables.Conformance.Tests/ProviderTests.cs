// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Postgres.Service;
using TrakHound.Services;
using TrakHound.Services.Tables;
using TrakHound.Sqlite.Service;
using TrakHound.SqlServer.Service;

namespace TrakHound.Tables.Conformance.Tests
{
    /// <summary>
    /// SQLite always runs, since it only needs a file
    /// </summary>
    public class SqliteConformanceTests : TableServiceConformanceTests, IDisposable
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("trakhound-sqlite-").FullName;
        private readonly SqliteTableService _service;

        protected override ITrakHoundTableService Service => _service;
        protected override string Dialect => TrakHoundTableDialects.Sqlite;

        public SqliteConformanceTests()
        {
            var configuration = new TrakHoundServiceConfiguration { Id = "sqlite-tests", Name = "sqlite-tests" };
            configuration.SetParameter("database", Path.Combine(_directory, "test.db"));
            _service = new SqliteTableService(configuration);
        }

        protected override Task Reset() => Task.CompletedTask; // Each test has its own files

        public void Dispose()
        {
            _service.Dispose();
            Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }
    }


    /// <summary>
    /// Runs when TRAKHOUND_POSTGRES_TEST_SERVER names a PostgreSQL server (with TRAKHOUND_POSTGRES_TEST_PORT, _DATABASE, _USER and _PASSWORD).
    /// Tables are created in the trakhound_conformance Schema, which is dropped first, so use a development database
    /// </summary>
    [Collection("Postgres")]
    public class PostgresConformanceTests : TableServiceConformanceTests, IDisposable
    {
        private const string _prefix = "TRAKHOUND_POSTGRES_TEST_";

        private readonly PostgresTableService _service;

        protected override ITrakHoundTableService Service => _service ?? throw new SkipException($"Set {_prefix}SERVER to run the PostgreSQL tests");
        protected override string Dialect => TrakHoundTableDialects.Postgres;

        public PostgresConformanceTests()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable($"{_prefix}SERVER"))) return;

            var configuration = new TrakHoundServiceConfiguration { Id = "postgres-tests", Name = "postgres-tests" };
            configuration.SetParameter("server", Environment.GetEnvironmentVariable($"{_prefix}SERVER"));
            configuration.SetParameter("port", Environment.GetEnvironmentVariable($"{_prefix}PORT") ?? "0");
            configuration.SetParameter("database", Environment.GetEnvironmentVariable($"{_prefix}DATABASE"));
            configuration.SetParameter("user", Environment.GetEnvironmentVariable($"{_prefix}USER"));
            configuration.SetParameter("password", Environment.GetEnvironmentVariable($"{_prefix}PASSWORD"));
            _service = new PostgresTableService(configuration);
        }

        protected override async Task Reset()
        {
            var response = await Service.ExecuteNonQuery($"drop schema if exists \"{Schema}\" cascade;");
            Assert.True(response.Success, response.Message);
        }

        public void Dispose() => _service?.Dispose();
    }


    /// <summary>
    /// Runs when TRAKHOUND_SQLSERVER_TEST_SERVER names a SQL Server (with TRAKHOUND_SQLSERVER_TEST_PORT, _DATABASE, _USER and _PASSWORD).
    /// Tables are created in the trakhound_conformance Schema, which is dropped first, so use a development database
    /// </summary>
    [Collection("SqlServer")]
    public class SqlServerConformanceTests : TableServiceConformanceTests, IDisposable
    {
        private const string _prefix = "TRAKHOUND_SQLSERVER_TEST_";

        private readonly SqlServerTableService _service;

        protected override ITrakHoundTableService Service => _service ?? throw new SkipException($"Set {_prefix}SERVER to run the SQL Server tests");
        protected override string Dialect => TrakHoundTableDialects.SqlServer;

        public SqlServerConformanceTests()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable($"{_prefix}SERVER"))) return;

            var configuration = new TrakHoundServiceConfiguration { Id = "sqlserver-tests", Name = "sqlserver-tests" };
            configuration.SetParameter("server", Environment.GetEnvironmentVariable($"{_prefix}SERVER"));
            configuration.SetParameter("port", Environment.GetEnvironmentVariable($"{_prefix}PORT") ?? "0");
            configuration.SetParameter("database", Environment.GetEnvironmentVariable($"{_prefix}DATABASE"));
            configuration.SetParameter("user", Environment.GetEnvironmentVariable($"{_prefix}USER"));
            configuration.SetParameter("password", Environment.GetEnvironmentVariable($"{_prefix}PASSWORD"));
            _service = new SqlServerTableService(configuration);
        }

        protected override async Task Reset()
        {
            var query = $@"declare @sql nvarchar(max) = N'';
select @sql += N'drop table [{Schema}].' + QUOTENAME([name]) + N';' from sys.tables where [schema_id] = SCHEMA_ID('{Schema}');
exec (@sql);
if exists (select * from sys.schemas where [name] = '{Schema}') exec('drop schema [{Schema}]');";

            var response = await Service.ExecuteNonQuery(query);
            Assert.True(response.Success, response.Message);
        }

        public void Dispose() => _service?.Dispose();
    }
}
