// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Logging;
using TrakHound.Services;
using TrakHound.Services.Tables;
using TrakHound.Volumes;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// How a Router sends Table Requests to its Targets
    /// </summary>
    public class TrakHoundTableRouterTests
    {
        /// <summary>
        /// A Table Service that returns set Status Codes (and counts the calls it receives)
        /// </summary>
        private class TestTableService : ITrakHoundTableService
        {
            private readonly int _statusCode;

            public string Id { get; }
            public string Name => Id;
            public ITrakHoundServiceConfiguration Configuration { get; }
            public ITrakHoundVolume Volume => null;
            public int Calls { get; private set; }
            public string LastQuery { get; private set; }

            public event EventHandler<TrakHoundLogItem> LogReceived { add { } remove { } }


            public TestTableService(string id, int statusCode = 200)
            {
                Id = id;
                Configuration = new TrakHoundServiceConfiguration { Id = id, Name = id };
                _statusCode = statusCode;
            }

            private TrakHoundServiceResponse<T> Respond<T>(T result)
            {
                Calls++;
                return new TrakHoundServiceResponse<T>(_statusCode < 400 ? result : default, _statusCode, Id);
            }

            public Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default) => Task.FromResult(Respond(new TrakHoundServiceAvailabilityResponse { IsAvailable = true }));
            public Task<TrakHoundServiceResponse<TrakHoundTable[]>> List(CancellationToken cancellationToken = default) => Task.FromResult(Respond(new[] { new TrakHoundTable { Name = Id } }));
            public Task<TrakHoundServiceResponse<TrakHoundTableColumn[]>> GetColumns(string tableName, CancellationToken cancellationToken = default) => Task.FromResult(Respond(new[] { new TrakHoundTableColumn { Name = tableName } }));

            public Task<TrakHoundServiceResponse<TrakHoundTableResult[]>> ExecuteQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, CancellationToken cancellationToken = default)
            {
                LastQuery = query;
                return Task.FromResult(Respond(new[] { new TrakHoundTableResult { Message = Id } }));
            }

            public Task<TrakHoundServiceResponse> ExecuteNonQuery(string query, int? timeout = null, TrakHoundTableParameterCollection parameters = null, CancellationToken cancellationToken = default)
            {
                Calls++;
                LastQuery = query;
                return Task.FromResult(new TrakHoundServiceResponse(_statusCode, Id));
            }

            public Task<TrakHoundServiceResponse<string[]>> GetDialects(CancellationToken cancellationToken = default) => Task.FromResult(Respond(new[] { Id }));

            public Task<TrakHoundServiceResponse<TrakHoundTableStepResult[]>> Execute(TrakHoundTableRequest request, CancellationToken cancellationToken = default) => Task.FromResult(Respond(new[] { new TrakHoundTableStepResult { Message = Id } }));

            public void Dispose() { }
        }

        [Fact]
        public async Task Execute_TriesTheNextTargetWhenNoVariantCanRun()
        {
            // 405 = the Target has no Query Variant for its Dialects, so nothing ran
            var a = new TestTableService("a", 405);
            var b = new TestTableService("b");
            var router = Create(a, b);

            var response = await router.Tables.Execute(TrakHoundTableRequest.Ansi("select 1;"));

            Assert.True(response.Success);
            Assert.Equal("b", response.Result[0].Message);
            Assert.Equal(1, b.Calls);
        }

        [Fact]
        public async Task Execute_DoesNotRunAQueryErrorOnTheNextTarget()
        {
            var a = new TestTableService("a", 400);
            var b = new TestTableService("b");
            var router = Create(a, b);

            var response = await router.Tables.Execute(TrakHoundTableRequest.Ansi("select 1;"));

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(0, b.Calls);
        }

        private static TrakHoundServiceRouter Create(params TestTableService[] services)
        {
            var configuration = new TrakHoundServiceRouterConfiguration
            {
                Id = "r",
                Name = "r",
                Routes = new[]
                {
                    new TrakHoundServiceRouteConfiguration
                    {
                        Id = "r-route",
                        Patterns = new[] { "*" },
                        Targets = services.Select(o => new TrakHoundServiceTargetConfiguration { Id = $"target-{o.Id}", Type = "Service", Source = o.Id }).ToArray()
                    }
                }
            };

            var router = new TrakHoundServiceRouter(configuration, null, null);
            router.MapTargets(Array.Empty<TrakHoundServiceRouter>(), services);
            router.Initialize();
            return router;
        }


        [Fact]
        public async Task Requests_ReachTheService()
        {
            var service = new TestTableService("a");
            var router = Create(service);

            Assert.True((await router.Tables.CheckAvailability()).Result.IsAvailable);
            Assert.Equal("a", (await router.Tables.List()).Result[0].Name);
            Assert.Equal("x.y", (await router.Tables.GetColumns("x.y")).Result[0].Name);
            Assert.Equal("a", (await router.Tables.ExecuteQuery("select 1;")).Result[0].Message);
            Assert.Equal("a", (await router.Tables.ExecuteNonQuery("delete 1;")).Message);
            Assert.Equal("delete 1;", service.LastQuery);
            Assert.Equal(5, service.Calls);
        }

        [Fact]
        public async Task NotAvailable_FallsBackToTheNextTarget()
        {
            var a = new TestTableService("a", 503);
            var b = new TestTableService("b");
            var router = Create(a, b);

            var response = await router.Tables.ExecuteQuery("select 1;");

            Assert.True(response.Success);
            Assert.Equal("b", response.Result[0].Message);
        }

        [Theory]
        [InlineData(400)]
        [InlineData(409)]
        public async Task QueryErrors_AreNotRunOnTheNextTarget(int statusCode)
        {
            var a = new TestTableService("a", statusCode);
            var b = new TestTableService("b");
            var router = Create(a, b);

            var response = await router.Tables.ExecuteNonQuery("insert 1;");

            Assert.Equal(statusCode, response.StatusCode);
            Assert.Equal(0, b.Calls);
        }
    }
}
