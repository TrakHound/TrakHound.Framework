// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;
using TrakHound.Security;

namespace TrakHound.Instance.Http.Tests
{
    public class ForwardedHeadersTests
    {
        private static readonly IPAddress Proxy = IPAddress.Parse("10.0.0.5");
        private static readonly IPAddress Instance = IPAddress.Parse("10.0.0.10");


        private static async Task<HttpContext> Send(List<string> trustedProxies, IPAddress remoteAddress, string forwardedFor = null, string forwardedProto = null)
        {
            HttpContext seen = null;
            var middleware = new TrakHoundForwardedHeadersMiddleware(context => { seen = context; return Task.CompletedTask; }, () => trustedProxies, NullLoggerFactory.Instance);

            var context = new DefaultHttpContext();
            context.Connection.RemoteIpAddress = remoteAddress;
            context.Connection.LocalIpAddress = Instance;
            context.Request.Scheme = "http";
            if (forwardedFor != null) context.Request.Headers["X-Forwarded-For"] = forwardedFor;
            if (forwardedProto != null) context.Request.Headers["X-Forwarded-Proto"] = forwardedProto;

            await middleware.Invoke(context);
            return seen;
        }


        [Theory]
        [InlineData("127.0.0.1", true)]
        [InlineData(" ::1 ", true)]
        [InlineData("10.0.0.0/8", true)]
        [InlineData("fd00::/8", true)]
        [InlineData("nginx", false)]
        [InlineData("10.0.0.0/40", false)]
        [InlineData("", false)]
        public void Parses_Addresses_And_Networks(string entry, bool valid)
        {
            Assert.Equal(valid, TrakHoundTrustedProxies.TryParse(entry, out _, out _));
        }

        [Fact]
        public void Reports_Invalid_Entries()
        {
            var invalid = TrakHoundTrustedProxies.GetInvalid(new[] { "127.0.0.1", "", "proxy.local", "10.0.0.0/8" });

            Assert.Equal(new[] { "proxy.local" }, invalid);
        }

        [Fact]
        public void No_Options_Without_Valid_Entries()
        {
            Assert.Null(TrakHoundTrustedProxies.CreateOptions(null));
            Assert.Null(TrakHoundTrustedProxies.CreateOptions(new[] { "proxy.local" }));
        }

        [Fact]
        public async Task Uses_The_Client_Address_From_A_Trusted_Proxy()
        {
            var context = await Send(new List<string> { "10.0.0.5" }, Proxy, "203.0.113.7", "https");

            Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
            Assert.Equal("https", context.Request.Scheme);
        }

        [Fact]
        public async Task Ignores_Headers_From_Other_Addresses()
        {
            var client = IPAddress.Parse("203.0.113.7");
            var context = await Send(new List<string> { "10.0.0.5" }, client, "127.0.0.1", "https");

            Assert.Equal(client, context.Connection.RemoteIpAddress);
            Assert.Equal("http", context.Request.Scheme);
        }

        [Fact]
        public async Task Ignores_Headers_Without_Trusted_Proxies()
        {
            var context = await Send(null, IPAddress.Loopback, "203.0.113.7");

            Assert.Equal(IPAddress.Loopback, context.Connection.RemoteIpAddress);
        }

        [Fact]
        public async Task Loopback_Is_Not_Trusted_By_Default()
        {
            // ASP.NET Core trusts loopback unless the Known Proxies are cleared
            var context = await Send(new List<string> { "10.0.0.5" }, IPAddress.Loopback, "203.0.113.7");

            Assert.Equal(IPAddress.Loopback, context.Connection.RemoteIpAddress);
        }

        [Fact]
        public async Task Ignores_An_Address_The_Client_Added()
        {
            // The proxy appends the address it received the Request from to what the client sent
            var context = await Send(new List<string> { "10.0.0.5" }, Proxy, "127.0.0.1, 203.0.113.7");

            Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
        }

        [Fact]
        public async Task Follows_Chained_Trusted_Proxies()
        {
            var context = await Send(new List<string> { "10.0.0.0/8" }, Proxy, "127.0.0.1, 203.0.113.7, 10.0.0.2");

            Assert.Equal(IPAddress.Parse("203.0.113.7"), context.Connection.RemoteIpAddress);
        }

        [Fact]
        public async Task Applies_Saved_Changes()
        {
            HttpContext seen = null;
            var trustedProxies = new List<string>();
            var middleware = new TrakHoundForwardedHeadersMiddleware(context => { seen = context; return Task.CompletedTask; }, () => trustedProxies, NullLoggerFactory.Instance);

            HttpContext CreateContext()
            {
                var context = new DefaultHttpContext();
                context.Connection.RemoteIpAddress = Proxy;
                context.Request.Headers["X-Forwarded-For"] = "203.0.113.7";
                return context;
            }

            await middleware.Invoke(CreateContext());
            Assert.Equal(Proxy, seen.Connection.RemoteIpAddress);

            // Saving the Settings replaces the list
            trustedProxies = new List<string> { "10.0.0.5" };
            await middleware.Invoke(CreateContext());
            Assert.Equal(IPAddress.Parse("203.0.113.7"), seen.Connection.RemoteIpAddress);
        }

        [Fact]
        public async Task Module_Tokens_Are_Not_Local_Through_A_Proxy()
        {
            var direct = await Send(new List<string> { "127.0.0.1" }, IPAddress.Loopback);
            Assert.True(TrakHoundModuleTokenManager.IsLocalRequest(direct));

            var proxied = await Send(new List<string> { "127.0.0.1" }, IPAddress.Loopback, "203.0.113.7");
            Assert.False(TrakHoundModuleTokenManager.IsLocalRequest(proxied));
        }
    }
}
