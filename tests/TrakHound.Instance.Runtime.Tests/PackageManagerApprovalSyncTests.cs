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
using System.Security.Cryptography;
using System.Text.Json;
using TrakHound.Instance.Configurations;
using TrakHound.Instance.Pages.Packages;
using TrakHound.Instance.PackageSources;
using TrakHound.Packages;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// Pinning the server key of a TrakHound Package Manager Source, and reading and keeping its attestation lists (RequireApproval)
    /// </summary>
    public class PackageManagerApprovalSyncTests : IDisposable
    {
        private const string PackageId = "Contoso.Function";

        private readonly string _root = Path.Combine(Path.GetTempPath(), $"trakhound-approval-sync-tests-{Guid.NewGuid()}");
        private readonly LogFactory _logFactory;
        private readonly MemoryTarget _log = new MemoryTarget("memory") { Layout = "${level}|${message}" };
        private readonly Logger _logger;
        private readonly ECDsa _serverKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly ECDsa _otherServerKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        private readonly List<RemoteConfiguration> _sources = new List<RemoteConfiguration> { new RemoteConfiguration { Id = "pm", Name = "Contoso PM", BaseUrl = "https://pm.contoso.com/feed", PackageManager = true } };

        private (int, TrakHoundPackageManagerServerKey, string) _keyResponse;
        private (int, TrakHoundPackageAttestationEnvelope, string) _listResponse;
        private DateTime _now = DateTime.UtcNow;


        public PackageManagerApprovalSyncTests()
        {
            _logFactory = new LogFactory();
            var configuration = new LoggingConfiguration(_logFactory);
            configuration.AddRule(NLog.LogLevel.Trace, NLog.LogLevel.Fatal, _log);
            _logFactory.Configuration = configuration;
            _logger = _logFactory.GetLogger("test");

            _keyResponse = (200, ServerKey(_serverKey), null);
            _listResponse = (200, Sign(_serverKey, Approved("abc")), null);
        }

        public void Dispose()
        {
            _logFactory.Dispose();
            _serverKey.Dispose();
            _otherServerKey.Dispose();
            try { Directory.Delete(_root, true); } catch { }
        }


        private List<string> Logs(string level) => _log.Logs.Where(o => o.StartsWith(level + "|")).ToList();

        private static TrakHoundPackageManagerServerKey ServerKey(ECDsa key)
        {
            var pem = key.ExportSubjectPublicKeyInfoPem();
            return new TrakHoundPackageManagerServerKey { ServerId = "server-1", KeyId = TrakHoundPackageSigner.GetKeyId(TrakHoundPackageSigner.ReadPublicKeyPem(pem)), PublicKeyPem = pem, Created = DateTime.UtcNow };
        }

        private static TrakHoundPackageAttestation[] Approved(params string[] contentHashes)
        {
            return contentHashes.Select(o => new TrakHoundPackageAttestation { Id = PackageId, Version = "1.0.0", ContentHash = o, State = TrakHoundPackageAttestation.Approved }).ToArray();
        }

        private TrakHoundPackageAttestationEnvelope Sign(ECDsa key, TrakHoundPackageAttestation[] packages, TimeSpan? lifetime = null, DateTime? issued = null)
        {
            var list = new TrakHoundPackageAttestationList
            {
                ServerId = "server-1",
                KeyId = ServerKey(key).KeyId,
                Channel = "Prod",
                InstanceId = "i-1",
                Issued = issued ?? _now.AddMinutes(-1),
                Expires = (issued ?? _now) + (lifetime ?? TimeSpan.FromDays(7)),
                Packages = packages.ToList()
            };

            var payload = JsonSerializer.SerializeToUtf8Bytes(list);
            return new TrakHoundPackageAttestationEnvelope
            {
                Format = TrakHoundPackageAttestationEnvelope.FormatName,
                FormatVersion = 1,
                Payload = Convert.ToBase64String(payload),
                KeyId = list.KeyId,
                Signature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256))
            };
        }

        private TrakHoundPackageManagerApprovalSync CreateSync()
        {
            return new TrakHoundPackageManagerApprovalSync(
                _root,
                () => _sources,
                (source, token) => Task.FromResult(_keyResponse),
                (source, token) => Task.FromResult(_listResponse),
                TimeSpan.FromHours(1),
                _logger,
                () => _now);
        }

        private bool IsApproved(TrakHoundPackageManagerApprovalSync sync, string contentHash, bool install = false) => sync.GetApprovals().Check(PackageId, "1.0.0", contentHash, install, _now).Approved;


        [Fact]
        public async Task Sync_PinsTheFirstKey_AndKeepsTheList()
        {
            var sync = CreateSync();
            var changed = 0;
            sync.Changed += (s, e) => changed++;

            // Nothing is approved before a list is read
            Assert.False(IsApproved(sync, "abc"));

            await sync.Sync();

            Assert.Equal(1, changed);
            Assert.True(IsApproved(sync, "abc"));
            Assert.False(IsApproved(sync, "other"));

            var status = Assert.Single(sync.GetStatus());
            Assert.Equal(ServerKey(_serverKey).KeyId, status.PinnedKey.KeyId);
            Assert.Equal("Prod", status.List.Channel);
            Assert.Null(status.Error);
            Assert.True(File.Exists(TrakHoundPackageManagerKeys.GetPath(_root)));
            Assert.True(File.Exists(TrakHoundPackageManagerAttestations.GetPath(_root)));

            // The same list again isn't a change
            await sync.Sync();
            Assert.Equal(1, changed);
        }

        [Fact]
        public async Task Sync_ADifferentKey_IsRefused_UntilThePinnedKeyIsCleared()
        {
            var sync = CreateSync();
            var problems = new List<(string Kind, string Title)>();
            sync.Problem += (kind, key, title, message) => problems.Add((kind, title));
            await sync.Sync();

            // The Package Manager now offers another key, and a list signed with it
            _keyResponse = (200, ServerKey(_otherServerKey), null);
            _listResponse = (200, Sign(_otherServerKey, Approved("new")), null);
            await sync.Sync();
            await sync.Sync();

            var status = Assert.Single(sync.GetStatus());
            Assert.Equal(ServerKey(_serverKey).KeyId, status.PinnedKey.KeyId);
            Assert.Equal(ServerKey(_otherServerKey).KeyId, status.RefusedKeyId);
            Assert.Contains("different server key", status.Error);
            Assert.Single(Logs("Error"), o => o.Contains("different server key"));
            Assert.Equal("Package Manager Server Key Changed", Assert.Single(problems, o => o.Kind.Contains(":key:") && o.Title != null).Title);

            // The last list (signed by the pinned key) is still used, and the new one isn't
            Assert.True(IsApproved(sync, "abc"));
            Assert.False(IsApproved(sync, "new"));

            // Cleared by an administrator : the next key is pinned
            Assert.True(sync.ClearPinnedKey("pm"));
            Assert.False(IsApproved(sync, "abc"));
            await sync.Sync();

            status = Assert.Single(sync.GetStatus());
            Assert.Equal(ServerKey(_otherServerKey).KeyId, status.PinnedKey.KeyId);
            Assert.Null(status.RefusedKeyId);
            Assert.True(IsApproved(sync, "new"));
            Assert.Null(problems.Last(o => o.Kind.Contains(":key:")).Title);
        }

        [Fact]
        public async Task Sync_BadList_IsRefused_AndTheLastValidListIsKept()
        {
            var sync = CreateSync();
            await sync.Sync();

            // Signed by another key than the pinned one
            _listResponse = (200, Sign(_otherServerKey, Approved("other")), null);
            await sync.Sync();
            Assert.True(IsApproved(sync, "abc"));
            Assert.False(IsApproved(sync, "other"));
            Assert.Contains("signature", Assert.Single(sync.GetStatus()).Error);

            // Already expired
            _listResponse = (200, Sign(_serverKey, Approved("other"), TimeSpan.FromDays(7), _now.AddDays(-8)), null);
            await sync.Sync();
            Assert.False(IsApproved(sync, "other"));
            Assert.Contains("expired", Assert.Single(sync.GetStatus()).Error);

            // An older list than the one kept (ex. replayed), which would bring back a revoked build
            _listResponse = (200, Sign(_serverKey, Approved("revoked"), issued: _now.AddHours(-1)), null);
            await sync.Sync();
            Assert.False(IsApproved(sync, "revoked"));

            // Unreachable
            _keyResponse = (0, null, "Connection refused");
            _listResponse = (0, null, "Connection refused");
            await sync.Sync();
            Assert.True(IsApproved(sync, "abc"));
        }

        [Fact]
        public async Task Restart_WithoutThePackageManager_UsesTheKeptList()
        {
            await CreateSync().Sync();

            _keyResponse = (0, null, "Connection refused");
            _listResponse = (0, null, "Connection refused");
            var restarted = CreateSync();

            Assert.True(IsApproved(restarted, "abc"));
            await restarted.Sync();
            Assert.True(IsApproved(restarted, "abc"));
        }

        [Fact]
        public async Task ExpiredList_KeepsItsPackages_InstallsNothing_AndIsReportedUntilANewListArrives()
        {
            var sync = CreateSync();
            var problems = new List<(string Kind, string Key, string Title)>();
            sync.Problem += (kind, key, title, message) => problems.Add((kind, key, title));
            await sync.Sync();

            // Cut off from the Package Manager for longer than the list's lifetime
            _now = _now.AddDays(8);
            _keyResponse = (0, null, "Connection refused");
            _listResponse = (0, null, "Connection refused");
            await sync.Sync();

            Assert.True(IsApproved(sync, "abc"));
            Assert.False(IsApproved(sync, "abc", true));
            Assert.True(Assert.Single(sync.GetStatus()).ListExpired);
            Assert.Contains(Logs("Warn"), o => o.Contains("expired"));
            Assert.Equal("Package Manager Approvals Expired", problems.Last(o => o.Kind.Contains(":expired:")).Title);

            // Reported again on the next read, until a new list arrives
            await sync.Sync();
            Assert.Equal(2, Logs("Warn").Count(o => o.Contains("expired")));

            _keyResponse = (200, ServerKey(_serverKey), null);
            _listResponse = (200, Sign(_serverKey, Approved("abc")), null);
            await sync.Sync();

            Assert.True(IsApproved(sync, "abc", true));
            Assert.Null(problems.Last(o => o.Kind.Contains(":expired:")).Title);
        }

        [Fact]
        public async Task SourceNoLongerAPackageManager_StopsApproving()
        {
            var sync = CreateSync();
            await sync.Sync();

            _sources.Clear();
            Assert.False(IsApproved(sync, "abc"));
            Assert.Contains("no Package Source", sync.GetApprovals().NoListReason);

            await sync.Sync();
            Assert.Empty(TrakHoundPackageManagerAttestations.Read(_root).Sources);

            // The pinned key is kept, so adding the Source back doesn't trust another key
            Assert.NotNull(TrakHoundPackageManagerKeys.Read(_root).Get("pm"));
        }

        [Fact]
        public async Task RefreshBeforeInstall_ReadsTheListsAtMostOnceAMinute()
        {
            var reads = 0;
            var sync = new TrakHoundPackageManagerApprovalSync(_root, () => _sources, (source, token) => Task.FromResult(_keyResponse), (source, token) => { reads++; return Task.FromResult(_listResponse); }, TimeSpan.FromHours(1), _logger, () => _now);

            await sync.RefreshBeforeInstall();
            await sync.RefreshBeforeInstall();
            Assert.Equal(1, reads);

            _now = _now.AddMinutes(2);
            await sync.RefreshBeforeInstall();
            Assert.Equal(2, reads);
        }

        [Fact]
        public async Task DamagedKeyFile_ApprovesNothing_AndIsNotReplaced()
        {
            Directory.CreateDirectory(Path.GetDirectoryName(TrakHoundPackageManagerKeys.GetPath(_root)));
            File.WriteAllText(TrakHoundPackageManagerKeys.GetPath(_root), "not json");

            var sync = CreateSync();
            await sync.Sync();

            Assert.False(IsApproved(sync, "abc"));
            Assert.Equal("not json", File.ReadAllText(TrakHoundPackageManagerKeys.GetPath(_root)));
            Assert.Contains("Key file", sync.GetApprovals().NoListReason);
        }

        [Fact]
        public async Task Client_ReadsTheKeyAndTheList_WithTheAccessToken()
        {
            var tokens = new ConcurrentQueue<string>();
            var envelope = Sign(_serverKey, Approved("abc"));

            var builder = WebApplication.CreateBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            await using var app = builder.Build();
            app.MapGet("/feed/server", (HttpContext context) =>
            {
                tokens.Enqueue(context.Request.Headers["X-TrakHound-Api"].ToString());
                return Results.Content(JsonSerializer.Serialize(ServerKey(_serverKey)), "application/json");
            });
            app.MapGet("/feed/attestations", (HttpContext context) =>
            {
                tokens.Enqueue(context.Request.Headers["X-TrakHound-Api"].ToString());
                return Results.Content(JsonSerializer.Serialize(envelope), "application/json");
            });
            await app.StartAsync();

            var baseUrl = app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>().Addresses.First().TrimEnd('/') + "/feed";
            var client = PackagesContext.CreateClient(new RemoteConfiguration { BaseUrl = baseUrl, AccessTokenCode = "good", PackageManager = true });

            var key = await client.GetServerKey();
            Assert.Equal(200, key.StatusCode);
            Assert.Null(TrakHoundPackageAttestationVerifier.CheckServerKey(key.Key));

            var list = await client.GetAttestations();
            Assert.Equal(200, list.StatusCode);
            var (read, error) = TrakHoundPackageAttestationVerifier.Read(list.Envelope, key.Key, DateTime.UtcNow);
            Assert.Null(error);
            Assert.Equal("abc", Assert.Single(read.Packages).ContentHash);

            Assert.Equal(new[] { "good", "good" }, tokens);

            await app.StopAsync();
        }
    }
}
