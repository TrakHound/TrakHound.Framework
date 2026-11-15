// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.CLI.Packages;
using PackagesCommands = TrakHound.CLI.Packages.Packages;

namespace TrakHound.CLI.Tests
{
    /// <summary>
    /// Publish Targets (trakhound.package.publish.json) : finding the file, choosing the Target, and checking it before a build
    /// </summary>
    public class PublishTargetsTests
    {
        private const string Targets = """
            {
              // Comments and trailing commas are allowed
              "defaultTarget": "development",
              "targets": {
                "development": {
                  "destinations": [ { "managementServer": "http://dev.test", "organization": "Dev" } ],
                  "versionSuffix": "dev{branch}.{timestamp}",
                  "allowDirty": true,
                },
                "Release": {
                  "destinations": [ { "managementServer": "https://packages.test", "organization": "Contoso", "apiTokenVariable": "TEST_RELEASE_TOKEN" } ],
                  "requireSigning": true
                },
                "plain": {
                  "destinations": [ { "managementServer": "http://plain.test", "organization": "Plain" } ]
                }
              }
            }
            """;

        private static string Package(TestRepository repo) => repo.PathOf("pkg");

        private static TestRepository CreateRepository(string targets = Targets, string prefix = "0.3")
        {
            var repo = new TestRepository();
            repo.WritePackage("pkg", prefix);
            if (targets != null) repo.Write("trakhound.package.publish.json", targets);
            repo.Commit();
            return repo;
        }


        [Fact]
        public void NoFile_HasNoTarget()
        {
            using var repo = CreateRepository(targets: null);

            var settings = PackagesCommands.ReadPublishSettings(Package(repo), log: false);
            Assert.NotNull(settings);
            Assert.False(settings.HasTarget);
            Assert.Null(settings.Destinations);
        }

        [Fact]
        public void NoFile_WithATarget_IsAnError()
        {
            using var repo = CreateRepository(targets: null);

            Assert.Null(PackagesCommands.ReadPublishSettings(Package(repo), "release", log: false));
        }

        [Fact]
        public void TheDefaultTargetIsUsed()
        {
            using var repo = CreateRepository();

            var settings = PackagesCommands.ReadPublishSettings(Package(repo), log: false);
            Assert.Equal("development", settings.TargetName);
            Assert.Equal("http://dev.test", Assert.Single(settings.Destinations).ManagementServer);
            Assert.Equal(repo.PathOf("trakhound.package.publish.json"), settings.Path);
        }

        [Fact]
        public void TargetNamesAreCaseInsensitive()
        {
            using var repo = CreateRepository();

            var settings = PackagesCommands.ReadPublishSettings(Package(repo), "release", log: false);
            Assert.Equal("Release", settings.TargetName);
            Assert.True(settings.Target.RequireSigning);
            Assert.Equal("TEST_RELEASE_TOKEN", Assert.Single(settings.Destinations).ApiTokenVariable);
        }

        [Fact]
        public void UnknownTarget_IsAnError()
        {
            using var repo = CreateRepository();

            Assert.Null(PackagesCommands.ReadPublishSettings(Package(repo), "staging", log: false));
        }

        [Fact]
        public void NoDefaultTarget_IsAnError()
        {
            using var repo = CreateRepository(Targets.Replace("\"defaultTarget\": \"development\",", ""));

            Assert.Null(PackagesCommands.ReadPublishSettings(Package(repo), log: false));
            Assert.NotNull(PackagesCommands.ReadPublishSettings(Package(repo), "plain", log: false));
        }

        [Fact]
        public void TheNearestFileIsUsedAsAWhole()
        {
            using var repo = CreateRepository();
            repo.Write("pkg/trakhound.package.publish.json", """{ "defaultTarget": "own", "targets": { "own": { "destinations": [ { "managementServer": "http://own.test" } ] } } }""");

            var settings = PackagesCommands.ReadPublishSettings(Package(repo), log: false);
            Assert.Equal("own", settings.TargetName);

            // The root file's Targets aren't merged in
            Assert.Null(PackagesCommands.ReadPublishSettings(Package(repo), "release", log: false));
        }

        [Theory]
        [InlineData("""{ "version": "1.0.0", "defaultTarget": "a", "targets": { "a": { "destinations": [ { "organization": "A" } ] } } }""")]
        [InlineData("""{ "destinations": [ { "managementServer": "http://old.test" } ] }""")]
        [InlineData("""{ "defaultTarget": "a", "targets": { "a": { "destinations": [ { "organization": "A" } ], "versionSufix": "dev" } } }""")]
        [InlineData("""{ "defaultTarget": "a", "targets": { "a": { "destinations": [ { "organization": "A", "apiKey": "secret" } ] } } }""")]
        [InlineData("""{ "defaultTarget": "a", "targets": { "a": { "destinations": [ ] } } }""")]
        [InlineData("""{ "defaultTarget": "a", "targets": { } }""")]
        [InlineData("""{ "defaultTarget": "a", "targets": { "a": { "destinations": [ { "organization": "A" } ], "allowDirty": true, "versionSuffix": "dev" } } }""")]
        [InlineData("""{ "defaultTarget": "a", "targets": { "a": { "destinations": [ { "organization": "A" } ], "versionSuffix": "dev {timestamp}" } } }""")]
        [InlineData("not json")]
        public void InvalidFile_IsAnError(string json)
        {
            // A leftover from the old format, a misspelled or removed setting, or a Target that can't be used is reported instead of ignored
            using var repo = CreateRepository(json);

            Assert.Null(PackagesCommands.ReadPublishSettings(Package(repo), log: false));
        }

        [Fact]
        public void AMistakeInAnotherTarget_IsAnError()
        {
            // Every Target is checked, so a mistake is found before the Target is used
            using var repo = CreateRepository(Targets.Replace("\"requireSigning\": true", "\"requireSigning\": true, \"allowDirty\": true"));

            Assert.Null(PackagesCommands.ReadPublishSettings(Package(repo), "plain", log: false));
        }


        [Fact]
        public void Version_UsesTheTargetsSuffix()
        {
            using var repo = CreateRepository();
            var information = repo.PathOf("pkg/trakhound.package.json");

            var development = PackagesCommands.ReadPublishSettings(Package(repo), log: false);
            Assert.Equal($"0.3.1-dev.{PackageVersions.Timestamp}", PackagesCommands.GetPackageVersion(Package(repo), information, null, null, development, log: false));

            var release = PackagesCommands.ReadPublishSettings(Package(repo), "release", log: false);
            Assert.Equal("0.3.1", PackagesCommands.GetPackageVersion(Package(repo), information, null, null, release, log: false));
        }

        [Fact]
        public void Version_OnABranch_HasTheBranchInItsSuffix()
        {
            using var repo = CreateRepository();
            repo.Checkout("feature/opc-ua", create: true);

            var development = PackagesCommands.ReadPublishSettings(Package(repo), log: false);
            Assert.Equal($"0.3.1-dev-feature-opc-ua.{PackageVersions.Timestamp}", PackagesCommands.GetPackageVersion(Package(repo), repo.PathOf("pkg/trakhound.package.json"), null, null, development, log: false));
        }

        [Fact]
        public void Version_Options_ReplaceTheTargets()
        {
            using var repo = CreateRepository();
            var information = repo.PathOf("pkg/trakhound.package.json");
            var development = PackagesCommands.ReadPublishSettings(Package(repo), log: false);

            Assert.Equal("0.3.1-beta.2", PackagesCommands.GetPackageVersion(Package(repo), information, null, "beta.2", development, log: false));
            Assert.Equal($"2.0.0-dev.{PackageVersions.Timestamp}", PackagesCommands.GetPackageVersion(Package(repo), information, "2.0.0", null, development, log: false));

            // A --version that's already a Prerelease is used as is
            Assert.Equal("2.0.0-rc.1", PackagesCommands.GetPackageVersion(Package(repo), information, "2.0.0-rc.1", null, development, log: false));
        }

        [Fact]
        public void Version_WithoutATarget_HasNoSuffix()
        {
            // pack, and dotnet without --publish
            using var repo = CreateRepository();

            Assert.Equal("0.3.1", PackagesCommands.GetPackageVersion(Package(repo), repo.PathOf("pkg/trakhound.package.json"), null, null, null, log: false));
        }

        [Fact]
        public void Version_InvalidSuffixOption_IsAnError()
        {
            using var repo = CreateRepository();

            Assert.Null(PackagesCommands.GetPackageVersion(Package(repo), repo.PathOf("pkg/trakhound.package.json"), null, "dev build", null, log: false));
        }


        [Fact]
        public void ServerOptions_AreOnlyForAPackageWithoutTargets()
        {
            using var repo = CreateRepository();
            var settings = PackagesCommands.ReadPublishSettings(Package(repo), log: false);

            Assert.False(PackagesCommands.CheckServerOptions(settings, "http://other.test", null));
            Assert.False(PackagesCommands.CheckServerOptions(settings, null, "Other"));
            Assert.True(PackagesCommands.CheckServerOptions(settings, null, null));

            using var noTargets = CreateRepository(targets: null);
            Assert.True(PackagesCommands.CheckServerOptions(PackagesCommands.ReadPublishSettings(Package(noTargets), log: false), "http://other.test", "Other"));
        }

        [Fact]
        public void ApiTokens_MustBeSet()
        {
            var variable = $"TEST_TOKEN_{Guid.NewGuid():N}";
            using var repo = CreateRepository(Targets.Replace("TEST_RELEASE_TOKEN", variable));
            var release = PackagesCommands.ReadPublishSettings(Package(repo), "release", log: false);

            Assert.False(PackagesCommands.CheckApiTokens(release));

            Environment.SetEnvironmentVariable(variable, "token");
            try
            {
                Assert.True(PackagesCommands.CheckApiTokens(release));
                Assert.Equal("token", PackageClient.GetApiToken(release.Destinations[0]));
            }
            finally
            {
                Environment.SetEnvironmentVariable(variable, null);
            }

            // A Destination without a variable uses the CLI's Api Token
            Assert.True(PackagesCommands.CheckApiTokens(PackagesCommands.ReadPublishSettings(Package(repo), "plain", log: false)));
        }


        [Fact]
        public void RequireSigning_NeedsASigningKey()
        {
            using var repo = CreateRepository();
            var release = PackagesCommands.ReadPublishSettings(Package(repo), "release", log: false);

            var allowDirty = false;
            Assert.False(PackagesCommands.CheckPublishTarget(release, Package(repo), false, ref allowDirty));
            Assert.True(PackagesCommands.CheckPublishTarget(release, Package(repo), true, ref allowDirty));
        }

        [Fact]
        public void UncommittedChanges_OnlyAllowDirtyTargetsBuildThem()
        {
            using var repo = CreateRepository();
            repo.Write("pkg/changed.txt", "uncommitted");

            // A Target with allowDirty builds them, and lets the build sign them
            var allowDirty = false;
            Assert.True(PackagesCommands.CheckPublishTarget(PackagesCommands.ReadPublishSettings(Package(repo), "development", log: false), Package(repo), true, ref allowDirty));
            Assert.True(allowDirty);

            // A Target without it publishes the version of the commit : --allow-dirty overrides that, unless the Target requires signing
            var plain = PackagesCommands.ReadPublishSettings(Package(repo), "plain", log: false);
            allowDirty = false;
            Assert.False(PackagesCommands.CheckPublishTarget(plain, Package(repo), false, ref allowDirty));
            allowDirty = true;
            Assert.True(PackagesCommands.CheckPublishTarget(plain, Package(repo), false, ref allowDirty));

            var release = PackagesCommands.ReadPublishSettings(Package(repo), "release", log: false);
            allowDirty = true;
            Assert.False(PackagesCommands.CheckPublishTarget(release, Package(repo), true, ref allowDirty));
        }

        [Fact]
        public void CommittedChanges_AreAllowedEverywhere()
        {
            using var repo = CreateRepository();
            var release = PackagesCommands.ReadPublishSettings(Package(repo), "release", log: false);

            var allowDirty = false;
            Assert.True(PackagesCommands.CheckPublishTarget(release, Package(repo), true, ref allowDirty));
            Assert.False(allowDirty);
        }
    }
}
