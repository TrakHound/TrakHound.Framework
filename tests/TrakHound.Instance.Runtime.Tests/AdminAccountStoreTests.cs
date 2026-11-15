// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Security;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// The Admin Accounts: created and changed by an Owner, each with Roles, and there is always an enabled Owner
    /// </summary>
    public class AdminAccountStoreTests : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"trakhound-admin-accounts-tests-{Guid.NewGuid()}");
        private TrakHoundAdminAccountStore _store;


        public AdminAccountStoreTests()
        {
            _store = new TrakHoundAdminAccountStore(_directory);
            Assert.Null(_store.SetOwner("owner", "secret-1", out _));
        }

        public void Dispose()
        {
            try { Directory.Delete(_directory, true); } catch { }
        }

        private TrakHoundAdminAccount Owner => _store.GetAccountByUsername("owner");

        private TrakHoundAdminAccount Create(string username, params string[] roleIds)
        {
            Assert.Null(_store.Create(username, null, roleIds, "secret-1"));
            return _store.GetAccountByUsername(username);
        }

        private void Reload() => _store = new TrakHoundAdminAccountStore(_directory);


        [Fact]
        public void Create_AddsAnAccountWithItsRoles()
        {
            var account = Create("packages", TrakHoundAdminSecurity.PackagesRoleId, TrakHoundAdminSecurity.ViewerRoleId);

            Assert.Equal(new[] { TrakHoundAdminSecurity.PackagesRoleId, TrakHoundAdminSecurity.ViewerRoleId }, account.RoleIds);
            Assert.True(account.Enabled);
            Assert.False(account.IsOwner);
            Assert.NotNull(_store.Verify("packages", "secret-1"));
        }

        [Fact]
        public void Accounts_AreKeptAfterReload()
        {
            var account = Create("packages", TrakHoundAdminSecurity.PackagesRoleId);
            Assert.Null(_store.SetEnabled(account.Id, false));

            Reload();

            var reloaded = _store.GetAccount(account.Id);
            Assert.Equal("packages", reloaded.Username);
            Assert.Equal(new[] { TrakHoundAdminSecurity.PackagesRoleId }, reloaded.RoleIds);
            Assert.False(reloaded.Enabled); // A disabled Account stays disabled
            Assert.True(Owner.IsOwner);
        }

        [Fact]
        public void Username_IsNotCaseSensitive_AndCantBeUsedTwice()
        {
            Create("packages", TrakHoundAdminSecurity.PackagesRoleId);

            Assert.NotNull(_store.Verify("PACKAGES", "secret-1"));
            Assert.Contains("already used", _store.Create("Packages", null, new[] { TrakHoundAdminSecurity.ViewerRoleId }, "secret-1"));
        }

        [Theory]
        [InlineData("abc", "secret-1", TrakHoundAdminSecurity.ViewerRoleId)] // Username too short
        [InlineData(" viewer", "secret-1", TrakHoundAdminSecurity.ViewerRoleId)]
        [InlineData("viewer", "abc", TrakHoundAdminSecurity.ViewerRoleId)] // Password too short
        [InlineData("viewer", "secret-1", "")] // No Role
        [InlineData("viewer", "secret-1", "no-such-role")]
        public void Create_InvalidAccount_IsRefused(string username, string password, string roleId)
        {
            Assert.NotNull(_store.Create(username, null, new[] { roleId }, password));
            Assert.Single(_store.GetAccounts());
        }

        [Fact]
        public void Password_IsNotStored()
        {
            var account = Create("viewer", TrakHoundAdminSecurity.ViewerRoleId);

            Assert.DoesNotContain("secret-1", File.ReadAllText(_store.GetAccountPath(account.Id)));
        }

        [Fact]
        public void WrongPassword_UnknownUsername_AndDisabledAccount_CantSignIn()
        {
            var account = Create("viewer", TrakHoundAdminSecurity.ViewerRoleId);

            Assert.Null(_store.Verify("viewer", "wrong"));
            Assert.Null(_store.Verify("nobody", "secret-1"));

            Assert.Null(_store.SetEnabled(account.Id, false));
            Assert.Null(_store.Verify("viewer", "secret-1"));
        }

        [Fact]
        public void SetPassword_ChangesThePassword_AndEndsSessions()
        {
            var account = Create("viewer", TrakHoundAdminSecurity.ViewerRoleId);

            Assert.Null(_store.SetPassword(account.Id, "secret-2"));

            Assert.Null(_store.Verify("viewer", "secret-1"));
            Assert.NotNull(_store.Verify("viewer", "secret-2"));
            Assert.False(_store.IsSessionValid(account.Id, account.SessionStamp));
        }

        [Fact]
        public void Update_ChangesRoles_AndKeepsSessions()
        {
            var account = Create("viewer", TrakHoundAdminSecurity.ViewerRoleId);

            Assert.Null(_store.Update(account.Id, "Jane Smith", new[] { TrakHoundAdminSecurity.SecurityOperatorRoleId }));

            var updated = _store.GetAccount(account.Id);
            Assert.Equal("Jane Smith", updated.DisplayName);
            Assert.Equal(new[] { TrakHoundAdminSecurity.SecurityOperatorRoleId }, updated.RoleIds);
            Assert.True(_store.IsSessionValid(account.Id, account.SessionStamp));
        }

        [Fact]
        public void EndSessions_EndsTheAccountsSessions()
        {
            var account = Create("viewer", TrakHoundAdminSecurity.ViewerRoleId);

            Assert.Null(_store.EndSessions(account.Id));

            Assert.False(_store.IsSessionValid(account.Id, account.SessionStamp));
        }

        [Fact]
        public void Delete_RemovesTheAccount()
        {
            var account = Create("viewer", TrakHoundAdminSecurity.ViewerRoleId);

            Assert.Null(_store.Delete(account.Id));

            Assert.Null(_store.GetAccount(account.Id));
            Assert.False(File.Exists(_store.GetAccountPath(account.Id)));
            Reload();
            Assert.Single(_store.GetAccounts());
        }

        [Fact]
        public void TheOnlyOwner_CantBeDeletedDisabledOrDemoted()
        {
            Create("viewer", TrakHoundAdminSecurity.ViewerRoleId);
            var owner = Owner;

            Assert.NotNull(_store.Delete(owner.Id));
            Assert.NotNull(_store.SetEnabled(owner.Id, false));
            Assert.NotNull(_store.Update(owner.Id, null, new[] { TrakHoundAdminSecurity.ViewerRoleId }));

            Assert.True(Owner.IsOwner);
            Assert.True(Owner.Enabled);
        }

        [Fact]
        public void AnOwner_CanBeRemoved_WhenAnotherEnabledOwnerExists()
        {
            var second = Create("second", TrakHoundAdminSecurity.OwnerRoleId);

            // A disabled Owner doesn't count
            Assert.Null(_store.SetEnabled(second.Id, false));
            Assert.NotNull(_store.Delete(Owner.Id));

            Assert.Null(_store.SetEnabled(second.Id, true));
            Assert.Null(_store.Delete(Owner.Id));
            Assert.NotNull(_store.Delete(second.Id)); // Now the only Owner
        }

        [Fact]
        public void AnAdminWhoIsntAnOwner_CantChangeAccounts()
        {
            var viewer = Create("viewer", TrakHoundAdminSecurity.ViewerRoleId);

            using (TrakHoundAdminContext.Begin(new TrakHoundAdminContext(viewer)))
            {
                Assert.Equal(TrakHoundAdminSecurity.DeniedMessage, _store.Create("another", null, new[] { TrakHoundAdminSecurity.OwnerRoleId }, "secret-1"));
                Assert.Equal(TrakHoundAdminSecurity.DeniedMessage, _store.Update(viewer.Id, null, new[] { TrakHoundAdminSecurity.OwnerRoleId }));
                Assert.Equal(TrakHoundAdminSecurity.DeniedMessage, _store.SetPassword(Owner.Id, "secret-2"));
                Assert.Equal(TrakHoundAdminSecurity.DeniedMessage, _store.SetEnabled(Owner.Id, false));
                Assert.Equal(TrakHoundAdminSecurity.DeniedMessage, _store.EndSessions(Owner.Id));
                Assert.Equal(TrakHoundAdminSecurity.DeniedMessage, _store.Delete(Owner.Id));

                // An Admin can always change their own Password
                Assert.Null(_store.SetPassword(viewer.Id, "secret-2"));
            }

            Assert.Equal(2, _store.GetAccounts().Count());
            Assert.False(_store.GetAccount(viewer.Id).IsOwner);
            Assert.NotNull(_store.Verify("owner", "secret-1"));
        }

        [Fact]
        public void AnOwner_CanChangeAccounts()
        {
            using (TrakHoundAdminContext.Begin(new TrakHoundAdminContext(Owner)))
            {
                Assert.Null(_store.Create("viewer", null, new[] { TrakHoundAdminSecurity.ViewerRoleId }, "secret-1"));
                Assert.Null(_store.Delete(_store.GetAccountByUsername("viewer").Id));
            }
        }

        private static Dictionary<string, IEnumerable<string>> Zones(string roleId, params string[] zones) => new Dictionary<string, IEnumerable<string>> { { roleId, zones } };

        [Fact]
        public void ARole_CanBeLimitedToZones()
        {
            Assert.Null(_store.Create("dev-admin", null, new[] { TrakHoundAdminSecurity.PackagesRoleId, TrakHoundAdminSecurity.ViewerRoleId }, "secret-1", Zones(TrakHoundAdminSecurity.PackagesRoleId, "dev", " line-1 ", "dev")));

            Reload();

            var account = _store.GetAccountByUsername("dev-admin");
            Assert.Equal(new[] { "dev", "line-1" }, account.Assignments.Single(o => o.RoleId == TrakHoundAdminSecurity.PackagesRoleId).Zones);
            Assert.Null(account.Assignments.Single(o => o.RoleId == TrakHoundAdminSecurity.ViewerRoleId).Zones);
        }

        [Fact]
        public void Update_ChangesOrKeepsTheZones()
        {
            Assert.Null(_store.Create("dev-admin", null, new[] { TrakHoundAdminSecurity.PackagesRoleId }, "secret-1", Zones(TrakHoundAdminSecurity.PackagesRoleId, "dev")));
            var id = _store.GetAccountByUsername("dev-admin").Id;

            // Without Zones given, the Roles the Account already holds keep theirs
            Assert.Null(_store.Update(id, "Dev Admin", new[] { TrakHoundAdminSecurity.PackagesRoleId, TrakHoundAdminSecurity.ViewerRoleId }));
            Assert.Equal(new[] { "dev" }, _store.GetAccount(id).Assignments.Single(o => o.RoleId == TrakHoundAdminSecurity.PackagesRoleId).Zones);

            Assert.Null(_store.Update(id, "Dev Admin", new[] { TrakHoundAdminSecurity.PackagesRoleId }, Zones(TrakHoundAdminSecurity.PackagesRoleId, "line-1")));
            Assert.Equal(new[] { "line-1" }, _store.GetAccount(id).Assignments.Single().Zones);

            // No Zones for the Role : it applies everywhere again
            Assert.Null(_store.Update(id, "Dev Admin", new[] { TrakHoundAdminSecurity.PackagesRoleId }, new Dictionary<string, IEnumerable<string>>()));
            Assert.Null(_store.GetAccount(id).Assignments.Single().Zones);
        }

        [Fact]
        public void TheOwnerRole_CantBeLimitedToZones()
        {
            Assert.Contains("Owner", _store.Create("second", null, new[] { TrakHoundAdminSecurity.OwnerRoleId }, "secret-1", Zones(TrakHoundAdminSecurity.OwnerRoleId, "dev")));
            Assert.Contains("Owner", _store.Update(Owner.Id, null, new[] { TrakHoundAdminSecurity.OwnerRoleId }, Zones(TrakHoundAdminSecurity.OwnerRoleId, "dev")));

            Assert.True(Owner.IsOwner);
            Assert.Single(_store.GetAccounts());
        }

        [Fact]
        public void ARoleWithNothingAboutAModule_CantBeLimitedToZones()
        {
            // The Security Operator Role is only Users, API Keys and viewing Profiles
            Assert.NotNull(_store.Create("operator", null, new[] { TrakHoundAdminSecurity.SecurityOperatorRoleId }, "secret-1", Zones(TrakHoundAdminSecurity.SecurityOperatorRoleId, "dev")));
            Assert.Single(_store.GetAccounts());
        }

        [Fact]
        public void AnOwnerLimitedToZonesInItsFile_IsntAnOwner()
        {
            // ex. a file that was edited : it must not count as the Owner that can always sign in and manage the Accounts
            var account = new TrakHoundAdminAccount
            {
                Enabled = true,
                Assignments = new List<TrakHoundAdminRoleAssignment> { new TrakHoundAdminRoleAssignment { RoleId = TrakHoundAdminSecurity.OwnerRoleId, Zones = new List<string> { "dev" } } }
            };

            Assert.False(account.IsOwner);
            Assert.False(new TrakHoundAdminContext(account).CanSome(TrakHoundAdminSecurity.Admins, TrakHoundAdminSecurity.ReadPermission));
        }

        [Fact]
        public void AnAccountWrittenByAnotherProcess_IsSeenAfterLoad()
        {
            // ex. the set-admin command, run while the Instance is running
            Assert.Null(new TrakHoundAdminAccountStore(_directory).SetOwner("second", "secret-2", out var created));
            Assert.True(created);

            _store.Load();

            Assert.NotNull(_store.Verify("second", "secret-2"));
        }
    }
}
