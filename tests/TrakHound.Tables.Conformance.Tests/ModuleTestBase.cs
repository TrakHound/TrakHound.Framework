// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Postgres.Service;
using TrakHound.Services;
using TrakHound.Services.Tables;
using TrakHound.Sqlite.Service;

namespace TrakHound.Tables.Conformance.Tests
{
    /// <summary>
    /// Creates a new empty database for a first-party module to run against
    /// </summary>
    public interface IModuleDatabase : IDisposable
    {
        TrakHoundSqlTableService Create();
    }

    public sealed class SqliteModuleDatabase : IModuleDatabase
    {
        private readonly string _directory = Directory.CreateTempSubdirectory("trakhound-sqlite-").FullName;
        private SqliteTableService _service;

        public TrakHoundSqlTableService Create()
        {
            var configuration = new TrakHoundServiceConfiguration { Id = "sqlite-tests", Name = "sqlite-tests" };
            configuration.SetParameter("database", Path.Combine(_directory, "test.db"));
            return _service = new SqliteTableService(configuration);
        }

        public void Dispose()
        {
            _service?.Dispose();
            try { Directory.Delete(_directory, true); } catch (IOException) { }
        }
    }

    /// <summary>
    /// Uses the server named by TRAKHOUND_SQLSERVER_TEST_SERVER. The trakhound Schema is dropped first, so use a development database
    /// </summary>
    public sealed class SqlServerModuleDatabase : IModuleDatabase
    {
        private const string _prefix = "TRAKHOUND_SQLSERVER_TEST_";

        private TrakHound.SqlServer.Service.SqlServerTableService _service;

        public TrakHoundSqlTableService Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable($"{_prefix}SERVER"))) throw new SkipException($"Set {_prefix}SERVER to run the SQL Server tests");

            var configuration = new TrakHoundServiceConfiguration { Id = "sqlserver-tests", Name = "sqlserver-tests" };
            configuration.SetParameter("server", Environment.GetEnvironmentVariable($"{_prefix}SERVER"));
            configuration.SetParameter("port", Environment.GetEnvironmentVariable($"{_prefix}PORT") ?? "0");
            configuration.SetParameter("database", Environment.GetEnvironmentVariable($"{_prefix}DATABASE"));
            configuration.SetParameter("user", Environment.GetEnvironmentVariable($"{_prefix}USER"));
            configuration.SetParameter("password", Environment.GetEnvironmentVariable($"{_prefix}PASSWORD"));
            _service = new TrakHound.SqlServer.Service.SqlServerTableService(configuration);

            var query = @"declare @sql nvarchar(max) = N'';
select @sql += N'drop view [trakhound].' + QUOTENAME([name]) + N';' from sys.views where [schema_id] = SCHEMA_ID('trakhound');
select @sql += N'drop table [trakhound].' + QUOTENAME([name]) + N';' from sys.tables where [schema_id] = SCHEMA_ID('trakhound');
exec (@sql);
if exists (select * from sys.schemas where [name] = 'trakhound') exec('drop schema [trakhound]');";

            var response = _service.ExecuteNonQuery(query).GetAwaiter().GetResult();
            Assert.True(response.Success, response.Message);

            return _service;
        }

        public void Dispose() => _service?.Dispose();
    }

    /// <summary>
    /// Uses the server named by TRAKHOUND_POSTGRES_TEST_SERVER. The trakhound Schema is dropped first, so use a development database
    /// </summary>
    public sealed class PostgresModuleDatabase : IModuleDatabase
    {
        private const string _prefix = "TRAKHOUND_POSTGRES_TEST_";

        private PostgresTableService _service;

        public TrakHoundSqlTableService Create()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable($"{_prefix}SERVER"))) throw new SkipException($"Set {_prefix}SERVER to run the PostgreSQL tests");

            var configuration = new TrakHoundServiceConfiguration { Id = "postgres-tests", Name = "postgres-tests" };
            configuration.SetParameter("server", Environment.GetEnvironmentVariable($"{_prefix}SERVER"));
            configuration.SetParameter("port", Environment.GetEnvironmentVariable($"{_prefix}PORT") ?? "0");
            configuration.SetParameter("database", Environment.GetEnvironmentVariable($"{_prefix}DATABASE"));
            configuration.SetParameter("user", Environment.GetEnvironmentVariable($"{_prefix}USER"));
            configuration.SetParameter("password", Environment.GetEnvironmentVariable($"{_prefix}PASSWORD"));
            _service = new PostgresTableService(configuration);

            var response = _service.ExecuteNonQuery("drop schema if exists \"trakhound\" cascade;").GetAwaiter().GetResult();
            Assert.True(response.Success, response.Message);

            return _service;
        }

        public void Dispose() => _service?.Dispose();
    }
}
