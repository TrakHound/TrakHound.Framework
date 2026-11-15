// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Instances;
using TrakHound.Packages;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// What an Instance reports to a TrakHound Package Manager (the Verification Report and the Inventory), and trusting the publishers synced from one
    /// </summary>
    public class PackageInventoryTests : IDisposable
    {
        private const string PackageId = "Test.Function";

        private readonly string _root;
        private readonly string _source;
        private readonly string _packages;
        private readonly TrakHoundPackageSigner _signer = TrakHoundPackageSigner.Generate();
        private readonly TrakHoundPackageSigner _otherSigner = TrakHoundPackageSigner.Generate();
        private readonly TrakHoundPackageManager _manager;


        public PackageInventoryTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trakhound-inventory-tests-{Guid.NewGuid()}");
            _source = Path.Combine(_root, "source");
            _packages = Path.Combine(_root, "packages");

            Directory.CreateDirectory(Path.Combine(_source, "dist"));
            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), "module");

            _manager = new TrakHoundPackageManager(_packages);
            _manager.TrustedPublishersProvider = () => Trust(_signer);
        }

        public void Dispose()
        {
            _manager.Dispose();
            _signer.Dispose();
            _otherSigner.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        private byte[] Pack(string version = "1.0.0", TrakHoundPackageSigner signer = null)
        {
            var builder = new TrakHoundPackageBuilder { Category = "function", Id = PackageId, Version = version };
            return builder.Build().Package(_source, signer);
        }

        private static TrakHoundTrustedPublisher Publisher(string name, TrakHoundPackageSigner signer, params string[] prefixes)
        {
            return new TrakHoundTrustedPublisher { Name = name, PublicKey = Convert.ToBase64String(TrakHoundPackageSigner.ReadPublicKeyPem(signer.ExportPublicKeyPem())), PackageIdPrefixes = prefixes.ToList() };
        }

        private static TrakHoundTrustedPublishers Trust(TrakHoundPackageSigner signer)
        {
            return new TrakHoundTrustedPublishers { Publishers = new List<TrakHoundTrustedPublisher> { Publisher("Local", signer) } };
        }


        [Fact]
        public void VerificationReport_ListsUuidContentHashInstalledAndInUse()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Warn;
            var before = DateTime.UtcNow.AddMinutes(-1);
            Assert.True(TrakHoundPackage.Install(Pack("1.0.0", _signer), _packages));
            Assert.True(TrakHoundPackage.Install(Pack("2.0.0", _signer), _packages));

            var used = TrakHoundPackage.GenerateUuid(PackageId, "1.0.0");
            _manager.InUsePackagesProvider = () => new[] { used };
            _manager.Load();

            var packages = _manager.GetVerificationReport().Packages.ToList();
            Assert.Equal(2, packages.Count);

            var v1 = packages.Single(o => o.Version == "1.0.0");
            var v2 = packages.Single(o => o.Version == "2.0.0");

            Assert.Equal(used, v1.Uuid);
            Assert.True(v1.InUse);
            Assert.False(v2.InUse);

            // The hash recorded when it was packed, which tells two builds of one Version apart
            Assert.Equal(_manager.Get(PackageId, "1.0.0").ContentHash, v1.ContentHash);
            Assert.False(string.IsNullOrEmpty(v1.ContentHash));

            Assert.NotNull(v1.Installed);
            Assert.InRange(v1.Installed.Value, before, DateTime.UtcNow.AddMinutes(1));
        }

        [Fact]
        public void VerificationReport_InUseProviderFails_ReportsNotInUse()
        {
            Assert.True(TrakHoundPackage.Install(Pack("1.0.0", _signer), _packages));
            _manager.InUsePackagesProvider = () => throw new InvalidOperationException("Not ready");
            _manager.Load();

            Assert.False(Assert.Single(_manager.GetVerificationReport().Packages).InUse);
        }

        [Fact]
        public void Inventory_ListsEveryVersion_IncludingRefused()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            Assert.True(TrakHoundPackage.Install(Pack("1.0.0", _signer), _packages));
            Assert.True(TrakHoundPackage.Install(Pack("2.0.0", _otherSigner), _packages));
            _manager.Load();

            var inventory = TrakHoundPackageInventory.Create(_manager.GetVerificationReport(), TrakHoundPackageInventoryReason.Startup);

            Assert.Equal("Require", inventory.VerificationMode);
            Assert.Equal("startup", inventory.Reason);
            Assert.Equal(new[] { "2.0.0", "1.0.0" }, inventory.Packages.Select(o => o.Version));
            Assert.Equal("Untrusted", inventory.Packages[0].State);
            Assert.Equal("Verified", inventory.Packages[1].State);
            Assert.Equal("Local", inventory.Packages[1].Publisher);
            Assert.Equal(_signer.KeyId, inventory.Packages[1].KeyId);
        }

        [Fact]
        public void Inventory_Json_UsesTheContractFieldNames()
        {
            var inventory = new TrakHoundPackageInventory
            {
                InstanceId = "i-1",
                InstanceName = "Line 1",
                HostName = "host",
                FrameworkVersion = "0.15.2",
                OperatingSystem = "Windows",
                VerificationMode = "Warn",
                Reason = TrakHoundPackageInventoryReason.Heartbeat,
                Timestamp = DateTime.UtcNow,
                Packages = new List<TrakHoundPackageInventoryItem>
                {
                    new TrakHoundPackageInventoryItem { Id = PackageId, Version = "1.0.0", Uuid = "u", Category = "function", ContentHash = "h", Installed = DateTime.UtcNow, InUse = false, State = "Verified", Publisher = "P", KeyId = "k", Repository = "r", RepositoryCommit = "c" }
                }
            };

            using var json = JsonDocument.Parse(JsonSerializer.Serialize(inventory));
            var root = json.RootElement;

            Assert.Equal(new[] { "instanceId", "instanceName", "hostName", "frameworkVersion", "operatingSystem", "verificationMode", "reason", "timestamp", "packages" }, root.EnumerateObject().Select(o => o.Name));
            Assert.Equal("heartbeat", root.GetProperty("reason").GetString());

            var package = root.GetProperty("packages")[0];
            Assert.Equal(new[] { "id", "version", "uuid", "category", "contentHash", "installed", "inUse", "state", "publisher", "keyId", "repository", "repositoryCommit" }, package.EnumerateObject().Select(o => o.Name));

            // False is sent, not left out
            Assert.Equal(JsonValueKind.False, package.GetProperty("inUse").ValueKind);
        }

        [Fact]
        public void VerificationInformation_Json_KeepsInUseWhenFalse()
        {
            var json = Json.Convert(new TrakHoundPackageVerificationInformation { Id = PackageId, InUse = false });

            Assert.Contains("\"inUse\":false", json);
        }


        [Fact]
        public void Combine_TrustsSyncedPublishers_AndALocalKeyWins()
        {
            var local = new TrakHoundTrustedPublishers { Publishers = new List<TrakHoundTrustedPublisher> { Publisher("Local", _signer, "Test.") } };
            var synced = new[]
            {
                Publisher("Synced", _signer),
                Publisher("Other", _otherSigner),
                new TrakHoundTrustedPublisher { Name = "Broken", PublicKey = "not a key" }
            };

            var combined = TrakHoundTrustedPublishers.Combine(local, synced);

            Assert.Equal(new[] { "Local", "Other" }, combined.Publishers.Select(o => o.Name));
            Assert.Equal(new[] { "Test." }, combined.Find(_signer.KeyId).PackageIdPrefixes);
            Assert.Single(local.Publishers);
        }

        [Fact]
        public void Require_PackageSignedByASyncedPublisher_IsVerifiedAndLoaded()
        {
            var cache = new TrakHoundPackageManagerPublishers();
            cache.Set("pm", "Package Manager", new[] { Publisher("Contoso", _otherSigner) }, DateTime.UtcNow);

            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            _manager.TrustedPublishersProvider = () => TrakHoundTrustedPublishers.Combine(Trust(_signer), cache.GetPublishers(new[] { "pm" }));

            Assert.True(TrakHoundPackage.Install(Pack("1.0.0", _otherSigner), _packages));
            _manager.Load();

            var information = Assert.Single(_manager.GetVerificationReport().Packages);
            Assert.Equal(TrakHoundPackageVerificationState.Verified, information.State);
            Assert.Equal("Contoso", information.Publisher);
            Assert.NotNull(_manager.Get(PackageId, "1.0.0"));

            // A Source that is no longer a Package Manager isn't trusted
            _manager.TrustedPublishersProvider = () => TrakHoundTrustedPublishers.Combine(Trust(_signer), cache.GetPublishers(Array.Empty<string>()));
            _manager.VerifyAll();
            Assert.Single(_manager.RefusedPackages);
        }

        [Fact]
        public async Task Install_PackageSignedByASyncedPublisher_IsAllowedUnderRequire()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            _manager.TrustedPublishersProvider = () => TrakHoundTrustedPublishers.Combine(new TrakHoundTrustedPublishers(), new[] { Publisher("Contoso", _otherSigner) });

            var results = await _manager.Install(Pack("1.0.0", _otherSigner));

            Assert.True(Assert.Single(results).Success);
        }


        [Fact]
        public void PackageManagerPublishers_SavesReadsAndReportsChanges()
        {
            var cache = new TrakHoundPackageManagerPublishers();
            Assert.True(cache.Set("pm", "PM", new[] { Publisher("A", _signer) }, DateTime.UtcNow));
            Assert.False(cache.Set("pm", "PM", new[] { Publisher("A", _signer) }, DateTime.UtcNow));
            Assert.True(cache.Set("pm", "PM", new[] { Publisher("A", _signer, "Contoso.") }, DateTime.UtcNow));
            Assert.True(cache.Set("other", "Other", new[] { Publisher("B", _otherSigner) }, DateTime.UtcNow));

            cache.Save(_root);
            Assert.True(File.Exists(Path.Combine(_root, "security", "package-manager-publishers.json")));

            var read = TrakHoundPackageManagerPublishers.Read(_root);
            Assert.Equal(new[] { "A", "B" }, read.GetPublishers().Select(o => o.Name));
            Assert.Equal(new[] { "B" }, read.GetPublishers(new[] { "other" }).Select(o => o.Name));
            Assert.Equal("PM", read.GetSource("pm").SourceName);

            Assert.True(read.RemoveOtherSources(new[] { "pm" }));
            Assert.False(read.RemoveOtherSources(new[] { "pm" }));
            Assert.Equal(new[] { "A" }, read.GetPublishers().Select(o => o.Name));
        }

        [Fact]
        public void PackageManagerPublishers_NoFile_IsEmpty_AndADamagedFileThrows()
        {
            Assert.Empty(TrakHoundPackageManagerPublishers.Read(_root).Sources);

            var path = TrakHoundPackageManagerPublishers.GetPath(_root);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "{ not json");

            Assert.ThrowsAny<Exception>(() => TrakHoundPackageManagerPublishers.Read(_root));
        }


        [Fact]
        public void Configuration_InventoryInterval_DefaultsToSixHours_AndKeepsZero()
        {
            Assert.Equal(6, new TrakHoundInstanceConfiguration().PackageInventoryReportInterval);

            var path = Path.Combine(_root, TrakHoundInstanceConfiguration.Filename);
            Directory.CreateDirectory(_root);
            new TrakHoundInstanceConfiguration { PackageInventoryReportInterval = 0 }.Save(path, false);

            Assert.Equal(0, TrakHoundInstanceConfiguration.Read(path).PackageInventoryReportInterval);
        }
    }
}
