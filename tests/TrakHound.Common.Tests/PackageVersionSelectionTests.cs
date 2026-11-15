// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.IO.Compression;
using System.Text;
using TrakHound.Extensions;
using TrakHound.Logging;
using TrakHound.Modules;
using TrakHound.Packages;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// Which installed Version a Wildcard Version ("*") selects, including Versions with a Prerelease Label (ex. 0.0.2-e2e)
    /// </summary>
    public class PackageVersionSelectionTests : IDisposable
    {
        private const string PackageId = "Test.Service";
        private const string Category = "service";

        private readonly string _root;


        public PackageVersionSelectionTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trakhound-package-tests-{Guid.NewGuid()}");
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }


        private void Install(string version)
        {
            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    var information = archive.CreateEntry(TrakHoundPackage.InformationFilename);
                    using (var writer = new StreamWriter(information.Open(), new UTF8Encoding(false)))
                    {
                        writer.Write($"{{\"id\":\"{PackageId}\",\"category\":\"{Category}\",\"version\":\"{version}\",\"buildDate\":\"{DateTime.UtcNow:o}\"}}");
                    }

                    var file = archive.CreateEntry("dist/readme.txt");
                    using (var writer = new StreamWriter(file.Open(), new UTF8Encoding(false))) writer.Write(version);
                }

                Assert.True(TrakHoundPackage.Install(stream.ToArray(), _root));
            }
        }

        private TrakHoundPackageManager LoadManager()
        {
            var manager = new TrakHoundPackageManager(_root);
            manager.Load();
            return manager;
        }


        [Theory]
        [InlineData("0.0.1-e2e", "0.0.2-e2e")]
        [InlineData("0.0.9-e2e", "0.0.10-e2e")]
        [InlineData("1.0.0-beta", "1.0.0")]
        [InlineData("1.0.0-alpha", "1.0.0-beta")]
        [InlineData("0.0.1", "0.0.2")]
        [InlineData("1.9.0", "1.10.0")]
        public void Latest_IsNewestVersion(string older, string newer)
        {
            Install(older);
            Install(newer);

            using var manager = LoadManager();

            Assert.Equal(newer, manager.GetLatest(PackageId)?.Version);
        }

        [Theory]
        [InlineData("*", "1.9.0")]
        [InlineData("*-*", "2.0.0-beta")]
        [InlineData("1.*", "1.9.0")]
        [InlineData("1.*-*", "1.10.0-rc")]
        [InlineData("2.*", "2.0.0-beta")]
        [InlineData("2.*-*", "2.0.0-beta")]
        [InlineData("1.10.0-*", "1.10.0-rc")]
        [InlineData("3.*", null)]
        public void Resolve_PrefersStable_UnlessPrereleasesAreIncluded(string requested, string expected)
        {
            Install("1.9.0");
            Install("1.10.0-rc");
            Install("2.0.0-beta");

            using var manager = LoadManager();

            Assert.Equal(expected, manager.Resolve(PackageId, requested)?.Version);

            var directory = TrakHoundPackage.GetPackageVersionDirectory(_root, Category, PackageId, requested);
            Assert.Equal(expected, directory != null ? Path.GetFileName(directory) : null);
        }

        [Fact]
        public void Latest_StaysStable_WhenPrereleaseIsNewer()
        {
            Install("1.0.0");
            Install("2.0.0-beta");

            using var manager = LoadManager();

            Assert.Equal("1.0.0", manager.GetLatest(PackageId)?.Version);
            Assert.Equal("1.0.0", Path.GetFileName(TrakHoundPackage.GetLatestPackageDirectory(_root, Category, PackageId)));
        }

        [Fact]
        public void Latest_FallsBackToPrerelease_WhenStableIsRemoved()
        {
            Install("1.0.0");
            Install("2.0.0-beta");

            using var manager = LoadManager();

            Assert.True(manager.Uninstall(PackageId, "1.0.0"));

            Assert.Equal("2.0.0-beta", manager.GetLatest(PackageId)?.Version);
        }

        [Theory]
        [InlineData("*", true)]
        [InlineData("*-*", false)]
        public void ModuleManager_LogsSkippedPrerelease(string requested, bool expected)
        {
            Install("1.0.0");
            Install("2.0.0-beta");

            var messages = new List<string>();
            var logger = new TrakHoundLogger("test");
            logger.LogEntryReceived += (_, entry) => messages.Add(entry.Message);

            try
            {
                using var manager = LoadManager();
                using var moduleManager = new TrakHoundModuleManager<object>(manager, Category) { Logger = logger };

                moduleManager.Get(PackageId, requested);

                Assert.Equal(expected, messages.Any(o => o.Contains("Newer Prerelease Not Selected") && o.Contains("2.0.0-beta")));
            }
            finally
            {
                var modules = Path.Combine(AppContext.BaseDirectory, TrakHoundModuleManager<object>._directory);
                if (Directory.Exists(modules)) Directory.Delete(modules, true);
            }
        }

        [Fact]
        public void Latest_FallsBackToNextNewest_WhenRemoved()
        {
            Install("0.0.1-e2e");
            Install("0.0.2-e2e");
            Install("0.0.10-e2e");

            using var manager = LoadManager();

            Assert.True(manager.Uninstall(PackageId, "0.0.10-e2e"));

            Assert.Equal("0.0.2-e2e", manager.GetLatest(PackageId)?.Version);
        }

        // A leftover copy of the older Version must not matter. The Version is selected from the Packages folder, and _modules is only read afterwards.
        // The Package has no Assembly, so the recorded Failure shows which Version the Module Manager selected
        [Fact]
        public void ModuleManager_Wildcard_SelectsNewestVersion_WithLeftoverModulesFolder()
        {
            Install("0.0.1-e2e");
            Install("0.0.2-e2e");

            var leftover = Path.Combine(AppContext.BaseDirectory, TrakHoundModuleManager<object>._directory, $"{PackageId}-0.0.1-e2e-00000000");
            Directory.CreateDirectory(leftover);

            try
            {
                using var manager = LoadManager();
                using var moduleManager = new TrakHoundModuleManager<object>(manager, Category);

                Assert.Null(moduleManager.Get(PackageId, "*"));

                var information = Assert.Single(moduleManager.GetInformation());
                Assert.Equal("0.0.2-e2e", information.PackageVersion);
            }
            finally
            {
                Directory.Delete(Path.GetDirectoryName(leftover), true);
            }
        }

        [Theory]
        [InlineData("*", "0.0.10-e2e")]
        [InlineData("0.*", "0.0.10-e2e")]
        [InlineData("0.0.*", "0.0.10-e2e")]
        public void ModuleManager_PartialWildcard_SelectsNewestVersion(string requested, string expected)
        {
            Install("0.0.9-e2e");
            Install("0.0.10-e2e");

            try
            {
                using var manager = LoadManager();
                using var moduleManager = new TrakHoundModuleManager<object>(manager, Category);

                Assert.Null(moduleManager.Get(PackageId, requested));

                Assert.Equal(expected, Assert.Single(moduleManager.GetInformation()).PackageVersion);
            }
            finally
            {
                var modules = Path.Combine(AppContext.BaseDirectory, TrakHoundModuleManager<object>._directory);
                if (Directory.Exists(modules)) Directory.Delete(modules, true);
            }
        }

        [Fact]
        public void LatestDirectory_IsNewestVersion()
        {
            Install("0.0.9-e2e");
            Install("0.0.10-e2e");

            var directory = TrakHoundPackage.GetLatestPackageDirectory(_root, Category, PackageId);

            Assert.Equal("0.0.10-e2e", Path.GetFileName(directory));
        }

        [Fact]
        public void WildcardDirectory_IsNewestVersion()
        {
            Install("0.0.9-e2e");
            Install("0.0.10-e2e");

            var directory = TrakHoundPackage.GetPackageVersionDirectory(_root, Category, PackageId, "*");

            Assert.Equal("0.0.10-e2e", Path.GetFileName(directory));
        }


        [Theory]
        [InlineData("1.0.0-beta", "*", true)]
        [InlineData("1.2.3-beta", "1.*", true)]
        [InlineData("1.2.3-beta", "1.2.*", true)]
        [InlineData("1.2.3-beta", "2.*", false)]
        [InlineData("1.2.3-beta", "*-*", true)]
        [InlineData("1.2.3", "*-*", true)]
        [InlineData("1.2.3-beta", "1.*-*", true)]
        [InlineData("1.2.3-beta", "2.*-*", false)]
        [InlineData("1.2.3-beta", "1.2.3-*", true)]
        [InlineData("1.2.4-beta", "1.2.3-*", false)]
        [InlineData("1.2.3-beta", "1.2.3-beta", true)]
        [InlineData("1.2.3-beta", "1.2.3", false)]
        [InlineData("1.2.3", "1.*", true)]
        [InlineData("not-a-version", "*", false)]
        public void MatchVersion_IgnoresPrereleaseLabel_ForWildcards(string target, string match, bool expected)
        {
            Assert.Equal(expected, StringFunctions.MatchVersion(target, match));
        }

        [Fact]
        public void PreferStableComparer_OrdersStableAbovePrereleases()
        {
            var ordered = new[] { "not-a-version", "1.0.0-beta", "3.0.0-beta", "1.0.0", "2.0.0" };

            Assert.Equal(ordered, Enumerable.Reverse(ordered).OrderBy(o => o, VersionComparer.PreferStable));
        }

        [Fact]
        public void Comparer_OrdersVersions()
        {
            var ordered = new[]
            {
                "not-a-version",
                "1.0.0-alpha",
                "1.0.0-alpha.1",
                "1.0.0-alpha.2",
                "1.0.0-alpha.10",
                "1.0.0-alpha.beta",
                "1.0.0-beta",
                "1.0.0-rc.1",
                "1.0.0",
                "1.0.1-dev",
                "1.0.1",
                "1.10.0"
            };

            var shuffled = Enumerable.Reverse(ordered).ToList();

            Assert.Equal(ordered, shuffled.OrderBy(o => o, VersionComparer.Default));
        }

        [Theory]
        [InlineData("1.0.0-beta", "1.0.0", false)]
        [InlineData("1.1.0-beta", "1.0.0", true)]
        [InlineData("1.0.0", "1.0.0", true)]
        public void Dependency_MinimumVersion_ComparesPrereleases(string version, string minimum, bool expected)
        {
            var dependency = new TrakHoundPackageDependency(PackageId, null, minimum);

            Assert.Equal(expected, TrakHoundPackageDependency.IsSatisfiedBy(dependency, version));
        }
    }
}
