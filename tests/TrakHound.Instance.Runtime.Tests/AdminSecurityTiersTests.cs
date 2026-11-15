// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Security;
using TrakHound.Security;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// An Admin who manages Users and API Keys can't hand out (or take over) access to the System API or Services: giving a Role that a
    /// Security Profile gives that access to, or changing a User or Key that has one, needs the Permission to change those Profiles
    /// </summary>
    public class AdminSecurityTiersTests : IDisposable
    {
        private const string InstanceRole = "instance-admins";
        private const string AppRole = "operators";

        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"trakhound-admin-tiers-tests-{Guid.NewGuid()}");
        private readonly TrakHoundUserStore _users;
        private readonly TrakHoundApiKeyStore _keys;


        public AdminSecurityTiersTests()
        {
            // The same check the Security Manager gives the stores : "instance-admins" is given System API access by a Profile
            static bool ReachesInstance(IEnumerable<string> roles) => roles != null && roles.Contains(InstanceRole);

            _users = new TrakHoundUserStore(Path.Combine(_directory, "users")) { ReachesInstance = ReachesInstance };
            _keys = new TrakHoundApiKeyStore(Path.Combine(_directory, "api-keys")) { ReachesInstance = ReachesInstance };
        }

        public void Dispose()
        {
            try { Directory.Delete(_directory, true); } catch { }
        }

        private static IDisposable SignIn(string roleId) => TrakHoundAdminContext.Begin(new TrakHoundAdminContext("test", new[] { TrakHoundAdminSecurity.GetRole(roleId) }));

        private TrakHoundUser CreateUser(string username, params string[] roles)
        {
            Assert.Null(_users.Create(username, null, roles, "secret-123"));
            return _users.GetUserByUsername(username);
        }


        [Theory]
        [InlineData("*", true)]
        [InlineData("system:logs", true)]
        [InlineData("system:*", true)]
        [InlineData("SYSTEM:logs", true)]
        [InlineData("s*", true)] // Matches "system:..." and "service:..."
        [InlineData("sys*", true)]
        [InlineData("service:redis:key-values:get", true)]
        [InlineData("service:redis:*", true)]
        [InlineData("System.Api.Information", true)] // Saved before the System API Resources were added
        [InlineData("api:entities:*", false)]
        [InlineData("a*", false)]
        [InlineData("app:dashboard:/", false)]
        [InlineData("systems-app:*", false)]
        [InlineData("", false)]
        [InlineData(null, false)]
        public void ProfileResources_ThatReachTheSystemApiOrServices(string resourcePattern, bool expected)
        {
            Assert.Equal(expected, TrakHoundAdminSecurity.IsInstanceResource(resourcePattern));
        }

        private static TrakHoundIdentityAssignment Assignment(string resourceId, params string[] roles)
        {
            return new TrakHoundIdentityAssignment { Id = Guid.NewGuid().ToString(), ResourceId = resourceId, Roles = roles, Permissions = new[] { "*" } };
        }

        private static TrakHoundSecurityProfile Profile(string providerId, params TrakHoundIdentityAssignment[] assignments)
        {
            return new TrakHoundSecurityProfile { Id = Guid.NewGuid().ToString(), ProviderId = providerId, Assignments = assignments };
        }


        [Fact]
        public void Roles_ReachTheInstance_ThroughTheProfilesOfTheirProvider()
        {
            var profiles = new[]
            {
                Profile("users", Assignment("api:entities:*", AppRole), Assignment("system:*", InstanceRole), Assignment("service:redis:*", "data-*")),
                Profile("api-keys", Assignment("system:logs", "log-readers"))
            };

            Assert.True(TrakHoundAdminProfileRules.ReachesInstance(profiles, "users", new[] { InstanceRole }));
            Assert.True(TrakHoundAdminProfileRules.ReachesInstance(profiles, "users", new[] { AppRole, "data-readers" })); // A Role pattern
            Assert.False(TrakHoundAdminProfileRules.ReachesInstance(profiles, "users", new[] { AppRole }));
            Assert.False(TrakHoundAdminProfileRules.ReachesInstance(profiles, "users", null));

            // The same Role name only counts for the Identity Provider whose Profile uses it
            Assert.False(TrakHoundAdminProfileRules.ReachesInstance(profiles, "users", new[] { "log-readers" }));
            Assert.True(TrakHoundAdminProfileRules.ReachesInstance(profiles, "api-keys", new[] { "log-readers" }));
            Assert.False(TrakHoundAdminProfileRules.ReachesInstance(profiles, "api-keys", new[] { InstanceRole }));
        }

        [Fact]
        public void AProfileForEveryIdentity_MakesEveryIdentityReachTheInstance()
        {
            // "*" matches any Identity, including one without Roles, so creating any User for the Provider gives that access
            var profiles = new[] { Profile("users", Assignment("system:*", "*")) };

            Assert.True(TrakHoundAdminProfileRules.ReachesInstance(profiles, "users", null));
            Assert.True(TrakHoundAdminProfileRules.ReachesInstance(profiles, "users", new[] { AppRole }));
        }

        [Fact]
        public void ProfileRoles_ListTheNamedRoles_AndWhichReachTheInstance()
        {
            var profiles = new[]
            {
                Profile("users", Assignment("api:entities:*", AppRole, "engineers"), Assignment("system:*", InstanceRole), Assignment("app:*", "*", "team-*")),
                Profile("api-keys", Assignment("api:*", "reporting"))
            };

            Assert.Equal(new[] { ("engineers", false), (InstanceRole, true), (AppRole, false) }, TrakHoundAdminProfileRules.GetRoles(profiles, "users"));
            Assert.Equal(new[] { ("reporting", false) }, TrakHoundAdminProfileRules.GetRoles(profiles, "api-keys"));
            Assert.Empty(TrakHoundAdminProfileRules.GetRoles(profiles, "ldap"));
        }

        [Fact]
        public void ChangingProfiles_NeedsMoreForAssignmentsThatReachTheInstance()
        {
            var appAssignments = new[] { Assignment("api:entities:*", AppRole) };
            var instanceAssignments = new[] { Assignment("api:entities:*", AppRole), Assignment("system:*", InstanceRole) };

            // A Role that can change Profiles for Apis and Apps, but isn't an Owner
            var profileAdmin = new TrakHoundAdminRole
            {
                Id = "app-security",
                Grants = new List<TrakHoundAdminGrant> { new TrakHoundAdminGrant(TrakHoundAdminSecurity.SecurityProfiles, TrakHoundAdminSecurity.WritePermission) }
            };

            using (TrakHoundAdminContext.Begin(new TrakHoundAdminContext("test", new[] { profileAdmin })))
            {
                TrakHoundAdminProfileRules.DemandWrite(appAssignments);
                TrakHoundAdminProfileRules.DemandWrite(null, appAssignments);

                // Saving one, or replacing or removing one (ex. deleting the Profile that has it)
                Assert.Throws<TrakHoundAdminAccessException>(() => TrakHoundAdminProfileRules.DemandWrite(instanceAssignments));
                Assert.Throws<TrakHoundAdminAccessException>(() => TrakHoundAdminProfileRules.DemandWrite(appAssignments, instanceAssignments));
            }

            using (SignIn(TrakHoundAdminSecurity.SecurityOperatorRoleId))
            {
                Assert.Throws<TrakHoundAdminAccessException>(() => TrakHoundAdminProfileRules.DemandWrite(appAssignments));
            }

            using (SignIn(TrakHoundAdminSecurity.OwnerRoleId))
            {
                TrakHoundAdminProfileRules.DemandWrite(instanceAssignments);
            }
        }

        [Fact]
        public void OnlyAnOwner_CanChangeInstanceProfiles()
        {
            Assert.True(TrakHoundAdminSecurity.Owner.Allows(TrakHoundAdminSecurity.InstanceSecurityProfiles, TrakHoundAdminSecurity.WritePermission));
            Assert.All(TrakHoundAdminSecurity.Roles.Where(o => o.Id != TrakHoundAdminSecurity.OwnerRoleId), o => Assert.False(o.Allows(TrakHoundAdminSecurity.InstanceSecurityProfiles, TrakHoundAdminSecurity.WritePermission)));
        }

        [Fact]
        public void SecurityOperator_CanGiveRolesThatOnlyReachApisAndApps()
        {
            using (SignIn(TrakHoundAdminSecurity.SecurityOperatorRoleId))
            {
                Assert.Null(_users.Create("jsmith", null, new[] { AppRole }, "secret-123"));
                Assert.Null(_users.Update(_users.GetUserByUsername("jsmith").Id, "Jane Smith", new[] { AppRole, "engineers" }));
                Assert.NotNull(_keys.Create("reporting", null, new[] { AppRole }, null));
            }
        }

        [Fact]
        public void SecurityOperator_CantGiveARoleThatReachesTheInstance()
        {
            var user = CreateUser("jsmith", AppRole);

            using (SignIn(TrakHoundAdminSecurity.SecurityOperatorRoleId))
            {
                Assert.Equal(TrakHoundAdminSecurity.InstanceRoleDeniedMessage, _users.Create("another", null, new[] { InstanceRole }, "secret-123"));
                Assert.Equal(TrakHoundAdminSecurity.InstanceRoleDeniedMessage, _users.Update(user.Id, null, new[] { AppRole, InstanceRole }));
                Assert.Equal(TrakHoundAdminSecurity.InstanceRoleDeniedMessage, _keys.GetRoleDenial(new[] { InstanceRole }));
                Assert.Throws<TrakHoundAdminAccessException>(() => _keys.Create("automation", null, new[] { InstanceRole }, null));
            }

            Assert.Null(_users.GetUserByUsername("another"));
            Assert.Equal(new[] { AppRole }, _users.GetUser(user.Id).Roles);
            Assert.Empty(_keys.GetKeys());
        }

        [Fact]
        public void SecurityOperator_CantTakeOverAUserWhoReachesTheInstance()
        {
            var user = CreateUser("root", InstanceRole);

            using (SignIn(TrakHoundAdminSecurity.SecurityOperatorRoleId))
            {
                // Setting the Password would let the Operator sign in as the User
                Assert.Equal(TrakHoundAdminSecurity.InstanceRoleDeniedMessage, _users.SetPassword(user.Id, "secret-456"));
                Assert.Equal(TrakHoundAdminSecurity.InstanceRoleDeniedMessage, _users.SetEnabled(user.Id, false));
                Assert.Equal(TrakHoundAdminSecurity.InstanceRoleDeniedMessage, _users.Update(user.Id, null, new[] { AppRole }));
                Assert.Equal(TrakHoundAdminSecurity.InstanceRoleDeniedMessage, _users.EndSessions(user.Id));
                Assert.Throws<TrakHoundAdminAccessException>(() => _users.Delete(user.Id));
            }

            Assert.NotNull(_users.Verify("root", "secret-123"));
            Assert.Equal(new[] { InstanceRole }, _users.GetUser(user.Id).Roles);
        }

        [Fact]
        public void SecurityOperator_CantRevokeAKeyThatReachesTheInstance()
        {
            _keys.Create("automation", null, new[] { InstanceRole }, null);
            var key = Assert.Single(_keys.GetKeys());

            using (SignIn(TrakHoundAdminSecurity.SecurityOperatorRoleId))
            {
                Assert.Throws<TrakHoundAdminAccessException>(() => _keys.Revoke(key.Id));
            }

            Assert.Single(_keys.GetKeys());
        }

        [Fact]
        public void AnOwner_CanGiveAndChangeEveryRole()
        {
            using (SignIn(TrakHoundAdminSecurity.OwnerRoleId))
            {
                Assert.Null(_users.Create("root", null, new[] { InstanceRole }, "secret-123"));
                var user = _users.GetUserByUsername("root");
                Assert.Null(_users.SetPassword(user.Id, "secret-456"));
                _users.Delete(user.Id);

                _keys.Create("automation", null, new[] { InstanceRole }, null);
                _keys.Revoke(Assert.Single(_keys.GetKeys()).Id);
            }

            Assert.Empty(_users.GetUsers());
            Assert.Empty(_keys.GetKeys());
        }

        [Fact]
        public void AViewer_CantChangeUsersOrKeys()
        {
            var user = CreateUser("jsmith", AppRole);

            using (SignIn(TrakHoundAdminSecurity.ViewerRoleId))
            {
                Assert.Equal(TrakHoundAdminSecurity.DeniedMessage, _users.Create("another", null, null, "secret-123"));
                Assert.Equal(TrakHoundAdminSecurity.DeniedMessage, _users.SetPassword(user.Id, "secret-456"));
                Assert.Throws<TrakHoundAdminAccessException>(() => _users.Delete(user.Id));
                Assert.Throws<TrakHoundAdminAccessException>(() => _keys.Create("reporting", null, null, null));
            }

            Assert.Single(_users.GetUsers());
        }

        [Fact]
        public void AUserSigningIn_IsNotCheckedAgainstAnAdmin()
        {
            // Verifying a Password can save the User (a Hash made with fewer Iterations is upgraded), which isn't a change made by an Admin
            CreateUser("root", InstanceRole);

            using (SignIn(TrakHoundAdminSecurity.ViewerRoleId))
            {
                Assert.NotNull(_users.Verify("root", "secret-123"));
            }
        }
    }
}
