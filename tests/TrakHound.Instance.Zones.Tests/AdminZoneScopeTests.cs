// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Functions;
using TrakHound.Instance.Security;
using TrakHound.Workers;
using TrakHound.Zones;

namespace TrakHound.Instance.Zones.Tests
{
    /// <summary>
    /// An Admin Role held for certain Zones only applies to the Modules in those Zones, and to the Services and Volumes their Rules allow
    /// </summary>
    public class AdminZoneScopeTests : IDisposable
    {
        private const string Read = TrakHoundAdminSecurity.ReadPermission;
        private const string Write = TrakHoundAdminSecurity.WritePermission;

        private readonly TrakHoundZoneManager _manager;


        public AdminZoneScopeTests()
        {
            var profile = new InMemoryConfigurationProfile();

            // "dev" lists a Function and every Worker named "dev-*", and its Members reach one Service's Key Values and their own Volume
            profile.Add(new TrakHoundZoneConfiguration
            {
                Id = "dev",
                Members = new[] { "function:dev-import", "worker:dev-*", "group:sandbox" },
                Rules = new[]
                {
                    new TrakHoundZoneRule { Resource = "service:redis-dev:key-values:*", Permissions = new[] { "read", "write" } },
                    new TrakHoundZoneRule { Resource = "service:sql:tables:query", Permissions = new[] { "read" } },
                    new TrakHoundZoneRule { Resource = "volume:dev-files:*", Permissions = new[] { "read", "write" } }
                }
            });

            profile.Add(new TrakHoundZoneConfiguration { Id = "line-1", Members = new[] { "worker:line-1-reader", "function:dev-import" } });

            // Modules in the "sandbox" Group, which "dev" lists
            profile.Add(new TrakHoundFunctionConfiguration { Id = "sandbox-report", GroupId = "sandbox" });
            profile.Add(new TrakHoundWorkerConfiguration { Id = "production-reader" });

            _manager = new TrakHoundZoneManager(profile, null);
            TrakHoundAdminScope.ZoneManager = () => _manager;
        }

        public void Dispose()
        {
            TrakHoundAdminScope.ZoneManager = null;
        }

        private static TrakHoundAdminRole Role(string roleId) => TrakHoundAdminSecurity.GetRole(roleId);

        // The Packages Role (Modules, their Configurations, Volumes and Packages), held only for the "dev" Zone
        private static TrakHoundAdminContext DevAdmin() => new TrakHoundAdminContext("dev-admin", new[] { (Role(TrakHoundAdminSecurity.PackagesRoleId), (IEnumerable<string>)new[] { "dev" }) });

        private static TrakHoundAdminTarget Module(string type, string id) => TrakHoundAdminTarget.Module(type, id);


        [Fact]
        public void AScopedRole_AppliesToTheModulesInItsZones()
        {
            var admin = DevAdmin();

            Assert.True(admin.Can(TrakHoundAdminSecurity.Modules, Write, Module("worker", "dev-reader"))); // A pattern
            Assert.True(admin.Can(TrakHoundAdminSecurity.FunctionConfigurations, Write, Module("function", "sandbox-report"))); // Its Group
            Assert.True(admin.Can(TrakHoundAdminSecurity.WorkerConfigurations, Write, Module("worker", "dev-reader")));

            Assert.False(admin.Can(TrakHoundAdminSecurity.Modules, Read, Module("worker", "production-reader")));
            Assert.False(admin.Can(TrakHoundAdminSecurity.Modules, Write, Module("worker", "line-1-reader")));
            Assert.False(admin.Can(TrakHoundAdminSecurity.WorkerConfigurations, Write, Module("worker", "production-reader")));
        }

        [Fact]
        public void AModuleListedInTwoZones_BelongsToTheFirst()
        {
            // "function:dev-import" is listed by "dev" and "line-1" : it belongs to "dev" (the first by ID), as the Zone Manager decides
            Assert.Equal("dev", _manager.GetMemberZone("function:dev-import").Id);

            var lineAdmin = new TrakHoundAdminContext("line-admin", new[] { (Role(TrakHoundAdminSecurity.PackagesRoleId), (IEnumerable<string>)new[] { "line-1" }) });

            Assert.True(DevAdmin().Can(TrakHoundAdminSecurity.Modules, Write, Module("function", "dev-import")));
            Assert.False(lineAdmin.Can(TrakHoundAdminSecurity.Modules, Write, Module("function", "dev-import")));
            Assert.True(lineAdmin.Can(TrakHoundAdminSecurity.Modules, Write, Module("worker", "line-1-reader")));
        }

        [Fact]
        public void AScopedRole_GivesNothingOnAreasThatArentAboutAModule()
        {
            var admin = DevAdmin();

            // The Packages Role has these, but not for an Admin who only holds it for a Zone
            Assert.False(admin.Can(TrakHoundAdminSecurity.Packages, Write));
            Assert.False(admin.CanSome(TrakHoundAdminSecurity.Packages, Read));
            Assert.False(admin.CanSome(TrakHoundAdminSecurity.Instance, Read));
            Assert.False(admin.CanSome(TrakHoundAdminSecurity.Logs, Read));
            Assert.False(admin.CanSome(TrakHoundAdminSecurity.GroupConfigurations, Read));

            // It shows the areas it applies to, and is checked against the Module when something is done
            Assert.True(admin.CanSome(TrakHoundAdminSecurity.Modules, Write));
            Assert.False(admin.Can(TrakHoundAdminSecurity.Modules, Write));
            Assert.False(admin.Can(TrakHoundAdminSecurity.Modules, Write, null));
            Assert.Equal(new[] { "dev" }, admin.GetZones(TrakHoundAdminSecurity.Modules, Read));
        }

        [Fact]
        public void AScopedRole_ReachesTheServicesAndVolumesItsZonesRulesAllow()
        {
            // A Role that views and changes Service data and Volumes, held for the "dev" Zone
            var role = new TrakHoundAdminRole
            {
                Id = "data",
                Grants = new List<TrakHoundAdminGrant> { new TrakHoundAdminGrant(TrakHoundAdminSecurity.Services, Write), new TrakHoundAdminGrant(TrakHoundAdminSecurity.Volumes, Write) }
            };
            var admin = new TrakHoundAdminContext("dev-data", new[] { (role, (IEnumerable<string>)new[] { "dev" }) });

            TrakHoundAdminTarget Call(string resourceId, string permission) => TrakHoundAdminTarget.Call(null, resourceId, permission);

            Assert.True(admin.Can(TrakHoundAdminSecurity.Services, Write, Call("service:redis-dev:key-values:publish", "write")));
            Assert.True(admin.Can(TrakHoundAdminSecurity.Services, Read, Call("service:sql:tables:query", "read")));
            Assert.False(admin.Can(TrakHoundAdminSecurity.Services, Write, Call("service:sql:tables:query", "write"))); // The Rule only allows read
            Assert.False(admin.Can(TrakHoundAdminSecurity.Services, Read, Call("service:redis-production:key-values:get", "read")));

            Assert.True(admin.Can(TrakHoundAdminSecurity.Volumes, Write, Call("volume:dev-files:/a.txt", "write")));
            Assert.False(admin.Can(TrakHoundAdminSecurity.Volumes, Read, Call("volume:production-files:/a.txt", "read")));

            // The Service authorizer used for the Admin Interface's own Service calls
            var authorizer = new TrakHoundAdminServiceAuthorizer(admin);
            Assert.True(authorizer.Authorize(null, "service:redis-dev:key-values:get", "read").IsAllowed);
            Assert.False(authorizer.Authorize(null, "service:redis-production:key-values:get", "read").IsAllowed);

            // Which Services are listed in the Explorer
            Assert.True(TrakHoundAdminScope.CouldReachService(new[] { "dev" }, "redis-dev"));
            Assert.True(TrakHoundAdminScope.CouldReachService(new[] { "dev" }, "sql"));
            Assert.False(TrakHoundAdminScope.CouldReachService(new[] { "dev" }, "redis-production"));
            Assert.False(TrakHoundAdminScope.CouldReachService(new[] { "line-1" }, "redis-dev"));
        }

        [Fact]
        public void AnUnscopedRole_StillAppliesEverywhere()
        {
            var admin = new TrakHoundAdminContext("mixed", new[]
            {
                (Role(TrakHoundAdminSecurity.PackagesRoleId), (IEnumerable<string>)new[] { "dev" }),
                (Role(TrakHoundAdminSecurity.ViewerRoleId), (IEnumerable<string>)null)
            });

            // Views everything (Viewer), and changes only the Modules in "dev" (Packages, for that Zone)
            Assert.True(admin.Can(TrakHoundAdminSecurity.Modules, Read, Module("worker", "production-reader")));
            Assert.False(admin.Can(TrakHoundAdminSecurity.Modules, Write, Module("worker", "production-reader")));
            Assert.True(admin.Can(TrakHoundAdminSecurity.Modules, Write, Module("worker", "dev-reader")));
            Assert.True(admin.Can(TrakHoundAdminSecurity.Packages, Read));
            Assert.False(admin.Can(TrakHoundAdminSecurity.Packages, Write));
            Assert.Empty(admin.GetZones(TrakHoundAdminSecurity.Modules, Read));
        }

        [Fact]
        public void SavingAConfiguration_IsCheckedWithTheGroupItIsSavedWith()
        {
            var admin = DevAdmin();

            // A new Function in the "sandbox" Group lands in "dev". Without the Group (or in another one) it would be outside the Admin's Zones
            Assert.True(admin.Can(TrakHoundAdminSecurity.FunctionConfigurations, Write, TrakHoundAdminTarget.Module("function", "new-report", "sandbox")));
            Assert.False(admin.Can(TrakHoundAdminSecurity.FunctionConfigurations, Write, TrakHoundAdminTarget.Module("function", "new-report", null)));
            Assert.False(admin.Can(TrakHoundAdminSecurity.FunctionConfigurations, Write, TrakHoundAdminTarget.Module("function", "new-report", "production")));

            // Taking a Module out of the Group moves it out of the Zone
            Assert.False(admin.Can(TrakHoundAdminSecurity.FunctionConfigurations, Write, TrakHoundAdminTarget.Module("function", "sandbox-report", null)));

            // An ID a Zone lists is in it whatever its Group
            Assert.True(admin.Can(TrakHoundAdminSecurity.WorkerConfigurations, Write, TrakHoundAdminTarget.Module("worker", "dev-new", null)));
        }

        [Fact]
        public void ClientCalls_AreCheckedAgainstTheModuleTheyAreMadeOn()
        {
            using (TrakHoundAdminContext.Begin(DevAdmin()))
            {
                // Starting a Worker, running a Function, and reading a Module's Configuration through the Instance Client
                Assert.Null(Denial("worker:dev-reader", "worker:dev-reader", "run"));
                Assert.Null(Denial("function:dev-import", "function:dev-import", "run"));
                Assert.Null(Denial(null, "manage:configuration:worker:dev-reader", "write"));

                Assert.Equal(TrakHoundAdminSecurity.ZoneDeniedMessage, Denial("worker:production-reader", "worker:production-reader", "run"));
                Assert.Equal(TrakHoundAdminSecurity.ZoneDeniedMessage, Denial(null, "manage:configuration:worker:production-reader", "write"));

                // Not something a Role that is limited to Zones gives at all
                Assert.Equal(TrakHoundAdminSecurity.DeniedMessage, Denial(null, "manage:instance:abc", "write"));
                Assert.Equal(TrakHoundAdminSecurity.DeniedMessage, Denial(null, "manage:configuration:service:redis-dev", "read"));
            }
        }

        private static string Denial(string targetMemberId, string zoneResourceId, string zonePermission)
        {
            var resourceId = TrakHoundAdminSecurity.GetZoneResource(zoneResourceId, zonePermission, out var permission);
            return TrakHoundAdminContext.GetDenial(resourceId, permission, TrakHoundAdminSecurity.GetZoneTarget(targetMemberId, zoneResourceId, zonePermission));
        }

        [Fact]
        public void WithoutAZoneManager_AScopedRoleGivesNothing()
        {
            TrakHoundAdminScope.ZoneManager = null;

            Assert.False(DevAdmin().Can(TrakHoundAdminSecurity.Modules, Read, Module("worker", "dev-reader")));
        }

        [Fact]
        public void AZoneThatDoesntExist_GivesNothing()
        {
            var admin = new TrakHoundAdminContext("typo", new[] { (Role(TrakHoundAdminSecurity.PackagesRoleId), (IEnumerable<string>)new[] { "no-such-zone" }) });

            Assert.False(admin.Can(TrakHoundAdminSecurity.Modules, Read, Module("worker", "dev-reader")));
            Assert.False(admin.Can(TrakHoundAdminSecurity.Modules, Read, Module("worker", "production-reader")));
        }
    }
}
