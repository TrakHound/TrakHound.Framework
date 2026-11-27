// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Packages;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// The packages file (trakhound-packages.json) keeps what it lists when the installed Packages are written to it again
    /// </summary>
    public class PackagesFileTests : IDisposable
    {
        private readonly string _root;
        private readonly string _source;
        private readonly string _packages;
        private readonly TrakHoundPackageManager _manager;


        public PackagesFileTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trakhound-packages-file-tests-{Guid.NewGuid()}");
            _source = Path.Combine(_root, "source");
            _packages = Path.Combine(_root, "packages");

            Directory.CreateDirectory(Path.Combine(_source, "dist"));
            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), "module");
            Directory.CreateDirectory(_packages);

            _manager = new TrakHoundPackageManager(_packages);
        }

        public void Dispose()
        {
            _manager.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        private byte[] Pack(string id, string version = "1.0.0")
        {
            var builder = new TrakHoundPackageBuilder { Category = "function", Id = id, Version = version };
            return builder.Build().Package(_source, null);
        }

        private TrakHoundPackagesFileItem Item(string id) => TrakHoundPackagesFile.Read(_packages).Packages.Single(o => o.Id == id);


        [Fact]
        public async Task Install_KeepsTheAutoUpdates()
        {
            var autoUpdates = new[] { new TrakHoundPackageUpdateConfiguration("function", "Test.A", "1.*") };
            TrakHoundPackagesFile.Write(new[] { new TrakHoundPackagesFileItem("function", "Test.Old", "1.0.0", "1.0.0") }, autoUpdates, _packages);

            await _manager.Install(Pack("Test.A"));

            var autoUpdate = Assert.Single(TrakHoundPackagesFile.Read(_packages).AutoUpdates);
            Assert.Equal("Test.A", autoUpdate.Id);
            Assert.Equal("1.*", autoUpdate.Version);
        }

        [Fact]
        public async Task RequestedVersion_IsOnlyForThePackageInstalled()
        {
            await _manager.Install(Pack("Test.A"), "1.*");
            await _manager.Install(Pack("Test.B"), "*");
            await _manager.Install(Pack("Test.C"));

            Assert.Equal("1.*", Item("Test.A").Version);
            Assert.Equal("*", Item("Test.B").Version);
            Assert.Equal("1.0.0", Item("Test.C").Version);
            Assert.Equal("1.0.0", Item("Test.A").Installed);
        }

        [Fact]
        public async Task RequestedVersion_IsKeptWhenLoadedAgain()
        {
            await _manager.Install(Pack("Test.A"), "1.*");

            _manager.Load();

            Assert.Equal("1.*", Item("Test.A").Version);
        }

        [Fact]
        public void Update_KeepsWhatTheFileListsForTheSameInstalledVersion()
        {
            var origin = new TrakHoundPackageOrigin("https://packages.contoso.com", "Contoso");
            var listed = new TrakHoundPackagesFileItem("function", "Test.A", "1.*", "1.0.0") { Origin = origin };
            var legacy = new TrakHoundPackagesFileItem("function", "Test.B", "2.0.0"); // Written before the installed Version was recorded
            TrakHoundPackagesFile.Write(new[] { listed, legacy }, path: _packages);

            TrakHoundPackagesFile.Update(new[]
            {
                new TrakHoundPackagesFileItem("function", "test.a", null, "1.0.0"),
                new TrakHoundPackagesFileItem("function", "Test.B", null, "2.0.0"),
                new TrakHoundPackagesFileItem("function", "Test.A", null, "1.1.0")
            }, _packages, keepOrigins: true);

            var items = TrakHoundPackagesFile.Read(_packages).Packages.ToList();
            Assert.Equal("1.*", items[0].Version);
            Assert.Equal("Contoso", items[0].Origin?.Organization);
            Assert.Equal("2.0.0", items[1].Version);
            Assert.Equal("2.0.0", items[1].Installed);

            // Another Version isn't the one the file listed
            Assert.Equal("1.1.0", items[2].Version);
            Assert.Null(items[2].Origin);
        }

        [Fact]
        public void Update_WithoutKeepOrigins_WritesOnlyTheItemsOrigins()
        {
            var listed = new TrakHoundPackagesFileItem("function", "Test.A", "1.0.0", "1.0.0") { Origin = new TrakHoundPackageOrigin("https://packages.contoso.com", "Contoso") };
            TrakHoundPackagesFile.Write(new[] { listed }, path: _packages);

            TrakHoundPackagesFile.Update(new[] { new TrakHoundPackagesFileItem("function", "Test.A", null, "1.0.0") }, _packages);

            Assert.Null(Item("Test.A").Origin);
        }
    }
}
