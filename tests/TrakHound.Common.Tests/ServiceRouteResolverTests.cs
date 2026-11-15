// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Services;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// Where TrakHoundServiceRouteResolver says a Request would go (the same rules the Router uses)
    /// </summary>
    public class ServiceRouteResolverTests
    {
        private static readonly string[] KeyValues = { "KeyValues" };
        private static readonly string[] Blobs = { "Blobs" };

        private static TrakHoundServiceRouteService Service(string id, IReadOnlyCollection<string> routes) => new TrakHoundServiceRouteService { Id = id, Name = id, Routes = routes };

        private static TrakHoundServiceTargetConfiguration ServiceTarget(string source, params string[] fallbackOn) => new TrakHoundServiceTargetConfiguration
        {
            Type = "Service",
            Source = source,
            FallbackOn = fallbackOn.Length > 0 ? fallbackOn : null
        };

        private static TrakHoundServiceTargetConfiguration RouterTarget(string routerId) => new TrakHoundServiceTargetConfiguration { Type = "Router", Source = routerId };

        private static TrakHoundServiceRouterConfiguration Router(string id, string name, params (string[] Patterns, TrakHoundServiceTargetConfiguration[] Targets)[] routes) => new TrakHoundServiceRouterConfiguration
        {
            Id = id,
            Name = name,
            Routes = routes.Select(o => new TrakHoundServiceRouteConfiguration { Patterns = o.Patterns, Targets = o.Targets }).ToList()
        };


        [Theory]
        [InlineData("*", "Tables", true)]
        [InlineData("KeyValues", "KeyValues", true)]
        [InlineData("keyvalues", "KeyValues", false)]
        [InlineData("Message.*", "MessageQueues", true)]
        [InlineData("^Blobs$", "Tables", false)]
        public void Patterns_Are_Case_Sensitive_Regular_Expressions(string pattern, string route, bool expected)
        {
            Assert.Equal(expected, TrakHoundServiceRouteResolver.IsPatternMatch(pattern, route));
        }

        [Fact]
        public void Invalid_Patterns_Do_Not_Match_A_Route()
        {
            Assert.ThrowsAny<ArgumentException>(() => TrakHoundServiceRouteResolver.IsPatternMatch("[Blobs", "Blobs"));
            Assert.False(TrakHoundServiceRouteResolver.IsRouteMatch(new[] { "[Blobs" }, "Blobs"));
        }

        [Fact]
        public void Targets_Are_Tried_In_Route_Then_Target_Order()
        {
            var routers = new[]
            {
                Router("r1", "default",
                    (KeyValues, new[] { ServiceTarget("cache", "NotFound"), ServiceTarget("database") }),
                    (Blobs, new[] { ServiceTarget("files") }),
                    (new[] { "*" }, new[] { ServiceTarget("archive") }))
            };
            var services = new[] { Service("cache", KeyValues), Service("database", KeyValues), Service("files", Blobs), Service("archive", new[] { "KeyValues", "Blobs" }) };

            var path = TrakHoundServiceRouteResolver.Resolve(routers, "r1", "KeyValues", services);

            Assert.Equal(new[] { "cache", "database", "archive" }, path.Targets.Select(o => o.Source));
            Assert.Equal(new[] { TrakHoundServiceResultType.NotFound }, path.Targets[0].FallbackOn);
            Assert.All(path.Targets, o => Assert.Null(o.Problem));
        }

        [Fact]
        public void A_Service_That_Does_Not_Handle_The_Route_Is_Skipped()
        {
            var routers = new[] { Router("r1", "default", (new[] { "*" }, new[] { ServiceTarget("files"), ServiceTarget("database") })) };
            var services = new[] { Service("files", Blobs), Service("database", KeyValues) };

            var path = TrakHoundServiceRouteResolver.Resolve(routers, "r1", "KeyValues", services);

            Assert.Empty(path.Targets[0].Services);
            Assert.NotNull(path.Targets[0].Problem);
            Assert.Equal("database", Assert.Single(path.Targets[1].Services).Id);
        }

        [Fact]
        public void A_Wildcard_Target_Sends_To_Every_Matching_Service_Except_Denied_Ones()
        {
            var routers = new[] { Router("r1", "default", (new[] { "KeyValues", "!Blobs" }, new[] { ServiceTarget("*") })) };
            var services = new[] { Service("cache", KeyValues), Service("both", new[] { "KeyValues", "Blobs" }), Service("database", KeyValues) };

            var path = TrakHoundServiceRouteResolver.Resolve(routers, "r1", "KeyValues", services);

            Assert.Equal(new[] { "cache", "database" }, path.Targets[0].Services.Select(o => o.Id));
        }

        [Fact]
        public void Services_With_Unknown_Routes_Are_Kept()
        {
            var routers = new[] { Router("r1", "default", (new[] { "KeyValues", "!Blobs" }, new[] { ServiceTarget("*") })) };
            var services = new[] { Service("remote", null) };

            var path = TrakHoundServiceRouteResolver.Resolve(routers, "r1", "KeyValues", services);

            Assert.Equal("remote", Assert.Single(path.Targets[0].Services).Id);
        }

        [Fact]
        public void Router_Targets_Follow_The_Other_Router_And_Stop_At_Loops()
        {
            var routers = new[]
            {
                Router("a", "default", (new[] { "*" }, new[] { RouterTarget("b"), RouterTarget("missing") })),
                Router("b", "archive", (new[] { "*" }, new[] { ServiceTarget("database"), RouterTarget("a") }))
            };
            var services = new[] { Service("database", KeyValues) };

            var path = TrakHoundServiceRouteResolver.Resolve(routers, "a", "KeyValues", services);

            var archive = path.Targets[0].Router;
            Assert.Equal("archive", archive.RouterName);
            Assert.Equal("database", Assert.Single(archive.Targets[0].Services).Id);
            Assert.Null(archive.Targets[1].Router);
            Assert.NotNull(archive.Targets[1].Problem);

            Assert.Null(path.Targets[1].Router);
            Assert.NotNull(path.Targets[1].Problem);
        }

        [Fact]
        public void Routers_Are_Found_By_Id_Then_Name()
        {
            var routers = new[] { Router("r1", "first"), Router("r2", "default"), Router("Default", "other") };

            Assert.Equal("r2", TrakHoundServiceRouteResolver.FindRouter(routers, null).Id);
            Assert.Equal("Default", TrakHoundServiceRouteResolver.FindRouter(routers, "Default").Id);
            Assert.Equal("r1", TrakHoundServiceRouteResolver.FindRouter(routers, "FIRST").Id);
            Assert.Null(TrakHoundServiceRouteResolver.FindRouter(routers, "missing"));
        }

        [Fact]
        public void Without_A_Default_Router_The_First_Is_Used()
        {
            var routers = new[] { Router("r1", "first"), Router("r2", "second") };

            Assert.Equal("r1", TrakHoundServiceRouteResolver.FindRouter(routers, null).Id);
        }
    }
}
