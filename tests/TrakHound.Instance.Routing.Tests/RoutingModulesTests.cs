// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;
using TrakHound.Groups;
using TrakHound.Instance.Pages.Configuration.Routing;
using TrakHound.Workers;
using TrakHound.Zones;

namespace TrakHound.Instance.Routing.Tests
{
    /// <summary>
    /// The Router each Module's Service calls go through, as the Routing pages show it : the Zone's Router, else the Module's own, else its Group's
    /// </summary>
    public class RoutingModulesTests
    {
        private static TrakHoundConfigurationProfile Profile(params ITrakHoundConfiguration[] configurations)
        {
            var profile = new TrakHoundConfigurationProfile("test");
            foreach (var configuration in configurations) profile.Add(configuration);
            return profile;
        }

        private static TrakHoundWorkerConfiguration Worker(string id, string routerId = null, string groupId = "line-1")
        {
            return new TrakHoundWorkerConfiguration { Id = id, PackageId = "Test.Package", PackageVersion = "*", RouterId = routerId, GroupId = groupId };
        }

        private static RoutingModule Get(TrakHoundConfigurationProfile profile, string id) => RoutingModules.Load(profile).Single(o => o.ConfigurationId == id);


        [Fact]
        public void Module_Without_A_Router_Uses_Its_Group_Router()
        {
            var profile = Profile(new TrakHoundGroupConfiguration { Id = "line-1", RouterId = "line-router" }, Worker("collector"));

            Assert.Equal("line-router", Get(profile, "collector").RouterKey);
        }

        [Fact]
        public void Module_Router_Comes_Before_The_Group_Router()
        {
            var profile = Profile(new TrakHoundGroupConfiguration { Id = "line-1", RouterId = "line-router" }, Worker("collector", "own"));

            Assert.Equal("own", Get(profile, "collector").RouterKey);
        }

        [Fact]
        public void Zone_That_Lists_The_Group_Sets_The_Router()
        {
            var profile = Profile(
                new TrakHoundGroupConfiguration { Id = "line-1", RouterId = "line-router" },
                new TrakHoundZoneConfiguration { Id = "sandbox", Members = new[] { "group:line-1" }, RouterId = "test-data" },
                Worker("collector", "own"));

            var module = Get(profile, "collector");
            Assert.Equal("sandbox", module.ZoneId);
            Assert.Equal("test-data", module.RouterKey);
        }
    }
}
