// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.IO.Compression;
using System.Text;
using TrakHound.Packages;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// Packages installed and uninstalled by another process (ex. th packages install) while a Package Manager is watching the directory
    /// </summary>
    public class PackageManagerWatcherTests : IDisposable
    {
        private const string PackageId = "Test.Function";

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private readonly string _root;
        private readonly TrakHoundPackageManager _manager;


        public PackageManagerWatcherTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trakhound-package-tests-{Guid.NewGuid()}");
            Directory.CreateDirectory(_root);

            _manager = new TrakHoundPackageManager(_root);
            _manager.Load();
        }

        public void Dispose()
        {
            _manager.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        private static byte[] CreatePackage(string version)
        {
            using (var stream = new MemoryStream())
            {
                using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, true))
                {
                    var information = archive.CreateEntry(TrakHoundPackage.InformationFilename);
                    using (var writer = new StreamWriter(information.Open(), new UTF8Encoding(false)))
                    {
                        writer.Write($"{{\"id\":\"{PackageId}\",\"category\":\"function\",\"version\":\"{version}\",\"buildDate\":\"{DateTime.UtcNow:o}\"}}");
                    }

                    var file = archive.CreateEntry("dist/readme.txt");
                    using (var writer = new StreamWriter(file.Open(), new UTF8Encoding(false))) writer.Write(version);
                }

                return stream.ToArray();
            }
        }

        private string GetLatestVersion() => _manager.GetLatest(PackageId)?.Version;

        private static async Task<bool> WaitFor(Func<bool> condition)
        {
            var stop = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < stop)
            {
                if (condition()) return true;
                await Task.Delay(100);
            }

            return condition();
        }


        [Fact]
        public async Task InstalledVersion_BecomesLatest()
        {
            Assert.True(TrakHoundPackage.Install(CreatePackage("1.0.0.1"), _root));
            Assert.True(await WaitFor(() => GetLatestVersion() == "1.0.0.1"));

            Assert.True(TrakHoundPackage.Install(CreatePackage("1.0.0.2"), _root));
            Assert.True(await WaitFor(() => GetLatestVersion() == "1.0.0.2"), $"Latest Version is {GetLatestVersion()}");
            Assert.Contains("1.0.0.2", _manager.GetVersions(PackageId));
        }

        [Fact]
        public async Task InstalledPrereleaseVersion_BecomesLatest()
        {
            Assert.True(TrakHoundPackage.Install(CreatePackage("0.0.1-e2e"), _root));
            Assert.True(await WaitFor(() => GetLatestVersion() == "0.0.1-e2e"));

            Assert.True(TrakHoundPackage.Install(CreatePackage("0.0.2-e2e"), _root));
            Assert.True(await WaitFor(() => GetLatestVersion() == "0.0.2-e2e"), $"Latest Version is {GetLatestVersion()}");
        }

        [Fact]
        public async Task UninstalledVersion_LatestFallsBack()
        {
            Assert.True(TrakHoundPackage.Install(CreatePackage("1.0.0.1"), _root));
            Assert.True(TrakHoundPackage.Install(CreatePackage("1.0.0.2"), _root));
            Assert.True(await WaitFor(() => GetLatestVersion() == "1.0.0.2"));

            Assert.True(TrakHoundPackage.RemovePackageVersion(TrakHoundPackage.GetPackageVersionDirectory(_root, "function", PackageId, "1.0.0.2")));
            Assert.True(await WaitFor(() => GetLatestVersion() == "1.0.0.1"), $"Latest Version is {GetLatestVersion()}");
            Assert.DoesNotContain("1.0.0.2", _manager.GetVersions(PackageId) ?? Enumerable.Empty<string>());
        }
    }
}
