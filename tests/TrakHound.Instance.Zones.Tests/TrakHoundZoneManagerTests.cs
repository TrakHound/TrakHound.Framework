// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Zones;

namespace TrakHound.Instance.Zones.Tests
{
    public class TrakHoundZoneManagerTests
    {
        private const string Read = TrakHoundZoneResources.ReadPermission;
        private const string Write = TrakHoundZoneResources.WritePermission;

        private static readonly TrakHoundApiRequestSource SandboxFunction = new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Function, "ai-function");
        private static readonly TrakHoundApiRequestSource SandboxWorker = new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Worker, "ai-worker");
        private static readonly TrakHoundApiRequestSource ProductionApi = new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Api, "production-api");


        private static (TrakHoundZoneManager Manager, InMemoryConfigurationProfile Profile) Create(params TrakHoundZoneConfiguration[] zones)
        {
            var profile = new InMemoryConfigurationProfile();
            foreach (var zone in zones) profile.Add(zone);
            return (new TrakHoundZoneManager(profile, null), profile);
        }

        private static TrakHoundZoneConfiguration Sandbox(TrakHoundZoneMode mode = TrakHoundZoneMode.Enforce, params TrakHoundZoneRule[] rules)
        {
            return new TrakHoundZoneConfiguration
            {
                Id = "sandbox",
                Mode = mode,
                Members = new[] { "function:ai-function", "worker:ai-*" },
                Rules = rules
            };
        }

        private static TrakHoundZoneRule Rule(string resource, params string[] permissions) => new TrakHoundZoneRule { Resource = resource, Permissions = permissions };


        [Fact]
        public void Callers_Not_In_A_Zone_Are_Not_Restricted()
        {
            var (manager, _) = Create(Sandbox());

            var decision = manager.Authorize(ProductionApi, "api:entities", "api:entities:/objects", Write);

            Assert.True(decision.IsAllowed);
            Assert.Empty(manager.GetActivity());
        }

        [Fact]
        public void Instance_And_Http_Callers_Are_Not_Restricted()
        {
            var (manager, _) = Create(Sandbox());
            var target = new TrakHoundZoneConfiguration { Id = "locked", Members = new[] { "api:entities" }, AllowedCallers = new[] { "api:other" } };
            manager.Save(target);

            Assert.True(manager.Authorize(TrakHoundApiRequestSource.Instance, "api:entities", "api:entities:/objects", Write).IsAllowed);
            Assert.True(manager.Authorize(new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Http, "127.0.0.1"), "api:entities", "api:entities:/objects", Write).IsAllowed);
            Assert.True(manager.Authorize(null, "api:entities", "api:entities:/objects", Write).IsAllowed);
        }

        [Fact]
        public void Members_Are_Denied_Anything_Without_A_Rule()
        {
            var (manager, _) = Create(Sandbox());

            var decision = manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Read);

            Assert.False(decision.IsAllowed);
            Assert.Equal("sandbox", decision.ZoneId);

            var activity = Assert.Single(manager.GetActivity());
            Assert.True(activity.Enforced);
            Assert.Equal(TrakHoundZoneDirection.Outbound, activity.Direction);
            Assert.Equal("function:ai-function", activity.CallerId);
            Assert.Equal("api:entities:/objects", activity.ResourceId);
        }

        [Fact]
        public void Member_Patterns_Match()
        {
            var (manager, _) = Create(Sandbox());

            Assert.Equal("sandbox", manager.GetMemberZone("worker:ai-worker")?.Id);
            Assert.Null(manager.GetMemberZone("worker:production"));
            Assert.False(manager.Authorize(SandboxWorker, null, "volume:shared:/data.json", Read).IsAllowed);
        }

        [Fact]
        public void Rules_Allow_Only_Their_Permissions()
        {
            var (manager, _) = Create(Sandbox(TrakHoundZoneMode.Enforce, Rule("api:entities:*", Read)));

            Assert.True(manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects/query", Read).IsAllowed);
            Assert.False(manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects/delete", Write).IsAllowed);
            Assert.False(manager.Authorize(SandboxFunction, "api:other", "api:other:/objects", Read).IsAllowed);
        }

        [Fact]
        public void A_Rule_Without_Permissions_Allows_Any_Permission()
        {
            var (manager, _) = Create(Sandbox(TrakHoundZoneMode.Enforce, Rule("service:test-db:*")));

            Assert.True(manager.Authorize(SandboxFunction, "service:test-db", "service:test-db:key-values:publish", Write).IsAllowed);
            Assert.True(manager.Authorize(SandboxFunction, "service:test-db", "service:test-db:key-values:read", Read).IsAllowed);
        }

        [Fact]
        public void A_Rule_Can_Limit_A_Module_To_Reading_Time_Series()
        {
            var (manager, _) = Create(Sandbox(TrakHoundZoneMode.Enforce, Rule("service:historian:time-series:*", Read)));

            Assert.True(manager.Authorize(SandboxFunction, "service:historian", "service:historian:time-series:read", Read).IsAllowed);
            Assert.False(manager.Authorize(SandboxFunction, "service:historian", "service:historian:time-series:write", Write).IsAllowed);
            Assert.False(manager.Authorize(SandboxFunction, "service:other", "service:other:time-series:read", Read).IsAllowed);
        }

        [Fact]
        public void Matching_Ignores_Case()
        {
            var (manager, _) = Create(Sandbox(TrakHoundZoneMode.Enforce, Rule("API:Entities:*", "READ")));

            Assert.True(manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Read).IsAllowed);
        }

        [Fact]
        public void Audit_Mode_Allows_And_Records()
        {
            var (manager, _) = Create(Sandbox(TrakHoundZoneMode.Audit));

            var first = manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Write);
            var second = manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Write);

            Assert.True(first.IsAllowed);
            Assert.True(first.IsAudited);
            Assert.True(second.IsAllowed);

            var activity = Assert.Single(manager.GetActivity());
            Assert.False(activity.Enforced);
            Assert.Equal(2, activity.Count);
        }

        [Fact]
        public void Members_Can_Call_Each_Other()
        {
            var (manager, _) = Create(Sandbox());

            Assert.True(manager.Authorize(SandboxWorker, "function:ai-function", "function:ai-function", TrakHoundZoneResources.RunPermission).IsAllowed);
        }

        [Fact]
        public void Members_Need_A_Rule_To_Call_Each_Other_When_Same_Zone_Is_Off()
        {
            var zone = Sandbox();
            zone.AllowSameZone = false;
            var (manager, _) = Create(zone);

            Assert.False(manager.Authorize(SandboxWorker, "function:ai-function", "function:ai-function", TrakHoundZoneResources.RunPermission).IsAllowed);
        }

        [Fact]
        public void A_Module_Can_Always_Call_Itself()
        {
            var zone = Sandbox();
            zone.AllowSameZone = false;
            var (manager, _) = Create(zone);

            Assert.True(manager.Authorize(SandboxFunction, "function:ai-function", "function:ai-function", TrakHoundZoneResources.RunPermission).IsAllowed);
        }

        [Fact]
        public void Allowed_Callers_Limit_Who_Can_Call_The_Members()
        {
            var production = new TrakHoundZoneConfiguration
            {
                Id = "production",
                Members = new[] { "service:production-db" },
                AllowedCallers = new[] { "api:production-api", "zone:trusted" }
            };
            var trusted = new TrakHoundZoneConfiguration
            {
                Id = "trusted",
                Members = new[] { "worker:reporting" },
                Rules = new[] { Rule("*") }
            };
            var (manager, _) = Create(production, trusted);

            Assert.True(manager.Authorize(ProductionApi, "service:production-db", "service:production-db:tables:executequery", Read).IsAllowed);
            Assert.True(manager.Authorize(new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Worker, "reporting"), "service:production-db", "service:production-db:tables:executequery", Read).IsAllowed);

            var decision = manager.Authorize(new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Function, "unzoned"), "service:production-db", "service:production-db:tables:executequery", Read);
            Assert.False(decision.IsAllowed);
            Assert.Equal("production", decision.ZoneId);
            Assert.Equal(TrakHoundZoneDirection.Inbound, Assert.Single(manager.GetActivity()).Direction);
        }

        [Fact]
        public void Granting_Outbound_Activity_Adds_A_Rule()
        {
            var (manager, profile) = Create(Sandbox());

            manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Read);
            var activity = Assert.Single(manager.GetActivity());

            Assert.True(manager.Grant(activity));

            var rule = Assert.Single(manager.GetZone("sandbox").Rules);
            Assert.Equal("api:entities:/objects", rule.Resource);
            Assert.Equal(new[] { Read }, rule.Permissions);
            Assert.Empty(manager.GetActivity());
            Assert.Equal(1, profile.SaveCount);

            Assert.True(manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Read).IsAllowed);
            Assert.False(manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Write).IsAllowed);
        }

        [Fact]
        public void Granting_Clears_Other_Activity_The_Rule_Now_Allows()
        {
            var (manager, _) = Create(Sandbox());

            manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Read);
            manager.Authorize(SandboxWorker, "api:entities", "api:entities:/objects", Read);
            manager.Authorize(SandboxWorker, "api:entities", "api:entities:/objects", Write);
            Assert.Equal(3, manager.GetActivity().Count());

            var functionActivity = manager.GetActivity().First(o => o.CallerId == "function:ai-function");
            manager.Grant(functionActivity, "api:entities:*");

            // The Worker's read is now allowed by the Rule, but not its write
            var remaining = Assert.Single(manager.GetActivity());
            Assert.Equal("worker:ai-worker", remaining.CallerId);
            Assert.Equal(Write, remaining.Permission);
        }

        [Fact]
        public void Granting_With_A_Pattern_Covers_The_Pattern()
        {
            var (manager, _) = Create(Sandbox());

            manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Read);
            manager.Grant(Assert.Single(manager.GetActivity()), "api:entities:*");

            Assert.True(manager.Authorize(SandboxFunction, "api:entities", "api:entities:/sources", Read).IsAllowed);
        }

        [Fact]
        public void Granting_Inbound_Activity_Adds_An_Allowed_Caller()
        {
            var production = new TrakHoundZoneConfiguration { Id = "production", Members = new[] { "api:entities" }, AllowedCallers = new[] { "api:other" } };
            var (manager, _) = Create(production);

            manager.Authorize(ProductionApi, "api:entities", "api:entities:/objects", Read);
            Assert.True(manager.Grant(Assert.Single(manager.GetActivity())));

            Assert.Contains("api:production-api", manager.GetZone("production").AllowedCallers);
            Assert.True(manager.Authorize(ProductionApi, "api:entities", "api:entities:/objects", Read).IsAllowed);
        }

        [Fact]
        public void Granting_Does_Not_Change_The_Loaded_Zone_Until_Saved()
        {
            var (manager, _) = Create(Sandbox());
            var before = manager.GetZone("sandbox");

            manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Read);
            manager.Grant(Assert.Single(manager.GetActivity()));

            Assert.Empty(before.Rules);
            Assert.NotSame(before, manager.GetZone("sandbox"));
        }

        [Fact]
        public void Zone_Changes_Apply_Immediately()
        {
            var (manager, profile) = Create(Sandbox());
            Assert.False(manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Read).IsAllowed);

            profile.Add(Sandbox(TrakHoundZoneMode.Enforce, Rule("api:*", Read)));
            Assert.True(manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Read).IsAllowed);

            profile.Remove(TrakHoundZoneConfiguration.ConfigurationCategory, "sandbox");
            Assert.Null(manager.GetMemberZone("function:ai-function"));
            Assert.True(manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Write).IsAllowed);
        }

        [Fact]
        public void A_Member_In_Two_Zones_Belongs_To_The_First_By_Id()
        {
            var a = new TrakHoundZoneConfiguration { Id = "a-zone", Members = new[] { "function:ai-function" }, Rules = new[] { Rule("*") } };
            var (manager, _) = Create(Sandbox(), a);

            Assert.Equal("a-zone", manager.GetMemberZone("function:ai-function").Id);
            Assert.Equal(2, manager.GetMemberZones("function:ai-function").Count());
        }

        [Fact]
        public void The_Zone_Router_Is_Used_For_Members()
        {
            var zone = Sandbox();
            zone.RouterId = "test-data";
            var (manager, _) = Create(zone);

            Assert.Equal("test-data", manager.GetRouterId(SandboxFunction));
            Assert.Null(manager.GetRouterId(ProductionApi));
        }

        [Fact]
        public void Evaluate_Does_Not_Record_Activity()
        {
            var (manager, _) = Create(Sandbox());

            var result = manager.Evaluate("function:ai-function", "api:entities", "api:entities:/objects", Read);

            Assert.False(result.Decision.IsAllowed);
            Assert.Equal(TrakHoundZoneDirection.Outbound, result.Direction);
            Assert.Empty(manager.GetActivity());
        }

        [Fact]
        public void Moving_A_Module_Into_A_Zone_Adds_It()
        {
            var (manager, _) = Create(Sandbox());

            Assert.True(manager.MoveMember("api:reports", null, "sandbox"));

            Assert.Contains("api:reports", manager.GetZone("sandbox").Members);
            Assert.Equal("sandbox", manager.GetMemberZone("api:reports").Id);
        }

        [Fact]
        public void Moving_A_Member_Between_Zones_Removes_It_From_The_First()
        {
            var other = new TrakHoundZoneConfiguration { Id = "other", Members = new List<string>() };
            var (manager, _) = Create(Sandbox(), other);

            Assert.True(manager.MoveMember("function:ai-function", "sandbox", "other"));

            Assert.DoesNotContain("function:ai-function", manager.GetZone("sandbox").Members);
            Assert.Contains("function:ai-function", manager.GetZone("other").Members);
        }

        [Fact]
        public void Adding_A_Module_Listed_In_Another_Zone_Moves_It()
        {
            var other = new TrakHoundZoneConfiguration { Id = "other", Members = new List<string>() };
            var (manager, _) = Create(Sandbox(), other);

            manager.MoveMember("function:ai-function", null, "other");

            Assert.Single(manager.GetMemberZones("function:ai-function"));
            Assert.Equal("other", manager.GetMemberZone("function:ai-function").Id);
        }

        [Fact]
        public void Removing_A_Member_Only_Changes_Its_Zone()
        {
            var other = new TrakHoundZoneConfiguration { Id = "other", Members = new[] { "function:ai-function" } };
            var (manager, _) = Create(Sandbox(), other);

            Assert.True(manager.MoveMember("function:ai-function", "sandbox", null));

            Assert.DoesNotContain("function:ai-function", manager.GetZone("sandbox").Members);
            Assert.Contains("function:ai-function", manager.GetZone("other").Members);
        }

        [Fact]
        public void Moving_To_The_Same_Zone_Or_A_Missing_Zone_Changes_Nothing()
        {
            var (manager, profile) = Create(Sandbox());

            Assert.False(manager.MoveMember("function:ai-function", "sandbox", "sandbox"));
            Assert.False(manager.MoveMember("function:ai-function", "sandbox", "missing"));
            Assert.False(manager.MoveMember("api:not-a-member", "sandbox", null));
            Assert.Equal(0, profile.SaveCount);
            Assert.Contains("function:ai-function", manager.GetZone("sandbox").Members);
        }

        [Fact]
        public void Removing_A_Zone_Clears_Its_Activity()
        {
            var (manager, _) = Create(Sandbox());
            manager.Authorize(SandboxFunction, "api:entities", "api:entities:/objects", Read);

            manager.Remove("sandbox");

            Assert.Empty(manager.GetActivity());
            Assert.Empty(manager.GetZones());
        }
    }
}
