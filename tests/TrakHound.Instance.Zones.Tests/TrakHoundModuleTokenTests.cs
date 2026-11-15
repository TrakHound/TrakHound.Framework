// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Net;
using Microsoft.AspNetCore.Http;
using TrakHound.Api;
using TrakHound.Security;

namespace TrakHound.Instance.Zones.Tests
{
    public class TrakHoundModuleTokenTests
    {
        private static readonly TrakHoundApiRequestSource _api = new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Api, "python-api");
        private static readonly TrakHoundApiRequestSource _otherApi = new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Api, "other-api");


        [Fact]
        public void Tokens_Make_Requests_As_The_Module()
        {
            var manager = new TrakHoundModuleTokenManager(() => "http://127.0.0.1:8472/");

            var token = manager.Issue(_api, "test-router");

            Assert.StartsWith("thm_", token.Token);
            Assert.Equal("http://127.0.0.1:8472/", token.InstanceUrl);
            Assert.Equal("test-router", token.RouterId);
            Assert.Null(token.Expires);

            var entry = manager.Validate(token.Token);
            Assert.NotNull(entry);
            Assert.Same(_api, entry.Source);
            Assert.Equal("api:python-api", entry.MemberId);
            Assert.Equal("test-router", entry.RouterId);
        }

        [Theory]
        [InlineData(TrakHoundApiRequestSourceType.Instance, null)]
        [InlineData(TrakHoundApiRequestSourceType.Http, "127.0.0.1")]
        public void Tokens_Are_Only_Issued_To_Modules(TrakHoundApiRequestSourceType type, string id)
        {
            var manager = new TrakHoundModuleTokenManager();
            Assert.Null(manager.Issue(new TrakHoundApiRequestSource(type, id)));
            Assert.Null(manager.Issue(null));
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("thm_")]
        [InlineData("thk_abc_def")]
        [InlineData("thm_not-issued")]
        public void Unknown_Tokens_Are_Rejected(string value)
        {
            var manager = new TrakHoundModuleTokenManager();
            manager.Issue(_api);

            Assert.Null(manager.Validate(value));
        }

        [Fact]
        public void Expired_Tokens_Are_Rejected()
        {
            var manager = new TrakHoundModuleTokenManager();
            var token = manager.Issue(_api, lifetime: TimeSpan.FromMilliseconds(1));

            Assert.NotNull(token.Expires);
            Thread.Sleep(50);

            Assert.Null(manager.Validate(token.Token));
            Assert.Empty(manager.GetEntries());
        }

        [Fact]
        public void A_Module_Can_Only_Revoke_Its_Own_Tokens()
        {
            var manager = new TrakHoundModuleTokenManager();
            var token = manager.Issue(_api);

            Assert.False(manager.Revoke(token.Token, _otherApi));
            Assert.NotNull(manager.Validate(token.Token));

            Assert.True(manager.Revoke(token.Token, _api));
            Assert.Null(manager.Validate(token.Token));
        }

        [Fact]
        public void Unloading_A_Module_Revokes_Its_Tokens()
        {
            var manager = new TrakHoundModuleTokenManager();
            var first = manager.Issue(_api);
            var second = manager.Issue(_api);
            var other = manager.Issue(_otherApi);

            Assert.Equal(2, manager.RevokeAll("api:python-api"));

            Assert.Null(manager.Validate(first.Token));
            Assert.Null(manager.Validate(second.Token));
            Assert.NotNull(manager.Validate(other.Token));
        }

        [Fact]
        public void Tokens_Are_Read_From_The_Header_Or_A_Bearer_Token()
        {
            var header = new DefaultHttpContext();
            header.Request.Headers["X-TrakHound-Module-Token"] = "thm_abc";
            Assert.Equal("thm_abc", TrakHoundModuleTokenManager.GetRequestToken(header.Request));

            var bearer = new DefaultHttpContext();
            bearer.Request.Headers.Authorization = "Bearer thm_abc";
            Assert.Equal("thm_abc", TrakHoundModuleTokenManager.GetRequestToken(bearer.Request));

            // Other Bearer Tokens (ex. API Keys) are for the Identity Providers
            var apiKey = new DefaultHttpContext();
            apiKey.Request.Headers.Authorization = "Bearer thk_abc_def";
            Assert.Null(TrakHoundModuleTokenManager.GetRequestToken(apiKey.Request));

            Assert.Null(TrakHoundModuleTokenManager.GetRequestToken(new DefaultHttpContext().Request));
        }

        [Theory]
        [InlineData("127.0.0.1", "127.0.0.1", true)]
        [InlineData("::1", "::1", true)]
        [InlineData("192.168.1.10", "192.168.1.10", true)]
        [InlineData("::ffff:192.168.1.10", "192.168.1.10", true)]
        [InlineData("192.168.1.20", "192.168.1.10", false)]
        public void Tokens_Are_Only_Accepted_From_The_Same_Machine(string remoteAddress, string localAddress, bool expected)
        {
            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = IPAddress.Parse(remoteAddress);
            context.Connection.LocalIpAddress = IPAddress.Parse(localAddress);

            Assert.Equal(expected, TrakHoundModuleTokenManager.IsLocalRequest(context));
        }

        [Theory]
        [InlineData(null, 8472, "http://127.0.0.1:8472/")]
        [InlineData("0.0.0.0", 8472, "http://127.0.0.1:8472/")]
        [InlineData("192.168.1.10", 9000, "http://192.168.1.10:9000/")]
        [InlineData("::1", 8472, "http://[::1]:8472/")]
        public void The_Instance_Url_Is_Reachable_From_The_Same_Machine(string httpAddress, int httpPort, string expected)
        {
            var configuration = new TrakHound.Instances.TrakHoundInstanceConfiguration { HttpAddress = httpAddress, HttpPort = httpPort };
            Assert.Equal(expected, TrakHoundModuleTokenManager.GetInstanceUrl(configuration));
        }
    }
}
