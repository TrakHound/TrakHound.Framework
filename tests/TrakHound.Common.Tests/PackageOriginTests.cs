// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Packages;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// Where installed Packages were downloaded from (their origin), recorded in the packages file
    /// </summary>
    public class PackageOriginTests : IDisposable
    {
        private const string PackageId = "Test.Function";

        private readonly string _root;
        private readonly string _source;
        private readonly string _packages;
        private readonly TrakHoundPackageManager _manager;


        public PackageOriginTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trakhound-origin-tests-{Guid.NewGuid()}");
            _source = Path.Combine(_root, "source");
            _packages = Path.Combine(_root, "packages");

            Directory.CreateDirectory(Path.Combine(_source, "dist"));
            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), "module");

            _manager = new TrakHoundPackageManager(_packages);
        }

        public void Dispose()
        {
            _manager.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        private byte[] Pack(string version = "1.0.0")
        {
            var builder = new TrakHoundPackageBuilder { Category = "function", Id = PackageId, Version = version };
            return builder.Build().Package(_source, null);
        }

        private static readonly TrakHoundPackageOrigin Contoso = new TrakHoundPackageOrigin("https://packages.contoso.com/", "Contoso");


        [Fact]
        public async Task Install_RecordsTheOrigin_AndKeepsItWhenLoadedAgain()
        {
            Assert.True(Assert.Single(await _manager.Install(Pack(), origin: Contoso)).Success);

            Assert.True(_manager.GetOrigin(PackageId, "1.0.0").Matches("https://packages.contoso.com", "contoso"));

            var item = Assert.Single(TrakHoundPackagesFile.Read(_packages).Packages);
            Assert.Equal("Contoso", item.Origin.Organization);

            // Read back from the packages file
            _manager.Load();
            Assert.Equal("Contoso", _manager.GetOrigin(PackageId, "1.0.0")?.Organization);
        }

        [Fact]
        public async Task InstallFromAFile_ClearsTheOrigin()
        {
            await _manager.Install(Pack(), origin: Contoso);
            await _manager.Install(Pack());

            Assert.Null(_manager.GetOrigin(PackageId, "1.0.0"));
            Assert.Null(Assert.Single(TrakHoundPackagesFile.Read(_packages).Packages).Origin);
        }

        [Fact]
        public async Task EachVersion_HasItsOwnOrigin()
        {
            await _manager.Install(Pack("1.0.0"), origin: Contoso);
            await _manager.Install(Pack("2.0.0"), origin: new TrakHoundPackageOrigin("https://packages.trakhound.com", "TrakHound"));

            Assert.Equal("Contoso", _manager.GetOrigin(PackageId, "1.0.0")?.Organization);
            Assert.Equal("TrakHound", _manager.GetOrigin(PackageId, "2.0.0")?.Organization);

            Assert.True(_manager.Uninstall(PackageId, "2.0.0"));
            Assert.Null(_manager.GetOrigin(PackageId, "2.0.0"));
            Assert.Equal("Contoso", _manager.GetOrigin(PackageId, "1.0.0")?.Organization);
        }

        [Fact]
        public void Matches_IgnoresCaseAndATrailingSlash()
        {
            Assert.True(Contoso.Matches("HTTPS://packages.contoso.com", "CONTOSO"));
            Assert.False(Contoso.Matches("https://packages.contoso.com", "Public"));
            Assert.False(Contoso.Matches("https://packages.trakhound.com", "Contoso"));
        }

        [Fact]
        public void Client_OriginIsItsServerAndOrganization()
        {
            var client = new TrakHoundPackageClient("https://packages.contoso.com", "Contoso");
            Assert.True(client.Origin.Matches("https://packages.contoso.com/", "Contoso"));
            Assert.Equal("Public", client.ForOrganization("Public").Origin.Organization);
            Assert.Null(new TrakHoundPackageClient("https://packages.contoso.com").Origin);
        }
    }
}
