// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Workers;
using TrakHound.Zones;

namespace TrakHound.Instance.Zones.Tests
{
    /// <summary>
    /// "group:{groupId}" in a Zone's Members or AllowedCallers stands for every Module in the Group
    /// </summary>
    public class ZoneGroupTests
    {
        private const string Write = TrakHoundZoneResources.WritePermission;

        private static readonly TrakHoundApiRequestSource GroupedWorker = new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Worker, "collector");
        private static readonly TrakHoundApiRequestSource OtherWorker = new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Worker, "other");


        private static (TrakHoundZoneManager Manager, InMemoryConfigurationProfile Profile) Create(params TrakHoundZoneConfiguration[] zones)
        {
            var profile = new InMemoryConfigurationProfile();
            profile.Add(Worker("collector", "line-1"));
            profile.Add(Worker("other", null));
            foreach (var zone in zones) profile.Add(zone);
            return (new TrakHoundZoneManager(profile, null), profile);
        }

        private static TrakHoundWorkerConfiguration Worker(string id, string groupId)
        {
            return new TrakHoundWorkerConfiguration { Id = id, PackageId = "Test.Package", PackageVersion = "1.0.0", GroupId = groupId };
        }


        [Fact]
        public void Group_Member_Is_Restricted()
        {
            var (manager, _) = Create(new TrakHoundZoneConfiguration { Id = "line", Members = new[] { "group:line-1" } });

            Assert.Equal("line", manager.GetMemberZone("worker:collector")?.Id);
            Assert.False(manager.Authorize(GroupedWorker, "api:entities", "api:entities:/objects", Write).IsAllowed);
            Assert.True(manager.Authorize(OtherWorker, "api:entities", "api:entities:/objects", Write).IsAllowed);
        }

        [Fact]
        public void Zone_That_Lists_The_Module_Comes_Before_The_Group_Zone()
        {
            // "a-group" sorts first, but the Zone that lists the Module itself wins
            var (manager, _) = Create(
                new TrakHoundZoneConfiguration { Id = "a-group", Members = new[] { "group:line-1" } },
                new TrakHoundZoneConfiguration { Id = "b-own", Members = new[] { "worker:collector" } });

            Assert.Equal("b-own", manager.GetMemberZone("worker:collector")?.Id);
            Assert.Equal(new[] { "a-group", "b-own" }, manager.GetMemberZones("worker:collector").Select(o => o.Id));
        }

        [Fact]
        public void Group_Can_Be_An_Allowed_Caller()
        {
            var (manager, _) = Create(new TrakHoundZoneConfiguration { Id = "locked", Members = new[] { "api:entities" }, AllowedCallers = new[] { "group:line-1" } });

            Assert.True(manager.Authorize(GroupedWorker, "api:entities", "api:entities:/objects", Write).IsAllowed);
            Assert.False(manager.Authorize(OtherWorker, "api:entities", "api:entities:/objects", Write).IsAllowed);
        }

        [Fact]
        public void Module_That_Leaves_The_Group_Is_No_Longer_Restricted()
        {
            var (manager, profile) = Create(new TrakHoundZoneConfiguration { Id = "line", Members = new[] { "group:line-1" } });
            var updated = false;
            manager.ZonesUpdated += (s, e) => updated = true;

            profile.Add(Worker("collector", null));

            Assert.True(updated);
            Assert.Null(manager.GetMemberZone("worker:collector"));
            Assert.True(manager.Authorize(GroupedWorker, "api:entities", "api:entities:/objects", Write).IsAllowed);
        }

        [Fact]
        public void Module_That_Joins_The_Group_Is_Restricted()
        {
            var (manager, profile) = Create(new TrakHoundZoneConfiguration { Id = "line", Members = new[] { "group:line-1" } });

            profile.Add(Worker("other", "line-1"));

            Assert.Equal("line", manager.GetMemberZone("worker:other")?.Id);
        }

        [Fact]
        public void Unrelated_Module_Change_Does_Not_Raise_ZonesUpdated()
        {
            var (manager, profile) = Create(new TrakHoundZoneConfiguration { Id = "line", Members = new[] { "group:line-1" } });
            var updated = false;
            manager.ZonesUpdated += (s, e) => updated = true;

            var changed = Worker("collector", "line-1");
            changed.Description = "Changed";
            profile.Add(changed);

            Assert.False(updated);
        }
    }
}
