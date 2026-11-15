// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Pages.Security;
using TrakHound.Zones;

namespace TrakHound.Instance.Zones.Tests
{
    public class ZoneMapGraphTests
    {
        private static TrakHoundZoneConfiguration Sandbox() => new TrakHoundZoneConfiguration
        {
            Id = "sandbox",
            Name = "AI Sandbox",
            Mode = TrakHoundZoneMode.Audit,
            Members = new[] { "function:ai-report", "worker:dev-*" },
            Rules = new[]
            {
                new TrakHoundZoneRule { Resource = "api:entities:/objects", Permissions = new[] { "read" } },
                new TrakHoundZoneRule { Resource = "api:entities:/sources", Permissions = new[] { "read", "write" } },
                new TrakHoundZoneRule { Resource = "service:*" },
                new TrakHoundZoneRule { Resource = "function:cleanup", Permissions = new[] { "run" } }
            },
            AllowedCallers = new[] { "api:*", "zone:production" },
            RouterId = "test-data"
        };

        private static TrakHoundZoneConfiguration Production() => new TrakHoundZoneConfiguration
        {
            Id = "production",
            Mode = TrakHoundZoneMode.Enforce,
            Members = new[] { "service:database" }
        };


        [Theory]
        [InlineData("api:entities:/objects", "api:entities")]
        [InlineData("api:entities:*", "api:entities")]
        [InlineData("service:redis:key-values:publish", "service:redis")]
        [InlineData("volume:shared:/data.json", "volume:shared")]
        [InlineData("function:cleanup", "function:cleanup")]
        [InlineData("manage:instance:i-1", "manage")]
        [InlineData("service:*", "service:*")]
        [InlineData("api:dev-*:/x", "api:dev-*:/x")]
        [InlineData("*", "*")]
        public void Resources_Belong_To_Their_Module(string resource, string expected)
        {
            Assert.Equal(expected, ZoneMapGraph.GetTargetKey(resource));
        }

        [Theory]
        [InlineData("api:entities:/objects", "/objects")]
        [InlineData("api:entities:*", "all")]
        [InlineData("service:redis:key-values:publish", "key-values · publish")]
        [InlineData("function:cleanup", null)]
        [InlineData("function:*", "all")]
        [InlineData("manage:instance:i-1", "instance:i-1")]
        public void Details_Describe_The_Part_Of_The_Module(string resource, string expected)
        {
            Assert.Equal(expected, ZoneMapGraph.GetDetail(resource));
        }

        [Fact]
        public void Zones_Show_Their_Members()
        {
            var graph = ZoneMapGraph.Build(new[] { Sandbox() }, null);

            var zone = graph.GetNode("zone:sandbox");
            Assert.Equal(ZoneMapNodeKind.Zone, zone.Kind);
            Assert.Equal("AI Sandbox", zone.Title);
            Assert.Equal(new[] { "function:ai-report", "worker:dev-*" }, zone.Members.Select(o => o.Pattern));
            Assert.All(zone.Members, o => Assert.True(o.IsMissing)); // No Modules were given
        }

        [Fact]
        public void Rules_To_The_Same_Module_Share_One_Line()
        {
            var graph = ZoneMapGraph.Build(new[] { Sandbox() }, null);

            var api = Assert.Single(graph.Edges, o => o.To == "api:entities");
            Assert.Equal(ZoneMapEdgeKind.Rule, api.Kind);
            Assert.Equal(new[] { "/objects · read", "/sources · read, write" }, api.Details);

            Assert.Equal(new[] { "all · any" }, Assert.Single(graph.Edges, o => o.To == "service:*").Details);
            Assert.Equal(new[] { "run" }, Assert.Single(graph.Edges, o => o.To == "function:cleanup").Details);
        }

        [Fact]
        public void Allowed_Callers_Point_At_The_Zone()
        {
            var graph = ZoneMapGraph.Build(new[] { Sandbox(), Production() }, null);

            var pattern = Assert.Single(graph.Edges, o => o.Kind == ZoneMapEdgeKind.AllowedCaller && o.From == "caller:api:*");
            Assert.Equal("zone:sandbox", pattern.To);
            Assert.Equal(ZoneMapNodeKind.Caller, graph.GetNode("caller:api:*").Kind);

            // Another Zone is linked from the Zone itself
            Assert.Single(graph.Edges, o => o.Kind == ZoneMapEdgeKind.AllowedCaller && o.From == "zone:production" && o.To == "zone:sandbox");
        }

        [Fact]
        public void The_Router_Is_Shown()
        {
            var graph = ZoneMapGraph.Build(new[] { Sandbox() }, null);

            Assert.Equal(ZoneMapNodeKind.Router, graph.GetNode("router:test-data").Kind);
            Assert.Single(graph.Edges, o => o.Kind == ZoneMapEdgeKind.Router && o.From == "zone:sandbox" && o.To == "router:test-data");
        }

        [Fact]
        public void Denied_Calls_Are_Drawn_By_Result()
        {
            var activity = new[]
            {
                new TrakHoundZoneActivity { ZoneId = "sandbox", Direction = TrakHoundZoneDirection.Outbound, CallerId = "function:ai-report", ResourceId = "api:scheduled-tasks:/status", Permission = "write", Enforced = false, Count = 4 },
                new TrakHoundZoneActivity { ZoneId = "sandbox", Direction = TrakHoundZoneDirection.Outbound, CallerId = "worker:dev-1", ResourceId = "api:scheduled-tasks:/jobs", Permission = "read", Enforced = false, Count = 1 },
                new TrakHoundZoneActivity { ZoneId = "production", Direction = TrakHoundZoneDirection.Inbound, CallerId = "function:other", ResourceId = "service:database:tables:executequery", Permission = "read", Enforced = true, Count = 2 },
                new TrakHoundZoneActivity { ZoneId = "removed-zone", Direction = TrakHoundZoneDirection.Outbound, CallerId = "function:x", ResourceId = "api:x:/", Permission = "read", Count = 1 }
            };

            var graph = ZoneMapGraph.Build(new[] { Sandbox(), Production() }, null, activity);

            var wouldDeny = Assert.Single(graph.Edges, o => o.Kind == ZoneMapEdgeKind.WouldDeny);
            Assert.Equal("zone:sandbox", wouldDeny.From);
            Assert.Equal("api:scheduled-tasks", wouldDeny.To);
            Assert.Equal(5, wouldDeny.Count);
            Assert.Equal(new[] { "/status · write ×4", "/jobs · read ×1" }, wouldDeny.Details);

            var denied = Assert.Single(graph.Edges, o => o.Kind == ZoneMapEdgeKind.Denied);
            Assert.Equal("caller:function:other", denied.From);
            Assert.Equal("zone:production", denied.To);

            // Activity for a Zone that no longer exists is not drawn
            Assert.Null(graph.GetNode("api:x"));
        }

        [Fact]
        public void An_Inbound_Call_From_A_Member_Of_Another_Zone_Comes_From_That_Zone()
        {
            var activity = new[]
            {
                new TrakHoundZoneActivity { ZoneId = "production", Direction = TrakHoundZoneDirection.Inbound, CallerId = "function:ai-report", ResourceId = "service:database:blobs:read", Permission = "read", Enforced = true, Count = 1 }
            };

            var graph = ZoneMapGraph.Build(new[] { Sandbox(), Production() }, null, activity);

            Assert.Single(graph.Edges, o => o.Kind == ZoneMapEdgeKind.Denied && o.From == "zone:sandbox" && o.To == "zone:production");
        }
    }
}
