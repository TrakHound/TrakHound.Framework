// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;
using TrakHound.Packages;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// What a Package Manager does with Packages that pass or fail verification, for each Verification Mode
    /// </summary>
    public class PackageManagerVerificationTests : IDisposable
    {
        private const string PackageId = "Test.Function";

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private readonly string _root;
        private readonly string _source;
        private readonly string _packages;
        private readonly TrakHoundPackageSigner _signer = TrakHoundPackageSigner.Generate();
        private readonly TrakHoundPackageSigner _otherSigner = TrakHoundPackageSigner.Generate();
        private readonly TrakHoundPackageManager _manager;
        private readonly List<TrakHoundPackageVerifiedArgs> _verified = new List<TrakHoundPackageVerifiedArgs>();


        public PackageManagerVerificationTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trakhound-manager-verify-tests-{Guid.NewGuid()}");
            _source = Path.Combine(_root, "source");
            _packages = Path.Combine(_root, "packages");

            Directory.CreateDirectory(Path.Combine(_source, "dist"));
            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), "module");

            _manager = new TrakHoundPackageManager(_packages);
            _manager.TrustedPublishersProvider = () => new TrakHoundTrustedPublishers
            {
                Publishers = new List<TrakHoundTrustedPublisher>
                {
                    new TrakHoundTrustedPublisher { Name = "Test", PublicKey = Convert.ToBase64String(TrakHoundPackageSigner.ReadPublicKeyPem(_signer.ExportPublicKeyPem())) }
                }
            };
            _manager.PackageVerified += (s, args) => { lock (_verified) _verified.Add(args); };
        }

        public void Dispose()
        {
            _manager.Dispose();
            _signer.Dispose();
            _otherSigner.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        private byte[] Pack(string version = "1.0.0", TrakHoundPackageSigner signer = null, Action<TrakHoundPackageBuilder> configure = null, string id = PackageId)
        {
            var builder = new TrakHoundPackageBuilder { Category = "function", Id = id, Version = version };
            configure?.Invoke(builder);

            return builder.Build().Package(_source, signer);
        }

        private string PackageDirectory(string version = "1.0.0") => TrakHoundPackage.GetPackageVersionDirectory(_packages, "function", PackageId, version);

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


        [Theory]
        [InlineData(TrakHoundPackageVerificationMode.Off, false)]
        [InlineData(TrakHoundPackageVerificationMode.Warn, false)]
        [InlineData(TrakHoundPackageVerificationMode.Require, true)]
        public void CheckPackageFile_UnsignedPackage_ReportsWhatTheModeWouldDoWithoutInstalling(TrakHoundPackageVerificationMode mode, bool refused)
        {
            _manager.VerificationMode = mode;

            var information = _manager.CheckPackageFile(Pack());

            Assert.Equal(PackageId, information.Id);
            Assert.Equal(TrakHoundPackageVerificationState.Unsigned, information.State);
            Assert.Equal(refused, information.Refused);
            Assert.Null(_manager.Get(PackageId, "1.0.0"));
            Assert.Null(_manager.GetVerificationResult(PackageId, "1.0.0"));
            Assert.Empty(_verified);
        }

        [Fact]
        public void CheckPackageFile_TrustedSignature_IsVerified()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;

            var information = _manager.CheckPackageFile(Pack(signer: _signer));

            Assert.Equal(TrakHoundPackageVerificationState.Verified, information.State);
            Assert.Equal("Test", information.Publisher);
            Assert.False(information.Refused);
        }

        [Fact]
        public void CheckPackageFile_NotAPackage_IsRefusedUnderRequire()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;

            var information = _manager.CheckPackageFile(new byte[] { 1, 2, 3 });

            Assert.True(information.Refused);
            Assert.NotEmpty(information.Errors);
        }

        [Fact]
        public void Sbom_IsReadFromThePackageFileAndWhenInstalled()
        {
            File.WriteAllText(Path.Combine(_source, TrakHoundPackage.SbomFilename), "{\"components\":[{\"name\":\"Zeta\",\"version\":\"2.0.0\"},{\"name\":\"Alpha\",\"version\":\"1.0.0\",\"purl\":\"pkg:nuget/Alpha@1.0.0\"}]}");
            var packageBytes = Pack();

            var information = _manager.CheckPackageFile(packageBytes);
            Assert.True(information.HasSbom);
            Assert.Equal(new[] { "Alpha", "Zeta" }, TrakHoundPackageSbom.ReadArchive(packageBytes).Select(o => o.Name));

            Assert.True(TrakHoundPackage.Install(packageBytes, _packages));
            _manager.Load();

            Assert.Equal(new[] { "Alpha", "Zeta" }, _manager.GetSbom(PackageId, "1.0.0").Select(o => o.Name));
        }

        [Fact]
        public void Sbom_Unreadable_ReturnsNull()
        {
            File.WriteAllText(Path.Combine(_source, TrakHoundPackage.SbomFilename), "not json");

            Assert.Null(TrakHoundPackageSbom.ReadArchive(Pack()));
        }

        /// <summary>
        /// Write a Package with no Manifest straight to the Package directory (ex. one built before Manifests existed)
        /// </summary>
        private string InstallWithoutManifest(string id = PackageId, string version = "1.0.0")
        {
            var directory = TrakHoundPackage.GetPackageVersionDirectory(_packages, "function", id, version);

            Assert.True(TrakHoundPackage.Install(Pack(version, id: id), _packages));
            File.Delete(Path.Combine(directory, TrakHoundPackage.ManifestFilename));

            return directory;
        }

        [Fact]
        public void NoManifest_DoesNotReadTheFiles()
        {
            var directory = InstallWithoutManifest();

            // Reading this file would fail, and the failure would be reported as an error
            using (File.Open(Path.Combine(directory, "dist", "module.dll"), FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var result = TrakHoundPackageVerifier.VerifyDirectory(directory);

                Assert.Equal(TrakHoundPackageVerificationStatus.NoManifest, result.Status);
                Assert.Empty(result.Errors);
            }
        }

        [Fact]
        public void SignatureWithoutManifest_IsStillInvalid()
        {
            Assert.True(TrakHoundPackage.Install(Pack(signer: _signer), _packages));
            File.Delete(Path.Combine(PackageDirectory(), TrakHoundPackage.ManifestFilename));

            var result = TrakHoundPackageVerifier.VerifyDirectory(PackageDirectory());

            Assert.Equal(TrakHoundPackageVerificationStatus.Invalid, result.Status);
        }

        [Fact]
        public void Load_ManyPackages_AppliesEachResultOnceInOrder()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;

            var ids = Enumerable.Range(0, 40).Select(o => $"Test.Package{o:00}").ToList();
            for (var i = 0; i < ids.Count; i++)
            {
                switch (i % 4)
                {
                    case 0: Assert.True(TrakHoundPackage.Install(Pack(signer: _signer, id: ids[i]), _packages)); break;
                    case 1: Assert.True(TrakHoundPackage.Install(Pack(id: ids[i]), _packages)); break;
                    case 2: InstallWithoutManifest(ids[i]); break;
                    default:
                        Assert.True(TrakHoundPackage.Install(Pack(signer: _signer, id: ids[i]), _packages));
                        File.WriteAllText(Path.Combine(TrakHoundPackage.GetPackageVersionDirectory(_packages, "function", ids[i], "1.0.0"), "dist", "module.dll"), "tampered");
                        break;
                }
            }

            var inHandler = 0;
            var overlapped = false;
            _manager.PackageVerified += (s, args) =>
            {
                if (Interlocked.Increment(ref inHandler) > 1) overlapped = true;
                Thread.Sleep(5);
                Interlocked.Decrement(ref inHandler);
            };

            _manager.Load();

            Assert.False(overlapped);
            Assert.Equal(ids.OrderBy(o => o), _verified.Select(o => o.Package.Id).OrderBy(o => o));

            for (var i = 0; i < ids.Count; i++)
            {
                Assert.Equal(i % 4 == 0, _manager.Get(ids[i], "1.0.0") != null);
                Assert.Equal(i % 4 != 0, _manager.RefusedPackages.Any(o => o.Id == ids[i]));
            }

            Assert.Equal(TrakHoundPackageVerificationState.Modified, _manager.GetVerificationReport().Packages.Single(o => o.Id == ids[3]).State);
            Assert.Equal(TrakHoundPackageVerificationState.NoManifest, _manager.GetVerificationReport().Packages.Single(o => o.Id == ids[2]).State);
        }

        [Fact]
        public void Load_TrustedPublishersCantBeRead_RefusesAndReportsIt()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            _manager.TrustedPublishersProvider = () => throw new InvalidOperationException("Unreadable");
            Assert.True(TrakHoundPackage.Install(Pack(signer: _signer), _packages));

            _manager.Load();

            Assert.Null(_manager.Get(PackageId, "1.0.0"));
            Assert.Equal("Unreadable", Assert.Single(_verified).TrustedPublishersError);
        }

        [Fact]
        public void VerifyAll_ChecksEveryPackage_EvenWhenOff()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Off;
            Assert.True(TrakHoundPackage.Install(Pack(signer: _signer), _packages));
            InstallWithoutManifest("Test.Other");
            _manager.Load();

            _manager.VerifyAll();

            var report = _manager.GetVerificationReport().Packages.ToList();
            Assert.Equal(TrakHoundPackageVerificationState.Verified, report.Single(o => o.Id == PackageId).State);
            Assert.Equal(TrakHoundPackageVerificationState.NoManifest, report.Single(o => o.Id == "Test.Other").State);
        }

        [Fact]
        public void Off_DoesNotCheck()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Off;
            Assert.True(TrakHoundPackage.Install(Pack(), _packages));

            _manager.Load();

            Assert.NotNull(_manager.Get(PackageId, "1.0.0"));
            Assert.Empty(_verified);
        }

        [Fact]
        public void Warn_LoadsUnsignedAndChangedPackages_AndReportsThem()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Warn;
            Assert.True(TrakHoundPackage.Install(Pack(), _packages));
            File.WriteAllText(Path.Combine(PackageDirectory(), "dist", "module.dll"), "tampered");

            _manager.Load();

            Assert.NotNull(_manager.Get(PackageId, "1.0.0"));
            Assert.Empty(_manager.RefusedPackages);

            var verified = Assert.Single(_verified);
            Assert.True(verified.Allowed);
            Assert.Equal(new[] { "dist/module.dll" }, verified.Result.Changed);
        }

        [Fact]
        public async Task Warn_InstallsUnsignedPackage()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Warn;

            var results = await _manager.Install(Pack());

            Assert.True(Assert.Single(results).Success);
        }

        [Fact]
        public void Require_LoadsTrustedPackage()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            Assert.True(TrakHoundPackage.Install(Pack(signer: _signer), _packages));

            _manager.Load();

            Assert.NotNull(_manager.Get(PackageId, "1.0.0"));
            Assert.Equal(TrakHoundPackageSignatureStatus.Trusted, _manager.GetVerificationResult(PackageId, "1.0.0").SignatureStatus);
        }

        [Fact]
        public void Require_RefusesAtLoad_UnsignedUntrustedAndChangedPackages()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;

            Assert.True(TrakHoundPackage.Install(Pack("1.0.1"), _packages));
            Assert.True(TrakHoundPackage.Install(Pack("1.0.2", _otherSigner), _packages));
            Assert.True(TrakHoundPackage.Install(Pack("1.0.3", _signer), _packages));
            File.WriteAllText(Path.Combine(PackageDirectory("1.0.3"), "dist", "module.dll"), "tampered");

            _manager.Load();

            Assert.Empty(_manager.Packages);
            Assert.Null(_manager.GetLatest(PackageId));
            Assert.True(_manager.GetVersions(PackageId).IsNullOrEmpty());
            Assert.Equal(3, _manager.RefusedPackages.Count());
            Assert.All(_verified, o => Assert.False(o.Allowed));
        }

        [Fact]
        public void Require_LatestFallsBackFromRefusedVersion()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            Assert.True(TrakHoundPackage.Install(Pack("1.0.0", _signer), _packages));
            Assert.True(TrakHoundPackage.Install(Pack("2.0.0"), _packages));

            _manager.Load();

            Assert.Equal("1.0.0", _manager.GetLatest(PackageId)?.Version);
        }

        [Fact]
        public async Task Require_RefusesInstall_AndLeavesNothingOnDisk()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;

            var result = Assert.Single(await _manager.Install(Pack()));

            Assert.False(result.Success);
            Assert.Contains("Unsigned", result.Message);
            Assert.False(Directory.Exists(PackageDirectory()));
            Assert.Null(_manager.Get(PackageId, "1.0.0"));
        }

        [Fact]
        public async Task Require_InstallsTrustedPackage()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;

            var result = Assert.Single(await _manager.Install(Pack(signer: _signer)));

            Assert.True(result.Success);
            Assert.NotNull(_manager.Get(PackageId, "1.0.0"));
        }

        [Fact]
        public async Task Require_RefusedPackage_DoesNotInstallItsDependencies()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;

            // A Dependency would need the (missing) Package Client, which reports a second result
            var results = await _manager.Install(Pack(configure: o => o.AddDependency(new TrakHoundPackageDependency("Other.Package", "1.0.0"))));

            Assert.Single(results);
        }

        [Fact]
        public async Task Require_PackageCopiedIn_IsRefused()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            _manager.Load();

            // Installed by another process, as a copied folder would be
            Assert.True(TrakHoundPackage.Install(Pack(), _packages));

            Assert.True(await WaitFor(() => _manager.RefusedPackages.Any()));
            Assert.Null(_manager.Get(PackageId, "1.0.0"));
        }

        [Fact]
        public async Task Require_ReplacedWithUnsigned_StopsBeingUsed()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            Assert.True(TrakHoundPackage.Install(Pack(signer: _signer), _packages));
            _manager.Load();
            Assert.True(await WaitFor(() => _manager.GetVerificationResult(PackageId, "1.0.0") != null));

            var removed = false;
            _manager.PackageRemoved += (s, args) => removed = true;

            Assert.True(TrakHoundPackage.Install(Pack(), _packages));

            Assert.True(await WaitFor(() => _manager.RefusedPackages.Any()));
            Assert.True(await WaitFor(() => removed));
            Assert.Null(_manager.Get(PackageId, "1.0.0"));
        }

        [Fact]
        public void Require_RefusedPackage_CanBeUninstalled()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            Assert.True(TrakHoundPackage.Install(Pack(), _packages));
            _manager.Load();

            Assert.True(_manager.Uninstall(PackageId, "1.0.0"));

            Assert.False(Directory.Exists(PackageDirectory()));
            Assert.Empty(_manager.RefusedPackages);
        }

        [Fact]
        public void Require_DamagedTrustedPublishers_RefusesEverything()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            _manager.TrustedPublishersProvider = () => throw new InvalidDataException("Damaged");
            Assert.True(TrakHoundPackage.Install(Pack(signer: _signer), _packages));

            _manager.Load();

            Assert.Empty(_manager.Packages);
            Assert.Equal("Damaged", Assert.Single(_verified).TrustedPublishersError);
        }

        [Fact]
        public void Load_ChecksEachPackageOnce()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Warn;
            Assert.True(TrakHoundPackage.Install(Pack(), _packages));

            _manager.Load();

            Assert.Single(_verified);
        }


        [Fact]
        public void Report_ListsUsedAndRefusedPackages()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            Assert.True(TrakHoundPackage.Install(Pack("1.0.0", _signer, o => o.SetMetadata(TrakHoundPackage.RepositoryCommitName, "abc")), _packages));
            Assert.True(TrakHoundPackage.Install(Pack("2.0.0"), _packages));
            _manager.Load();

            var report = _manager.GetVerificationReport();
            var packages = report.Packages.ToDictionary(o => o.Version);

            Assert.Equal(TrakHoundPackageVerificationMode.Require, report.Mode);

            Assert.Equal(TrakHoundPackageVerificationState.Verified, packages["1.0.0"].State);
            Assert.False(packages["1.0.0"].Refused);
            Assert.Equal("Test", packages["1.0.0"].Publisher);
            Assert.Equal("abc", packages["1.0.0"].RepositoryCommit);

            Assert.Equal(TrakHoundPackageVerificationState.Unsigned, packages["2.0.0"].State);
            Assert.True(packages["2.0.0"].Refused);
        }

        [Fact]
        public void Report_SerializesStatesAsNames()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            Assert.True(TrakHoundPackage.Install(Pack(), _packages));
            _manager.Load();

            var json = System.Text.Json.JsonSerializer.Serialize(_manager.GetVerificationReport());

            Assert.Contains("\"mode\":\"Require\"", json);
            Assert.Contains("\"state\":\"Unsigned\"", json);
        }

        [Fact]
        public void Off_ReportsNotChecked_UntilVerified()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Off;
            Assert.True(TrakHoundPackage.Install(Pack(signer: _signer), _packages));
            _manager.Load();

            Assert.Equal(TrakHoundPackageVerificationState.NotChecked, Assert.Single(_manager.GetVerificationReport().Packages).State);

            var result = _manager.Verify(PackageId, "1.0.0");

            Assert.Equal(TrakHoundPackageSignatureStatus.Trusted, result.SignatureStatus);
            Assert.Equal(TrakHoundPackageVerificationState.Verified, Assert.Single(_manager.GetVerificationReport().Packages).State);
        }

        [Fact]
        public void Verify_UnknownPackage_ReturnsNull()
        {
            Assert.Null(_manager.Verify("Not.Installed", "1.0.0"));
        }

        [Fact]
        public async Task VerifyAll_TrustingAPublisher_UsesItsRefusedPackages()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            Assert.True(TrakHoundPackage.Install(Pack(signer: _otherSigner), _packages));
            _manager.Load();
            Assert.Single(_manager.RefusedPackages);

            var added = false;
            _manager.PackageAdded += (s, args) => added = true;
            _manager.TrustedPublishersProvider = () =>
            {
                var trusted = new TrakHoundTrustedPublishers();
                trusted.Add("Other", _otherSigner.ExportPublicKeyPem());
                return trusted;
            };

            _manager.VerifyAll();

            // Usable right away, not only once the Added event is raised
            Assert.Empty(_manager.RefusedPackages);
            Assert.NotNull(_manager.Get(PackageId, "1.0.0"));
            Assert.Single(_manager.GetVerificationReport().Packages);
            Assert.True(await WaitFor(() => added));
        }

        [Fact]
        public async Task VerifyAll_RemovingAPublisher_StopsItsPackages()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            Assert.True(TrakHoundPackage.Install(Pack(signer: _signer), _packages));
            _manager.Load();
            Assert.NotNull(_manager.Get(PackageId, "1.0.0"));

            var removed = false;
            _manager.PackageRemoved += (s, args) => removed = true;
            _manager.TrustedPublishersProvider = () => new TrakHoundTrustedPublishers();

            _manager.VerifyAll();

            Assert.Single(_manager.RefusedPackages);
            Assert.Null(_manager.Get(PackageId, "1.0.0"));
            Assert.True(await WaitFor(() => removed));
        }

        [Fact]
        public void TrustedPublishers_Add_ValidatesTheKey()
        {
            var trusted = new TrakHoundTrustedPublishers();

            Assert.Throws<ArgumentException>(() => trusted.Add("A", _signer.ExportPrivateKeyPem()));
            Assert.Throws<ArgumentException>(() => trusted.Add("A", "not a key"));
            Assert.Throws<ArgumentException>(() => trusted.Add("A", null));
            Assert.Throws<ArgumentException>(() => trusted.Add(" ", _signer.ExportPublicKeyPem()));
            Assert.Empty(trusted.Publishers);

            trusted.Add("A", _signer.ExportPublicKeyPem());
            trusted.Add("B", _signer.ExportPublicKeyPem(), new[] { "Contoso." });

            Assert.Equal("B", Assert.Single(trusted.Publishers).Name);
        }

        [Fact]
        public void Configuration_DefaultsToWarn_AndKeepsOtherValuesWhenSaved()
        {
            Assert.Equal(TrakHoundPackageVerificationMode.Warn, new TrakHoundInstanceConfiguration().PackageVerification);

            var path = Path.Combine(_root, TrakHoundInstanceConfiguration.Filename);
            foreach (var mode in Enum.GetValues<TrakHoundPackageVerificationMode>())
            {
                new TrakHoundInstanceConfiguration { PackageVerification = mode }.Save(path, false);

                Assert.Equal(mode, TrakHoundInstanceConfiguration.Read(path).PackageVerification);
            }
        }

        [Fact]
        public void Describe_ListsTheProblems()
        {
            var result = new TrakHoundPackageVerificationResult();
            result.Errors.Add("Duplicate entry : dist/a.dll");

            Assert.Contains("dist/a.dll", TrakHoundPackageVerificationPolicy.Describe(result));
            Assert.Equal("No Manifest", TrakHoundPackageVerificationPolicy.Describe(new TrakHoundPackageVerificationResult()));
        }
    }
}
