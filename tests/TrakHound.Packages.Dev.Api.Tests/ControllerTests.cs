// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Api;
using TrakHound.Security;
using TrakHound.Testing;

namespace TrakHound.Packages.Dev.Api.Tests
{
    public class ControllerTests : IDisposable
    {
        private const string Organization = "Dev";
        private const string PackageId = "Test.Function";

        private readonly TrakHoundTestContext _context = new TrakHoundTestContext();
        private readonly string _source;


        public ControllerTests()
        {
            _source = Path.Combine(_context.Directory, "source");
            Directory.CreateDirectory(Path.Combine(_source, "dist"));
            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), "module");
        }

        public void Dispose() => _context.Dispose();


        private Task<Controller> CreateController(params (string Name, string Value)[] parameters)
        {
            foreach (var parameter in parameters) _context.Parameters[parameter.Name] = parameter.Value;
            return _context.CreateApiController<Controller>();
        }

        private byte[] Pack(string version = "1.0.0", string id = PackageId, string category = "function", TrakHoundPackageSigner signer = null)
        {
            var builder = new TrakHoundPackageBuilder { Category = category, Id = id, Version = version };
            return builder.Build().Package(_source, signer);
        }

        private static async Task<TrakHoundApiResponse> Upload(Controller controller, byte[] packageBytes, string organization = Organization, string token = null)
        {
            using (var stream = new MemoryStream(packageBytes)) return await controller.Upload(organization, stream, token);
        }

        private static T Read<T>(TrakHoundApiResponse response) => JsonSerializer.Deserialize<T>(response.GetContentBytes());

        private static string Text(TrakHoundApiResponse response) => System.Text.Encoding.UTF8.GetString(response.GetContentBytes() ?? Array.Empty<byte>());

        private string VolumePath(string path) => Path.Combine(_context.Directory, "volume", path);


        #region "Upload and Read"

        [Fact]
        public async Task Upload_KeepsTheFileByOrganizationIdAndVersion()
        {
            var controller = await CreateController();
            var packageBytes = Pack();

            var response = await Upload(controller, packageBytes);

            Assert.Equal(200, response.StatusCode);
            Assert.Equal(PackageId, Read<TrakHoundPackageJson>(response).Id);
            Assert.Equal(packageBytes, File.ReadAllBytes(VolumePath($"packages/{Organization}/{PackageId}/1.0.0.thp")));
        }

        [Fact]
        public async Task Get_ByIdAndVersion_ByUuid_AndLatest()
        {
            var controller = await CreateController();
            await Upload(controller, Pack("1.0.0"));
            await Upload(controller, Pack("1.2.0"));

            Assert.Equal("1.0.0", Read<TrakHoundPackageJson>(await controller.Get(Organization, PackageId, "1.0.0")).Version);
            Assert.Equal("1.2.0", Read<TrakHoundPackageJson>(await controller.Get(Organization, PackageId, "1.*")).Version);
            Assert.Equal("1.2.0", Read<TrakHoundPackageJson>(await controller.GetLatest(Organization, PackageId)).Version);
            Assert.Equal("1.0.0", Read<TrakHoundPackageJson>(await controller.Get(Organization, TrakHoundPackage.GenerateUuid(PackageId, "1.0.0"))).Version);
            Assert.Equal(new[] { "1.0.0", "1.2.0" }, Read<List<string>>(await controller.GetVersions(Organization, PackageId)));
        }

        [Fact]
        public async Task Latest_PrefersAStableVersion_AndWildcardPrereleaseSelectsEither()
        {
            var controller = await CreateController();
            await Upload(controller, Pack("1.0.0"));
            await Upload(controller, Pack("1.1.0-dev.1"));

            Assert.Equal("1.0.0", Read<TrakHoundPackageJson>(await controller.GetLatest(Organization, PackageId)).Version);
            Assert.Equal("1.1.0-dev.1", Read<TrakHoundPackageJson>(await controller.Get(Organization, PackageId, "*-*")).Version);
            Assert.Equal("1.1.0-dev.1", Read<TrakHoundPackageJson>(await controller.Get(Organization, PackageId, "1.1.0-dev.1")).Version);
        }

        [Fact]
        public async Task ShortVersion_IsListedWithThreeParts_AndDownloadsByEither()
        {
            var controller = await CreateController();
            var packageBytes = Pack("1.2");
            await Upload(controller, packageBytes);

            Assert.Equal(new[] { "1.2.0" }, Read<List<string>>(await controller.GetVersions(Organization, PackageId)));
            Assert.Equal(packageBytes, (await controller.Download(Organization, PackageId, "1.2")).GetContentBytes());
            Assert.Equal(packageBytes, (await controller.Download(Organization, PackageId, "1.2.0")).GetContentBytes());
        }

        [Fact]
        public async Task Download_ReturnsTheUploadedBytes()
        {
            var controller = await CreateController();
            var packageBytes = Pack();
            await Upload(controller, packageBytes);

            var byVersion = await controller.Download(Organization, PackageId, "1.0.0");
            var byUuid = await controller.Download(Organization, TrakHoundPackage.GenerateUuid(PackageId, "1.0.0"));
            var wildcard = await controller.Download(Organization, PackageId, "*");

            Assert.Equal(packageBytes, byVersion.GetContentBytes());
            Assert.Equal(packageBytes, byUuid.GetContentBytes());
            Assert.Equal(packageBytes, wildcard.GetContentBytes());
            Assert.Equal(404, (await controller.Download(Organization, "Test.Missing", "1.0.0")).StatusCode);
        }

        [Fact]
        public async Task Manifest_IsTheSignedBytesFromTheFile()
        {
            var controller = await CreateController();
            using var signer = TrakHoundPackageSigner.Generate();
            var packageBytes = Pack(signer: signer);
            await Upload(controller, packageBytes);

            var manifest = Read<TrakHoundPackageManifestJson>(await controller.GetManifest(Organization, PackageId, "1.0.0"));

            using var stream = new MemoryStream(packageBytes);
            var (expectedManifest, expectedSignature) = TrakHoundPackageManifest.ReadFromArchive(stream);
            Assert.Equal(expectedManifest, manifest.Manifest);
            Assert.Equal(expectedSignature, manifest.Signature);

            var manifests = Read<List<TrakHoundPackageManifestJson>>(await controller.GetManifests(Organization, new[] { TrakHoundPackage.GenerateUuid(PackageId, "1.0.0"), "missing" }));
            Assert.Single(manifests);
        }

        [Fact]
        public async Task QueryLatest_ListsEachPackage_AndFiltersByCategoryAndPrefix()
        {
            var controller = await CreateController();
            await Upload(controller, Pack("1.0.0"));
            await Upload(controller, Pack("1.0.0", id: "Test.App", category: "app"));

            var all = Read<List<TrakHoundPackageJson>>(await controller.QueryLatest(Organization));
            Assert.Equal(new[] { "Test.App", "Test.Function" }, all.Select(o => o.Id));

            var apps = Read<List<TrakHoundPackageJson>>(await controller.QueryByCategory(Organization, "app"));
            Assert.Equal("Test.App", Assert.Single(apps).Id);

            var prefix = Read<List<TrakHoundPackageJson>>(await controller.QueryLatest(Organization, "Test.F"));
            Assert.Equal(PackageId, Assert.Single(prefix).Id);

            Assert.Equal(404, (await controller.QueryLatest(Organization, "Missing")).StatusCode);
        }

        [Fact]
        public async Task Organization_MatchesCaseInsensitively()
        {
            var controller = await CreateController();
            await Upload(controller, Pack("1.0.0"));
            await Upload(controller, Pack("1.1.0"), organization: "dev");

            Assert.Equal(new[] { Organization }, Read<List<string>>(await controller.ListOrganizations()));
            Assert.True(File.Exists(VolumePath($"packages/{Organization}/{PackageId}/1.1.0.thp")));
            Assert.Equal(200, (await controller.Get("DEV", PackageId, "1.1.0")).StatusCode);
        }

        #endregion

        #region "Replacing and Keeping Versions"

        [Fact]
        public async Task Upload_SameVersion_IsRefused()
        {
            var controller = await CreateController();
            await Upload(controller, Pack());

            var response = await Upload(controller, Pack());

            Assert.Equal(409, response.StatusCode);
            Assert.Contains("already stored", Text(response));
        }

        [Fact]
        public async Task Upload_SameVersion_WithAllowOverwrite_ReplacesTheFile()
        {
            var controller = await CreateController((Controller.AllowOverwriteParameter, "true"));
            await Upload(controller, Pack());

            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), "changed");
            var changed = Pack();

            Assert.Equal(200, (await Upload(controller, changed)).StatusCode);
            Assert.Equal(changed, (await controller.Download(Organization, PackageId, "1.0.0")).GetContentBytes());
        }

        [Fact]
        public async Task KeepVersions_DeletesTheOldestVersions()
        {
            var controller = await CreateController((Controller.KeepVersionsParameter, "2"));
            await Upload(controller, Pack("1.0.0"));
            await Upload(controller, Pack("1.1.0"));
            await Upload(controller, Pack("1.2.0"));

            Assert.Equal(new[] { "1.1.0", "1.2.0" }, Read<List<string>>(await controller.GetVersions(Organization, PackageId)));
            Assert.False(File.Exists(VolumePath($"packages/{Organization}/{PackageId}/1.0.0.thp")));
        }

        [Fact]
        public async Task KeepVersions_KeepsAnOlderVersionJustUploaded()
        {
            var controller = await CreateController((Controller.KeepVersionsParameter, "1"));
            await Upload(controller, Pack("2.0.0"));
            await Upload(controller, Pack("1.0.0"));

            Assert.Equal(new[] { "1.0.0", "2.0.0" }, Read<List<string>>(await controller.GetVersions(Organization, PackageId)));
        }

        #endregion

        #region "Invalid Uploads"

        [Fact]
        public async Task Upload_NotAPackage_IsABadRequest()
        {
            var controller = await CreateController();

            Assert.Equal(400, (await Upload(controller, new byte[] { 1, 2, 3 })).StatusCode);
            Assert.False(Directory.Exists(VolumePath("packages")));
        }

        [Theory]
        [InlineData("..")]
        [InlineData(".hidden")]
        [InlineData("a/b")]
        [InlineData("a\\b")]
        public async Task Upload_InvalidOrganization_IsABadRequest(string organization)
        {
            var controller = await CreateController();

            Assert.Equal(400, (await Upload(controller, Pack(), organization)).StatusCode);
            Assert.False(Directory.Exists(VolumePath("packages")));
        }

        [Fact]
        public async Task Upload_LargerThanTheLimit_IsRefused()
        {
            var controller = await CreateController((Controller.MaxUploadSizeParameter, "1"));
            File.WriteAllBytes(Path.Combine(_source, "dist", "large.bin"), System.Security.Cryptography.RandomNumberGenerator.GetBytes(2 * 1024 * 1024));

            Assert.Equal(413, (await Upload(controller, Pack())).StatusCode);
            Assert.False(Directory.Exists(VolumePath("packages")));
        }

        #endregion

        #region "Files Added by Hand"

        [Fact]
        public async Task AFileCopiedIn_IsListed()
        {
            var controller = await CreateController();
            var packageBytes = Pack("3.0.0");

            Directory.CreateDirectory(VolumePath($"packages/{Organization}"));
            File.WriteAllBytes(VolumePath($"packages/{Organization}/copied-by-hand.thp"), packageBytes);
            File.WriteAllText(VolumePath($"packages/{Organization}/notes.thp"), "not a package");
            await controller.Store.Refresh();

            Assert.Equal(packageBytes, (await controller.Download(Organization, PackageId, "3.0.0")).GetContentBytes());
            Assert.Single(Read<List<TrakHoundPackageJson>>(await controller.QueryLatest(Organization)));
        }

        [Fact]
        public async Task AFileInThePackagesDirectory_HasNoOrganization_AndIsIgnored()
        {
            var controller = await CreateController();
            Directory.CreateDirectory(VolumePath("packages"));
            File.WriteAllBytes(VolumePath("packages/root.thp"), Pack());
            await controller.Store.Refresh();

            Assert.Equal(404, (await controller.ListOrganizations()).StatusCode);
        }

        [Fact]
        public async Task UploadingAVersionCopiedIn_ReplacesTheCopy()
        {
            var controller = await CreateController((Controller.AllowOverwriteParameter, "true"));
            Directory.CreateDirectory(VolumePath($"packages/{Organization}"));
            File.WriteAllBytes(VolumePath($"packages/{Organization}/copied-by-hand.thp"), Pack());
            await controller.Store.Refresh();

            Assert.Equal(200, (await Upload(controller, Pack())).StatusCode);

            Assert.False(File.Exists(VolumePath($"packages/{Organization}/copied-by-hand.thp")));
            Assert.True(File.Exists(VolumePath($"packages/{Organization}/{PackageId}/1.0.0.thp")));
        }

        #endregion

        #region "Delete"

        [Fact]
        public async Task DeleteVersion_RemovesTheFileAndItsDirectory()
        {
            var controller = await CreateController();
            await Upload(controller, Pack());

            Assert.Equal(200, (await controller.DeletePackageVersion(Organization, TrakHoundPackage.GenerateUuid(PackageId, "1.0.0"))).StatusCode);

            Assert.Equal(404, (await controller.Get(Organization, PackageId, "1.0.0")).StatusCode);
            Assert.False(Directory.Exists(VolumePath($"packages/{Organization}/{PackageId}")));
            Assert.Equal(404, (await controller.DeletePackageVersion(Organization, TrakHoundPackage.GenerateUuid(PackageId, "1.0.0"))).StatusCode);
        }

        [Fact]
        public async Task DeletePackage_RemovesEveryVersion()
        {
            var controller = await CreateController();
            await Upload(controller, Pack("1.0.0"));
            await Upload(controller, Pack("1.1.0"));

            Assert.Equal(200, (await controller.DeletePackage(Organization, PackageId)).StatusCode);

            Assert.Equal(404, (await controller.GetVersions(Organization, PackageId)).StatusCode);
        }

        #endregion

        #region "Authorization"

        private sealed class AllowAll : ITrakHoundAuthorizer
        {
            public TrakHoundAuthorizationResult Authorize(TrakHoundIdentity identity, string resourceId, string permission = null) => TrakHoundAuthorizationResult.Unrestricted();
        }

        [Fact]
        public async Task AnonymousRequest_CannotUploadOrDelete()
        {
            var controller = await CreateController((Controller.UploadTokenParameter, "secret-token"));

            using (TrakHoundSecurityContext.Begin(new AllowAll(), null))
            {
                Assert.Equal(401, (await Upload(controller, Pack())).StatusCode);
                Assert.Equal(401, (await Upload(controller, Pack(), token: "wrong")).StatusCode);
                Assert.Equal(401, (await controller.DeletePackage(Organization, PackageId)).StatusCode);
                Assert.Equal(401, (await controller.DeletePackageVersion(Organization, "uuid")).StatusCode);
            }

            Assert.False(Directory.Exists(VolumePath("packages")));
        }

        [Fact]
        public async Task AnonymousRequest_WithoutAnUploadToken_CannotUpload()
        {
            var controller = await CreateController();

            using (TrakHoundSecurityContext.Begin(new AllowAll(), null))
            {
                // An empty token never matches an unset uploadToken
                Assert.Equal(401, (await Upload(controller, Pack(), token: "")).StatusCode);
                Assert.Equal(401, (await Upload(controller, Pack(), token: "anything")).StatusCode);
            }
        }

        [Fact]
        public async Task UploadToken_CanUploadAndDelete()
        {
            var controller = await CreateController((Controller.UploadTokenParameter, "secret-token"));

            using (TrakHoundSecurityContext.Begin(new AllowAll(), null))
            {
                Assert.Equal(200, (await Upload(controller, Pack(), token: "secret-token")).StatusCode);
                Assert.Equal(200, (await controller.DeletePackage(Organization, PackageId, "secret-token")).StatusCode);
            }
        }

        [Fact]
        public async Task InstanceIdentity_CanUpload()
        {
            var controller = await CreateController();

            using (TrakHoundSecurityContext.Begin(new AllowAll(), new TrakHoundIdentity { UserId = "key-1", Name = "Developer Key" }))
            {
                Assert.Equal(200, (await Upload(controller, Pack())).StatusCode);
            }

            Assert.Contains(_context.Logs, o => o.Message.Contains("by Developer Key"));
        }

        [Fact]
        public async Task Reads_DoNotNeedAToken()
        {
            var controller = await CreateController((Controller.UploadTokenParameter, "secret-token"));
            await Upload(controller, Pack());

            using (TrakHoundSecurityContext.Begin(new AllowAll(), null))
            {
                Assert.Equal(200, (await controller.Get(Organization, PackageId, "1.0.0")).StatusCode);
                Assert.Equal(200, (await controller.Download(Organization, PackageId, "1.0.0")).StatusCode);
            }
        }

        #endregion
    }
}
