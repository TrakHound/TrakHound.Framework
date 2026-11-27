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
                  "destinations": [ { "packageServer": "http://dev.test", "organization": "Dev" } ],
                  "versionSuffix": "dev{branch}.{timestamp}",
                  "allowDirty": true,
                },
                "Release": {
                  "destinations": [ { "packageServer": "https://packages.test", "organization": "Contoso", "apiTokenVariable": "TEST_RELEASE_TOKEN" } ],
                  "requireSigning": true
                },
                "plain": {
                  "destinations": [ { "packageServer": "http://plain.test", "organization": "Plain" } ]
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
            Assert.Equal("http://dev.test", Assert.Single(settings.Destinations).PackageServer);
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
        public void SchemaProperty_IsAllowed()
        {
            using var repo = CreateRepository(Targets.Replace("\"defaultTarget\"", "\"$schema\": \"https://www.trakhound.com/schemas/trakhound.package.publish.schema.json\",\n  \"defaultTarget\""));

            var settings = PackagesCommands.ReadPublishSettings(Package(repo), log: false);
            Assert.NotNull(settings);
            Assert.Equal("development", settings.TargetName);
        }

        [Fact]
        public void UnknownProperty_IsAnError()
        {
            using var repo = CreateRepository(Targets.Replace("\"defaultTarget\"", "\"defaultTargets\": \"x\",\n  \"defaultTarget\""));

            Assert.Null(PackagesCommands.ReadPublishSettings(Package(repo), log: false));
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
            repo.Write("pkg/trakhound.package.publish.json", """{ "defaultTarget": "own", "targets": { "own": { "destinations": [ { "packageServer": "http://own.test" } ] } } }""");

            var settings = PackagesCommands.ReadPublishSettings(Package(repo), log: false);
            Assert.Equal("own", settings.TargetName);

            // The root file's Targets aren't merged in
            Assert.Null(PackagesCommands.ReadPublishSettings(Package(repo), "release", log: false));
        }

        [Fact]
        public void ANearerFile_ReportsTheOneItHides()
        {
            using var repo = CreateRepository();
            Assert.Null(PackagesCommands.GetHiddenPublishSettings(repo.PathOf("trakhound.package.publish.json")));

            repo.Write("pkg/trakhound.package.publish.json", """{ "defaultTarget": "own", "targets": { "own": { "destinations": [ { "packageServer": "http://own.test" } ] } } }""");
            Assert.Equal(repo.PathOf("trakhound.package.publish.json"), PackagesCommands.GetHiddenPublishSettings(repo.PathOf("pkg/trakhound.package.publish.json")));
        }

        [Fact]
        public void TheTemplatesLocalTarget_IsValid()
        {
            // trakhound.package.publish.json as the TrakHound templates create it (without --packageServer or --organization)
            using var repo = CreateRepository(targets: null);
            repo.Write("pkg/trakhound.package.publish.json", """
                {
                  // Publish Targets for this package
                  "defaultTarget": "local",
                  "targets": {
                    // The Dev Package Server of the development Instance
                    "local": {
                      "destinations": [
                        {
                          "packageServer": "http://localhost:8472",
                          "organization": "Dev"
                        }
                      ],
                      "versionSuffix": "dev{branch}.{timestamp}",
                      "allowDirty": true
                    }
                  }
                }
                """);
            repo.Write("pkg/changed.txt", "uncommitted");

            var settings = PackagesCommands.ReadPublishSettings(Package(repo), log: false);
            Assert.Equal("local", settings.TargetName);
            var destination = Assert.Single(settings.Destinations);
            Assert.Equal("http://localhost:8472", destination.PackageServer);
            Assert.Equal("Dev", destination.Organization);

            // Published again with uncommitted changes, as a prerelease
            var allowDirty = false;
            Assert.True(PackagesCommands.CheckPublishTarget(settings, Package(repo), false, ref allowDirty));
            Assert.StartsWith("0.3.", PackagesCommands.GetPackageVersion(Package(repo), repo.PathOf("pkg/trakhound.package.json"), null, null, settings, log: false));
            Assert.Contains("-dev.", PackagesCommands.GetPackageVersion(Package(repo), repo.PathOf("pkg/trakhound.package.json"), null, null, settings, log: false));
        }

        [Theory]
        [InlineData("""{ "version": "1.0.0", "defaultTarget": "a", "targets": { "a": { "destinations": [ { "organization": "A" } ] } } }""")]
        [InlineData("""{ "destinations": [ { "packageServer": "http://old.test" } ] }""")]
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
        public void Server_ReplacesTheTargetsServer_AndKeepsItsOrganizationAndSettings()
        {
            // ex. the 'local' Target published to a development container on another port
            using var repo = CreateRepository();
            var settings = PackagesCommands.ReadPublishSettings(Package(repo), log: false);

            Assert.True(PackagesCommands.ApplyServerOptions(settings, "http://localhost:32772", null, log: false));

            var destination = Assert.Single(settings.Destinations);
            Assert.Equal("http://localhost:32772", destination.PackageServer);
            Assert.Equal("Dev", destination.Organization);
            Assert.Equal("development", settings.TargetName);
            Assert.True(settings.Target.AllowDirty);
        }

        [Fact]
        public void Organization_ReplacesTheTargetsOrganization_AndKeepsItsServerAndToken()
        {
            using var repo = CreateRepository();
            var settings = PackagesCommands.ReadPublishSettings(Package(repo), "release", log: false);

            Assert.True(PackagesCommands.ApplyServerOptions(settings, null, "Other", log: false));

            var destination = Assert.Single(settings.Destinations);
            Assert.Equal("https://packages.test", destination.PackageServer);
            Assert.Equal("Other", destination.Organization);
            Assert.Equal("TEST_RELEASE_TOKEN", destination.ApiTokenVariable);
        }

        [Fact]
        public void AnotherServer_DoesNotGetTheTargetsApiToken()
        {
            // The token is for the Target's own server, so it isn't sent anywhere else
            using var repo = CreateRepository();
            var settings = PackagesCommands.ReadPublishSettings(Package(repo), "release", log: false);

            Assert.True(PackagesCommands.ApplyServerOptions(settings, "http://other.test", null, log: false));

            var destination = Assert.Single(settings.Destinations);
            Assert.Equal("Contoso", destination.Organization);
            Assert.Null(destination.ApiTokenVariable);
        }

        [Fact]
        public void ServerOptions_WithManyDestinations_NeedBoth()
        {
            using var repo = CreateRepository(Targets.Replace(
                """[ { "packageServer": "http://plain.test", "organization": "Plain" } ]""",
                """[ { "packageServer": "http://plain.test", "organization": "Plain" }, { "packageServer": "http://second.test", "organization": "Second" } ]"""));

            Assert.False(PackagesCommands.ApplyServerOptions(PackagesCommands.ReadPublishSettings(Package(repo), "plain", log: false), "http://other.test", null, log: false));

            var settings = PackagesCommands.ReadPublishSettings(Package(repo), "plain", log: false);
            Assert.True(PackagesCommands.ApplyServerOptions(settings, "http://other.test", "Other", log: false));
            Assert.Equal("http://other.test", Assert.Single(settings.Destinations).PackageServer);
        }

        [Fact]
        public void NoServerOptions_KeepTheTargetsDestinations()
        {
            using var repo = CreateRepository();
            var settings = PackagesCommands.ReadPublishSettings(Package(repo), log: false);

            Assert.True(PackagesCommands.ApplyServerOptions(settings, null, null, log: false));
            Assert.Equal("http://dev.test", Assert.Single(settings.Destinations).PackageServer);

            using var noTargets = CreateRepository(targets: null);
            Assert.True(PackagesCommands.ApplyServerOptions(PackagesCommands.ReadPublishSettings(Package(noTargets), log: false), "http://other.test", "Other", log: false));
        }

        [Fact]
        public void ManagementServer_IsNowPackageServer()
        {
            using var repo = CreateRepository("""{ "defaultTarget": "a", "targets": { "a": { "destinations": [ { "managementServer": "http://old.test", "organization": "A" } ] } } }""");

            Assert.Null(PackagesCommands.ReadPublishSettings(Package(repo), log: false));
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
