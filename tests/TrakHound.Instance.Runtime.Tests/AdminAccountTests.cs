// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Security;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// The first Admin Account (an Owner) can only be created on the machine the Instance runs on: with the set-admin command, from the
    /// Installer's Seed file, from a Password file named by Environment Variables, or (in a container) with a generated Password written to
    /// the container's output
    /// </summary>
    public class AdminAccountTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"trakhound-admin-tests-{Guid.NewGuid()}");
        private readonly Dictionary<string, string> _environment = new Dictionary<string, string>();

        private string ConfigurationPath => Path.Combine(_root, "config");
        private string ProfilePath => Path.Combine(ConfigurationPath, "default");


        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }

        private string GetEnvironmentVariable(string name) => _environment.GetValueOrDefault(name);

        private static TrakHoundAdminAccountStore GetStore(string profilePath) => new TrakHoundAdminAccountStore(TrakHoundAdminAccountStore.GetDirectory(profilePath));

        private TrakHoundAdminAccountStore Store => GetStore(ProfilePath);

        private bool Exists(string profilePath = null) => GetStore(profilePath ?? ProfilePath).Any();

        private (int ExitCode, string Output) SetAdmin(string password, params string[] args)
        {
            var output = new StringWriter();
            var exitCode = TrakHoundAdminAccountCommand.Run(new[] { "set-admin" }.Concat(args).ToArray(), new StringReader(password + Environment.NewLine), output, false, GetEnvironmentVariable);
            return (exitCode, output.ToString());
        }


        [Fact]
        public void SetAdmin_CreatesAnOwner()
        {
            var (exitCode, output) = SetAdmin("secret-1", "admin", "--configurationPath", ConfigurationPath);

            Assert.Equal(0, exitCode);
            Assert.Contains("created", output);

            var store = Store;
            var account = Assert.Single(store.GetAccounts());
            Assert.Equal("admin", account.Username);
            Assert.True(account.IsOwner);
            Assert.NotNull(store.Verify("admin", "secret-1"));
            Assert.DoesNotContain("secret-1", File.ReadAllText(store.GetAccountPath(account.Id)));
        }

        [Fact]
        public void SetAdmin_ExistingUsername_SetsItsPassword()
        {
            SetAdmin("secret-1", "admin", "--configurationPath", ConfigurationPath);

            var (exitCode, output) = SetAdmin("secret-2", "admin", "--configurationPath", ConfigurationPath);

            Assert.Equal(0, exitCode);
            Assert.Contains("updated", output);

            var store = Store;
            Assert.Single(store.GetAccounts());
            Assert.NotNull(store.Verify("admin", "secret-2"));
            Assert.Null(store.Verify("admin", "secret-1"));
        }

        [Fact]
        public void SetAdmin_AnotherUsername_AddsAnOwner()
        {
            SetAdmin("secret-1", "admin", "--configurationPath", ConfigurationPath);

            var (exitCode, output) = SetAdmin("secret-2", "operator", "--configurationPath", ConfigurationPath);

            Assert.Equal(0, exitCode);
            Assert.Contains("created", output);

            var store = Store;
            Assert.Equal(2, store.GetAccounts().Count());
            Assert.All(store.GetAccounts(), o => Assert.True(o.IsOwner));
            Assert.NotNull(store.Verify("admin", "secret-1"));
        }

        [Fact]
        public void SetAdmin_RecoversAnAccountThatCantSignIn()
        {
            // The way back in when no Owner can sign in: a disabled Account that isn't an Owner is enabled and made one
            var store = Store;
            Assert.Null(store.SetOwner("admin", "secret-1", out _));
            Assert.Null(store.Create("viewer", null, new[] { TrakHoundAdminSecurity.ViewerRoleId }, "secret-1"));
            Assert.Null(store.SetEnabled(store.GetAccountByUsername("viewer").Id, false));

            var (exitCode, _) = SetAdmin("secret-2", "viewer", "--configurationPath", ConfigurationPath);

            Assert.Equal(0, exitCode);
            var account = Store.Verify("viewer", "secret-2");
            Assert.NotNull(account);
            Assert.True(account.IsOwner);
        }

        [Fact]
        public void LeftoverUserFile_IsNotAnAccount()
        {
            // The file the single Admin User was kept in before Admin Accounts isn't read
            Directory.CreateDirectory(ProfilePath);
            File.WriteAllText(Path.Combine(ProfilePath, ".user"), "{\"username\":\"admin\",\"salt\":\"x\",\"hash\":\"y\",\"iterations\":600000}");

            Assert.False(Exists());
        }

        [Fact]
        public void SetAdmin_UsesProfileOption()
        {
            var (exitCode, _) = SetAdmin("secret-1", "admin", "--profile", "preview", "--configurationPath", ConfigurationPath);

            Assert.Equal(0, exitCode);
            Assert.True(Exists(Path.Combine(ConfigurationPath, "preview")));
            Assert.False(Exists());
        }

        [Fact]
        public void SetAdmin_UsesInstanceEnvironmentVariables()
        {
            // The same Profile and Path as the Instance when the Options aren't given
            _environment["TRAKHOUND_CONFIGURATION_PATH"] = ConfigurationPath;
            _environment["TRAKHOUND_CONFIGURATION_PROFILE"] = "preview";

            var (exitCode, _) = SetAdmin("secret-1", "admin");

            Assert.Equal(0, exitCode);
            Assert.True(Exists(Path.Combine(ConfigurationPath, "preview")));
        }

        [Fact]
        public void SetAdmin_OptionsTakePriorityOverEnvironmentVariables()
        {
            _environment["TRAKHOUND_CONFIGURATION_PATH"] = Path.Combine(_root, "other");
            _environment["TRAKHOUND_CONFIGURATION_PROFILE"] = "preview";

            var (exitCode, _) = SetAdmin("secret-1", "admin", "--profile", "default", "--configurationPath", ConfigurationPath);

            Assert.Equal(0, exitCode);
            Assert.True(Exists());
        }

        [Theory]
        [InlineData("abc")] // Too short
        [InlineData("")]
        public void SetAdmin_InvalidPassword_DoesNotChangeTheAccount(string password)
        {
            SetAdmin("secret-1", "admin", "--configurationPath", ConfigurationPath);

            var (exitCode, output) = SetAdmin(password, "admin", "--configurationPath", ConfigurationPath);

            Assert.Equal(1, exitCode);
            Assert.Contains("not changed", output);
            Assert.NotNull(Store.Verify("admin", "secret-1"));
        }

        [Theory]
        [InlineData("")]
        [InlineData("--profile")]
        [InlineData("admin --unknown x")]
        [InlineData("admin extra")]
        public void SetAdmin_InvalidArguments_ShowsUsage(string args)
        {
            var (exitCode, output) = SetAdmin("secret-1", args.Split(' ', StringSplitOptions.RemoveEmptyEntries));

            Assert.Equal(2, exitCode);
            Assert.Contains("Usage", output);
        }

        [Fact]
        public void SetAdmin_IsRecognized()
        {
            Assert.True(TrakHoundAdminAccountCommand.IsCommand(new[] { "set-admin", "admin" }));
            Assert.False(TrakHoundAdminAccountCommand.IsCommand(new[] { "default" }));
            Assert.False(TrakHoundAdminAccountCommand.IsCommand(new string[] { }));
        }


        [Fact]
        public void StartupSeed_CreatesAnOwnerAndIsDeleted()
        {
            Directory.CreateDirectory(ProfilePath);
            var seedPath = Path.Combine(ProfilePath, TrakHoundStartupAdminUser.SeedFilename);
            File.WriteAllText(seedPath, "{\"username\":\"admin\",\"password\":\"secret-1\"}");

            var result = TrakHoundStartupAdminUser.Apply(ProfilePath, getEnvironmentVariable: GetEnvironmentVariable);

            Assert.Equal(TrakHoundStartupAdminUser.ResultType.Created, result.Result);
            Assert.Equal(TrakHoundStartupAdminUser.SourceType.Seed, result.Source);
            Assert.NotNull(Store.Verify("admin", "secret-1"));
            Assert.False(File.Exists(seedPath));
        }

        [Fact]
        public void StartupPasswordFile_CreatesAnOwnerAndIsKept()
        {
            var passwordFile = Path.Combine(_root, "admin-password");
            Directory.CreateDirectory(_root);
            File.WriteAllText(passwordFile, "secret-1\n"); // Secret files usually end with a line break
            _environment[TrakHoundStartupAdminUser.UsernameEnvironmentVariable] = "admin";
            _environment[TrakHoundStartupAdminUser.PasswordFileEnvironmentVariable] = passwordFile;

            var result = TrakHoundStartupAdminUser.Apply(ProfilePath, getEnvironmentVariable: GetEnvironmentVariable);

            Assert.Equal(TrakHoundStartupAdminUser.ResultType.Created, result.Result);
            Assert.Equal(TrakHoundStartupAdminUser.SourceType.PasswordFile, result.Source);
            Assert.NotNull(Store.Verify("admin", "secret-1"));
            Assert.True(File.Exists(passwordFile));
        }

        [Fact]
        public void StartupPasswordFile_IsIgnoredWhenAnAccountExists()
        {
            SetAdmin("secret-1", "admin", "--configurationPath", ConfigurationPath);

            var passwordFile = Path.Combine(_root, "admin-password");
            File.WriteAllText(passwordFile, "secret-2");
            _environment[TrakHoundStartupAdminUser.UsernameEnvironmentVariable] = "operator";
            _environment[TrakHoundStartupAdminUser.PasswordFileEnvironmentVariable] = passwordFile;

            var result = TrakHoundStartupAdminUser.Apply(ProfilePath, getEnvironmentVariable: GetEnvironmentVariable);

            Assert.Equal(TrakHoundStartupAdminUser.ResultType.Ignored, result.Result);
            Assert.Equal("admin", Assert.Single(Store.GetAccounts()).Username);
        }

        [Fact]
        public void StartupPasswordFile_RequiresBothVariables()
        {
            _environment[TrakHoundStartupAdminUser.UsernameEnvironmentVariable] = "admin";

            var result = TrakHoundStartupAdminUser.Apply(ProfilePath, getEnvironmentVariable: GetEnvironmentVariable);

            Assert.Equal(TrakHoundStartupAdminUser.ResultType.Invalid, result.Result);
            Assert.False(Exists());
        }

        [Fact]
        public void StartupPasswordFile_MissingFile_IsInvalid()
        {
            _environment[TrakHoundStartupAdminUser.UsernameEnvironmentVariable] = "admin";
            _environment[TrakHoundStartupAdminUser.PasswordFileEnvironmentVariable] = Path.Combine(_root, "missing");

            var result = TrakHoundStartupAdminUser.Apply(ProfilePath, getEnvironmentVariable: GetEnvironmentVariable);

            Assert.Equal(TrakHoundStartupAdminUser.ResultType.Invalid, result.Result);
            Assert.False(Exists());
        }

        [Fact]
        public void Startup_WithNothingToApply_DoesNothing()
        {
            var result = TrakHoundStartupAdminUser.Apply(ProfilePath, getEnvironmentVariable: GetEnvironmentVariable);

            Assert.Equal(TrakHoundStartupAdminUser.ResultType.None, result.Result);
        }


        private (TrakHoundStartupAdminUser Result, string Output) ApplyInContainer()
        {
            _environment[TrakHoundStartupAdminUser.ContainerEnvironmentVariable] = "true";
            var output = new StringWriter();
            var result = TrakHoundStartupAdminUser.Apply(ProfilePath, getEnvironmentVariable: GetEnvironmentVariable, output: output);
            return (result, output.ToString());
        }

        [Fact]
        public void Startup_InAContainer_GeneratesAPasswordForAdmin()
        {
            var (result, output) = ApplyInContainer();

            Assert.Equal(TrakHoundStartupAdminUser.ResultType.Created, result.Result);
            Assert.Equal(TrakHoundStartupAdminUser.SourceType.Generated, result.Source);
            Assert.Equal(TrakHoundStartupAdminUser.GeneratedUsername, result.Username);

            // The Password is only in the output, and it signs in to an Owner
            var password = System.Text.RegularExpressions.Regex.Match(output, @"Password : (\S+)").Groups[1].Value;
            Assert.False(string.IsNullOrEmpty(password));
            Assert.Null(result.Message);

            var account = Assert.Single(Store.GetAccounts());
            Assert.True(account.IsOwner);
            Assert.NotNull(Store.Verify("admin", password));
            Assert.DoesNotContain(password, File.ReadAllText(Store.GetAccountPath(account.Id)));
        }

        [Fact]
        public void Startup_InAContainer_WithAnAccount_GeneratesNothing()
        {
            SetAdmin("secret-1", "operator", "--configurationPath", ConfigurationPath);

            var (result, output) = ApplyInContainer();

            Assert.Equal(TrakHoundStartupAdminUser.ResultType.None, result.Result);
            Assert.Equal(string.Empty, output);
            Assert.Equal("operator", Assert.Single(Store.GetAccounts()).Username);
        }

        [Fact]
        public void Startup_InAContainer_WithPasswordFile_UsesThePasswordFile()
        {
            var passwordFile = Path.Combine(_root, "password");
            Directory.CreateDirectory(_root);
            File.WriteAllText(passwordFile, "secret-1\n");
            _environment[TrakHoundStartupAdminUser.UsernameEnvironmentVariable] = "operator";
            _environment[TrakHoundStartupAdminUser.PasswordFileEnvironmentVariable] = passwordFile;

            var (result, output) = ApplyInContainer();

            Assert.Equal(TrakHoundStartupAdminUser.SourceType.PasswordFile, result.Source);
            Assert.Equal(string.Empty, output);
            Assert.Equal("operator", Assert.Single(Store.GetAccounts()).Username);
        }

        [Fact]
        public void Startup_InAContainer_WithAnInvalidPasswordFileSetting_DoesNotGenerate()
        {
            // A mistake in the settings is reported, rather than replaced by a generated Password
            _environment[TrakHoundStartupAdminUser.UsernameEnvironmentVariable] = "operator";

            var (result, output) = ApplyInContainer();

            Assert.Equal(TrakHoundStartupAdminUser.ResultType.Invalid, result.Result);
            Assert.Equal(string.Empty, output);
            Assert.False(Exists());
        }

        [Theory]
        [InlineData("false")]
        [InlineData("No")]
        [InlineData("0")]
        public void Startup_InAContainer_GenerateOff_GeneratesNothing(string value)
        {
            _environment[TrakHoundStartupAdminUser.GenerateEnvironmentVariable] = value;

            var (result, output) = ApplyInContainer();

            Assert.Equal(TrakHoundStartupAdminUser.ResultType.None, result.Result);
            Assert.Equal(string.Empty, output);
            Assert.False(Exists());
        }

        [Fact]
        public void Startup_OutsideAContainer_GeneratesNothing()
        {
            var output = new StringWriter();
            var result = TrakHoundStartupAdminUser.Apply(ProfilePath, getEnvironmentVariable: GetEnvironmentVariable, output: output);

            Assert.Equal(TrakHoundStartupAdminUser.ResultType.None, result.Result);
            Assert.Equal(string.Empty, output.ToString());
            Assert.False(Exists());
        }

        [Fact]
        public void GeneratedPasswords_AreLongAndDifferent()
        {
            var passwords = Enumerable.Range(0, 20).Select(_ => TrakHoundStartupAdminUser.GeneratePassword()).ToList();

            // Lowercase, with no characters that are easy to mistake for each other
            Assert.All(passwords, o => Assert.Matches("^[a-z2-9]{4}-[a-z2-9]{4}$", o));
            Assert.All(passwords, o => Assert.DoesNotMatch("[01ilo]", o));
            Assert.All(passwords, o => Assert.Null(TrakHoundAdminAccountStore.ValidatePassword(o)));
            Assert.Equal(passwords.Count, passwords.Distinct().Count());
        }


        [Fact]
        public void AdminAuthenticationService_CannotCreateAnAccount()
        {
            // Signing in never creates an Account (the first visitor to a new Instance must not be able to claim it). More Accounts are
            // created by a signed in Owner, through the store
            var methods = typeof(AdminAuthenticationService).GetMethods().Select(o => o.Name).ToList();

            Assert.DoesNotContain("Setup", methods);
            Assert.DoesNotContain(methods, o => o.Contains("Create") || o.Contains("SetPassword"));
        }
    }
}
