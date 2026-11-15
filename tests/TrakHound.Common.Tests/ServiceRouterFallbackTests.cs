// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Logging;
using TrakHound.Services;
using TrakHound.Services.KeyValues;
using TrakHound.Volumes;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// How a Router tries the Targets of a Route: in order, moving on to the next Target only for the results the Target falls back on
    /// </summary>
    public class ServiceRouterFallbackTests
    {
        /// <summary>
        /// A Key Value Service that returns a set Response (and counts the calls it receives)
        /// </summary>
        private class TestService : ITrakHoundKeyValueService
        {
            private readonly Func<TrakHoundServiceResponse<IEnumerable<string>>> _list;
            private readonly Func<TrakHoundServiceResponse> _publish;

            public string Id { get; }
            public string Name => Id;
            public ITrakHoundServiceConfiguration Configuration { get; }
            public ITrakHoundVolume Volume => null;
            public int Calls { get; private set; }

            public event EventHandler<TrakHoundLogItem> LogReceived { add { } remove { } }


            public TestService(string id, Func<TrakHoundServiceResponse<IEnumerable<string>>> list = null, Func<TrakHoundServiceResponse> publish = null)
            {
                Id = id;
                Configuration = new TrakHoundServiceConfiguration { Id = id, Name = id };
                _list = list ?? (() => TrakHoundServiceResponse<IEnumerable<string>>.Ok(new List<string> { id }));
                _publish = publish ?? (() => TrakHoundServiceResponse.Ok());
            }

            public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListCollections(CancellationToken cancellationToken = default)
            {
                Calls++;
                return Task.FromResult(_list());
            }

            public Task<TrakHoundServiceResponse> Publish(string collection, IReadOnlyDictionary<string, string> entries, TimeSpan? ttl = null, CancellationToken cancellationToken = default)
            {
                Calls++;
                return Task.FromResult(_publish());
            }

            public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, string pattern, int skip, int take, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>> Read(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse<int>> Count(string collection, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse> Delete(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default) => throw new NotImplementedException();
            public void Dispose() { }
        }

        private static TrakHoundServiceResponse<IEnumerable<string>> List(params string[] values) => TrakHoundServiceResponse<IEnumerable<string>>.Ok(values.ToList());

        private static TrakHoundServiceTargetConfiguration Service(string id, params string[] fallbackOn) => new TrakHoundServiceTargetConfiguration
        {
            Id = $"target-{id}",
            Type = "Service",
            Source = id,
            FallbackOn = fallbackOn.Length > 0 ? fallbackOn : null
        };

        private static TrakHoundServiceRouterConfiguration Router(string id, params TrakHoundServiceTargetConfiguration[] targets) => new TrakHoundServiceRouterConfiguration
        {
            Id = id,
            Name = id,
            Routes = new[] { new TrakHoundServiceRouteConfiguration { Id = $"{id}-route", Patterns = new[] { "*" }, Targets = targets } }
        };

        private static TrakHoundServiceRouter Create(TrakHoundServiceRouterConfiguration configuration, params ITrakHoundService[] services)
        {
            var router = new TrakHoundServiceRouter(configuration, null, null);
            router.MapTargets(Array.Empty<TrakHoundServiceRouter>(), services);
            router.Initialize();
            return router;
        }


        [Fact]
        public async Task Not_Found_Falls_Back_By_Default()
        {
            var a = new TestService("a", () => TrakHoundServiceResponse<IEnumerable<string>>.NotFound());
            var b = new TestService("b");
            var router = Create(Router("r", Service("a"), Service("b")), a, b);

            var response = await router.KeyValues.ListCollections();

            Assert.True(response.Success);
            Assert.Equal(new[] { "b" }, response.Result);
            Assert.Equal(1, b.Calls);
        }

        [Theory]
        [InlineData(400)]
        [InlineData(401)]
        [InlineData(403)]
        [InlineData(409)]
        public async Task Requests_That_Were_Wrong_Or_Not_Allowed_Do_Not_Fall_Back_By_Default(int statusCode)
        {
            var a = new TestService("a", () => new TrakHoundServiceResponse<IEnumerable<string>>(statusCode: statusCode));
            var b = new TestService("b");
            var router = Create(Router("r", Service("a"), Service("b")), a, b);

            var response = await router.KeyValues.ListCollections();

            Assert.Equal(statusCode, response.StatusCode);
            Assert.Equal(0, b.Calls);
        }

        [Theory]
        [InlineData(503)]
        [InlineData(504)]
        [InlineData(500)]
        public async Task Services_That_Cant_Answer_Fall_Back_By_Default(int statusCode)
        {
            var a = new TestService("a", publish: () => new TrakHoundServiceResponse(statusCode: statusCode));
            var b = new TestService("b");
            var router = Create(Router("r", Service("a"), Service("b")), a, b);

            var response = await router.KeyValues.Publish("collection", new Dictionary<string, string>());

            Assert.True(response.Success);
            Assert.Equal(1, b.Calls);
        }

        [Fact]
        public void Not_Available_Is_Not_Not_Found()
        {
            Assert.Equal(503, TrakHoundServiceResponse.NotAvailable().StatusCode);
            Assert.Equal(404, TrakHoundServiceResponse.NotFound().StatusCode);
            Assert.Equal(TrakHoundServiceResultType.NotAvailable, TrakHoundServiceFallback.GetResultType(503));
        }

        [Fact]
        public async Task Targets_Fall_Back_On_The_Results_They_List()
        {
            var a = new TestService("a", () => TrakHoundServiceResponse<IEnumerable<string>>.BadRequest());
            var b = new TestService("b");
            var router = Create(Router("r", Service("a", "BadRequest"), Service("b")), a, b);

            var response = await router.KeyValues.ListCollections();

            Assert.True(response.Success);
            Assert.Equal(1, b.Calls);
        }

        [Fact]
        public async Task Results_That_Are_Not_Listed_Do_Not_Fall_Back()
        {
            var a = new TestService("a", () => TrakHoundServiceResponse<IEnumerable<string>>.InternalError());
            var b = new TestService("b");
            var router = Create(Router("r", Service("a", "NotFound"), Service("b")), a, b);

            var response = await router.KeyValues.ListCollections();

            Assert.Equal(500, response.StatusCode);
            Assert.Equal(0, b.Calls);
        }

        [Fact]
        public async Task An_Empty_List_Never_Falls_Back()
        {
            var a = new TestService("a", () => TrakHoundServiceResponse<IEnumerable<string>>.NotFound());
            var b = new TestService("b");
            var target = Service("a");
            target.FallbackOn = Array.Empty<string>();
            var router = Create(Router("r", target, Service("b")), a, b);

            var response = await router.KeyValues.ListCollections();

            Assert.Equal(404, response.StatusCode);
            Assert.Equal(0, b.Calls);
        }

        [Fact]
        public async Task Status_Codes_Are_Accepted_As_Results()
        {
            var a = new TestService("a", () => TrakHoundServiceResponse<IEnumerable<string>>.BadRequest());
            var b = new TestService("b");
            var router = Create(Router("r", Service("a", "400"), Service("b")), a, b);

            var response = await router.KeyValues.ListCollections();

            Assert.True(response.Success);
        }

        [Fact]
        public async Task Targets_That_Dont_Handle_The_Request_Are_Always_Skipped()
        {
            var a = new TestService("a", () => TrakHoundServiceResponse<IEnumerable<string>>.RouteNotConfigured());
            var b = new TestService("b");
            var target = Service("a");
            target.FallbackOn = Array.Empty<string>();
            var router = Create(Router("r", target, Service("b")), a, b);

            var response = await router.KeyValues.ListCollections();

            Assert.True(response.Success);
            Assert.Equal(1, b.Calls);
        }

        [Fact]
        public async Task Empty_Results_Fall_Back_When_Listed()
        {
            var a = new TestService("a", () => List());
            var b = new TestService("b");
            var router = Create(Router("r", Service("a", "Empty"), Service("b")), a, b);

            var response = await router.KeyValues.ListCollections();

            Assert.Equal(new[] { "b" }, response.Result);
        }

        [Fact]
        public async Task Empty_Results_Are_Returned_When_No_Later_Target_Has_A_Result()
        {
            var a = new TestService("a", () => List());
            var b = new TestService("b", () => TrakHoundServiceResponse<IEnumerable<string>>.NotFound());
            var router = Create(Router("r", Service("a", "Empty"), Service("b")), a, b);

            var response = await router.KeyValues.ListCollections();

            Assert.True(response.Success);
            Assert.Empty(response.Result);
            Assert.Equal(1, b.Calls);
        }

        [Fact]
        public async Task Empty_Results_Are_Returned_When_Not_Listed()
        {
            var a = new TestService("a", () => List());
            var b = new TestService("b");
            var router = Create(Router("r", Service("a"), Service("b")), a, b);

            var response = await router.KeyValues.ListCollections();

            Assert.True(response.Success);
            Assert.Empty(response.Result);
            Assert.Equal(0, b.Calls);
        }

        [Fact]
        public async Task Targets_Are_Tried_In_The_Order_They_Are_Configured()
        {
            // A Router Target listed before a Service Target is tried first
            var inner = Create(Router("inner", Service("a")), new TestService("a"));
            var b = new TestService("b");

            var configuration = Router("outer", new TrakHoundServiceTargetConfiguration { Id = "to-inner", Type = "Router", Source = "inner" }, Service("b"));
            var router = new TrakHoundServiceRouter(configuration, null, null);
            router.MapTargets(new[] { inner }, new ITrakHoundService[] { b });
            router.Initialize();

            var response = await router.KeyValues.ListCollections();

            Assert.Equal(new[] { "a" }, response.Result);
            Assert.Equal(0, b.Calls);
        }
    }
}
