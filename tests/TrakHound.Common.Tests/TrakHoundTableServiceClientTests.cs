// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Globalization;
using System.Text.Json;
using TrakHound.Clients.Services;
using TrakHound.Services;
using TrakHound.Services.Tables;
using TrakHound.Services.Tables.Attributes;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// How the typed Table reads (ReadSingle, ReadMultiple, ...) turn Query results into Responses and objects
    /// </summary>
    public class TrakHoundTableServiceClientTests
    {
        /// <summary>
        /// A Table Service Client that returns a set Query Response
        /// </summary>
        private class TestClient : ITrakHoundTableServiceClient
        {
            private readonly TrakHoundServiceResponse<TrakHoundTableResult[]> _response;

            public TestClient(TrakHoundServiceResponse<TrakHoundTableResult[]> response) => _response = response;

            public TestClient(params TrakHoundTableResult[] results) : this(TrakHoundServiceResponse<TrakHoundTableResult[]>.Ok(results)) { }

            public Task<TrakHoundServiceResponse<TrakHoundTableResult[]>> ExecuteQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default) => Task.FromResult(_response);

            public Task<TrakHoundServiceResponse<string>> ReadSingleString(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default) => TrakHoundTableServiceClient.ReadSingleString(this, query, resultIndex);
            public Task<TrakHoundServiceResponse<IEnumerable<string>>> ReadMultipleString(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default) => TrakHoundTableServiceClient.ReadMultipleString(this, query, resultIndex);
            public Task<TrakHoundServiceResponse<T>> ReadSingle<T>(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default) => TrakHoundTableServiceClient.ReadSingle<T>(this, query, resultIndex);
            public Task<TrakHoundServiceResponse<IEnumerable<T>>> ReadMultiple<T>(string query, int? resultIndex = null, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default) => TrakHoundTableServiceClient.ReadMultiple<T>(this, query, resultIndex);

            public Task<TrakHoundServiceResponse<string[]>> GetDialects(string routerId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse<TrakHoundTableStepResult[]>> Execute(TrakHoundTableRequest request, string routerId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse<TrakHoundTable[]>> List(string routerId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse<TrakHoundTableColumn[]>> GetColumns(string tableName, string routerId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse> ExecuteNonQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, string routerId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        }

        public enum TestStatus
        {
            Running,
            Stopped
        }

        public class TestItem
        {
            public string Id { get; set; }

            [Column("MyName")]
            public string Name { get; set; }

            public int Count { get; set; }

            public decimal Amount { get; set; }

            public DateTime Timestamp { get; set; }

            public DateTimeOffset Received { get; set; }

            public TimeSpan Duration { get; set; }

            public TestStatus? Status { get; set; }

            public Guid Uuid { get; set; }

            public char Code { get; set; }

            public bool Enabled { get; set; }
        }

        public class NoConstructorItem
        {
            public NoConstructorItem(string id) { }
        }


        private static TrakHoundTableResult Table(string[] columns, params object[][] rows)
        {
            return new TrakHoundTableResult
            {
                Columns = columns.Select(o => new TrakHoundTableColumn { Name = o }).ToArray(),
                Rows = rows
            };
        }


        [Fact]
        public async Task FailedQuery_KeepsStatusCodeAndRouter()
        {
            var failed = TrakHoundServiceResponse<TrakHoundTableResult[]>.NotAvailable("Database Offline");
            failed.RouterId = "router";
            failed.ServiceId = "service";

            var response = await new TestClient(failed).ReadMultiple<TestItem>("select 1;");

            Assert.Equal(503, response.StatusCode);
            Assert.Equal("Database Offline", response.Message);
            Assert.Equal("router", response.RouterId);
            Assert.Equal("service", response.ServiceId);
        }

        [Fact]
        public async Task FailedQuery_WithoutStatusCodeIsInternalError()
        {
            var response = await new TestClient(new TrakHoundServiceResponse<TrakHoundTableResult[]>()).ReadSingleString("select 1;");

            Assert.Equal(500, response.StatusCode);
        }

        [Fact]
        public async Task MissingClientOrQuery_IsBadRequest()
        {
            Assert.Equal(400, (await TrakHoundTableServiceClient.ReadSingle<TestItem>(null, "select 1;")).StatusCode);
            Assert.Equal(400, (await new TestClient().ReadSingle<TestItem>("")).StatusCode);
        }

        [Fact]
        public async Task InvalidResultIndex_IsBadRequest()
        {
            var client = new TestClient(Table(new[] { "id" }, new object[] { "a" }));

            Assert.Equal(400, (await client.ReadMultiple<TestItem>("select 1;", resultIndex: 1)).StatusCode);
            Assert.Equal(400, (await client.ReadMultiple<TestItem>("select 1;", resultIndex: -1)).StatusCode);
        }

        [Fact]
        public async Task ResultIndex_ReadsOnlyThatResultSet()
        {
            var client = new TestClient(Table(new[] { "id" }, new object[] { "a" }), Table(new[] { "id" }, new object[] { "b" }));

            Assert.Equal(new[] { "b" }, (await client.ReadMultipleString("select 1;", resultIndex: 1)).Result);
            Assert.Equal(new[] { "a", "b" }, (await client.ReadMultipleString("select 1;")).Result);
        }

        [Fact]
        public async Task NoRows_MultipleIsEmptyAndSingleIsNotFound()
        {
            var client = new TestClient(Table(new[] { "id" }));

            var multiple = await client.ReadMultiple<TestItem>("select 1;");
            Assert.Equal(200, multiple.StatusCode);
            Assert.Empty(multiple.Result);

            var strings = await client.ReadMultipleString("select 1;");
            Assert.Equal(200, strings.StatusCode);
            Assert.Empty(strings.Result);

            Assert.Equal(404, (await client.ReadSingle<TestItem>("select 1;")).StatusCode);
            Assert.Equal(404, (await client.ReadSingleString("select 1;")).StatusCode);
        }

        [Fact]
        public async Task MultipleString_SkipsNulls()
        {
            var client = new TestClient(Table(new[] { "id" }, new object[] { "a" }, new object[] { null }, new object[] { "b" }));

            Assert.Equal(new[] { "a", "b" }, (await client.ReadMultipleString("select 1;")).Result);
        }

        [Fact]
        public async Task ValueThatCantBeConverted_IsInternalError()
        {
            var client = new TestClient(Table(new[] { "count" }, new object[] { "abc" }));

            var response = await client.ReadMultiple<TestItem>("select 1;");

            Assert.Equal(500, response.StatusCode);
            Assert.Contains("'count'", response.Message);
        }

        [Fact]
        public async Task NoParameterlessConstructor_IsInternalError()
        {
            var client = new TestClient(Table(new[] { "id" }, new object[] { "a" }));

            var response = await client.ReadMultiple<NoConstructorItem>("select 1;");

            Assert.Equal(500, response.StatusCode);
            Assert.Contains("parameterless constructor", response.Message);
        }

        [Fact]
        public async Task Columns_MatchIgnoringCase()
        {
            var client = new TestClient(Table(new[] { "ID", "myname" }, new object[] { "a", "Name A" }));

            var item = (await client.ReadSingle<TestItem>("select 1;")).Result;

            Assert.Equal("a", item.Id);
            Assert.Equal("Name A", item.Name);
        }

        [Fact]
        public async Task ShortAndNullRows_DontThrow()
        {
            var client = new TestClient(Table(new[] { "id", "count" }, new object[] { "a" }, null));

            var items = (await client.ReadMultiple<TestItem>("select 1;")).Result.ToList();

            Assert.Single(items);
            Assert.Equal("a", items[0].Id);
        }

        [Fact]
        public async Task ScalarTypes_ReadFirstColumn()
        {
            var client = new TestClient(Table(new[] { "value", "other" }, new object[] { 5L, "x" }, new object[] { 7L, "y" }));

            Assert.Equal(new[] { 5L, 7L }, (await client.ReadMultiple<long>("select 1;")).Result);
            Assert.Equal(new[] { "5", "7" }, (await client.ReadMultiple<string>("select 1;")).Result);
        }

        [Fact]
        public async Task Values_RoundTripFromTrakHoundTables()
        {
            var item = new TestItem
            {
                Id = "a",
                Name = "Größe",
                Count = 3,
                Amount = 1.5m,
                Timestamp = new DateTime(1965, 3, 1, 12, 0, 0, DateTimeKind.Utc), // Before 1970 (negative nanoseconds)
                Received = new DateTimeOffset(2026, 10, 21, 8, 0, 0, TimeSpan.FromHours(-4)),
                Duration = TimeSpan.FromDays(30),
                Status = TestStatus.Stopped,
                Uuid = Guid.NewGuid(),
                Code = 'Z',
                Enabled = true
            };

            // The values TrakHoundTables writes for each Column
            var properties = typeof(TestItem).GetProperties();
            var columns = properties.Select(TrakHoundTables.GetColumnName).ToArray();
            var row = properties.Select(o => TrakHoundTables.GetColumnValue(o.PropertyType, o.GetValue(item))).ToArray();

            var result = (await new TestClient(Table(columns, row)).ReadSingle<TestItem>("select 1;")).Result;

            Assert.Equal(item.Id, result.Id);
            Assert.Equal(item.Name, result.Name);
            Assert.Equal(item.Count, result.Count);
            Assert.Equal(item.Amount, result.Amount);
            Assert.Equal(item.Timestamp, result.Timestamp);
            Assert.Equal(item.Received, result.Received);
            Assert.Equal(item.Duration, result.Duration);
            Assert.Equal(item.Status, result.Status);
            Assert.Equal(item.Uuid, result.Uuid);
            Assert.Equal(item.Code, result.Code);
            Assert.Equal(item.Enabled, result.Enabled);
        }

        [Fact]
        public async Task Values_ReadFromJsonStrings()
        {
            // Over HTTP, values arrive as JSON strings and numbers
            var uuid = Guid.NewGuid();
            var table = Table(new[] { "uuid", "amount", "status", "duration" }, new object[] { uuid, 1.5m, "stopped", 3_000_000_000L });
            var json = JsonSerializer.Serialize(table);
            var deserialized = JsonSerializer.Deserialize<TrakHoundTableResult>(json);

            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");

                var item = (await new TestClient(deserialized).ReadSingle<TestItem>("select 1;")).Result;

                Assert.Equal(uuid, item.Uuid);
                Assert.Equal(1.5m, item.Amount);
                Assert.Equal(TestStatus.Stopped, item.Status);
                Assert.Equal(TimeSpan.FromSeconds(3), item.Duration);
            }
            finally
            {
                CultureInfo.CurrentCulture = culture;
            }
        }

        [Fact]
        public void TableResult_ReadsTimeSpans()
        {
            var table = Table(new[] { "duration" }, new object[] { 2_000_000_000L });

            Assert.Equal(TimeSpan.FromSeconds(2), table.ReadSingle<TestItem>().Duration);
            Assert.Null(Table(new[] { "duration" }).ReadMultiple<TestItem>());
        }
    }
}
