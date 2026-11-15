// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.IO.Compression;
using System.Text;
using TrakHound.Packages;

namespace TrakHound.Common.Tests
{
    public class PackageVerificationTests : IDisposable
    {
        private readonly string _root;
        private readonly string _source;
        private readonly string _packages;


        public PackageVerificationTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trakhound-verify-tests-{Guid.NewGuid()}");
            _source = Path.Combine(_root, "source");
            _packages = Path.Combine(_root, "packages");

            Directory.CreateDirectory(Path.Combine(_source, "dist", "sub"));
            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), "module");
            File.WriteAllText(Path.Combine(_source, "dist", "sub", "data.txt"), "data");
        }

        public void Dispose()
        {
            try { Directory.Delete(_root, true); } catch { }
        }


        private byte[] Pack(params (string Key, string Value)[] metadata)
        {
            var builder = new TrakHoundPackageBuilder { Category = "function", Id = "Test.Function", Version = "1.0.0" };
            foreach (var entry in metadata) builder.SetMetadata(entry.Key, entry.Value);

            return builder.Build().Package(_source);
        }

        private string Install(byte[] packageBytes)
        {
            Assert.True(TrakHoundPackage.Install(packageBytes, _packages));
            return TrakHoundPackage.GetPackageVersionDirectory(_packages, "function", "Test.Function", "1.0.0");
        }

        /// <summary>
        /// Copy a package, replacing, removing or adding entries (a null value removes the entry)
        /// </summary>
        private static byte[] Rewrite(byte[] packageBytes, Dictionary<string, string> changes)
        {
            using (var output = new MemoryStream())
            {
                using (var input = new ZipArchive(new MemoryStream(packageBytes), ZipArchiveMode.Read))
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, true))
                {
                    foreach (var entry in input.Entries)
                    {
                        var path = entry.FullName.Replace('\\', '/');
                        if (changes.ContainsKey(path)) continue;

                        using (var source = entry.Open())
                        using (var destination = archive.CreateEntry(entry.FullName).Open())
                        {
                            source.CopyTo(destination);
                        }
                    }

                    foreach (var change in changes.Where(o => o.Value != null))
                    {
                        using (var writer = new StreamWriter(archive.CreateEntry(change.Key).Open(), new UTF8Encoding(false))) writer.Write(change.Value);
                    }
                }

                return output.ToArray();
            }
        }


        [Fact]
        public void Archive_Unchanged_IsValid()
        {
            var result = TrakHoundPackageVerifier.VerifyArchive(Pack());

            Assert.Equal(TrakHoundPackageVerificationStatus.Valid, result.Status);
            Assert.Equal(new[] { "dist/module.dll", "dist/sub/data.txt" }, result.Manifest.Files.Select(o => o.Path));
        }

        [Fact]
        public void Archive_ChangedFile_IsReported()
        {
            var result = TrakHoundPackageVerifier.VerifyArchive(Rewrite(Pack(), new() { ["dist/module.dll"] = "tampered" }));

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
            Assert.Equal(new[] { "dist/module.dll" }, result.Changed);
        }

        [Fact]
        public void Archive_MissingFile_IsReported()
        {
            var result = TrakHoundPackageVerifier.VerifyArchive(Rewrite(Pack(), new() { ["dist/module.dll"] = null }));

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
            Assert.Equal(new[] { "dist/module.dll" }, result.Missing);
        }

        [Fact]
        public void Archive_AddedFile_IsReported()
        {
            var result = TrakHoundPackageVerifier.VerifyArchive(Rewrite(Pack(), new() { ["dist/extra.dll"] = "extra" }));

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
            Assert.Equal(new[] { "dist/extra.dll" }, result.Added);
        }

        [Fact]
        public void Archive_EntriesDifferingOnlyByCase_AreRejected()
        {
            var result = TrakHoundPackageVerifier.VerifyArchive(Rewrite(Pack(), new() { ["DIST/MODULE.DLL"] = "tampered" }));

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
            Assert.Contains(result.Errors, o => o.Contains("Duplicate entry"));
        }

        [Fact]
        public void Archive_IdentityNotMatchingManifest_IsRejected()
        {
            var packageBytes = Pack();
            var information = new TrakHoundPackageJson(TrakHoundPackage.ReadInformationFromPackage(packageBytes)) { Version = "2.0.0" };
            var json = System.Text.Json.JsonSerializer.Serialize(information);

            var result = TrakHoundPackageVerifier.VerifyArchive(Rewrite(packageBytes, new() { [TrakHoundPackage.InformationFilename] = json }));

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
            Assert.Contains(result.Errors, o => o.Contains("Version"));
        }

        [Fact]
        public void Archive_CommitNotMatchingManifest_IsRejected()
        {
            var packageBytes = Pack((TrakHoundPackage.RepositoryCommitName, "abc"));
            var information = new TrakHoundPackageJson(TrakHoundPackage.ReadInformationFromPackage(packageBytes));
            information.Metadata[TrakHoundPackage.RepositoryCommitName] = "def";
            var json = System.Text.Json.JsonSerializer.Serialize(information);

            var result = TrakHoundPackageVerifier.VerifyArchive(Rewrite(packageBytes, new() { [TrakHoundPackage.InformationFilename] = json }));

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
            Assert.Contains(result.Errors, o => o.Contains("Metadata"));
        }

        [Fact]
        public void Archive_DependenciesNotMatchingManifest_AreRejected()
        {
            var packageBytes = Pack();
            var information = new TrakHoundPackageJson(TrakHoundPackage.ReadInformationFromPackage(packageBytes));
            information.Dependencies = new[] { new TrakHoundPackageDependency("Malicious.Package", "1.0.0") };
            var json = System.Text.Json.JsonSerializer.Serialize(information);

            var result = TrakHoundPackageVerifier.VerifyArchive(Rewrite(packageBytes, new() { [TrakHoundPackage.InformationFilename] = json }));

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
            Assert.Contains(result.Errors, o => o.Contains("Dependencies"));
        }

        [Fact]
        public void Installed_WithMetadataAndDependencies_IsValid()
        {
            var builder = new TrakHoundPackageBuilder { Category = "function", Id = "Test.Function", Version = "1.0.0" };
            builder.SetMetadata(TrakHoundPackage.DescriptionName, "Tést");
            builder.AddDependency(new TrakHoundPackageDependency("Other.Package", "1.*") { MinimumVersion = "1.2.0" });
            var packageBytes = builder.Build().Package(_source);

            Assert.Equal(TrakHoundPackageVerificationStatus.Valid, TrakHoundPackageVerifier.VerifyArchive(packageBytes).Status);

            var result = TrakHoundPackageVerifier.VerifyDirectory(Install(packageBytes));

            Assert.Empty(result.Errors);
            Assert.Equal(TrakHoundPackageVerificationStatus.Valid, result.Status);
        }


        private static TrakHoundTrustedPublishers Trust(TrakHoundPackageSigner signer, params string[] prefixes)
        {
            var publisher = new TrakHoundTrustedPublisher
            {
                Name = "Test Publisher",
                PublicKey = Convert.ToBase64String(TrakHoundPackageSigner.ReadPublicKeyPem(signer.ExportPublicKeyPem())),
                PackageIdPrefixes = prefixes.ToList()
            };

            return new TrakHoundTrustedPublishers { Publishers = new List<TrakHoundTrustedPublisher> { publisher } };
        }

        private byte[] PackSigned(TrakHoundPackageSigner signer)
        {
            return new TrakHoundPackageBuilder { Category = "function", Id = "Test.Function", Version = "1.0.0" }.Build().Package(_source, signer);
        }

        [Fact]
        public void Signature_Unsigned_IsReported()
        {
            using var signer = TrakHoundPackageSigner.Generate();

            var result = TrakHoundPackageVerifier.VerifyArchive(Pack(), Trust(signer));

            Assert.Equal(TrakHoundPackageVerificationStatus.Valid, result.Status);
            Assert.Equal(TrakHoundPackageSignatureStatus.Unsigned, result.SignatureStatus);
        }

        [Fact]
        public void Signature_FromTrustedPublisher_IsTrusted()
        {
            using var signer = TrakHoundPackageSigner.Generate();

            var result = TrakHoundPackageVerifier.VerifyArchive(PackSigned(signer), Trust(signer));

            Assert.Equal(TrakHoundPackageVerificationStatus.Valid, result.Status);
            Assert.Equal(TrakHoundPackageSignatureStatus.Trusted, result.SignatureStatus);
            Assert.Equal("Test Publisher", result.Publisher.Name);
        }

        [Fact]
        public void Signature_SurvivesInstall()
        {
            using var signer = TrakHoundPackageSigner.Generate();

            var result = TrakHoundPackageVerifier.VerifyDirectory(Install(PackSigned(signer)), Trust(signer));

            Assert.Equal(TrakHoundPackageVerificationStatus.Valid, result.Status);
            Assert.Equal(TrakHoundPackageSignatureStatus.Trusted, result.SignatureStatus);
        }

        [Fact]
        public void Signature_FromUnknownKey_IsUntrusted()
        {
            using var signer = TrakHoundPackageSigner.Generate();
            using var other = TrakHoundPackageSigner.Generate();

            var result = TrakHoundPackageVerifier.VerifyArchive(PackSigned(signer), Trust(other));

            Assert.Equal(TrakHoundPackageVerificationStatus.Valid, result.Status);
            Assert.Equal(TrakHoundPackageSignatureStatus.Untrusted, result.SignatureStatus);
            Assert.Equal(signer.KeyId, result.SignatureKeyId);
            Assert.Null(result.Publisher);
        }

        [Fact]
        public void Signature_WithoutTrustList_IsUntrusted()
        {
            using var signer = TrakHoundPackageSigner.Generate();

            Assert.Equal(TrakHoundPackageSignatureStatus.Untrusted, TrakHoundPackageVerifier.VerifyArchive(PackSigned(signer)).SignatureStatus);
        }

        [Fact]
        public void Signature_ForgedKeyId_IsInvalid()
        {
            using var signer = TrakHoundPackageSigner.Generate();
            using var attacker = TrakHoundPackageSigner.Generate();

            // Signed by the attacker, but claiming the Trusted Publisher's key
            var packageBytes = PackSigned(attacker);
            var signature = new TrakHoundPackageSignature { Algorithm = TrakHoundPackageSignature.EcdsaP256Sha256, KeyId = signer.KeyId, Signature = ReadSignature(packageBytes).Signature };
            var forged = Rewrite(packageBytes, new() { [TrakHoundPackage.SignatureFilename] = System.Text.Json.JsonSerializer.Serialize(signature) });

            var result = TrakHoundPackageVerifier.VerifyArchive(forged, Trust(signer));

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
            Assert.NotEqual(TrakHoundPackageSignatureStatus.Trusted, result.SignatureStatus);
        }

        [Fact]
        public void Signature_RewrittenManifest_IsInvalid()
        {
            using var signer = TrakHoundPackageSigner.Generate();

            // Change a file and update the Manifest to match, which is only caught by the signature
            var packageBytes = PackSigned(signer);
            var manifest = ReadManifest(packageBytes);
            manifest.Files.First(o => o.Path == "dist/module.dll").Size = 8;
            manifest.Files.First(o => o.Path == "dist/module.dll").Sha256 = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("tampered")));

            var information = new TrakHoundPackageJson(TrakHoundPackage.ReadInformationFromPackage(packageBytes)) { ContentHash = manifest.GetContentHash() };

            var tampered = Rewrite(packageBytes, new()
            {
                ["dist/module.dll"] = "tampered",
                [TrakHoundPackage.ManifestFilename] = System.Text.Json.JsonSerializer.Serialize(manifest),
                [TrakHoundPackage.InformationFilename] = System.Text.Json.JsonSerializer.Serialize(information)
            });

            var result = TrakHoundPackageVerifier.VerifyArchive(tampered, Trust(signer));

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
            Assert.Contains(result.Errors, o => o.Contains("Signature"));
            Assert.Empty(result.Changed);
        }

        [Fact]
        public void Signature_WithManifestRemoved_IsInvalid()
        {
            using var signer = TrakHoundPackageSigner.Generate();

            var result = TrakHoundPackageVerifier.VerifyArchive(Rewrite(PackSigned(signer), new() { [TrakHoundPackage.ManifestFilename] = null }), Trust(signer));

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
        }

        [Fact]
        public void Signature_OutsidePublisherPrefixes_IsRejected()
        {
            using var signer = TrakHoundPackageSigner.Generate();

            var result = TrakHoundPackageVerifier.VerifyArchive(PackSigned(signer), Trust(signer, "Contoso."));

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
            Assert.NotEqual(TrakHoundPackageSignatureStatus.Trusted, result.SignatureStatus);
        }

        [Fact]
        public void Signature_InsidePublisherPrefixes_IsTrusted()
        {
            using var signer = TrakHoundPackageSigner.Generate();

            Assert.Equal(TrakHoundPackageSignatureStatus.Trusted, TrakHoundPackageVerifier.VerifyArchive(PackSigned(signer), Trust(signer, "test.")).SignatureStatus);
        }

        [Fact]
        public void Signer_RoundTripsThroughPem()
        {
            using var signer = TrakHoundPackageSigner.Generate();
            using var loaded = TrakHoundPackageSigner.FromPem(signer.ExportPrivateKeyPem());

            Assert.Equal(signer.KeyId, loaded.KeyId);
        }

        [Fact]
        public void Signer_RejectsPublicKeyAndOtherCurves()
        {
            using var signer = TrakHoundPackageSigner.Generate();
            using var otherCurve = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);

            Assert.ThrowsAny<Exception>(() => TrakHoundPackageSigner.FromPem(signer.ExportPublicKeyPem()));
            Assert.ThrowsAny<Exception>(() => TrakHoundPackageSigner.FromPem(otherCurve.ExportPkcs8PrivateKeyPem()));
        }

        [Fact]
        public void TrustedPublishers_RoundTripThroughFile()
        {
            using var signer = TrakHoundPackageSigner.Generate();

            Assert.Empty(TrakHoundTrustedPublishers.Read(_root).Publishers);

            Trust(signer, "Contoso.").Save(_root);
            var read = TrakHoundTrustedPublishers.Read(_root);

            Assert.Equal("Test Publisher", read.Find(signer.KeyId).Name);
            Assert.Equal(new[] { "Contoso." }, read.Find(signer.KeyId).PackageIdPrefixes);
        }

        [Fact]
        public void TrustedPublishers_DamagedFile_Throws()
        {
            var path = TrakHoundTrustedPublishers.GetPath(_root);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, "not json");

            Assert.ThrowsAny<Exception>(() => TrakHoundTrustedPublishers.Read(_root));
        }

        private static TrakHoundPackageManifest ReadManifest(byte[] packageBytes) => ReadEntry<TrakHoundPackageManifest>(packageBytes, TrakHoundPackage.ManifestFilename);

        private static TrakHoundPackageSignature ReadSignature(byte[] packageBytes) => ReadEntry<TrakHoundPackageSignature>(packageBytes, TrakHoundPackage.SignatureFilename);

        private static T ReadEntry<T>(byte[] packageBytes, string name)
        {
            using (var archive = new ZipArchive(new MemoryStream(packageBytes), ZipArchiveMode.Read))
            using (var stream = archive.GetEntry(name).Open())
            {
                return System.Text.Json.JsonSerializer.Deserialize<T>(stream);
            }
        }

        [Fact]
        public void Archive_WithoutManifest_ReportsNoManifest()
        {
            var result = TrakHoundPackageVerifier.VerifyArchive(Rewrite(Pack(), new() { [TrakHoundPackage.ManifestFilename] = null }));

            Assert.Equal(TrakHoundPackageVerificationStatus.NoManifest, result.Status);
        }

        [Fact]
        public void Archive_UnreadableManifest_IsInvalid()
        {
            var result = TrakHoundPackageVerifier.VerifyArchive(Rewrite(Pack(), new() { [TrakHoundPackage.ManifestFilename] = "not json" }));

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
        }

        [Fact]
        public void Manifest_RecordsSource()
        {
            var result = TrakHoundPackageVerifier.VerifyArchive(Pack(
                (TrakHoundPackage.RepositoryName, "https://example.com/repo"),
                (TrakHoundPackage.RepositoryCommitName, "abc"),
                (TrakHoundPackage.RepositoryDirtyName, "true")
                ));

            Assert.Equal(TrakHoundPackageVerificationStatus.Valid, result.Status);
            Assert.Equal("https://example.com/repo", result.Manifest.Repository);
            Assert.Equal("abc", result.Manifest.RepositoryCommit);
            Assert.True(result.Manifest.RepositoryDirty);
        }

        [Fact]
        public void Manifest_RecordsHowItWasBuilt()
        {
            var manifest = TrakHoundPackageVerifier.VerifyArchive(Pack(
                (TrakHoundPackage.BuilderName, "https://example.com/runs/1"),
                (TrakHoundPackage.BuildConfigurationName, "Release"),
                (TrakHoundPackage.DotnetSdkVersionName, "10.0.403")
                )).Manifest;

            Assert.Equal("https://example.com/runs/1", manifest.Builder);
            Assert.Equal("Release", manifest.BuildConfiguration);
            Assert.Equal("10.0.403", manifest.DotnetSdkVersion);
        }

        [Fact]
        public void ExistingManifestInSource_IsReplaced()
        {
            File.WriteAllText(Path.Combine(_source, TrakHoundPackage.ManifestFilename), "stale");

            Assert.Equal(TrakHoundPackageVerificationStatus.Valid, TrakHoundPackageVerifier.VerifyArchive(Pack()).Status);
        }


        [Fact]
        public void Installed_Unchanged_IsValid()
        {
            var directory = Install(Pack());

            Assert.Equal(TrakHoundPackageVerificationStatus.Valid, TrakHoundPackageVerifier.VerifyDirectory(directory).Status);
        }

        [Fact]
        public void Installed_ChangedMissingAndAddedFiles_AreReported()
        {
            var directory = Install(Pack());

            File.WriteAllText(Path.Combine(directory, "dist", "module.dll"), "tampered");
            File.Delete(Path.Combine(directory, "dist", "sub", "data.txt"));
            File.WriteAllText(Path.Combine(directory, "dist", "extra.dll"), "extra");

            var result = TrakHoundPackageVerifier.VerifyDirectory(directory);

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
            Assert.Equal(new[] { "dist/module.dll" }, result.Changed);
            Assert.Equal(new[] { "dist/sub/data.txt" }, result.Missing);
            Assert.Equal(new[] { "dist/extra.dll" }, result.Added);
        }

        [Fact]
        public void Installed_KeepsContentHash()
        {
            var packageBytes = Pack();
            var directory = Install(packageBytes);

            var installed = TrakHoundPackage.ReadInformation(directory);

            Assert.False(string.IsNullOrEmpty(installed.ContentHash));
            Assert.Equal(TrakHoundPackage.ReadInformationFromPackage(packageBytes).ContentHash, installed.ContentHash);
        }


        [Fact]
        public void ContentHash_IsTheSameForIdenticalFiles()
        {
            var first = TrakHoundPackage.ReadInformationFromPackage(Pack()).ContentHash;
            var second = TrakHoundPackage.ReadInformationFromPackage(Pack()).ContentHash;

            Assert.Equal(first, second);
        }

        [Fact]
        public void ContentHash_ChangesWithTheFiles()
        {
            var first = TrakHoundPackage.ReadInformationFromPackage(Pack()).ContentHash;
            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), "changed");
            var second = TrakHoundPackage.ReadInformationFromPackage(Pack()).ContentHash;

            Assert.NotEqual(first, second);
        }


        private static TrakHoundPackage CreatePackage(DateTime buildDate, string contentHash)
        {
            return new TrakHoundPackageBuilder { Category = "function", Id = "Test.Function", Version = "1.0.0", BuildDate = buildDate, ContentHash = contentHash }.Build();
        }

        [Fact]
        public void IsSameBuild_ComparesHashAndContentHash()
        {
            var date = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            Assert.True(TrakHoundPackage.IsSameBuild(CreatePackage(date, "a"), CreatePackage(date, "a")));
            Assert.False(TrakHoundPackage.IsSameBuild(CreatePackage(date, "a"), CreatePackage(date, "b")));
            Assert.False(TrakHoundPackage.IsSameBuild(CreatePackage(date, "a"), CreatePackage(date.AddDays(1), "a")));
            Assert.True(TrakHoundPackage.IsSameBuild(CreatePackage(date, "a"), CreatePackage(date, null)));
            Assert.False(TrakHoundPackage.IsSameBuild(null, CreatePackage(date, "a")));
        }
    }
}
