// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Globalization;
using TrakHound.Services;
using TrakHound.Services.Tables;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.SqlServer.Service.Tests
{
    /// <summary>
    /// A Fact that only runs when TRAKHOUND_SQLSERVER_TEST_SERVER names a SQL Server to test against
    /// (with TRAKHOUND_SQLSERVER_TEST_PORT, _DATABASE, _USER and _PASSWORD; Windows authentication when no user is set)
    /// </summary>
    public sealed class SqlServerFactAttribute : FactAttribute
    {
        public SqlServerFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAKHOUND_SQLSERVER_TEST_SERVER")))
            {
                Skip = "Set TRAKHOUND_SQLSERVER_TEST_SERVER to run the SQL Server tests";
            }
        }
    }

    /// <summary>
    /// Creates the test schema before the tests and drops it after. Tables are created in the [trakhound_tests] schema only
    /// </summary>
    public sealed class SqlServerFixture : IDisposable
    {
        public const string Schema = "trakhound_tests";

        public SqlServerTableService Service { get; }


        public SqlServerFixture()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAKHOUND_SQLSERVER_TEST_SERVER"))) return;

            Service = new SqlServerTableService(CreateConfiguration());
            DropSchema();
        }

        public static TrakHoundServiceConfiguration CreateConfiguration(string database = null)
        {
            var configuration = new TrakHoundServiceConfiguration { Id = "sql-tests", Name = "sql-tests" };
            configuration.SetParameter("server", Environment.GetEnvironmentVariable("TRAKHOUND_SQLSERVER_TEST_SERVER"));
            configuration.SetParameter("port", Environment.GetEnvironmentVariable("TRAKHOUND_SQLSERVER_TEST_PORT") ?? "0");
            configuration.SetParameter("database", database ?? Environment.GetEnvironmentVariable("TRAKHOUND_SQLSERVER_TEST_DATABASE"));
            configuration.SetParameter("user", Environment.GetEnvironmentVariable("TRAKHOUND_SQLSERVER_TEST_USER"));
            configuration.SetParameter("password", Environment.GetEnvironmentVariable("TRAKHOUND_SQLSERVER_TEST_PASSWORD"));
            return configuration;
        }

        public void Dispose()
        {
            if (Service == null) return;

            DropSchema();
            Service.Dispose();
        }

        private void DropSchema()
        {
            var query = $@"declare @sql nvarchar(max) = N'';
select @sql += N'drop table [{Schema}].' + QUOTENAME([name]) + N';' from sys.tables where [schema_id] = SCHEMA_ID('{Schema}');
exec (@sql);
if exists (select * from sys.schemas where [name] = '{Schema}') exec('drop schema [{Schema}]');";

            var response = Service.ExecuteNonQuery(query).GetAwaiter().GetResult();
            if (!response.Success) throw new InvalidOperationException($"Drop Test Schema Failed : {response.StatusCode} : {response.Message}");
        }
    }

    /// <summary>
    /// The SQL Server Table Service, TrakHoundTables and the typed reads against a real SQL Server
    /// </summary>
    [Collection("SqlServer")]
    public class SqlServerIntegrationTests : IClassFixture<SqlServerFixture>
    {
        public enum ItemStatus
        {
            Running,
            Stopped
        }

        [Table("item", SqlServerFixture.Schema)]
        [IgnoreDuplicateKey]
        public class Item
        {
            [PrimaryKey]
            public string Id { get; set; }

            [Column(Length = TrakHoundTableColumnDefinition.MaxLength, Unicode = true)]
            [Update]
            public string Name { get; set; }

            public double Value { get; set; }

            public decimal Amount { get; set; }

            public DateTime Timestamp { get; set; }

            public TimeSpan Duration { get; set; }

            public ItemStatus? Status { get; set; }

            public Guid? Uuid { get; set; }

            public bool Enabled { get; set; }

            [Column("item_count")]
            public int Count { get; set; }
        }

        [Table("strict_item", SqlServerFixture.Schema)]
        public class StrictItem
        {
            [PrimaryKey]
            public string Id { get; set; }

            public string Name { get; set; }
        }


        private readonly SqlServerTableService _service;

        public SqlServerIntegrationTests(SqlServerFixture fixture)
        {
            _service = fixture.Service;
        }


        private async Task Run(TrakHoundTableRequestBuilder builder)
        {
            var response = await _service.Execute(builder.Build());
            Assert.True(response.Success, $"{response.StatusCode} : {response.Message}");
        }

        private async Task<List<Item>> ReadItems(string where = "1 = 1", TrakHoundTableParameterCollection parameters = null)
        {
            var response = await _service.ExecuteQuery($"select * from [{SqlServerFixture.Schema}].[item] where {where} order by [id];", parameters: parameters);
            Assert.True(response.Success, $"{response.StatusCode} : {response.Message}");
            return response.Result[0].ReadMultiple<Item>()?.ToList() ?? new List<Item>();
        }

        private async Task CreateItems()
        {
            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendEnsureTable<Item>();
            await Run(builder);

            var response = await _service.ExecuteNonQuery($"delete from [{SqlServerFixture.Schema}].[item];");
            Assert.True(response.Success, response.Message);
        }

        private static Item CreateItem(string id) => new Item
        {
            Id = id,
            Name = "Größe 測定 'quoted'\r\nGO\r\nGo to settings",
            Value = 1.5,
            Amount = 12345.678m,
            Timestamp = new DateTime(1965, 3, 1, 12, 30, 15, 123, DateTimeKind.Utc),
            Duration = TimeSpan.FromDays(30) + TimeSpan.FromTicks(1234567),
            Status = ItemStatus.Stopped,
            Uuid = Guid.NewGuid(),
            Enabled = true,
            Count = 42
        };

        private static void AssertEqual(Item expected, Item actual)
        {
            Assert.Equal(expected.Id, actual.Id);
            Assert.Equal(expected.Name, actual.Name);
            Assert.Equal(expected.Value, actual.Value);
            Assert.Equal(expected.Amount, actual.Amount);
            Assert.Equal(expected.Timestamp, actual.Timestamp);
            Assert.Equal(expected.Duration, actual.Duration);
            Assert.Equal(expected.Status, actual.Status);
            Assert.Equal(expected.Uuid, actual.Uuid);
            Assert.Equal(expected.Enabled, actual.Enabled);
            Assert.Equal(expected.Count, actual.Count);
        }


#pragma warning disable CS0618 // Packages built against the obsolete Query Builder have to keep working
        [SqlServerFact]
        public async Task ObsoleteQueryBuilder_ScriptsStillRun()
        {
            var first = CreateItem("first");
            var second = CreateItem("second");

            // A Batch Size of 1 puts each Statement in its own batch, so the script has GO lines
            var create = new TrakHoundTableQueryBuilder(1);
            create.AppendCreateTable<Item>();
            create.AppendInsert(new[] { first });
            var response = await _service.ExecuteNonQuery(create.Build());
            Assert.True(response.Success, $"{response.StatusCode} : {response.Message}");

            var delete = await _service.ExecuteNonQuery($"delete from [{SqlServerFixture.Schema}].[item];");
            Assert.True(delete.Success, delete.Message);

            var insert = new TrakHoundTableQueryBuilder { UseParameters = true };
            insert.AppendInsert(new[] { first, second });
            response = await _service.ExecuteNonQuery(insert.Build(), parameters: insert.Parameters);
            Assert.True(response.Success, $"{response.StatusCode} : {response.Message}");

            var items = await ReadItems();
            AssertEqual(first, items[0]);
            AssertEqual(second, items[1]);

            second.Name = "Merged 測定";
            var merge = TrakHoundTables.CreateBuilder();
            merge.AppendMerge(new[] { second });
            response = await _service.ExecuteNonQuery(merge.Build());
            Assert.True(response.Success, $"{response.StatusCode} : {response.Message}");

            AssertEqual(second, (await ReadItems())[1]);
        }
#pragma warning restore CS0618

        [SqlServerFact]
        public async Task CheckAvailability_IsAvailable()
        {
            var response = await _service.CheckAvailability();

            Assert.True(response.Result.IsAvailable, response.Result.Message);
        }

        [SqlServerFact]
        public async Task CheckAvailability_ReturnsTheReason()
        {
            using var service = new SqlServerTableService(SqlServerFixture.CreateConfiguration("trakhound_database_that_does_not_exist"));

            var response = await service.CheckAvailability();

            Assert.False(response.Result.IsAvailable);
            Assert.Contains("trakhound_database_that_does_not_exist", response.Result.Message);
        }

        [SqlServerFact]
        public async Task Insert_RoundTripsValues()
        {
            await CreateItems();

            var items = new[] { CreateItem("a"), CreateItem("b") };
            items[1].Status = null;
            items[1].Uuid = null;

            // Numbers must not depend on the culture
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                var builder = new TrakHoundTableRequestBuilder();
                builder.AppendInsert(items);
                await Run(builder);
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }

            var results = await ReadItems();

            Assert.Equal(2, results.Count);
            AssertEqual(items[0], results[0]);
            AssertEqual(items[1], results[1]);
        }

        [SqlServerFact]
        public async Task Insert_ManyRowsInBatches()
        {
            await CreateItems();

            var items = Enumerable.Range(0, 2500).Select(i => CreateItem($"item-{i:D5}")).ToList();

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(items);
            await Run(builder);

            var count = await _service.ExecuteQuery($"select count(*) from [{SqlServerFixture.Schema}].[item];");
            Assert.Equal("2500", count.Result[0].ReadSingleString());
        }

        [SqlServerFact]
        public async Task Insert_OverwriteReplacesRows()
        {
            await CreateItems();

            var item = CreateItem("a");
            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { item });
            await Run(builder);

            item.Count = 7;
            builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { item }, true);
            await Run(builder);

            var results = await ReadItems();
            Assert.Single(results);
            Assert.Equal(7, results[0].Count);
        }

        [SqlServerFact]
        public async Task Merge_UpdatesAndInserts()
        {
            await CreateItems();

            var a = CreateItem("a");
            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { a });
            await Run(builder);

            // Changing the Update column to null must still update the row
            a.Name = null;
            a.Count = 99;
            var b = CreateItem("b");
            b.Name = new string('x', 5000); // Longer than varchar(255)

            builder = new TrakHoundTableRequestBuilder();
            builder.AppendMerge(new[] { a, b });
            await Run(builder);

            var results = await ReadItems();
            Assert.Equal(2, results.Count);
            Assert.Null(results[0].Name);
            Assert.Equal(99, results[0].Count);
            Assert.Equal(b.Name, results[1].Name);
        }

        [SqlServerFact]
        public async Task Parameters_MatchUnicodeValues()
        {
            await CreateItems();

            var item = CreateItem("a");
            item.Name = "Größe 測定";
            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { item });
            await Run(builder);

            var parameters = new TrakHoundTableParameterCollection();
            parameters.Add("name", "Größe 測定");

            var results = await ReadItems("[name] = @name", parameters);

            Assert.Single(results);
        }

        [SqlServerFact]
        public async Task List_And_GetColumns()
        {
            await CreateItems();

            var tables = await _service.List();
            Assert.Contains(tables.Result, o => o.Schema == SqlServerFixture.Schema && o.Name == "item" && o.Type == TrakHoundTableType.Table);

            var columns = await _service.GetColumns($"{SqlServerFixture.Schema}.item");
            Assert.True(columns.Success, columns.Message);

            var byName = columns.Result.ToDictionary(o => o.Name);
            Assert.Equal(TrakHoundTableDataType.String, byName["id"].DataType);
            Assert.Equal(255, byName["id"].DataLength);
            Assert.False(byName["id"].AllowNull);
            Assert.Null(byName["name"].DataLength); // nvarchar(max)
            Assert.True(byName["name"].AllowNull);
            Assert.Equal(TrakHoundTableDataType.Decimal, byName["amount"].DataType);
            Assert.Equal(TrakHoundTableDataType.Guid, byName["uuid"].DataType);
            Assert.Equal(TrakHoundTableDataType.Boolean, byName["enabled"].DataType);
            Assert.Equal(TrakHoundTableDataType.Int64, byName["timestamp"].DataType);
            Assert.Equal(TrakHoundTableDataType.Int32, byName["item_count"].DataType);

            Assert.Equal(404, (await _service.GetColumns($"{SqlServerFixture.Schema}.missing_table")).StatusCode);
        }

        [SqlServerFact]
        public async Task ExecuteQuery_ReportsRowsAffected()
        {
            await CreateItems();

            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendInsert(new[] { CreateItem("a"), CreateItem("b") });
            await Run(builder);

            var response = await _service.ExecuteQuery($"update [{SqlServerFixture.Schema}].[item] set [item_count] = 1; select [id] from [{SqlServerFixture.Schema}].[item];");

            Assert.True(response.Success, response.Message);
            Assert.Equal(new[] { "a", "b" }, response.Result.Last().ReadMultipleString());
            Assert.Equal("(2 rows affected)", response.Result.Last().Message);
        }

        [SqlServerFact]
        public async Task Errors_HaveStatusCodes()
        {
            var builder = new TrakHoundTableRequestBuilder();
            builder.AppendEnsureTable<StrictItem>();
            await Run(builder);
            await _service.ExecuteNonQuery($"delete from [{SqlServerFixture.Schema}].[strict_item];");

            Assert.Equal(400, (await _service.ExecuteQuery("selec 1;")).StatusCode);
            Assert.Equal(400, (await _service.ExecuteQuery($"select * from [{SqlServerFixture.Schema}].[no_such_table];")).StatusCode);

            var insert = $"insert into [{SqlServerFixture.Schema}].[strict_item] ([id], [name]) values (N'a', N'x');";
            Assert.True((await _service.ExecuteNonQuery(insert)).Success);
            Assert.Equal(409, (await _service.ExecuteNonQuery(insert)).StatusCode);

            Assert.Equal(504, (await _service.ExecuteQuery("waitfor delay '00:00:03'; select 1;", timeout: 1)).StatusCode);

            using var missingDatabase = new SqlServerTableService(SqlServerFixture.CreateConfiguration("trakhound_database_that_does_not_exist"));
            Assert.Equal(503, (await missingDatabase.ExecuteQuery("select 1;")).StatusCode);
        }
    }
}
