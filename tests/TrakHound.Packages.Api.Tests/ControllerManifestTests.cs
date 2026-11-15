// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using TrakHound.Api;
using TrakHound.Hosting;
using TrakHound.Services;
using TrakHound.SqlServer.Service;
using TrakHound.Testing;

namespace TrakHound.Packages.Api.Tests
{
    /// <summary>
    /// A Fact that only runs when TRAKHOUND_SQLSERVER_TEST_SERVER names a SQL Server to test against (ex. (localdb)\MSSQLLocalDB).
    /// The tests create their own database and drop it after, since the Api's Tables have a fixed schema
    /// </summary>
    public sealed class SqlServerFactAttribute : FactAttribute
    {
        public SqlServerFactAttribute()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAKHOUND_SQLSERVER_TEST_SERVER")))
            {
                Skip = "Set TRAKHOUND_SQLSERVER_TEST_SERVER to run the SQL Server tests";
            }
        }
    }

    /// <summary>
    /// The Api's endpoints against a real SQL Server
    /// </summary>
    public class ControllerManifestTests : IAsyncLifetime
    {
        private const string Organization = "Test";
        private const string PackageId = "Test.Function";

        private readonly TrakHoundTestContext _context = new TrakHoundTestContext();
        private readonly TrakHoundPackageSigner _signer = TrakHoundPackageSigner.Generate();
        private readonly string _database = $"trakhound_packages_tests_{Guid.NewGuid():N}";
        private readonly string _source;
        private SqlServerTableService _master;
        private SqlServerTableService _tables;
        private Controller _controller;


        public ControllerManifestTests()
        {
            _source = Path.Combine(_context.Directory, "source");
            Directory.CreateDirectory(Path.Combine(_source, "dist"));
            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), "module");
        }

        public async Task InitializeAsync()
        {
            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("TRAKHOUND_SQLSERVER_TEST_SERVER"))) return;

            _master = new SqlServerTableService(CreateConfiguration("master"));
            await Execute(_master, $"create database [{_database}];");

            _tables = new SqlServerTableService(CreateConfiguration(_database));
            _context.Client.Services.Use(_tables);
            _controller = await _context.CreateApiController<Controller>();
        }

        public async Task DisposeAsync()
        {
            _tables?.Dispose();

            if (_master != null)
            {
                await Execute(_master, $"alter database [{_database}] set single_user with rollback immediate; drop database [{_database}];");
                _master.Dispose();
            }

            _signer.Dispose();
            _context.Dispose();
        }


        private static TrakHoundServiceConfiguration CreateConfiguration(string database)
        {
            var configuration = new TrakHoundServiceConfiguration { Id = "sql-tests", Name = "sql-tests" };
            configuration.SetParameter("server", Environment.GetEnvironmentVariable("TRAKHOUND_SQLSERVER_TEST_SERVER"));
            configuration.SetParameter("port", Environment.GetEnvironmentVariable("TRAKHOUND_SQLSERVER_TEST_PORT") ?? "0");
            configuration.SetParameter("database", database);
            configuration.SetParameter("user", Environment.GetEnvironmentVariable("TRAKHOUND_SQLSERVER_TEST_USER"));
            configuration.SetParameter("password", Environment.GetEnvironmentVariable("TRAKHOUND_SQLSERVER_TEST_PASSWORD"));
            return configuration;
        }

        private static async Task Execute(SqlServerTableService service, string query)
        {
            var response = await service.ExecuteNonQuery(query);
            Assert.True(response.Success, response.Message);
        }

        private byte[] Pack(string version = "1.0.0", TrakHoundPackageSigner signer = null, params (string Key, string Value)[] metadata)
        {
            var builder = new TrakHoundPackageBuilder { Category = "function", Id = PackageId, Version = version };
            foreach (var entry in metadata) builder.SetMetadata(entry.Key, entry.Value);

            return builder.Build().Package(_source, signer);
        }

        private async Task Upload(byte[] packageBytes)
        {
            using (var stream = new MemoryStream(packageBytes))
            {
                var response = await _controller.Upload(Organization, stream);
                Assert.Equal(200, response.StatusCode);
            }
        }

        private async Task<TrakHoundPackageManifestJson> GetManifest(string version = "1.0.0", string organization = Organization)
        {
            var response = await _controller.GetManifest(organization, PackageId, version);
            Assert.Equal(200, response.StatusCode);

            return JsonSerializer.Deserialize<TrakHoundPackageManifestJson>(response.GetContentBytes());
        }

        private async Task<TrakHoundPackageJson> GetPackage(string version = "1.0.0")
        {
            var response = await _controller.Get(Organization, PackageId, version);
            Assert.Equal(200, response.StatusCode);

            return JsonSerializer.Deserialize<TrakHoundPackageJson>(response.GetContentBytes());
        }

        private static (byte[] Manifest, byte[] Signature) Read(byte[] packageBytes)
        {
            using (var stream = new MemoryStream(packageBytes)) return TrakHoundPackageManifest.ReadFromArchive(stream);
        }


        [SqlServerFact]
        public async Task GetManifest_ReturnsBytesThatVerifyOnTheClient()
        {
            var packageBytes = Pack(signer: _signer);
            await Upload(packageBytes);

            var result = await GetManifest();

            var (manifest, signature) = Read(packageBytes);
            Assert.Equal(manifest, result.Manifest);
            Assert.Equal(signature, result.Signature);

            var publishers = new TrakHoundTrustedPublishers { Publishers = new List<TrakHoundTrustedPublisher> { new TrakHoundTrustedPublisher { Name = "Test", PublicKey = Convert.ToBase64String(TrakHoundPackageSigner.ReadPublicKeyPem(_signer.ExportPublicKeyPem())) } } };
            Assert.Equal(TrakHoundPackageManifestState.Trusted, TrakHoundPackageVerifier.VerifyManifest(result.Manifest, result.Signature, publishers).State);
        }

        [SqlServerFact]
        public async Task GetManifest_Wildcard_ReturnsTheHighestVersion()
        {
            await Upload(Pack("1.0.0"));
            await Upload(Pack("1.2.0"));

            var result = await GetManifest("1.*");

            Assert.Equal(TrakHoundPackage.GenerateUuid(PackageId, "1.2.0"), result.Uuid);
        }

        [SqlServerFact]
        public async Task GetManifest_OrganizationIsCaseInsensitive()
        {
            await Upload(Pack());

            Assert.NotNull((await GetManifest(organization: "test")).Manifest);
        }

        [SqlServerFact]
        public async Task GetManifest_Unknown_IsNotFound()
        {
            Assert.Equal(404, (await _controller.GetManifest(Organization, "Test.Missing", "1.0.0")).StatusCode);
            Assert.Equal(404, (await _controller.GetManifest(Organization, "Test.Missing", "*")).StatusCode);
        }

        [SqlServerFact]
        public async Task Upload_Again_ReplacesTheCachedManifest()
        {
            await Upload(Pack(signer: _signer));
            Assert.NotNull((await GetManifest()).Signature);

            await Upload(Pack());

            Assert.Null((await GetManifest()).Signature);
        }

        [SqlServerFact]
        public async Task Upload_Again_ReplacesWhatIsListed()
        {
            await Upload(Pack(metadata: ("removed", "value")));
            var first = await GetPackage();

            await Task.Delay(1100);
            File.WriteAllText(Path.Combine(_source, "dist", "module.dll"), "changed");
            var second = Pack();
            await Upload(second);

            var package = await GetPackage();
            var manifest = JsonSerializer.Deserialize<TrakHoundPackageManifest>((await GetManifest()).Manifest);

            // What's listed has to match the Manifest, or a client would see a genuine Manifest as wrong
            Assert.NotEqual(first.BuildDate, package.BuildDate);
            Assert.Equal(manifest.BuildDate.ToUniversalTime(), package.BuildDate.ToUniversalTime());
            Assert.Equal(manifest.GetContentHash(), package.ContentHash);
            Assert.False(package.Metadata?.ContainsKey("removed") ?? false);
        }

        [SqlServerFact]
        public async Task Listing_ReturnsTheContentHash()
        {
            await Upload(Pack());

            var response = await _controller.QueryLatest(Organization);
            var package = JsonSerializer.Deserialize<List<TrakHoundPackageJson>>(response.GetContentBytes()).Single();

            Assert.False(string.IsNullOrEmpty(package.ContentHash));
        }

        [SqlServerFact]
        public async Task Initialize_AddsTheContentHashColumnToAnExistingTable()
        {
            await Execute(_tables, "alter table [trakhound].[package] drop column [content_hash];");

            var controller = await _context.CreateApiController<Controller>();
            using (var stream = new MemoryStream(Pack()))
            {
                Assert.Equal(200, (await controller.Upload(Organization, stream)).StatusCode);
            }

            Assert.False(string.IsNullOrEmpty((await GetPackage()).ContentHash));
        }

        [SqlServerFact]
        public async Task Delete_RemovesTheManifest()
        {
            await Upload(Pack());
            await GetManifest();

            var uuid = TrakHoundPackage.GenerateUuid(PackageId, "1.0.0");
            Assert.Equal(200, (await _controller.DeletePackageVersion(Organization, uuid)).StatusCode);

            Assert.Equal(404, (await _controller.GetManifest(Organization, PackageId, "1.0.0")).StatusCode);
        }

        [SqlServerFact]
        public async Task GetManifests_LeavesOutWhatIsMissing()
        {
            await Upload(Pack("1.0.0"));
            await Upload(Pack("2.0.0", _signer));

            var uuids = new[] { TrakHoundPackage.GenerateUuid(PackageId, "1.0.0"), "missing", TrakHoundPackage.GenerateUuid(PackageId, "2.0.0") };
            var response = await _controller.GetManifests(Organization, uuids);
            var results = JsonSerializer.Deserialize<List<TrakHoundPackageManifestJson>>(response.GetContentBytes());

            Assert.Equal(200, response.StatusCode);
            Assert.Equal(new[] { uuids[0], uuids[2] }, results.Select(o => o.Uuid));
            Assert.Null(results[0].Signature);
            Assert.NotNull(results[1].Signature);
        }

        [SqlServerFact]
        public async Task Http_RoutesAndBodiesReachTheEndpoints()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Services.AddTrakHound(builder.Configuration.GetSection(TrakHoundHostOptions.SectionName));

            await using var app = builder.Build();
            app.MapTrakHoundApi(new TrakHoundApiHostOptions { Assembly = typeof(Controller).Assembly, Client = _context.Client, Volume = _context.Volume, Variables = _context.Variables, Configuration = new TrakHoundApiConfiguration { Id = "packages", Route = "packages" } });
            await app.StartAsync();

            var baseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First().TrimEnd('/');
            using var http = new HttpClient();

            var packageBytes = Pack(signer: _signer);
            using (var content = new ByteArrayContent(packageBytes))
            {
                content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/octet-stream");
                Assert.True((await http.PostAsync($"{baseUrl}/{Organization}", content)).IsSuccessStatusCode);
            }

            var (manifest, signature) = Read(packageBytes);

            var single = JsonSerializer.Deserialize<TrakHoundPackageManifestJson>(await http.GetByteArrayAsync($"{baseUrl}/{Organization}/{PackageId}/1.0.0/manifest"));
            Assert.Equal(manifest, single.Manifest);
            Assert.Equal(signature, single.Signature);

            // The Get endpoint with the same first segments still answers
            Assert.True((await http.GetAsync($"{baseUrl}/{Organization}/{PackageId}/1.0.0")).IsSuccessStatusCode);

            var body = JsonSerializer.Serialize(new[] { TrakHoundPackage.GenerateUuid(PackageId, "1.0.0") });
            using (var content = new StringContent(body, System.Text.Encoding.UTF8, "application/json"))
            {
                var response = await http.PostAsync($"{baseUrl}/{Organization}/manifests", content);
                Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());

                var batch = JsonSerializer.Deserialize<List<TrakHoundPackageManifestJson>>(await response.Content.ReadAsByteArrayAsync());
                Assert.Equal(manifest, Assert.Single(batch).Manifest);
            }

            await app.StopAsync();
        }

        [SqlServerFact]
        public async Task GetManifests_OverTheLimit_IsBadRequest()
        {
            var uuids = Enumerable.Range(0, 201).Select(o => o.ToString());

            Assert.Equal(400, (await _controller.GetManifests(Organization, uuids)).StatusCode);
        }


        private const string DevBuild1 = "0.3.4-dev.20261114150000";
        private const string DevBuild2 = "0.3.4-dev.20261114160000";

        private async Task<List<string>> GetVersions()
        {
            var response = await _controller.GetVersions(Organization, PackageId);
            Assert.Equal(200, response.StatusCode);

            return JsonSerializer.Deserialize<List<string>>(response.GetContentBytes());
        }

        private async Task<string> GetLatestVersion()
        {
            var response = await _controller.GetLatest(Organization, PackageId);
            Assert.Equal(200, response.StatusCode);

            return JsonSerializer.Deserialize<TrakHoundPackageJson>(response.GetContentBytes()).Version;
        }

        [SqlServerFact]
        public async Task Prerelease_KeepsItsLabel()
        {
            await Upload(Pack(DevBuild1));

            var package = await GetPackage(DevBuild1);
            Assert.Equal(DevBuild1, package.Version);
            Assert.Equal(TrakHoundPackage.GenerateUuid(PackageId, DevBuild1), package.Uuid);

            // The Stable Version isn't one of its Prereleases
            Assert.Equal(404, (await _controller.Get(Organization, PackageId, "0.3.4")).StatusCode);
        }

        [SqlServerFact]
        public async Task Versions_IncludePrereleases_InSemanticVersioningOrder()
        {
            await Upload(Pack("0.3.4"));
            await Upload(Pack(DevBuild2));
            await Upload(Pack("0.3.3"));
            await Upload(Pack(DevBuild1));

            Assert.Equal(new[] { "0.3.3", DevBuild1, DevBuild2, "0.3.4" }, await GetVersions());
        }

        [SqlServerFact]
        public async Task Latest_IsTheNewestPrerelease_WithoutAStableVersion()
        {
            // Development builds of the same commit share 0.3.4, so the newest Prerelease Label decides (not the upload order)
            await Upload(Pack(DevBuild2));
            await Upload(Pack(DevBuild1));

            Assert.Equal(DevBuild2, await GetLatestVersion());
        }

        [SqlServerFact]
        public async Task Latest_PrefersAStableVersion()
        {
            await Upload(Pack("0.3.3"));
            await Upload(Pack(DevBuild1));

            // The same Version an Instance installs for "*"
            Assert.Equal("0.3.3", await GetLatestVersion());

            await Upload(Pack("0.3.4"));
            Assert.Equal("0.3.4", await GetLatestVersion());
        }

        [SqlServerFact]
        public async Task Wildcard_OnlyIncludesPrereleasesWhenAsked()
        {
            await Upload(Pack("0.3.3"));
            await Upload(Pack(DevBuild1));

            Assert.Equal("0.3.3", (await GetPackage("0.3.*")).Version);
            Assert.Equal(DevBuild1, (await GetPackage("0.3.*-*")).Version);
            Assert.Equal(TrakHoundPackage.GenerateUuid(PackageId, DevBuild1), (await GetManifest("0.*-*")).Uuid);
        }

        [SqlServerFact]
        public async Task Upload_InvalidVersion_IsBadRequest()
        {
            using (var stream = new MemoryStream(Pack("not-a-version")))
            {
                Assert.Equal(400, (await _controller.Upload(Organization, stream)).StatusCode);
            }
        }

        [SqlServerFact]
        public async Task Initialize_AddsTheVersionPrereleaseColumnToAnExistingTable()
        {
            await Execute(_tables, "alter table [trakhound].[package] drop column [version_prerelease];");

            var controller = await _context.CreateApiController<Controller>();
            using (var stream = new MemoryStream(Pack(DevBuild1)))
            {
                Assert.Equal(200, (await controller.Upload(Organization, stream)).StatusCode);
            }

            Assert.Equal(DevBuild1, (await GetPackage(DevBuild1)).Version);
        }
    }
}
