// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Logging;
using TrakHound.Security;
using TrakHound.Services;
using TrakHound.Services.TimeSeries;
using TrakHound.Volumes;
using TrakHound.Zones;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// Time Series Services are reached through a Router and are authorized like the other Service types
    /// </summary>
    public class TimeSeriesRouterTests
    {
        private class TestService : ITrakHoundTimeSeriesService
        {
            private readonly TrakHoundServiceResponse _write;
            private readonly TrakHoundServiceResponse<IEnumerable<string>> _list;

            public string Id { get; }
            public string Name => Id;
            public ITrakHoundServiceConfiguration Configuration { get; }
            public ITrakHoundVolume Volume => null;
            public int Calls { get; private set; }

            public event EventHandler<TrakHoundLogItem> LogReceived { add { } remove { } }


            public TestService(string id, TrakHoundServiceResponse? write = null, TrakHoundServiceResponse<IEnumerable<string>>? list = null)
            {
                Id = id;
                Configuration = new TrakHoundServiceConfiguration { Id = id, Name = id };
                _write = write ?? TrakHoundServiceResponse.Ok();
                _list = list ?? TrakHoundServiceResponse<IEnumerable<string>>.Ok(new[] { id });
            }

            public Task<TrakHoundServiceResponse> Write(IEnumerable<TrakHoundTimeSeriesPoint> points, CancellationToken cancellationToken = default)
            {
                Calls++;
                return Task.FromResult(_write);
            }

            public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListMeasurements(CancellationToken cancellationToken = default)
            {
                Calls++;
                return Task.FromResult(_list);
            }

            public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeries>>> ListSeries(string measurement, IReadOnlyDictionary<string, string> tags = null, int skip = 0, int take = 1000, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse<IEnumerable<TrakHoundTimeSeriesField>>> ListFields(string measurement, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Read(TrakHoundTimeSeriesQuery query, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesLatestResult[]>> ReadLatest(string measurement, IReadOnlyDictionary<string, string[]> tags = null, IEnumerable<string> fields = null, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse<TrakHoundTimeSeriesResult[]>> Aggregate(TrakHoundTimeSeriesAggregateQuery query, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse> Delete(string measurement, IReadOnlyDictionary<string, string> tags, DateTimeOffset start, DateTimeOffset stop, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public void Dispose() { }
        }


        /// <summary>
        /// Allows a Permission and denies the other one
        /// </summary>
        private class Authorizer : ITrakHoundAuthorizer, ITrakHoundZoneAuthorizer
        {
            private readonly string _allowed;

            public string LastResourceId { get; private set; }

            public Authorizer(string allowed)
            {
                _allowed = allowed;
            }

            public TrakHoundAuthorizationResult Authorize(TrakHoundIdentity identity, string resourceId, string permission = null)
            {
                LastResourceId = resourceId;
                return permission == _allowed ? TrakHoundAuthorizationResult.Allowed(Array.Empty<string>()) : TrakHoundAuthorizationResult.Forbidden("Denied");
            }

            public TrakHoundZoneDecision Authorize(TrakHoundApiRequestSource caller, string targetMemberId, string resourceId, string permission)
            {
                LastResourceId = resourceId;
                return permission == _allowed ? TrakHoundZoneDecision.Allowed : TrakHoundZoneDecision.Denied("zone", "Denied");
            }
        }


        private static TrakHoundServiceRouter Create(string pattern, params ITrakHoundService[] services)
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
                        Patterns = new[] { pattern },
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
        public void Route_Is_Listed()
        {
            Assert.Contains("TimeSeries", TrakHoundServiceRoutes.Names);
            Assert.Equal("TimeSeries", TrakHoundServiceRoutes.GetRoutes()[typeof(ITrakHoundTimeSeriesService)]);
        }

        [Theory]
        [InlineData("*")]
        [InlineData("TimeSeries")]
        public async Task Router_Sends_Requests_To_The_Service(string pattern)
        {
            var service = new TestService("a");
            var router = Create(pattern, service);

            var response = await router.TimeSeries.ListMeasurements();

            Assert.Equal(new[] { "a" }, response.Result);
            Assert.Equal(1, service.Calls);
        }

        [Fact]
        public async Task A_Route_For_Another_Type_Does_Not_Reach_The_Service()
        {
            var service = new TestService("a");
            var router = Create("KeyValues", service);

            var response = await router.TimeSeries.ListMeasurements();

            Assert.False(response.Success);
            Assert.Equal(0, service.Calls);
        }

        [Fact]
        public async Task An_Unsupported_Operation_Falls_Back_To_The_Next_Target()
        {
            // A read-only Service, such as one for an existing historian, returns 405 from Write
            var readOnly = new TestService("historian", write: TrakHoundServiceResponse.RouteNotConfigured());
            var writable = new TestService("database");
            var router = Create("TimeSeries", readOnly, writable);

            var response = await router.TimeSeries.Write(new[] { new TrakHoundTimeSeriesPoint() });

            Assert.True(response.Success);
            Assert.Equal(1, readOnly.Calls);
            Assert.Equal(1, writable.Calls);
        }

        [Fact]
        public async Task When_No_Target_Handles_A_Request_The_Caller_Learns_Why()
        {
            var first = new TestService("a", write: TrakHoundServiceResponse.RouteNotConfigured("This database can't write"), list: TrakHoundServiceResponse<IEnumerable<string>>.RouteNotConfigured("This database can't list"));
            var second = new TestService("b", write: TrakHoundServiceResponse.RouteNotConfigured("Another reason"));
            var router = Create("TimeSeries", first, second);

            var write = await router.TimeSeries.Write(new[] { new TrakHoundTimeSeriesPoint() });
            Assert.Equal(405, write.StatusCode);
            Assert.Equal("This database can't write", write.Message);

            var list = await Create("TimeSeries", first).TimeSeries.ListMeasurements();
            Assert.Equal(405, list.StatusCode);
            Assert.Equal("This database can't list", list.Message);
        }

        [Fact]
        public async Task A_Later_Target_That_Handles_A_Request_Replaces_The_Reason()
        {
            var first = new TestService("a", write: TrakHoundServiceResponse.RouteNotConfigured("This database can't write"));
            var second = new TestService("b");

            var response = await Create("TimeSeries", first, second).TimeSeries.Write(new[] { new TrakHoundTimeSeriesPoint() });

            Assert.True(response.Success);
            Assert.True(string.IsNullOrEmpty(response.Message));
        }

        [Fact]
        public async Task A_Bad_Request_Does_Not_Fall_Back()
        {
            var first = new TestService("a", write: TrakHoundServiceResponse.BadRequest("Invalid"));
            var second = new TestService("b");
            var router = Create("TimeSeries", first, second);

            var response = await router.TimeSeries.Write(new[] { new TrakHoundTimeSeriesPoint() });

            Assert.Equal(400, response.StatusCode);
            Assert.Equal(0, second.Calls);
        }

        [Theory]
        [InlineData("Write", TrakHoundServiceSecurity.WritePermission)]
        [InlineData("Delete", TrakHoundServiceSecurity.WritePermission)]
        [InlineData("Read", TrakHoundServiceSecurity.ReadPermission)]
        [InlineData("ReadLatest", TrakHoundServiceSecurity.ReadPermission)]
        [InlineData("Aggregate", TrakHoundServiceSecurity.ReadPermission)]
        [InlineData("ListMeasurements", TrakHoundServiceSecurity.ReadPermission)]
        [InlineData("ListSeries", TrakHoundServiceSecurity.ReadPermission)]
        [InlineData("ListFields", TrakHoundServiceSecurity.ReadPermission)]
        public void Operations_Need_The_Right_Permission(string operation, string permission)
        {
            Assert.Equal(permission, TrakHoundServiceSecurity.GetPermission(operation));
        }

        [Fact]
        public async Task An_Identity_That_Can_Only_Read_Cant_Write()
        {
            var service = new TestService("db");
            var router = Create("TimeSeries", service);
            var authorizer = new Authorizer(TrakHoundServiceSecurity.ReadPermission);

            using (TrakHoundSecurityContext.Begin(authorizer, null))
            {
                Assert.True((await router.TimeSeries.ListMeasurements()).Success);
                Assert.Equal("service:db:time-series:listmeasurements", authorizer.LastResourceId);
                Assert.Equal(1, service.Calls);

                Assert.Equal(403, (await router.TimeSeries.Write(new[] { new TrakHoundTimeSeriesPoint() })).StatusCode);
                Assert.Equal("service:db:time-series:write", authorizer.LastResourceId);
                Assert.Equal(1, service.Calls);
            }
        }

        /// <summary>
        /// Denies every Permission with the specified log Message and Client Message
        /// </summary>
        private class DenyingAuthorizer : ITrakHoundAuthorizer
        {
            private readonly string _message;
            private readonly string _clientMessage;

            public DenyingAuthorizer(string message, string clientMessage)
            {
                _message = message;
                _clientMessage = clientMessage;
            }

            public TrakHoundAuthorizationResult Authorize(TrakHoundIdentity identity, string resourceId, string permission = null) => TrakHoundAuthorizationResult.Forbidden(_message, _clientMessage);
        }

        [Fact]
        public async Task A_Denial_Sends_Only_Its_Client_Message()
        {
            var router = Create("TimeSeries", new TestService("db"));

            using (TrakHoundSecurityContext.Begin(new DenyingAuthorizer("(user-1) has no Assignment for service:db", "Not allowed here"), null))
            {
                var response = await router.TimeSeries.ListMeasurements();

                Assert.Equal(403, response.StatusCode);
                Assert.Equal("Not allowed here", response.Message);
            }
        }

        [Fact]
        public async Task A_Denial_Without_A_Client_Message_Sends_No_Message()
        {
            // The log Message names the Identity and Resource, so it never goes back to the caller
            var router = Create("TimeSeries", new TestService("db"));

            using (TrakHoundSecurityContext.Begin(new DenyingAuthorizer("(user-1) has no Assignment for service:db", null), null))
            {
                var response = await router.TimeSeries.ListMeasurements();

                Assert.Equal(403, response.StatusCode);
                Assert.True(string.IsNullOrEmpty(response.Message));
            }
        }

        [Fact]
        public async Task A_Zone_That_Only_Allows_Reading_Blocks_A_Module_Writing()
        {
            var service = new TestService("db");
            var router = Create("TimeSeries", service);
            var authorizer = new Authorizer(TrakHoundServiceSecurity.ReadPermission);

            TrakHoundZoneContext.Enter(authorizer, new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Worker, "collector"));

            Assert.True((await router.TimeSeries.ListMeasurements()).Success);
            Assert.Equal(403, (await router.TimeSeries.Write(new[] { new TrakHoundTimeSeriesPoint() })).StatusCode);
            Assert.Equal("service:db:time-series:write", authorizer.LastResourceId);
            Assert.Equal(1, service.Calls);
        }

        [Fact]
        public void Security_Family_Is_Time_Series()
        {
            Assert.Equal("time-series", TrakHoundServiceSecurity.GetFamily(typeof(ITrakHoundTimeSeriesService)));
            Assert.Equal("service:db:time-series:write", TrakHoundServiceSecurity.GetResourceId("db", "time-series", "Write"));
        }
    }
}
