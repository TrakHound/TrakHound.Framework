// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.IO.Compression;
using TrakHound.Testing;

namespace TrakHound.Packages.Api.Tests
{
    /// <summary>
    /// Reading Manifests from stored Package files and caching them
    /// </summary>
    public class PackageManifestStoreTests : IDisposable
    {
        private const string Organization = "Test";
        private const string Collection = "trakhound.packages";

        private readonly TrakHoundTestContext _context = new TrakHoundTestContext();
        private readonly PackageManifestStore _store;
        private readonly string _source;
        private readonly TrakHoundPackageSigner _signer = TrakHoundPackageSigner.Generate();


        public PackageManifestStoreTests()
        {
            _store = new PackageManifestStore(_context.Client);

            _source = Path.Combine(_context.Directory, "source");
            Directory.CreateDirectory(Path.Combine(_source, "dist"));
            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), "module");
        }

        public void Dispose()
        {
            _signer.Dispose();
            _context.Dispose();
        }


        private byte[] Pack(string id = "Test.Function", TrakHoundPackageSigner signer = null)
        {
            return new TrakHoundPackageBuilder { Category = "function", Id = id, Version = "1.0.0" }.Build().Package(_source, signer);
        }

        private static string Uuid(string id = "Test.Function") => TrakHoundPackage.GenerateUuid(id, "1.0.0");

        private async Task Store(byte[] packageBytes, string id = "Test.Function")
        {
            using (var stream = new MemoryStream(packageBytes))
            {
                var response = await _context.Client.Services.Blobs.Upload(Collection, PackageManifestStore.GetBlobKey(Organization, Uuid(id)), stream);
                Assert.True(response.Success);
            }
        }

        private Task DeleteBlob(string id = "Test.Function") => _context.Client.Services.Blobs.Delete(Collection, PackageManifestStore.GetBlobKey(Organization, Uuid(id)));

        private static byte[] CreateArchive(string name, byte[] contents)
        {
            using (var output = new MemoryStream())
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                using (var stream = archive.CreateEntry(name).Open())
                {
                    stream.Write(contents);
                }

                return output.ToArray();
            }
        }


        [Fact]
        public async Task Read_ReturnsBytesThatStillVerify()
        {
            var packageBytes = Pack(signer: _signer);
            await Store(packageBytes);

            var result = await _store.Read(Organization, Uuid());

            using (var stream = new MemoryStream(packageBytes))
            {
                var (manifest, signature) = TrakHoundPackageManifest.ReadFromArchive(stream);
                Assert.Equal(manifest, result.Manifest);
                Assert.Equal(signature, result.Signature);
            }

            Assert.Equal(Uuid(), result.Uuid);
            Assert.Null(result.Error);
        }

        [Fact]
        public async Task Read_FromTheCache_ReturnsTheSameBytes()
        {
            await Store(Pack(signer: _signer));
            var first = await _store.Read(Organization, Uuid());

            // Only the cache can answer now
            await DeleteBlob();
            var second = await _store.Read(Organization, Uuid());

            Assert.NotNull(second);
            Assert.Equal(first.Manifest, second.Manifest);
            Assert.Equal(first.Signature, second.Signature);
        }

        [Fact]
        public async Task Read_NotStored_ReturnsNull()
        {
            Assert.Null(await _store.Read(Organization, Uuid()));
        }

        [Fact]
        public async Task Read_Unsigned_HasNoSignature()
        {
            await Store(Pack());

            var result = await _store.Read(Organization, Uuid());

            Assert.NotNull(result.Manifest);
            Assert.Null(result.Signature);
        }

        [Fact]
        public async Task Read_NoManifest_IsAnEmptyResultAndCached()
        {
            await Store(CreateArchive("dist/module.dll", new byte[] { 1 }));

            var result = await _store.Read(Organization, Uuid());
            await DeleteBlob();

            Assert.Null(result.Manifest);
            Assert.Null(result.Error);
            Assert.NotNull(await _store.Read(Organization, Uuid()));
        }

        [Fact]
        public async Task Read_NotAPackage_ReportsAnError()
        {
            await Store(new byte[] { 1, 2, 3 });

            var result = await _store.Read(Organization, Uuid());

            Assert.Null(result.Manifest);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public async Task Read_LargeManifest_IsReadEachTimeInsteadOfCached()
        {
            await Store(CreateArchive(TrakHoundPackage.ManifestFilename, new byte[1024 * 1024 + 1]));

            Assert.NotNull((await _store.Read(Organization, Uuid())).Manifest);

            await DeleteBlob();
            Assert.Null(await _store.Read(Organization, Uuid()));
        }

        [Fact]
        public async Task Remove_NextReadUsesTheNewPackage()
        {
            await Store(Pack(signer: _signer));
            Assert.NotNull((await _store.Read(Organization, Uuid())).Signature);

            await Store(Pack());
            Assert.True(await _store.Remove(Organization, Uuid()));

            Assert.Null((await _store.Read(Organization, Uuid())).Signature);
        }

        [Fact]
        public async Task Read_Multiple_LeavesOutWhatIsMissing()
        {
            await Store(Pack("Test.A"), "Test.A");
            await Store(Pack("Test.B", _signer), "Test.B");

            // One from the cache, one from its blob, and one that doesn't exist
            await _store.Read(Organization, Uuid("Test.A"));
            var results = (await _store.Read(Organization, new[] { Uuid("Test.A"), Uuid("Test.Missing"), Uuid("Test.B") })).ToList();

            Assert.Equal(new[] { Uuid("Test.A"), Uuid("Test.B") }, results.Select(o => o.Uuid));
        }

        [Fact]
        public async Task Read_KeepsOrganizationsApart()
        {
            await Store(Pack());
            await _store.Read(Organization, Uuid());

            Assert.Null(await _store.Read("Other", Uuid()));
        }
    }
}
