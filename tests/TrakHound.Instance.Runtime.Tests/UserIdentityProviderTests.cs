// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Http;
using TrakHound.Instance.Security;
using TrakHound.Security;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// The built in Users Identity Provider: Users are stored (with only a Password Hash) in the Configuration Profile, and changing a User ends their Sessions
    /// </summary>
    public class UserIdentityProviderTests : IDisposable
    {
        private const string Password = "password-1";

        private readonly string _directory = Path.Combine(Path.GetTempPath(), $"trakhound-users-tests-{Guid.NewGuid()}");
        private readonly TrakHoundUserStore _store;
        private readonly TrakHoundUserIdentityProvider _provider;


        public UserIdentityProviderTests()
        {
            _store = new TrakHoundUserStore(_directory);
            _provider = new TrakHoundUserIdentityProvider(_store);
        }

        public void Dispose()
        {
            try { Directory.Delete(_directory, true); } catch { }
        }

        private TrakHoundUser CreateUser(string username = "jsmith", params string[] roles)
        {
            Assert.Null(_store.Create(username, "Jane Smith", roles, Password));
            return _store.GetUserByUsername(username);
        }

        private Task<TrakHoundIdentityResult> SignIn(string username, string password)
        {
            return _provider.SignIn(null, new TrakHoundIdentityCredentials(username, password));
        }

        private TrakHoundUserStore Reload()
        {
            var store = new TrakHoundUserStore(_directory);
            store.Load();
            return store;
        }


        [Fact]
        public async Task SignIn_ReturnsIdentityWithRoles()
        {
            CreateUser("jsmith", "operators", "engineers");

            var result = await SignIn("jsmith", Password);

            Assert.True(result.IsSuccess);
            Assert.Equal(TrakHoundUserIdentityProvider.ProviderId, result.Identity.ProviderId);
            Assert.Equal("jsmith", result.Identity.UserId);
            Assert.Equal("Jane Smith", result.Identity.Name);
            Assert.Equal(TrakHoundIdentityUserType.Origin, result.Identity.Type);
            Assert.Equal(new[] { "operators", "engineers" }, result.Identity.Roles);
        }

        [Fact]
        public async Task SignIn_UsernameIgnoresCase()
        {
            CreateUser("jsmith");

            Assert.True((await SignIn("JSmith", Password)).IsSuccess);
        }

        [Fact]
        public async Task SignIn_WrongPassword_Fails()
        {
            CreateUser();

            var result = await SignIn("jsmith", "password-2");

            // Fail (not Error), so the Sign In Throttle counts it
            Assert.Equal(TrakHoundIdentityResultStatus.Fail, result.Status);
        }

        [Fact]
        public async Task SignIn_UnknownUser_Fails()
        {
            CreateUser();

            Assert.Equal(TrakHoundIdentityResultStatus.Fail, (await SignIn("someone", Password)).Status);
        }

        [Fact]
        public async Task SignIn_DisabledUser_Fails()
        {
            var user = CreateUser();
            Assert.Null(_store.SetEnabled(user.Id, false));

            Assert.Equal(TrakHoundIdentityResultStatus.Fail, (await SignIn("jsmith", Password)).Status);

            Assert.Null(_store.SetEnabled(user.Id, true));
            Assert.True((await SignIn("jsmith", Password)).IsSuccess);
        }

        [Fact]
        public void Create_RejectsDuplicateUsernameAndShortPassword()
        {
            CreateUser("jsmith");

            Assert.NotNull(_store.Create("JSMITH", null, null, Password));
            Assert.NotNull(_store.Create("other", null, null, "short"));
            Assert.NotNull(_store.Create(" padded", null, null, Password));
            Assert.Single(_store.GetUsers());
        }

        [Fact]
        public void Users_AreStoredWithoutThePassword()
        {
            var user = CreateUser("jsmith", "operators");
            Assert.Null(_store.SetEnabled(user.Id, false));

            var text = string.Concat(Directory.GetFiles(_directory).Select(File.ReadAllText));
            Assert.DoesNotContain(Password, text);

            // A disabled User stays disabled when read back (Enabled defaults to true)
            var reloaded = Reload().GetUserByUsername("jsmith");
            Assert.NotNull(reloaded);
            Assert.False(reloaded.Enabled);
            Assert.Equal(new[] { "operators" }, reloaded.Roles);
            Assert.Equal(TrakHoundPasswordHash.Iterations, reloaded.Iterations);
        }

        [Fact]
        public async Task SetPassword_ChangesPasswordAndEndsSessions()
        {
            var user = CreateUser();
            var identity = (await SignIn("jsmith", Password)).Identity;
            Assert.True(_provider.IsSessionValid(identity));

            Assert.Null(_store.SetPassword(user.Id, "password-2"));

            Assert.False(_provider.IsSessionValid(identity));
            Assert.False((await SignIn("jsmith", Password)).IsSuccess);
            Assert.True((await SignIn("jsmith", "password-2")).IsSuccess);
        }

        [Fact]
        public async Task Disable_EndsSessions()
        {
            var user = CreateUser();
            var identity = (await SignIn("jsmith", Password)).Identity;

            Assert.Null(_store.SetEnabled(user.Id, false));

            Assert.False(_provider.IsSessionValid(identity));
        }

        [Fact]
        public async Task Delete_EndsSessions()
        {
            var user = CreateUser();
            var identity = (await SignIn("jsmith", Password)).Identity;

            _store.Delete(user.Id);

            Assert.False(_provider.IsSessionValid(identity));
            Assert.Empty(Reload().GetUsers());
        }

        [Fact]
        public async Task RoleChange_EndsSessions_NameChangeDoesNot()
        {
            var user = CreateUser("jsmith", "operators");
            var identity = (await SignIn("jsmith", Password)).Identity;

            Assert.Null(_store.Update(user.Id, "Jane", new[] { "operators" }));
            Assert.True(_provider.IsSessionValid(identity));

            Assert.Null(_store.Update(user.Id, "Jane", new[] { "operators", "engineers" }));
            Assert.False(_provider.IsSessionValid(identity));
        }

        [Fact]
        public async Task EndSessions_EndsOnlyThatUsersSessions()
        {
            var user = CreateUser("jsmith");
            CreateUser("other");
            var identity = (await SignIn("jsmith", Password)).Identity;
            var otherIdentity = (await SignIn("other", Password)).Identity;

            Assert.Null(_store.EndSessions(user.Id));

            Assert.False(_provider.IsSessionValid(identity));
            Assert.True(_provider.IsSessionValid(otherIdentity));

            // The User can sign in again, with the same Password
            var newIdentity = (await SignIn("jsmith", Password)).Identity;
            Assert.True(_provider.IsSessionValid(newIdentity));
            Assert.True(Reload().GetUserByUsername("jsmith").Enabled);
        }

        [Fact]
        public async Task EndAllSessions_EndsEveryonesSessions()
        {
            CreateUser("jsmith");
            CreateUser("other");
            var identity = (await SignIn("jsmith", Password)).Identity;
            var otherIdentity = (await SignIn("other", Password)).Identity;

            Assert.Null(_store.EndAllSessions());

            Assert.False(_provider.IsSessionValid(identity));
            Assert.False(_provider.IsSessionValid(otherIdentity));

            // Kept when the store is read again (ex. after a restart)
            var reloaded = new TrakHoundUserIdentityProvider(Reload());
            Assert.False(reloaded.IsSessionValid(identity));
            Assert.True((await SignIn("jsmith", Password)).IsSuccess);
        }

        [Fact]
        public void SignInSettings_AreProviderParameters()
        {
            Assert.Null(_provider.Configuration.GetParameter(TrakHoundSignInPage.ProductParameter));

            Assert.Null(_store.SaveSettings(new TrakHoundUserSettings { SignInProduct = " Shop Floor ", SignInLogo = "/_init/assets/logo.svg", SignInRoute = "sign-in" }));

            // Normalized the way the sign in page uses them
            Assert.Equal("Shop Floor", _provider.Configuration.GetParameter(TrakHoundSignInPage.ProductParameter));
            Assert.Equal("_init/assets/logo.svg", _provider.Configuration.GetParameter(TrakHoundSignInPage.LogoParameter));
            Assert.Equal("/sign-in", _provider.Configuration.GetParameter(TrakHoundSignInPage.RouteParameter));
            Assert.Equal(TrakHoundUserIdentityProvider.ProviderId, _provider.Configuration.Id);

            // Blank values clear them
            Assert.Null(_store.SaveSettings(new TrakHoundUserSettings { SignInProduct = "Shop Floor", SignInRoute = " " }));
            Assert.Null(_provider.Configuration.GetParameter(TrakHoundSignInPage.RouteParameter));
            Assert.Null(_provider.Configuration.GetParameter(TrakHoundSignInPage.LogoParameter));
        }

        [Fact]
        public void SignInSettings_AreKeptAndNotReadAsUsers()
        {
            CreateUser("jsmith");
            Assert.Null(_store.SaveSettings(new TrakHoundUserSettings { SignInProduct = "Shop Floor", SignInRoute = "/sign-in" }));

            var reloaded = Reload();

            Assert.Equal("Shop Floor", reloaded.Settings.SignInProduct);
            Assert.Equal("/sign-in", reloaded.Settings.SignInRoute);
            Assert.Single(reloaded.GetUsers());
        }

        [Theory]
        [InlineData("https://example.com/sign-in", null)]
        [InlineData("//example.com/sign-in", null)]
        [InlineData("/sign in", null)]
        [InlineData(null, "https://example.com/logo.svg")]
        [InlineData(null, "logo.svg\"onerror=\"x")]
        public void SignInSettings_MustBeLocalPaths(string route, string logo)
        {
            Assert.NotNull(_store.SaveSettings(new TrakHoundUserSettings { SignInRoute = route, SignInLogo = logo }));
            Assert.Null(_store.Settings.SignInRoute);
            Assert.Null(_store.Settings.SignInLogo);
        }

        [Fact]
        public void SessionWithoutStamp_IsNotValid()
        {
            CreateUser();

            var identity = new TrakHoundIdentity { ProviderId = TrakHoundUserIdentityProvider.ProviderId, UserId = "jsmith" };

            Assert.False(_provider.IsSessionValid(identity));
        }

        [Fact]
        public async Task SignIn_UpgradesHashWithFewerIterations()
        {
            // A User file written with fewer Iterations (ex. by an earlier version)
            Directory.CreateDirectory(_directory);
            var salt = TrakHoundPasswordHash.CreateSalt();
            File.WriteAllText(Path.Combine(_directory, "abc123.yaml"), $"""
                id: abc123
                username: legacy
                enabled: true
                created: 2026-01-01T00:00:00.0000000Z
                sessionStamp: stamp-1
                salt: {salt}
                hash: {TrakHoundPasswordHash.Hash(Password, salt, 1000)}
                iterations: 1000
                """);
            _store.Load();

            Assert.True((await SignIn("legacy", Password)).IsSuccess);

            var reloaded = Reload().GetUserByUsername("legacy");
            Assert.Equal(TrakHoundPasswordHash.Iterations, reloaded.Iterations);
            Assert.Equal("stamp-1", reloaded.SessionStamp);
            Assert.True((await SignIn("legacy", Password)).IsSuccess);
        }
    }
}
