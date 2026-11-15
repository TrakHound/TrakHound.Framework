// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Security;
using TrakHound.Zones;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// The Admin Roles: what each built in Role allows, and how the code that makes a change checks the signed in Admin
    /// </summary>
    public class AdminSecurityTests
    {
        private const string Read = TrakHoundAdminSecurity.ReadPermission;
        private const string Write = TrakHoundAdminSecurity.WritePermission;

        private static TrakHoundAdminRole Role(string roleId) => TrakHoundAdminSecurity.GetRole(roleId);

        private static TrakHoundAdminContext Context(params string[] roleIds) => new TrakHoundAdminContext("test", roleIds.Select(Role));


        [Fact]
        public void Owner_CanChangeEverything()
        {
            Assert.All(TrakHoundAdminSecurity.Resources, o => Assert.True(TrakHoundAdminSecurity.Owner.Allows(o.Id, Write)));
        }

        [Fact]
        public void Viewer_CanViewEverythingExceptTheAdminAccounts_AndChangeNothing()
        {
            var viewer = Role(TrakHoundAdminSecurity.ViewerRoleId);

            Assert.All(TrakHoundAdminSecurity.Resources, o => Assert.False(viewer.Allows(o.Id, Write)));
            Assert.All(TrakHoundAdminSecurity.Resources.Where(o => o.Id != TrakHoundAdminSecurity.Admins), o => Assert.True(viewer.Allows(o.Id, Read)));
            Assert.False(viewer.Allows(TrakHoundAdminSecurity.Admins, Read));
        }

        [Fact]
        public void Packages_InstallsAndConfiguresApplications_ButNotServicesRoutingOrSecurity()
        {
            var role = Role(TrakHoundAdminSecurity.PackagesRoleId);

            Assert.True(role.Allows(TrakHoundAdminSecurity.Packages, Write));
            Assert.True(role.Allows(TrakHoundAdminSecurity.AppConfigurations, Write));
            Assert.True(role.Allows(TrakHoundAdminSecurity.WorkerConfigurations, Write));
            Assert.True(role.Allows(TrakHoundAdminSecurity.Modules, Write));

            foreach (var resourceId in new[]
            {
                TrakHoundAdminSecurity.Services, TrakHoundAdminSecurity.ServiceConfigurations, TrakHoundAdminSecurity.RouterConfigurations,
                TrakHoundAdminSecurity.Settings, TrakHoundAdminSecurity.Variables, TrakHoundAdminSecurity.Deployments,
                TrakHoundAdminSecurity.IdentityProviders, TrakHoundAdminSecurity.SecurityProfiles, TrakHoundAdminSecurity.Users,
                TrakHoundAdminSecurity.ApiKeys, TrakHoundAdminSecurity.Zones, TrakHoundAdminSecurity.Licenses, TrakHoundAdminSecurity.Admins
            })
            {
                Assert.False(role.Allows(resourceId, Read), resourceId);
            }
        }

        [Fact]
        public void SecurityOperator_ManagesUsersAndApiKeys_ButOnlyViewsProfilesAndProviders()
        {
            var role = Role(TrakHoundAdminSecurity.SecurityOperatorRoleId);

            Assert.True(role.Allows(TrakHoundAdminSecurity.Users, Write));
            Assert.True(role.Allows(TrakHoundAdminSecurity.ApiKeys, Write));

            Assert.True(role.Allows(TrakHoundAdminSecurity.SecurityProfiles, Read));
            Assert.False(role.Allows(TrakHoundAdminSecurity.SecurityProfiles, Write));
            Assert.True(role.Allows(TrakHoundAdminSecurity.IdentityProviders, Read));
            Assert.False(role.Allows(TrakHoundAdminSecurity.IdentityProviders, Write));

            Assert.False(role.Allows(TrakHoundAdminSecurity.Admins, Read));
            Assert.False(role.Allows(TrakHoundAdminSecurity.Packages, Read));
        }

        [Fact]
        public void SeveralRoles_AllowWhatAnyOfThemAllows()
        {
            var context = Context(TrakHoundAdminSecurity.PackagesRoleId, TrakHoundAdminSecurity.SecurityOperatorRoleId);

            Assert.True(context.Can(TrakHoundAdminSecurity.Packages, Write));
            Assert.True(context.Can(TrakHoundAdminSecurity.Users, Write));
            Assert.False(context.Can(TrakHoundAdminSecurity.Services, Read));
        }

        [Fact]
        public void EveryRoleGrant_NamesAResource()
        {
            foreach (var grant in TrakHoundAdminSecurity.Roles.SelectMany(o => o.Grants).Where(o => o.Resource != TrakHoundZoneResources.Wildcard))
            {
                Assert.NotNull(TrakHoundAdminSecurity.GetResource(grant.Resource));
            }
        }

        [Theory]
        [InlineData("app", TrakHoundAdminSecurity.AppConfigurations)]
        [InlineData("service", TrakHoundAdminSecurity.ServiceConfigurations)]
        [InlineData("router", TrakHoundAdminSecurity.RouterConfigurations)]
        [InlineData("group", TrakHoundAdminSecurity.GroupConfigurations)]
        [InlineData("identity", TrakHoundAdminSecurity.IdentityProviders)]
        [InlineData("zone", TrakHoundAdminSecurity.Zones)]
        public void ConfigurationCategory_HasAResource(string category, string resourceId)
        {
            Assert.Equal(resourceId, TrakHoundAdminSecurity.GetConfigurationResource(category));
            Assert.NotNull(TrakHoundAdminSecurity.GetResource(resourceId));
        }

        [Theory]
        [InlineData("volume:files:/a.txt", TrakHoundZoneResources.ReadPermission, TrakHoundAdminSecurity.Volumes, Read)]
        [InlineData("volume:files:/a.txt", TrakHoundZoneResources.WritePermission, TrakHoundAdminSecurity.Volumes, Write)]
        [InlineData("function:import", TrakHoundZoneResources.RunPermission, TrakHoundAdminSecurity.Modules, Write)]
        [InlineData("worker:reader", TrakHoundZoneResources.RunPermission, TrakHoundAdminSecurity.Modules, Write)]
        [InlineData("manage:api:entities", TrakHoundZoneResources.WritePermission, TrakHoundAdminSecurity.Modules, Write)]
        [InlineData("manage:instance:abc", TrakHoundZoneResources.WritePermission, TrakHoundAdminSecurity.Instance, Write)]
        [InlineData("manage:configuration:service:redis", TrakHoundZoneResources.ReadPermission, TrakHoundAdminSecurity.ServiceConfigurations, Read)]
        [InlineData("manage:configuration:router", TrakHoundZoneResources.WritePermission, TrakHoundAdminSecurity.RouterConfigurations, Write)]
        [InlineData("service:redis:key-values:publish", TrakHoundZoneResources.WritePermission, TrakHoundAdminSecurity.Services, Write)]
        public void ClientCalls_MapToAdminResources(string zoneResourceId, string zonePermission, string resourceId, string permission)
        {
            Assert.Equal(resourceId, TrakHoundAdminSecurity.GetZoneResource(zoneResourceId, zonePermission, out var adminPermission));
            Assert.Equal(permission, adminPermission);
        }

        [Fact]
        public void ServiceOperations_AreCheckedAgainstTheAdminsRoles()
        {
            var viewer = new TrakHoundAdminServiceAuthorizer(Context(TrakHoundAdminSecurity.ViewerRoleId));
            Assert.True(viewer.Authorize(null, "service:redis:key-values:get", "read").IsAllowed);
            Assert.False(viewer.Authorize(null, "service:redis:key-values:publish", "write").IsAllowed);

            var packages = new TrakHoundAdminServiceAuthorizer(Context(TrakHoundAdminSecurity.PackagesRoleId));
            Assert.False(packages.Authorize(null, "service:redis:key-values:get", "read").IsAllowed);

            var owner = new TrakHoundAdminServiceAuthorizer(Context(TrakHoundAdminSecurity.OwnerRoleId));
            Assert.True(owner.Authorize(null, "service:redis:key-values:publish", "write").IsAllowed);
        }

        [Fact]
        public void WithoutAnAdmin_CallsArentGoverned()
        {
            // ex. startup, a Module, or a System API Request
            Assert.Null(TrakHoundAdminContext.Current);
            Assert.True(TrakHoundAdminContext.IsAllowed(TrakHoundAdminSecurity.Admins, Write));
            TrakHoundAdminContext.DemandWrite(TrakHoundAdminSecurity.Admins);
        }

        [Fact]
        public void Demand_ThrowsWhenTheAdminLacksThePermission()
        {
            using (TrakHoundAdminContext.Begin(Context(TrakHoundAdminSecurity.ViewerRoleId)))
            {
                TrakHoundAdminContext.Demand(TrakHoundAdminSecurity.Packages, Read);

                var exception = Assert.Throws<TrakHoundAdminAccessException>(() => TrakHoundAdminContext.DemandWrite(TrakHoundAdminSecurity.Packages));
                Assert.Equal(TrakHoundAdminSecurity.Packages, exception.ResourceId);
                Assert.Equal(TrakHoundAdminSecurity.DeniedMessage, TrakHoundAdminContext.GetDenial(TrakHoundAdminSecurity.Packages, Write));
            }

            Assert.Null(TrakHoundAdminContext.Current);
        }

        [Fact]
        public void ASessionThatIsNoLongerValid_AllowsNothing()
        {
            using (TrakHoundAdminContext.Begin(new TrakHoundAdminContext((TrakHoundAdminAccount)null)))
            {
                Assert.All(TrakHoundAdminSecurity.Resources, o => Assert.False(TrakHoundAdminContext.IsAllowed(o.Id, Read)));
            }
        }

        [Fact]
        public void ADisabledAccount_AllowsNothing()
        {
            var account = new TrakHoundAdminAccount
            {
                Id = "a",
                Username = "owner",
                Enabled = false,
                Assignments = new List<TrakHoundAdminRoleAssignment> { new TrakHoundAdminRoleAssignment { RoleId = TrakHoundAdminSecurity.OwnerRoleId } }
            };

            Assert.Empty(TrakHoundAdminContext.GetRoles(account));
        }

        [Theory]
        [InlineData(TrakHoundAdminSecurity.OwnerRoleId, "_admin/explorer")]
        [InlineData(TrakHoundAdminSecurity.PackagesRoleId, "_admin/explorer")]
        [InlineData(TrakHoundAdminSecurity.SecurityOperatorRoleId, "_admin/security")]
        public void HomePage_IsTheFirstAreaTheAdminCanView(string roleId, string path)
        {
            Assert.Equal(path, TrakHoundAdminSecurity.GetHomePath(new[] { Role(roleId) }));
        }
    }
}
