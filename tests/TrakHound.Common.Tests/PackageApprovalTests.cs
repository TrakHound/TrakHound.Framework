// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Security.Cryptography;
using System.Text.Json;
using TrakHound.Packages;

namespace TrakHound.Common.Tests
{
    /// <summary>
    /// The attestation lists of a TrakHound Package Manager, and the RequireApproval Verification Mode
    /// </summary>
    public class PackageApprovalTests : IDisposable
    {
        private const string PackageId = "Test.Function";

        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(15);

        private readonly string _root;
        private readonly string _source;
        private readonly string _packages;
        private readonly TrakHoundPackageSigner _signer = TrakHoundPackageSigner.Generate();
        private readonly ECDsa _serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa _otherServerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly TrakHoundPackageManager _manager;
        private TrakHoundPackageApprovals _approvals;


        public PackageApprovalTests()
        {
            _root = Path.Combine(Path.GetTempPath(), $"trakhound-approval-tests-{Guid.NewGuid()}");
            _source = Path.Combine(_root, "source");
            _packages = Path.Combine(_root, "packages");

            Directory.CreateDirectory(Path.Combine(_source, "dist"));
            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), "module");

            _manager = new TrakHoundPackageManager(_packages);
            _manager.VerificationMode = TrakHoundPackageVerificationMode.RequireApproval;
            _manager.TrustedPublishersProvider = () => new TrakHoundTrustedPublishers
            {
                Publishers = new List<TrakHoundTrustedPublisher>
                {
                    new TrakHoundTrustedPublisher { Name = "Test", PublicKey = Convert.ToBase64String(TrakHoundPackageSigner.ReadPublicKeyPem(_signer.ExportPublicKeyPem())) }
                }
            };
            _manager.ApprovalsProvider = () => _approvals;
        }

        public void Dispose()
        {
            _manager.Dispose();
            _signer.Dispose();
            _serverKey.Dispose();
            _otherServerKey.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        public static TrakHoundPackageManagerServerKey CreateServerKey(ECDsa key)
        {
            var pem = key.ExportSubjectPublicKeyInfoPem();
            return new TrakHoundPackageManagerServerKey
            {
                ServerId = "server-1",
                KeyId = TrakHoundPackageSigner.GetKeyId(TrakHoundPackageSigner.ReadPublicKeyPem(pem)),
                PublicKeyPem = pem,
                Created = DateTime.UtcNow
            };
        }

        /// <summary>
        /// Sign a list the way a Package Manager does : the signature is over the payload's UTF-8 JSON bytes
        /// </summary>
        public static TrakHoundPackageAttestationEnvelope Sign(ECDsa key, IEnumerable<TrakHoundPackageAttestation> packages, DateTime? expires = null, DateTime? issued = null, string payloadKeyId = null)
        {
            var keyId = CreateServerKey(key).KeyId;
            var list = new TrakHoundPackageAttestationList
            {
                ServerId = "server-1",
                KeyId = payloadKeyId ?? keyId,
                Channel = "Prod",
                InstanceId = "i-1",
                Issued = issued ?? DateTime.UtcNow.AddMinutes(-1),
                Expires = expires ?? DateTime.UtcNow.AddDays(7),
                Packages = packages.ToList()
            };

            var payload = JsonSerializer.SerializeToUtf8Bytes(list);
            return new TrakHoundPackageAttestationEnvelope
            {
                Format = TrakHoundPackageAttestationEnvelope.FormatName,
                FormatVersion = 1,
                Payload = Convert.ToBase64String(payload),
                KeyId = keyId,
                Signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256))
            };
        }

        private byte[] Pack(string version = "1.0.0", bool sign = true, string content = "module")
        {
            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), content);
            var builder = new TrakHoundPackageBuilder { Category = "function", Id = PackageId, Version = version };
            return builder.Build().Package(_source, sign ? _signer : null);
        }

        private static TrakHoundPackageAttestation Attest(byte[] packageBytes, string state = TrakHoundPackageAttestation.Approved)
        {
            var package = TrakHoundPackage.ReadInformationFromPackage(packageBytes);
            return new TrakHoundPackageAttestation { Id = package.Id, Version = package.Version, Uuid = package.Uuid, ContentHash = package.ContentHash, State = state };
        }

        private void Approve(params TrakHoundPackageAttestation[] packages) => Approve(DateTime.UtcNow.AddDays(7), packages);

        private void Approve(DateTime expires, params TrakHoundPackageAttestation[] packages)
        {
            var (list, error) = TrakHoundPackageAttestationVerifier.Read(Sign(_serverKey, packages, expires), CreateServerKey(_serverKey), DateTime.UtcNow, true);
            Assert.Null(error);
            _approvals = new TrakHoundPackageApprovals(new[] { ("Contoso PM", list) });
        }

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


        #region "List check"

        [Fact]
        public void Read_ValidList_ReturnsIt()
        {
            var package = new TrakHoundPackageAttestation { Id = PackageId, Version = "1.0.0", ContentHash = "abc", State = "approved" };

            var (list, error) = TrakHoundPackageAttestationVerifier.Read(Sign(_serverKey, new[] { package }), CreateServerKey(_serverKey), DateTime.UtcNow);

            Assert.Null(error);
            Assert.Equal("Prod", list.Channel);
            Assert.Equal("abc", Assert.Single(list.Packages).ContentHash);
        }

        [Fact]
        public void Read_SignedByAnotherKey_IsRefused()
        {
            var (list, error) = TrakHoundPackageAttestationVerifier.Read(Sign(_otherServerKey, Array.Empty<TrakHoundPackageAttestation>()), CreateServerKey(_serverKey), DateTime.UtcNow);

            Assert.Null(list);
            Assert.Contains("signature", error);
        }

        [Fact]
        public void Read_ChangedPayload_IsRefused()
        {
            var envelope = Sign(_serverKey, new[] { new TrakHoundPackageAttestation { Id = PackageId, Version = "1.0.0", ContentHash = "abc", State = "approved" } });

            // Another build added to the signed list
            var payload = JsonSerializer.Deserialize<TrakHoundPackageAttestationList>(Convert.FromBase64String(envelope.Payload));
            payload.Packages.Add(new TrakHoundPackageAttestation { Id = "Other.Package", Version = "1.0.0", ContentHash = "def", State = "approved" });
            envelope.Payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(payload));

            var (list, error) = TrakHoundPackageAttestationVerifier.Read(envelope, CreateServerKey(_serverKey), DateTime.UtcNow);

            Assert.Null(list);
            Assert.Contains("signature", error);
        }

        [Fact]
        public void Read_Expired_IsRefused_UnlessReadingBackAKeptList()
        {
            var envelope = Sign(_serverKey, Array.Empty<TrakHoundPackageAttestation>(), DateTime.UtcNow.AddMinutes(-1));

            var (list, error) = TrakHoundPackageAttestationVerifier.Read(envelope, CreateServerKey(_serverKey), DateTime.UtcNow);
            Assert.Null(list);
            Assert.Contains("expired", error);

            Assert.NotNull(TrakHoundPackageAttestationVerifier.Read(envelope, CreateServerKey(_serverKey), DateTime.UtcNow, true).List);
        }

        [Fact]
        public void Read_NoPinnedKey_IsRefused()
        {
            var (list, error) = TrakHoundPackageAttestationVerifier.Read(Sign(_serverKey, Array.Empty<TrakHoundPackageAttestation>()), null, DateTime.UtcNow);

            Assert.Null(list);
            Assert.Contains("pinned", error);
        }

        [Theory]
        [InlineData("other-format", 1)]
        [InlineData(TrakHoundPackageAttestationEnvelope.FormatName, 2)]
        [InlineData(TrakHoundPackageAttestationEnvelope.FormatName, 0)]
        public void Read_UnknownFormat_IsRefused(string format, int formatVersion)
        {
            var envelope = Sign(_serverKey, Array.Empty<TrakHoundPackageAttestation>());
            envelope.Format = format;
            envelope.FormatVersion = formatVersion;

            Assert.Null(TrakHoundPackageAttestationVerifier.Read(envelope, CreateServerKey(_serverKey), DateTime.UtcNow).List);
        }

        [Fact]
        public void Read_PayloadNamesAnotherKey_IsRefused()
        {
            var envelope = Sign(_serverKey, Array.Empty<TrakHoundPackageAttestation>(), payloadKeyId: "0000000000000000");

            var (list, error) = TrakHoundPackageAttestationVerifier.Read(envelope, CreateServerKey(_serverKey), DateTime.UtcNow);

            Assert.Null(list);
            Assert.Contains("another key", error);
        }

        [Fact]
        public void CheckServerKey_KeyIdThatIsntTheKeys_IsRefused()
        {
            var key = CreateServerKey(_serverKey);
            Assert.Null(TrakHoundPackageAttestationVerifier.CheckServerKey(key));

            key.KeyId = CreateServerKey(_otherServerKey).KeyId;
            Assert.NotNull(TrakHoundPackageAttestationVerifier.CheckServerKey(key));

            Assert.NotNull(TrakHoundPackageAttestationVerifier.CheckServerKey(new TrakHoundPackageManagerServerKey { KeyId = "x", PublicKeyPem = "not a key" }));
        }

        #endregion

        #region "Approvals"

        [Fact]
        public void Approvals_NoList_RefusesEverything_AndSaysWhy()
        {
            var approval = new TrakHoundPackageApprovals(null, "the server key hasn't been read").Check(PackageId, "1.0.0", "abc", false, DateTime.UtcNow);

            Assert.False(approval.Approved);
            Assert.Contains("the server key hasn't been read", approval.Description);
        }

        [Fact]
        public void Approvals_MatchTheContentHashIdAndVersion()
        {
            var now = DateTime.UtcNow;
            var list = new TrakHoundPackageAttestationList
            {
                Expires = now.AddDays(1),
                Packages = new List<TrakHoundPackageAttestation>
                {
                    new TrakHoundPackageAttestation { Id = PackageId, Version = "1.0.0", ContentHash = "abc", State = "approved" },
                    new TrakHoundPackageAttestation { Id = PackageId, Version = "0.9.0", ContentHash = "old", State = "deprecated" },
                    new TrakHoundPackageAttestation { Id = PackageId, Version = "0.8.0", ContentHash = "held", State = "pending" }
                }
            };
            var approvals = new TrakHoundPackageApprovals(new[] { ("PM", list) });

            Assert.True(approvals.Check(PackageId, "1.0.0", "ABC", true, now).Approved);
            Assert.True(approvals.Check(PackageId, "0.9.0", "old", true, now).Approved);

            // Another build of the Version, the same files as another Version or ID, or a state that isn't approved
            Assert.Equal(TrakHoundPackageApprovals.NotApprovedDescription, approvals.Check(PackageId, "1.0.0", "other", false, now).Description);
            Assert.False(approvals.Check(PackageId, "1.0.1", "abc", false, now).Approved);
            Assert.False(approvals.Check("Other.Package", "1.0.0", "abc", false, now).Approved);
            Assert.False(approvals.Check(PackageId, "0.8.0", "held", false, now).Approved);
            Assert.False(approvals.Check(PackageId, "1.0.0", null, false, now).Approved);
        }

        [Fact]
        public void Approvals_ExpiredList_KeepsWhatsOnIt_ButInstallsNothing()
        {
            var now = DateTime.UtcNow;
            var list = new TrakHoundPackageAttestationList
            {
                Expires = now.AddMinutes(-1),
                Packages = new List<TrakHoundPackageAttestation> { new TrakHoundPackageAttestation { Id = PackageId, Version = "1.0.0", ContentHash = "abc", State = "approved" } }
            };
            var approvals = new TrakHoundPackageApprovals(new[] { ("PM", list) });

            var loaded = approvals.Check(PackageId, "1.0.0", "abc", false, now);
            Assert.True(loaded.Approved);
            Assert.True(loaded.Expired);

            var installed = approvals.Check(PackageId, "1.0.0", "abc", true, now);
            Assert.False(installed.Approved);
            Assert.Contains("expired", installed.Description);

            Assert.False(approvals.Check(PackageId, "1.0.0", "other", false, now).Approved);
        }

        [Fact]
        public void Approvals_AValidListOfAnotherSource_StillInstalls()
        {
            var now = DateTime.UtcNow;
            var package = new TrakHoundPackageAttestation { Id = PackageId, Version = "1.0.0", ContentHash = "abc", State = "approved" };
            var expired = new TrakHoundPackageAttestationList { Expires = now.AddMinutes(-1), Packages = new List<TrakHoundPackageAttestation> { package } };
            var valid = new TrakHoundPackageAttestationList { Expires = now.AddDays(1), Packages = new List<TrakHoundPackageAttestation> { package } };

            var approval = new TrakHoundPackageApprovals(new[] { ("Old", expired), ("New", valid) }).Check(PackageId, "1.0.0", "abc", true, now);

            Assert.True(approval.Approved);
            Assert.Equal("New", approval.Source);
        }

        [Fact]
        public void ApprovalFiles_KeptListIsOnlyUsedWithItsPinnedKey()
        {
            var profile = Path.Combine(_root, "profile");
            var keys = new TrakHoundPackageManagerKeys();
            keys.Pin("pm", "Contoso PM", CreateServerKey(_serverKey), DateTime.UtcNow);
            keys.Save(profile);

            var attestations = new TrakHoundPackageManagerAttestations();
            attestations.Set("pm", "Contoso PM", Sign(_serverKey, new[] { new TrakHoundPackageAttestation { Id = PackageId, Version = "1.0.0", ContentHash = "abc", State = "approved" } }), DateTime.UtcNow);
            attestations.Save(profile);

            Assert.True(TrakHoundPackageManagerApprovalFiles.ReadApprovals(profile).Check(PackageId, "1.0.0", "abc", true, DateTime.UtcNow).Approved);
            Assert.False(TrakHoundPackageManagerApprovalFiles.ReadApprovals(profile, new[] { "other" }).HasList);

            // A different key pinned (ex. the file was edited) : the kept list doesn't verify
            keys.Pin("pm", "Contoso PM", CreateServerKey(_otherServerKey), DateTime.UtcNow);
            keys.Save(profile);

            var approvals = TrakHoundPackageManagerApprovalFiles.ReadApprovals(profile);
            Assert.False(approvals.HasList);
            Assert.Contains("signature", approvals.NoListReason);
        }

        #endregion

        #region "RequireApproval"

        [Fact]
        public void RequireApproval_LoadsApprovedPackage()
        {
            var packageBytes = Pack();
            Approve(Attest(packageBytes));
            Assert.True(TrakHoundPackage.Install(packageBytes, _packages));

            _manager.Load();

            Assert.NotNull(_manager.Get(PackageId, "1.0.0"));
            var information = Assert.Single(_manager.GetVerificationReport().Packages);
            Assert.Equal(TrakHoundPackageVerificationState.Verified, information.State);
            Assert.Contains("Approved by the Package Manager (Contoso PM)", information.Description);
        }

        [Fact]
        public void RequireApproval_PackageNotOnTheList_IsRefusedAsNotApproved()
        {
            // Signed by a Trusted Publisher, so Require would use it
            Approve(Attest(Pack(content: "approved build")));
            Assert.True(TrakHoundPackage.Install(Pack(content: "another build"), _packages));

            _manager.Load();

            Assert.Null(_manager.Get(PackageId, "1.0.0"));
            var information = Assert.Single(_manager.GetVerificationReport().Packages);
            Assert.True(information.Refused);
            Assert.Equal(TrakHoundPackageVerificationState.NotApproved, information.State);
            Assert.Equal(TrakHoundPackageApprovals.NotApprovedDescription, information.Description);
            Assert.Equal("NotApproved", TrakHoundPackageInventoryItem.Create(information).State);
        }

        [Fact]
        public void RequireApproval_NoList_RefusesEveryPackage()
        {
            _approvals = null;
            Assert.True(TrakHoundPackage.Install(Pack(), _packages));

            _manager.Load();

            Assert.Null(_manager.Get(PackageId, "1.0.0"));
            Assert.Equal(TrakHoundPackageVerificationState.NotApproved, Assert.Single(_manager.GetVerificationReport().Packages).State);
        }

        [Fact]
        public void RequireApproval_StillRequiresATrustedSignature()
        {
            var packageBytes = Pack(sign: false);
            Approve(Attest(packageBytes));
            Assert.True(TrakHoundPackage.Install(packageBytes, _packages));

            _manager.Load();

            Assert.Null(_manager.Get(PackageId, "1.0.0"));
            Assert.Equal(TrakHoundPackageVerificationState.Unsigned, Assert.Single(_manager.GetVerificationReport().Packages).State);
        }

        [Fact]
        public void RequireApproval_NoManifest_IsRefused()
        {
            var packageBytes = Pack();
            Approve(Attest(packageBytes));
            Assert.True(TrakHoundPackage.Install(packageBytes, _packages));
            File.Delete(Path.Combine(TrakHoundPackage.GetPackageVersionDirectory(_packages, "function", PackageId, "1.0.0"), TrakHoundPackage.ManifestFilename));
            File.Delete(Path.Combine(TrakHoundPackage.GetPackageVersionDirectory(_packages, "function", PackageId, "1.0.0"), TrakHoundPackage.SignatureFilename));

            _manager.Load();

            Assert.Null(_manager.Get(PackageId, "1.0.0"));
        }

        [Fact]
        public async Task RequireApproval_InstallsApprovedPackage_AndRefusesAnother()
        {
            var approved = Pack("1.0.0");
            Approve(Attest(approved));
            _manager.Load();

            var refreshed = 0;
            _manager.ApprovalsRefresh = token => { Interlocked.Increment(ref refreshed); return Task.CompletedTask; };

            Assert.True(Assert.Single(await _manager.Install(approved)).Success);

            var refused = Assert.Single(await _manager.Install(Pack("2.0.0")));
            Assert.False(refused.Success);
            Assert.Contains(TrakHoundPackageApprovals.NotApprovedDescription, refused.Message);
            Assert.Null(_manager.Get(PackageId, "2.0.0"));

            // The lists are read again before each install
            Assert.Equal(2, refreshed);
        }

        [Fact]
        public async Task RequireApproval_ExpiredList_KeepsInstalledPackages_ButInstallsNothing()
        {
            var installed = Pack("1.0.0");
            var other = Pack("2.0.0");
            Approve(DateTime.UtcNow.AddMinutes(-1), Attest(installed), Attest(other));
            Assert.True(TrakHoundPackage.Install(installed, _packages));

            _manager.Load();

            Assert.NotNull(_manager.Get(PackageId, "1.0.0"));
            Assert.Contains("expired", Assert.Single(_manager.GetVerificationReport().Packages).Description);

            var result = Assert.Single(await _manager.Install(other));
            Assert.False(result.Success);
            Assert.Contains("expired", result.Message);
            Assert.True(_manager.CheckPackageFile(other).Refused);
        }

        [Fact]
        public void RequireApproval_CheckPackageFile_ReportsWhetherItWouldInstall()
        {
            var approved = Pack("1.0.0");
            Approve(Attest(approved));

            Assert.False(_manager.CheckPackageFile(approved).Refused);

            var other = _manager.CheckPackageFile(Pack("2.0.0"));
            Assert.True(other.Refused);
            Assert.Equal(TrakHoundPackageVerificationState.NotApproved, other.State);
        }

        [Fact]
        public async Task RequireApproval_PackageCopiedIn_IsRefused()
        {
            Approve(Attest(Pack("1.0.0")));
            _manager.Load();

            // Installed by another process, as a copied folder would be. Signed by a Trusted Publisher, but not approved
            Assert.True(TrakHoundPackage.Install(Pack("2.0.0"), _packages));

            Assert.True(await WaitFor(() => _manager.RefusedPackages.Any()));
            Assert.Null(_manager.Get(PackageId, "2.0.0"));
        }

        [Fact]
        public void RequireApproval_VerifyAll_AppliesANewList()
        {
            var packageBytes = Pack();
            Assert.True(TrakHoundPackage.Install(packageBytes, _packages));
            _manager.Load();
            Assert.Null(_manager.Get(PackageId, "1.0.0"));

            Approve(Attest(packageBytes));
            _manager.VerifyAll();
            Assert.NotNull(_manager.Get(PackageId, "1.0.0"));

            // Revoked : the next list leaves it out
            Approve();
            _manager.VerifyAll();
            Assert.Null(_manager.Get(PackageId, "1.0.0"));
        }

        [Fact]
        public void Require_DoesNotCheckApprovals()
        {
            _manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            _approvals = null;
            Assert.True(TrakHoundPackage.Install(Pack(), _packages));

            _manager.Load();

            Assert.NotNull(_manager.Get(PackageId, "1.0.0"));
            Assert.Equal(TrakHoundPackageVerificationState.Verified, Assert.Single(_manager.GetVerificationReport().Packages).State);
        }

        [Fact]
        public void WouldRefuse_RequireApproval_ChecksTheListedManifest()
        {
            var approved = Pack("1.0.0");
            var other = Pack("2.0.0");
            Approve(Attest(approved));

            var packages = new[] { TrakHoundPackage.ReadInformationFromPackage(approved), TrakHoundPackage.ReadInformationFromPackage(other) };
            var manifests = new[] { approved, other }.Select(bytes =>
            {
                using var stream = new MemoryStream(bytes);
                var (manifest, signature) = TrakHoundPackageManifest.ReadFromArchive(stream);
                var package = TrakHoundPackage.ReadInformationFromPackage(bytes);
                return new TrakHoundPackageManifestJson { Uuid = package.Uuid, Manifest = manifest, Signature = signature };
            });

            var checks = _manager.CheckManifests(packages, manifests);

            Assert.False(_manager.WouldRefuse(checks[packages[0].Uuid]));
            Assert.True(_manager.WouldRefuse(checks[packages[1].Uuid]));
        }

        #endregion
    }
}
