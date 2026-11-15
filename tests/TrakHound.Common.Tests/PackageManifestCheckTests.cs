// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.IO.Compression;
using System.Text;
using TrakHound.Packages;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// Checking a Manifest and its signature without the Package's files
    /// </summary>
    public class PackageManifestCheckTests : IDisposable
    {
        private readonly string _source;
        private readonly TrakHoundPackageSigner _signer = TrakHoundPackageSigner.Generate();
        private readonly TrakHoundPackageSigner _otherSigner = TrakHoundPackageSigner.Generate();


        public PackageManifestCheckTests()
        {
            _source = Path.Combine(Path.GetTempPath(), $"trakhound-manifest-check-tests-{Guid.NewGuid()}");

            Directory.CreateDirectory(Path.Combine(_source, "dist"));
            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), "module");
        }

        public void Dispose()
        {
            _signer.Dispose();
            _otherSigner.Dispose();
            try { Directory.Delete(_source, true); } catch { }
        }


        private byte[] Pack(TrakHoundPackageSigner signer = null)
        {
            var builder = new TrakHoundPackageBuilder { Category = "function", Id = "Test.Function", Version = "1.0.0" };

            return builder.Build().Package(_source, signer);
        }

        private TrakHoundTrustedPublishers Trusted(params string[] packageIdPrefixes)
        {
            var publisher = new TrakHoundTrustedPublisher { Name = "Test", PublicKey = Convert.ToBase64String(TrakHoundPackageSigner.ReadPublicKeyPem(_signer.ExportPublicKeyPem())) };
            if (packageIdPrefixes.Length > 0) publisher.PackageIdPrefixes = packageIdPrefixes.ToList();

            return new TrakHoundTrustedPublishers { Publishers = new List<TrakHoundTrustedPublisher> { publisher } };
        }

        private static (byte[] Manifest, byte[] Signature) Read(byte[] packageBytes)
        {
            using (var stream = new MemoryStream(packageBytes)) return TrakHoundPackageManifest.ReadFromArchive(stream);
        }

        private static byte[] ReadEntry(byte[] packageBytes, string name)
        {
            using (var archive = new ZipArchive(new MemoryStream(packageBytes), ZipArchiveMode.Read))
            using (var stream = archive.GetEntry(name).Open())
            using (var output = new MemoryStream())
            {
                stream.CopyTo(output);
                return output.ToArray();
            }
        }

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
        public void ReadFromArchive_ReturnsTheExactBytes()
        {
            var packageBytes = Pack(_signer);

            var (manifest, signature) = Read(packageBytes);

            Assert.Equal(ReadEntry(packageBytes, TrakHoundPackage.ManifestFilename), manifest);
            Assert.Equal(ReadEntry(packageBytes, TrakHoundPackage.SignatureFilename), signature);
        }

        [Fact]
        public void ReadFromArchive_Unsigned_HasNoSignature()
        {
            var (manifest, signature) = Read(Pack());

            Assert.NotNull(manifest);
            Assert.Null(signature);
        }

        [Fact]
        public void ReadFromArchive_NoManifest_ReturnsNulls()
        {
            var (manifest, signature) = Read(CreateArchive("dist/module.dll", new byte[] { 1 }));

            Assert.Null(manifest);
            Assert.Null(signature);
        }

        [Fact]
        public void ReadFromArchive_NotAPackage_Throws()
        {
            using (var stream = new MemoryStream(new byte[] { 1, 2, 3 }))
            {
                Assert.Throws<InvalidDataException>(() => TrakHoundPackageManifest.ReadFromArchive(stream));
            }
        }

        [Fact]
        public void ReadFromArchive_TooLarge_Throws()
        {
            var packageBytes = CreateArchive(TrakHoundPackage.ManifestFilename, new byte[16 * 1024 * 1024 + 1]);

            using (var stream = new MemoryStream(packageBytes))
            {
                Assert.Throws<InvalidDataException>(() => TrakHoundPackageManifest.ReadFromArchive(stream));
            }
        }

        [Fact]
        public void ReadFromArchive_LeavesTheStreamOpen()
        {
            using (var stream = new MemoryStream(Pack()))
            {
                TrakHoundPackageManifest.ReadFromArchive(stream);

                Assert.True(stream.CanRead);
            }
        }


        [Fact]
        public void TrustedSignature_IsTrusted()
        {
            var (manifest, signature) = Read(Pack(_signer));

            var check = TrakHoundPackageVerifier.VerifyManifest(manifest, signature, Trusted());

            Assert.Equal(TrakHoundPackageManifestState.Trusted, check.State);
            Assert.Equal("Test", check.Publisher.Name);
            Assert.Equal("Test.Function", check.Manifest.Id);
            Assert.Empty(check.Errors);
        }

        [Fact]
        public void State_CantSayVerified()
        {
            // Only a check of the files can say a Package is Verified
            Assert.False(Enum.IsDefined(typeof(TrakHoundPackageManifestState), "Verified"));
        }

        [Fact]
        public void Unsigned_IsUnsigned()
        {
            var (manifest, signature) = Read(Pack());

            Assert.Equal(TrakHoundPackageManifestState.Unsigned, TrakHoundPackageVerifier.VerifyManifest(manifest, signature, Trusted()).State);
        }

        [Fact]
        public void OtherKey_IsUntrusted()
        {
            var (manifest, signature) = Read(Pack(_otherSigner));

            var check = TrakHoundPackageVerifier.VerifyManifest(manifest, signature, Trusted());

            Assert.Equal(TrakHoundPackageManifestState.Untrusted, check.State);
            Assert.Null(check.Publisher);
            Assert.NotNull(check.KeyId);
        }

        [Fact]
        public void NoTrustedPublishers_IsUntrusted()
        {
            var (manifest, signature) = Read(Pack(_signer));

            Assert.Equal(TrakHoundPackageManifestState.Untrusted, TrakHoundPackageVerifier.VerifyManifest(manifest, signature).State);
        }

        [Fact]
        public void NoManifest_IsNoManifest()
        {
            Assert.Equal(TrakHoundPackageManifestState.NoManifest, TrakHoundPackageVerifier.VerifyManifest(null, null, Trusted()).State);
        }

        [Fact]
        public void SignatureWithoutManifest_IsInvalid()
        {
            var (_, signature) = Read(Pack(_signer));

            Assert.Equal(TrakHoundPackageManifestState.Invalid, TrakHoundPackageVerifier.VerifyManifest(null, signature, Trusted()).State);
        }

        [Fact]
        public void ChangedManifest_IsInvalid()
        {
            var (manifest, signature) = Read(Pack(_signer));
            var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest).Replace("Test.Function", "Test.Other"));

            var check = TrakHoundPackageVerifier.VerifyManifest(changed, signature, Trusted());

            Assert.Equal(TrakHoundPackageManifestState.Invalid, check.State);
            Assert.Null(check.Publisher);
        }

        [Fact]
        public void ReserializedManifest_IsInvalid()
        {
            var (manifest, signature) = Read(Pack(_signer));
            var reformatted = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(manifest).Replace("\n", "\r\n"));

            Assert.Equal(TrakHoundPackageManifestState.Invalid, TrakHoundPackageVerifier.VerifyManifest(reformatted, signature, Trusted()).State);
        }

        [Fact]
        public void PublisherNotAllowedThisPackage_IsInvalid()
        {
            var (manifest, signature) = Read(Pack(_signer));

            Assert.Equal(TrakHoundPackageManifestState.Invalid, TrakHoundPackageVerifier.VerifyManifest(manifest, signature, Trusted("Other.")).State);
        }

        [Fact]
        public void UnreadableManifest_IsInvalid()
        {
            var check = TrakHoundPackageVerifier.VerifyManifest(Encoding.UTF8.GetBytes("not json"), null, Trusted());

            Assert.Equal(TrakHoundPackageManifestState.Invalid, check.State);
            Assert.NotEmpty(check.Errors);
        }

        private TrakHoundPackageManifestJson ToJson(byte[] packageBytes)
        {
            var (manifest, signature) = Read(packageBytes);
            return new TrakHoundPackageManifestJson { Manifest = manifest, Signature = signature };
        }

        private static TrakHoundPackage Listed(byte[] packageBytes) => TrakHoundPackage.ReadInformationFromPackage(packageBytes);

        [Fact]
        public void Listing_Matching_IsTrusted()
        {
            var packageBytes = Pack(_signer);

            var check = TrakHoundPackageVerifier.VerifyListedManifest(ToJson(packageBytes), Listed(packageBytes), Trusted());

            Assert.Equal(TrakHoundPackageManifestState.Trusted, check.State);
        }

        [Fact]
        public void Listing_OtherVersion_IsInvalid()
        {
            var manifest = ToJson(new TrakHoundPackageBuilder { Category = "function", Id = "Test.Function", Version = "1.0.0" }.Build().Package(_source, _signer));
            var listed = Listed(new TrakHoundPackageBuilder { Category = "function", Id = "Test.Function", Version = "2.0.0" }.Build().Package(_source));

            Assert.Equal(TrakHoundPackageManifestState.Invalid, TrakHoundPackageVerifier.VerifyListedManifest(manifest, listed, Trusted()).State);
        }

        [Fact]
        public void Listing_OtherBuild_IsInvalid()
        {
            var old = Pack(_signer);
            Thread.Sleep(1100);
            var listed = Listed(Pack());

            Assert.Equal(TrakHoundPackageManifestState.Invalid, TrakHoundPackageVerifier.VerifyListedManifest(ToJson(old), listed, Trusted()).State);
        }

        [Fact]
        public void Listing_OtherContentHash_IsInvalid()
        {
            var old = Pack(_signer);
            var builder = new TrakHoundPackageBuilder { Category = "function", Id = "Test.Function", Version = "1.0.0", BuildDate = Listed(old).BuildDate, ContentHash = "other" };

            Assert.Equal(TrakHoundPackageManifestState.Invalid, TrakHoundPackageVerifier.VerifyListedManifest(ToJson(old), builder.Build(), Trusted()).State);
        }

        [Fact]
        public void Listing_WithoutContentHash_IsStillTrusted()
        {
            var packageBytes = Pack(_signer);
            var builder = new TrakHoundPackageBuilder { Category = "function", Id = "Test.Function", Version = "1.0.0", BuildDate = Listed(packageBytes).BuildDate };

            Assert.Equal(TrakHoundPackageManifestState.Trusted, TrakHoundPackageVerifier.VerifyListedManifest(ToJson(packageBytes), builder.Build(), Trusted()).State);
        }

        [Fact]
        public void Listing_ErrorFromTheSource_IsInvalid()
        {
            var check = TrakHoundPackageVerifier.VerifyListedManifest(new TrakHoundPackageManifestJson { Error = "Too large" }, Listed(Pack()), Trusted());

            Assert.Equal(TrakHoundPackageManifestState.Invalid, check.State);
            Assert.Contains("Too large", check.Errors);
        }

        [Fact]
        public void Listing_PackageWithoutManifest_IsNoManifest()
        {
            Assert.Equal(TrakHoundPackageManifestState.NoManifest, TrakHoundPackageVerifier.VerifyListedManifest(new TrakHoundPackageManifestJson(), Listed(Pack()), Trusted()).State);
        }

        [Theory]
        [InlineData(TrakHoundPackageVerificationMode.Warn, false, false)]
        [InlineData(TrakHoundPackageVerificationMode.Require, false, true)]
        [InlineData(TrakHoundPackageVerificationMode.Require, true, false)]
        public void Manager_ChecksManifestsAndReportsRefusals(TrakHoundPackageVerificationMode mode, bool signed, bool refused)
        {
            using var manager = new TrakHoundPackageManager(Path.Combine(_source, "packages"));
            manager.VerificationMode = mode;
            manager.TrustedPublishersProvider = () => Trusted();

            var packageBytes = Pack(signed ? _signer : null);
            var listed = Listed(packageBytes);
            var manifest = ToJson(packageBytes);
            manifest.Uuid = listed.Uuid;

            var checks = manager.CheckManifests(new[] { listed }, new[] { manifest, new TrakHoundPackageManifestJson { Uuid = "not-listed" } });

            var check = Assert.Single(checks).Value;
            Assert.Equal(refused, manager.WouldRefuse(check));
            Assert.False(manager.WouldRefuse(null));
        }

        [Fact]
        public void UnreadableSignature_IsInvalid()
        {
            var (manifest, _) = Read(Pack());

            Assert.Equal(TrakHoundPackageManifestState.Invalid, TrakHoundPackageVerifier.VerifyManifest(manifest, Encoding.UTF8.GetBytes("not json"), Trusted()).State);
        }
    }
}
