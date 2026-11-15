// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Groups;
using TrakHound.Workers;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// The Router a Module's Client uses : its own RouterId, else its Group's RouterId, else the Default Router (null)
    /// </summary>
    public class GroupsTests
    {
        private static TrakHoundConfigurationProfile CreateProfile(params TrakHoundGroupConfiguration[] groups)
        {
            var profile = new TrakHoundConfigurationProfile("test");
            foreach (var group in groups) profile.Add(group);
            return profile;
        }


        [Fact]
        public void ModuleRouter_IsUsed_EvenWithAGroupRouter()
        {
            var profile = CreateProfile(new TrakHoundGroupConfiguration { Id = "line-1", RouterId = "group-router" });

            Assert.Equal("module-router", TrakHoundGroups.GetRouterId(profile, "module-router", "line-1"));
        }

        [Fact]
        public void GroupRouter_IsUsed_WhenTheModuleHasNoRouter()
        {
            var profile = CreateProfile(new TrakHoundGroupConfiguration { Id = "line-1", RouterId = "group-router" });

            Assert.Equal("group-router", TrakHoundGroups.GetRouterId(profile, null, "line-1"));
            Assert.Equal("group-router", TrakHoundGroups.GetRouterId(profile, " ", "line-1"));
        }

        [Fact]
        public void DefaultRouter_IsUsed_WhenTheGroupHasNoRouter()
        {
            var profile = CreateProfile(new TrakHoundGroupConfiguration { Id = "line-1" });

            Assert.Null(TrakHoundGroups.GetRouterId(profile, null, "line-1"));
        }

        [Fact]
        public void DefaultRouter_IsUsed_WhenTheGroupIsMissing()
        {
            var profile = CreateProfile();

            Assert.Null(TrakHoundGroups.GetRouterId(profile, null, "missing"));
            Assert.Null(TrakHoundGroups.GetRouterId(profile, null, null));
        }

        [Fact]
        public void GroupRouter_FollowsChangesToTheGroup()
        {
            var profile = CreateProfile(new TrakHoundGroupConfiguration { Id = "line-1", RouterId = "first" });
            profile.Add(new TrakHoundGroupConfiguration { Id = "line-1", RouterId = "second" });

            Assert.Equal("second", TrakHoundGroups.GetRouterId(profile, null, "line-1"));
        }

        [Fact]
        public void GroupId_ChangesTheConfigurationHash()
        {
            var configuration = new TrakHoundWorkerConfiguration { Id = "worker", PackageId = "Test.Package" };
            var hash = configuration.Hash;

            configuration.GroupId = "line-1";

            Assert.NotEqual(hash, configuration.Hash);
            Assert.Equal("line-1", new TrakHoundWorkerConfiguration(configuration).GroupId);
        }
    }
}
