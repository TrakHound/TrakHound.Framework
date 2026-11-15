// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using NLog;
using NLog.Config;
using NLog.Targets;
using System.Collections.Concurrent;
using System.Text.Json;
using TrakHound.Instance.Configurations;
using TrakHound.Instance.Pages.Packages;
using TrakHound.Instance.PackageSources;
using TrakHound.Packages;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// Reporting the installed Packages to the Package Sources that are TrakHound Package Managers, and syncing their Trusted Publishers
    /// </summary>
    public class PackageManagerSourceTests : IDisposable
    {
        private readonly string _root = Path.Combine(Path.GetTempPath(), $"trakhound-package-manager-tests-{Guid.NewGuid()}");
        private readonly LogFactory _logFactory;
        private readonly MemoryTarget _log = new MemoryTarget("memory") { Layout = "${level}|${message}" };
        private readonly Logger _logger;

        private static readonly RemoteConfiguration _source = new RemoteConfiguration { Id = "pm", Name = "Contoso PM", BaseUrl = "https://pm.contoso.com/feed", PackageManager = true };


        public PackageManagerSourceTests()
        {
            _logFactory = new LogFactory();
            var configuration = new LoggingConfiguration(_logFactory);
            configuration.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, _log);
            _logFactory.Configuration = configuration;
            _logger = _logFactory.GetLogger("test");
        }

        public void Dispose()
        {
            _logFactory.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        private List<string> Warnings => _log.Logs.Where(o => o.StartsWith("Warn|")).ToList();

        private static TrakHoundPackageInventory Inventory(string reason, string version = "1.0.0")
        {
            return new TrakHoundPackageInventory
            {
                Reason = reason,
                Packages = new List<TrakHoundPackageInventoryItem> { new TrakHoundPackageInventoryItem { Id = "Test.Function", Version = version, State = "Verified" } }
            };
        }

        private TrakHoundPackageInventoryReporter CreateReporter(ConcurrentQueue<string> sent, Func<int> status = null, Func<string> version = null, TimeSpan? changeDelay = null)
        {
            return new TrakHoundPackageInventoryReporter(
                () => new[] { _source },
                reason => Inventory(reason, version?.Invoke() ?? "1.0.0"),
                (source, inventory, token) =>
                {
                    sent.Enqueue(inventory.Reason);
                    var code = status?.Invoke() ?? 200;
                    return Task.FromResult((code, code == 200 ? inventory.Packages.Count : 0, code == 200 ? (string)null : $"Status {code}"));
                },
                () => true,
                () => 0,
                changeDelay ?? TimeSpan.FromMilliseconds(300),
                _logger);
        }

        private static async Task<bool> WaitFor(Func<bool> condition, int timeoutMs = 5000)
        {
            var stop = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (DateTime.UtcNow < stop)
            {
                if (condition()) return true;
                await Task.Delay(50);
            }

            return condition();
        }


        [Fact]
        public async Task Changes_AreDebouncedIntoOneReport()
        {
            var sent = new ConcurrentQueue<string>();
            var version = 1;
            using var reporter = CreateReporter(sent, version: () => $"{version}.0.0");

            await reporter.StartAsync(CancellationToken.None);
            Assert.True(await WaitFor(() => sent.Count == 1));
            Assert.Equal("startup", sent.Single());

            // A burst of changes, each within the delay of the last
            version = 2;
            for (var i = 0; i < 5; i++)
            {
                reporter.NotifyChanged();
                await Task.Delay(100);
            }

            Assert.Single(sent);
            Assert.True(await WaitFor(() => sent.Count == 2));
            await Task.Delay(600);

            Assert.Equal(new[] { "startup", "change" }, sent);
            await reporter.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task Change_WithThePackagesLastReported_IsNotSent()
        {
            var sent = new ConcurrentQueue<string>();
            using var reporter = CreateReporter(sent);

            await reporter.StartAsync(CancellationToken.None);
            Assert.True(await WaitFor(() => sent.Count == 1));

            reporter.NotifyChanged();
            await Task.Delay(800);
            Assert.Single(sent);

            // A Heartbeat is sent even when nothing changed
            await reporter.Report(TrakHoundPackageInventoryReason.Heartbeat);
            Assert.Equal(new[] { "startup", "heartbeat" }, sent);
            await reporter.StopAsync(CancellationToken.None);
        }

        [Fact]
        public async Task Changes_BeforeTheStartupReport_AreLeftToIt()
        {
            var sent = new ConcurrentQueue<string>();
            using var reporter = CreateReporter(sent);

            reporter.NotifyChanged();
            await Task.Delay(600);

            Assert.Empty(sent);
        }

        [Theory]
        [InlineData(401, "401 Unauthorized")]
        [InlineData(403, "403 Forbidden")]
        public async Task RefusedToken_IsLoggedOnce_UntilItSucceedsAgain(int statusCode, string expected)
        {
            var sent = new ConcurrentQueue<string>();
            var status = statusCode;
            using var reporter = CreateReporter(sent, status: () => status);

            await reporter.Report(TrakHoundPackageInventoryReason.Startup);
            await reporter.Report(TrakHoundPackageInventoryReason.Heartbeat);
            await reporter.Report(TrakHoundPackageInventoryReason.Heartbeat);

            Assert.Equal(3, sent.Count);
            var warning = Assert.Single(Warnings);
            Assert.Contains(expected, warning);
            Assert.Contains("Contoso PM", warning);

            // A different kind of failure is logged, then recovering is logged once
            status = 0;
            await reporter.Report(TrakHoundPackageInventoryReason.Heartbeat);
            Assert.Equal(2, Warnings.Count);
            Assert.Contains("couldn't be reached", Warnings[1]);

            status = 200;
            await reporter.Report(TrakHoundPackageInventoryReason.Heartbeat);
            await reporter.Report(TrakHoundPackageInventoryReason.Heartbeat);
            Assert.Single(_log.Logs, o => o.StartsWith("Info|") && o.Contains("again"));

            // Failing again after recovering is logged again
            status = statusCode;
            await reporter.Report(TrakHoundPackageInventoryReason.Heartbeat);
            Assert.Equal(3, Warnings.Count);
        }

        [Fact]
        public async Task Report_SendFails_IsNotThrown_AndIsSentWithTheNextChange()
        {
            var sent = new ConcurrentQueue<string>();
            var fail = true;
            using var reporter = new TrakHoundPackageInventoryReporter(
                () => new[] { _source },
                reason => Inventory(reason),
                (source, inventory, token) =>
                {
                    sent.Enqueue(inventory.Reason);
                    if (fail) throw new HttpRequestException("Connection refused");
                    return Task.FromResult((200, 1, (string)null));
                },
                () => true, () => 0, TimeSpan.FromMilliseconds(100), _logger);

            await reporter.Report(TrakHoundPackageInventoryReason.Startup);
            Assert.Single(Warnings);

            // The same Packages are sent with a change, since the last report didn't reach the Package Manager
            fail = false;
            await reporter.Report(TrakHoundPackageInventoryReason.Change);
            Assert.Equal(new[] { "startup", "change" }, sent);
        }

        [Fact]
        public void PackageManagerSources_OnlyFlaggedSourcesWithAUrl()
        {
            var remotes = new RemotesConfiguration
            {
                Remotes = new[]
                {
                    new RemoteConfiguration { Id = "public", BaseUrl = "https://packages.trakhound.com" },
                    _source,
                    new RemoteConfiguration { Id = "no-url", PackageManager = true }
                }
            };

            Assert.Equal(new[] { "pm" }, TrakHoundPackageInventoryReporter.GetPackageManagerSources(remotes).Select(o => o.Id));
        }

        [Fact]
        public void RemotesConfiguration_KeepsThePackageManagerFlag()
        {
            Directory.CreateDirectory(_root);
            var path = Path.Combine(_root, RemotesConfiguration.Filename);
            RemotesConfiguration.Write(new RemotesConfiguration { Remotes = new[] { _source, new RemoteConfiguration { Id = "public", BaseUrl = "https://packages.trakhound.com" } } }, path);

            var read = RemotesConfiguration.Read(path);

            Assert.Equal(new[] { true, false }, read.Remotes.Select(o => o.PackageManager));
        }


        private static TrakHoundTrustedPublishers Publishers(params (string Name, TrakHoundPackageSigner Signer)[] publishers)
        {
            return new TrakHoundTrustedPublishers
            {
                Publishers = publishers.Select(o => new TrakHoundTrustedPublisher { Name = o.Name, PublicKey = Convert.ToBase64String(TrakHoundPackageSigner.ReadPublicKeyPem(o.Signer.ExportPublicKeyPem())) }).ToList()
            };
        }

        [Fact]
        public async Task PublisherSync_CachesThePublishers_AndKeepsTheLastGoodCopy()
        {
            using var signer = TrakHoundPackageSigner.Generate();
            var sources = new List<RemoteConfiguration> { _source };
            (int, TrakHoundTrustedPublishers, string) response = (200, Publishers(("Contoso", signer)), null);
            var changed = 0;

            var sync = new TrakHoundPackageManagerPublisherSync(_root, () => sources, (source, token) => Task.FromResult(response), TimeSpan.FromHours(1), _logger);
            sync.Changed += (s, e) => changed++;

            await sync.Sync();
            Assert.Equal(1, changed);
            Assert.Equal("Contoso", Assert.Single(sync.GetPublishers()).Name);
            Assert.True(File.Exists(Path.Combine(_root, "security", "package-manager-publishers.json")));
            Assert.Equal("Contoso PM", Assert.Single(sync.GetSources()).SourceName);

            // The same publishers again aren't a change
            await sync.Sync();
            Assert.Equal(1, changed);

            // A failed read (here 403) keeps the last copy, and is logged once
            response = (403, null, "Forbidden");
            await sync.Sync();
            await sync.Sync();
            Assert.Equal(1, changed);
            Assert.Single(sync.GetPublishers());
            Assert.Contains("403 Forbidden", Assert.Single(Warnings));

            // Read from the file by a new Instance that starts offline
            response = (0, null, "Connection refused");
            var restarted = new TrakHoundPackageManagerPublisherSync(_root, () => sources, (source, token) => Task.FromResult(response), TimeSpan.FromHours(1), _logger);
            Assert.Equal("Contoso", Assert.Single(restarted.GetPublishers()).Name);

            // A Source that is no longer a Package Manager stops being trusted straight away, and is removed from the file on the next sync
            sources.Clear();
            Assert.Empty(restarted.GetPublishers());
            await restarted.Sync();
            Assert.Empty(TrakHoundPackageManagerPublishers.Read(_root).Sources);
        }

        [Fact]
        public async Task PublisherSync_SyncedPublisher_IsTrustedByVerification()
        {
            using var signer = TrakHoundPackageSigner.Generate();
            var sync = new TrakHoundPackageManagerPublisherSync(_root, () => new[] { _source }, (source, token) => Task.FromResult((200, Publishers(("Contoso", signer)), (string)null)), TimeSpan.FromHours(1), _logger);
            await sync.Sync();

            var packageSource = Path.Combine(_root, "source");
            Directory.CreateDirectory(Path.Combine(packageSource, "dist"));
            File.WriteAllText(Path.Combine(packageSource, "dist", "module.dll"), "module");
            var packageBytes = new TrakHoundPackageBuilder { Category = "function", Id = "Contoso.Function", Version = "1.0.0" }.Build().Package(packageSource, signer);

            using var manager = new TrakHoundPackageManager(Path.Combine(_root, "packages"));
            manager.VerificationMode = TrakHoundPackageVerificationMode.Require;
            manager.TrustedPublishersProvider = () => TrakHoundTrustedPublishers.Combine(new TrakHoundTrustedPublishers(), sync.GetPublishers());

            var check = manager.CheckPackageFile(packageBytes);
            Assert.Equal(TrakHoundPackageVerificationState.Verified, check.State);
            Assert.Equal("Contoso", check.Publisher);
            Assert.True(Assert.Single(await manager.Install(packageBytes)).Success);
        }


        [Fact]
        public async Task Client_SendsTheInventoryAndReadsThePublishers_WithTheAccessToken()
        {
            using var signer = TrakHoundPackageSigner.Generate();
            var requests = new ConcurrentQueue<(string Path, string Token, string Body)>();

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var app = builder.Build();
            app.MapPost("/feed/inventory", async (HttpContext context) =>
            {
                using var reader = new StreamReader(context.Request.Body);
                var body = await reader.ReadToEndAsync();
                var token = context.Request.Headers["X-TrakHound-Api"].ToString();
                requests.Enqueue((context.Request.Path, token, body));

                if (token != "good") return Results.StatusCode(token == "" ? 401 : 403);

                var count = JsonDocument.Parse(body).RootElement.GetProperty("packages").GetArrayLength();
                return Results.Json(new { received = count });
            });
            app.MapGet("/feed/trusted-publishers", (HttpContext context) =>
            {
                requests.Enqueue((context.Request.Path, context.Request.Headers["X-TrakHound-Api"].ToString(), null));
                return Results.Content(JsonSerializer.Serialize(Publishers(("Contoso", signer))), "application/json");
            });
            await app.StartAsync();

            var baseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First().TrimEnd('/') + "/feed";

            var client = PackagesContext.CreateClient(new RemoteConfiguration { BaseUrl = baseUrl, AccessTokenCode = "good", PackageManager = true });
            var result = await client.ReportInventory(Inventory(TrakHoundPackageInventoryReason.Startup));
            Assert.Equal(200, result.StatusCode);
            Assert.Equal(1, result.Received);

            Assert.True(requests.TryDequeue(out var request));
            Assert.Equal("/feed/inventory", request.Path);
            Assert.Equal("good", request.Token);
            var package = JsonDocument.Parse(request.Body).RootElement.GetProperty("packages")[0];
            Assert.Equal(JsonValueKind.False, package.GetProperty("inUse").ValueKind);
            Assert.Equal("Verified", package.GetProperty("state").GetString());

            var refused = await PackagesContext.CreateClient(new RemoteConfiguration { BaseUrl = baseUrl, AccessTokenCode = "revoked" }).ReportInventory(Inventory("startup"));
            Assert.Equal(403, refused.StatusCode);
            Assert.NotNull(refused.Message);

            var publishers = await client.GetTrustedPublishers();
            Assert.Equal(200, publishers.StatusCode);
            Assert.Equal(signer.KeyId, Assert.Single(publishers.Publishers.Publishers).KeyId);

            await app.StopAsync();
        }
    }
}
