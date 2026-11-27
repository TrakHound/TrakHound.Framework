// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using System.Text.Json;
using TrakHound.Instance.Configurations;
using TrakHound.Instance.Pages.Packages;
using TrakHound.Packages;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// Installed Packages are checked for Updates where they were downloaded from (their origin), not only on the selected Source
    /// </summary>
    public class PackageOriginUpdateTests : IAsyncLifetime
    {
        private const string PackageId = "Test.Function";
        private const string OtherPackageId = "Test.Other";

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"trakhound-origin-update-tests-{Guid.NewGuid()}");
        private WebApplication _server;
        private string _baseUrl;
        private TrakHoundPackageManager _manager;


        // The latest Packages of each Organization on the test server
        private static readonly Dictionary<string, (string Id, string Version)[]> _latest = new Dictionary<string, (string, string)[]>(StringComparer.OrdinalIgnoreCase)
        {
            ["Public"] = new[] { (PackageId, "2.0.0"), (OtherPackageId, "5.0.0") },
            ["Contoso"] = new[] { (PackageId, "3.0.0") }
        };

        public async Task InitializeAsync()
        {
            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            _server = builder.Build();
            _server.MapGet("/packages/{organization}/latest", (string organization) =>
            {
                var packages = _latest.GetValueOrDefault(organization) ?? Array.Empty<(string, string)>();
                var json = packages.Select(o => new TrakHoundPackageJson { Uuid = TrakHoundPackage.GenerateUuid(o.Item1, o.Item2), Id = o.Item1, Version = o.Item2, Category = "function", BuildDate = DateTime.UtcNow });
                return Results.Content(JsonSerializer.Serialize(json), "application/json");
            });
            await _server.StartAsync();

            _baseUrl = _server.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First().TrimEnd('/');

            Directory.CreateDirectory(Path.Combine(_root, "source", "dist"));
            File.WriteAllText(Path.Combine(_root, "source", "dist", "module.dll"), "module");
            _manager = new TrakHoundPackageManager(Path.Combine(_root, "packages"));
        }

        public async Task DisposeAsync()
        {
            _manager.Dispose();
            await _server.DisposeAsync();
            try { Directory.Delete(_root, true); } catch { }
        }


        private async Task Install(string id, string version, TrakHoundPackageOrigin origin)
        {
            var builder = new TrakHoundPackageBuilder { Category = "function", Id = id, Version = version };
            var result = Assert.Single(await _manager.Install(builder.Build().Package(Path.Combine(_root, "source"), null), origin: origin));
            Assert.True(result.Success, result.Message);
        }

        private RemoteConfiguration Source(string organization) => new RemoteConfiguration { Id = organization.ToLower(), Name = organization, BaseUrl = _baseUrl, Organization = organization };

        private async Task<(Dictionary<string, TrakHoundPackage> Packages, Dictionary<string, TrakHoundPackageClient> Clients)> Query(IEnumerable<RemoteConfiguration> sources)
        {
            var selected = PackagesContext.CreateClient(sources.First());
            var (packages, _) = await PackagesContext.QueryAvailable(selected, _manager, new HashSet<string>());
            return await PackagesContext.QueryOrigins(selected, packages, sources, _manager, new HashSet<string>());
        }


        [Fact]
        public async Task APackage_IsComparedWithItsOrigin_NotTheSelectedSource()
        {
            await Install(PackageId, "1.0.0", new TrakHoundPackageOrigin(_baseUrl, "Contoso"));
            await Install(OtherPackageId, "1.0.0", null);

            // Public is selected, and Contoso isn't a Source : it's found as another Organization on Public's server
            var (packages, clients) = await Query(new[] { Source("Public") });

            Assert.Equal("3.0.0", packages[PackageId].Version);
            Assert.Equal("Contoso", clients[packages[PackageId].Uuid].Organization);

            // Without an origin (ex. uploaded), the selected Source
            Assert.Equal("5.0.0", packages[OtherPackageId].Version);
            Assert.False(clients.ContainsKey(packages[OtherPackageId].Uuid));
        }

        [Fact]
        public async Task APackage_FromTheSelectedSource_UsesIt()
        {
            await Install(PackageId, "1.0.0", new TrakHoundPackageOrigin(_baseUrl + "/", "public"));

            var (packages, clients) = await Query(new[] { Source("Public"), Source("Contoso") });

            Assert.Equal("2.0.0", packages[PackageId].Version);
            Assert.Empty(clients);
        }

        [Fact]
        public async Task AnOrigin_OnAServerThatIsntASource_OffersNoUpdate()
        {
            await Install(PackageId, "1.0.0", new TrakHoundPackageOrigin("http://127.0.0.1:1", "Contoso"));

            var (packages, _) = await Query(new[] { Source("Public") });

            // Not the Public Package with the same ID
            Assert.False(packages.ContainsKey(PackageId));
            Assert.Null(PackagesContext.CreateOriginClient(new[] { Source("Public") }, new TrakHoundPackageOrigin("http://127.0.0.1:1", "Contoso")));
        }
    }
}
