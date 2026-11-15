// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Security.Claims;
using TrakHound.Instance.Security;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// Admin Sessions: a Login's one time Code is exchanged for the Session, which ends when it expires, is signed out (revoked), or its
    /// Admin Account's Password is changed or the Account is disabled or deleted. Other Accounts' Sessions aren't affected
    /// </summary>
    public class AdminSessionsTests : IDisposable
    {
        private readonly string _profilePath = Path.Combine(Path.GetTempPath(), $"trakhound-admin-sessions-tests-{Guid.NewGuid()}");
        private DateTime _now = DateTime.UtcNow;
        private readonly TrakHoundAdminAccountStore _accounts;
        private readonly TrakHoundAdminSessions _sessions;


        public AdminSessionsTests()
        {
            _accounts = new TrakHoundAdminAccountStore(TrakHoundAdminAccountStore.GetDirectory(_profilePath));
            Assert.Null(_accounts.SetOwner("admin", "secret-1", out _));
            Assert.Null(_accounts.Create("viewer", null, new[] { TrakHoundAdminSecurity.ViewerRoleId }, "secret-1"));

            _sessions = new TrakHoundAdminSessions(_profilePath, _accounts, () => _now);
        }

        public void Dispose()
        {
            try { Directory.Delete(_profilePath, true); } catch { }
        }

        private TrakHoundAdminAccount Admin => _accounts.GetAccountByUsername("admin");

        private TrakHoundAdminAccount Viewer => _accounts.GetAccountByUsername("viewer");

        private ClaimsPrincipal SignIn(string username = "admin") => _sessions.TakeCode(_sessions.CreateCode(_accounts.GetAccountByUsername(username)));


        [Fact]
        public void Code_GivesValidSession()
        {
            var principal = SignIn();

            Assert.NotNull(principal);
            Assert.Equal("admin", _sessions.Validate(principal)?.Username);
            Assert.Equal("admin", TrakHoundAdminSessions.GetUsername(principal));
            Assert.True(TrakHoundAdminSessions.HasSession(principal));
        }

        [Fact]
        public void Code_CanOnlyBeUsedOnce()
        {
            var code = _sessions.CreateCode(Admin);

            Assert.NotNull(_sessions.TakeCode(code));
            Assert.Null(_sessions.TakeCode(code));
        }

        [Fact]
        public void Code_Expires()
        {
            var code = _sessions.CreateCode(Admin);
            _now += TrakHoundAdminSessions.CodeLifetime;

            Assert.Null(_sessions.TakeCode(code));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("unknown")]
        public void UnknownCode_IsRejected(string code)
        {
            Assert.Null(_sessions.TakeCode(code));
        }

        [Fact]
        public void Session_Expires()
        {
            var principal = SignIn();
            _now += TrakHoundAdminSessions.SessionLifetime;

            Assert.Null(_sessions.Validate(principal));
        }

        [Fact]
        public void RevokedSession_IsInvalid()
        {
            var principal = SignIn();
            var other = SignIn();

            _sessions.Revoke(principal);

            Assert.Null(_sessions.Validate(principal));
            Assert.Equal("admin", _sessions.Validate(other)?.Username);
        }

        [Fact]
        public void RevokedSession_StaysRevokedAfterRestart()
        {
            var principal = SignIn();
            _sessions.Revoke(principal);

            var restarted = new TrakHoundAdminSessions(_profilePath, new TrakHoundAdminAccountStore(TrakHoundAdminAccountStore.GetDirectory(_profilePath)), () => _now);

            Assert.Null(restarted.Validate(principal));
        }

        [Fact]
        public void Session_IsValidAfterRestart()
        {
            var principal = SignIn();

            var restarted = new TrakHoundAdminSessions(_profilePath, new TrakHoundAdminAccountStore(TrakHoundAdminAccountStore.GetDirectory(_profilePath)), () => _now);

            Assert.Equal("admin", restarted.Validate(principal)?.Username);
        }

        [Fact]
        public void ChangedPassword_EndsOnlyThatAccountsSessions()
        {
            var admin = SignIn();
            var viewer = SignIn("viewer");

            Assert.Null(_accounts.SetPassword(Viewer.Id, "secret-2"));

            Assert.Null(_sessions.Validate(viewer));
            Assert.Equal("admin", _sessions.Validate(admin)?.Username);
        }

        [Fact]
        public void SetAdmin_EndsThatAccountsSessions()
        {
            var principal = SignIn();

            // ex. the set-admin command, run for the same Username
            Assert.Null(_accounts.SetOwner("admin", "secret-1", out _));

            Assert.Null(_sessions.Validate(principal));
        }

        [Fact]
        public void CodeFromBeforeThePasswordChanged_IsRejected()
        {
            var code = _sessions.CreateCode(Viewer);

            Assert.Null(_accounts.SetPassword(Viewer.Id, "secret-2"));

            Assert.Null(_sessions.TakeCode(code));
        }

        [Fact]
        public void DisabledAccount_EndsItsSessions()
        {
            var principal = SignIn("viewer");

            Assert.Null(_accounts.SetEnabled(Viewer.Id, false));

            Assert.Null(_sessions.Validate(principal));
        }

        [Fact]
        public void DeletedAccount_EndsItsSessions()
        {
            var principal = SignIn("viewer");

            Assert.Null(_accounts.Delete(Viewer.Id));

            Assert.Null(_sessions.Validate(principal));
        }

        [Fact]
        public void ChangedRoles_KeepTheSession_AndApplyToIt()
        {
            var principal = SignIn("viewer");

            Assert.Null(_accounts.Update(Viewer.Id, null, new[] { TrakHoundAdminSecurity.PackagesRoleId }));

            // Roles aren't kept in the Session, so the next thing the Account does uses the new ones
            var account = _sessions.Validate(principal);
            Assert.NotNull(account);
            Assert.Equal(new[] { TrakHoundAdminSecurity.PackagesRoleId }, account.RoleIds);
        }

        [Fact]
        public void OtherIdentities_AreNotAdminSessions()
        {
            // ex. an Identity Provider Session with the same Claims, which must never sign in to the Admin Interface
            var principal = SignIn();
            var copy = new ClaimsPrincipal(new ClaimsIdentity(principal.Claims, "TrakHound.Identity"));

            Assert.Null(_sessions.Validate(copy));
            Assert.False(TrakHoundAdminSessions.HasSession(copy));
            Assert.Null(_sessions.Validate(new ClaimsPrincipal(new ClaimsIdentity())));
            Assert.Null(_sessions.Validate(null));
        }
    }
}
