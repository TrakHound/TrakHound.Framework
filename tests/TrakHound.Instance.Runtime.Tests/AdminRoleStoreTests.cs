// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Security;
using TrakHound.Security;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// The Admin Roles an Owner adds: what they can include, how Accounts hold them, and which Roles their holders can give Users and API Keys
    /// </summary>
    public class AdminRoleStoreTests : IDisposable
    {
        private const string Read = TrakHoundAdminSecurity.ReadPermission;
        private const string Write = TrakHoundAdminSecurity.WritePermission;

        // Role IDs are unique to this run, since the Roles a store loads are shared by the process (TrakHoundAdminSecurity.GetRole)
        private readonly string _suffix = Guid.NewGuid().ToString("N").Substring(0, 8);
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"trakhound-admin-roles-tests-{Guid.NewGuid()}");
        private TrakHoundAdminRoleStore _store;


        public AdminRoleStoreTests()
        {
            _store = new TrakHoundAdminRoleStore(Path.Combine(_directory, "admin-roles"));
        }

        public void Dispose()
        {
            TrakHoundAdminSecurity.SetCustomRoles(null);
            try { Directory.Delete(_directory, true); } catch { }
        }

        private string Id(string name) => $"{name}-{_suffix}";

        private TrakHoundAdminRole Role(string name, params TrakHoundAdminGrant[] grants)
        {
            return new TrakHoundAdminRole { Id = Id(name), Name = name, Grants = grants.ToList() };
        }


        [Fact]
        public void Save_AddsARoleThatAccountsCanHold()
        {
            var role = Role("services", new TrakHoundAdminGrant(TrakHoundAdminSecurity.ServiceConfigurations, Write), new TrakHoundAdminGrant(TrakHoundAdminSecurity.Services, Read));

            Assert.Null(_store.Save(role));

            var found = TrakHoundAdminSecurity.GetRole(role.Id);
            Assert.NotNull(found);
            Assert.False(found.BuiltIn);
            Assert.True(found.Allows(TrakHoundAdminSecurity.ServiceConfigurations, Write));
            Assert.True(found.Allows(TrakHoundAdminSecurity.Services, Read));
            Assert.False(found.Allows(TrakHoundAdminSecurity.Services, Write));
            Assert.False(found.Allows(TrakHoundAdminSecurity.Packages, Read));
            Assert.Null(TrakHoundAdminAccountStore.ValidateRoles(new[] { role.Id }));
            Assert.Contains(TrakHoundAdminSecurity.AllRoles, o => o.Id == role.Id);
        }

        [Fact]
        public void Roles_AreKeptAfterReload()
        {
            var role = Role("services", new TrakHoundAdminGrant(TrakHoundAdminSecurity.Services, Read));
            role.Description = "Views Service data";
            role.GrantableRoles = new List<string> { "operators", "line-1-*" };
            Assert.Null(_store.Save(role));

            _store = new TrakHoundAdminRoleStore(Path.Combine(_directory, "admin-roles"));

            var reloaded = _store.GetRole(role.Id);
            Assert.Equal("Views Service data", reloaded.Description);
            Assert.Equal(new[] { "operators", "line-1-*" }, reloaded.GrantableRoles);
            Assert.True(TrakHoundAdminSecurity.GetRole(role.Id).Allows(TrakHoundAdminSecurity.Services, Read));
        }

        [Fact]
        public void Save_ChangesWhatTheRolesHoldersCanDo()
        {
            var role = Role("services", new TrakHoundAdminGrant(TrakHoundAdminSecurity.Services, Read));
            Assert.Null(_store.Save(role));

            var account = new TrakHoundAdminAccount { Id = "a", Username = "holder", Assignments = new List<TrakHoundAdminRoleAssignment> { new TrakHoundAdminRoleAssignment { RoleId = role.Id } } };
            Assert.False(new TrakHoundAdminContext(account).Can(TrakHoundAdminSecurity.Services, Write));

            role.Grants = new List<TrakHoundAdminGrant> { new TrakHoundAdminGrant(TrakHoundAdminSecurity.Services, Write) };
            Assert.Null(_store.Save(role));

            Assert.True(new TrakHoundAdminContext(account).Can(TrakHoundAdminSecurity.Services, Write));
        }

        [Theory]
        [InlineData("", "Name")]
        [InlineData("Has Spaces", "Name")]
        [InlineData("UPPER", "Name")]
        [InlineData("-leading", "Name")]
        [InlineData("../escape", "Name")]
        [InlineData(TrakHoundAdminSecurity.OwnerRoleId, "Name")] // A built in Role's ID
        [InlineData("valid-id", "")]
        public void Save_InvalidRole_IsRefused(string id, string name)
        {
            var role = new TrakHoundAdminRole { Id = id, Name = name, Grants = new List<TrakHoundAdminGrant>() };

            Assert.NotNull(_store.Save(role));
            Assert.Empty(_store.GetRoles());
        }

        [Fact]
        public void ARole_CantIncludeTheAdminAccounts()
        {
            // Its holders could make themselves Owners
            var role = Role("almost-owner", new TrakHoundAdminGrant(TrakHoundAdminSecurity.Admins, Write));

            Assert.Contains("Owner", _store.Save(role));
            Assert.Null(TrakHoundAdminSecurity.GetRole(role.Id));
        }

        [Theory]
        [InlineData("admin:no-such-area", Write)]
        [InlineData("*", Write)]
        [InlineData(TrakHoundAdminSecurity.Services, "execute")]
        public void ARole_OnlyIncludesKnownAreasAndPermissions(string resourceId, string permission)
        {
            var role = Role("custom", new TrakHoundAdminGrant(resourceId, permission));

            Assert.NotNull(_store.Save(role));
        }

        [Fact]
        public void ARoleFileThatCantBeUsed_GivesNoPermissions()
        {
            Directory.CreateDirectory(Path.Combine(_directory, "admin-roles"));
            File.WriteAllText(Path.Combine(_directory, "admin-roles", $"{Id("edited")}.yaml"), $"id: {Id("edited")}\nname: Edited\ngrants:\n- resource: admin:admins\n  permissions:\n  - write\n");

            _store.Load();

            Assert.Null(TrakHoundAdminSecurity.GetRole(Id("edited")));
        }

        [Fact]
        public void Delete_IsRefusedWhileAnAccountHoldsTheRole()
        {
            var role = Role("services", new TrakHoundAdminGrant(TrakHoundAdminSecurity.Services, Read));
            Assert.Null(_store.Save(role));

            var holder = new TrakHoundAdminAccount { Id = "a", Username = "holder", Assignments = new List<TrakHoundAdminRoleAssignment> { new TrakHoundAdminRoleAssignment { RoleId = role.Id } } };

            Assert.Contains("holder", _store.Delete(role.Id, new[] { holder }));
            Assert.NotNull(TrakHoundAdminSecurity.GetRole(role.Id));

            Assert.Null(_store.Delete(role.Id, Array.Empty<TrakHoundAdminAccount>()));
            Assert.Null(TrakHoundAdminSecurity.GetRole(role.Id));
            Assert.False(File.Exists(_store.GetRolePath(role.Id)));
        }

        [Fact]
        public void OnlyAnOwner_CanChangeRoles()
        {
            var role = Role("services", new TrakHoundAdminGrant(TrakHoundAdminSecurity.Services, Read));

            using (TrakHoundAdminContext.Begin(new TrakHoundAdminContext("viewer", new[] { TrakHoundAdminSecurity.GetRole(TrakHoundAdminSecurity.ViewerRoleId) })))
            {
                Assert.Equal(TrakHoundAdminSecurity.DeniedMessage, _store.Save(role));
            }

            using (TrakHoundAdminContext.Begin(new TrakHoundAdminContext("owner", new[] { TrakHoundAdminSecurity.Owner })))
            {
                Assert.Null(_store.Save(role));
                Assert.Null(_store.Delete(role.Id, null));
            }
        }

        [Fact]
        public void GrantableRoles_LimitWhichRolesAnAdminGivesUsersAndKeys()
        {
            var limited = Role("line-1-users", new TrakHoundAdminGrant(TrakHoundAdminSecurity.Users, Write));
            limited.GrantableRoles = new List<string> { "operators", "line-1-*" };

            var context = new TrakHoundAdminContext("test", new[] { limited });
            Assert.True(context.CanGiveRole(TrakHoundAdminSecurity.Users, "operators"));
            Assert.True(context.CanGiveRole(TrakHoundAdminSecurity.Users, "line-1-engineers"));
            Assert.False(context.CanGiveRole(TrakHoundAdminSecurity.Users, "line-2-engineers"));

            // The Role doesn't let its holders change API Keys at all
            Assert.False(context.CanGiveRole(TrakHoundAdminSecurity.ApiKeys, "operators"));

            using (TrakHoundAdminContext.Begin(context))
            {
                Assert.Null(TrakHoundAdminContext.GetGrantDenial(TrakHoundAdminSecurity.Users, new[] { "operators", "line-1-engineers" }));
                Assert.Contains("line-2-engineers", TrakHoundAdminContext.GetGrantDenial(TrakHoundAdminSecurity.Users, new[] { "operators", "line-2-engineers" }));
                Assert.Null(TrakHoundAdminContext.GetGrantDenial(TrakHoundAdminSecurity.Users, null));
            }
        }

        [Fact]
        public void ARoleWithoutGrantableRoles_GivesAnyRole_AndWidensALimitedOne()
        {
            var limited = Role("limited", new TrakHoundAdminGrant(TrakHoundAdminSecurity.Users, Write));
            limited.GrantableRoles = new List<string> { "operators" };

            var operatorRole = TrakHoundAdminSecurity.GetRole(TrakHoundAdminSecurity.SecurityOperatorRoleId);

            Assert.True(new TrakHoundAdminContext("test", new[] { operatorRole }).CanGiveRole(TrakHoundAdminSecurity.Users, "anything"));
            Assert.True(new TrakHoundAdminContext("test", new[] { limited, operatorRole }).CanGiveRole(TrakHoundAdminSecurity.Users, "anything"));
        }

        [Fact]
        public void GrantableRoles_AlsoStopChangingAUserWhoHasAnotherRole()
        {
            var users = new TrakHoundUserStore(Path.Combine(_directory, "users"));
            Assert.Null(users.Create("line1", null, new[] { "line-1-operators" }, "secret-123"));
            Assert.Null(users.Create("line2", null, new[] { "line-2-operators" }, "secret-123"));

            var limited = Role("line-1-users", new TrakHoundAdminGrant(TrakHoundAdminSecurity.Users, Write));
            limited.GrantableRoles = new List<string> { "line-1-*" };

            using (TrakHoundAdminContext.Begin(new TrakHoundAdminContext("test", new[] { limited })))
            {
                Assert.Null(users.SetPassword(users.GetUserByUsername("line1").Id, "secret-456"));
                Assert.Null(users.Create("another", null, new[] { "line-1-engineers" }, "secret-123"));

                Assert.StartsWith(TrakHoundAdminSecurity.GrantableRoleDeniedMessage, users.SetPassword(users.GetUserByUsername("line2").Id, "secret-456"));
                Assert.StartsWith(TrakHoundAdminSecurity.GrantableRoleDeniedMessage, users.Update(users.GetUserByUsername("line1").Id, null, new[] { "line-2-operators" }));
                Assert.StartsWith(TrakHoundAdminSecurity.GrantableRoleDeniedMessage, users.Create("third", null, new[] { "line-2-operators" }, "secret-123"));
            }

            Assert.NotNull(users.Verify("line2", "secret-123"));
        }
    }
}
