// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http;
using System.Net;
using TrakHound.Instances;

namespace TrakHound.Instance.Http.Tests
{
    /// <summary>
    /// The Admin Interface is only served to its Allowed Addresses (and always to loopback)
    /// </summary>
    public class AdminAddressesTests
    {
        private static async Task<(bool Passed, int StatusCode)> Send(List<string> allowed, string path, IPAddress remoteAddress)
        {
            var passed = false;
            var configuration = new TrakHoundInstanceConfiguration { AdminAllowedAddresses = allowed };
            var middleware = new TrakHoundAdminAddressMiddleware(context => { passed = true; return Task.CompletedTask; }, () => configuration);

            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Connection.RemoteIpAddress = remoteAddress;

            await middleware.Invoke(context);
            return (passed, context.Response.StatusCode);
        }


        [Theory]
        [InlineData("10.0.0.25", true)]
        [InlineData("10.0.0.26", false)]
        [InlineData("192.168.1.40", true)]
        [InlineData("192.168.2.40", false)]
        [InlineData("::ffff:10.0.0.25", true)] // An IPv4 client on a dual-mode socket
        [InlineData("fd00::5", true)]
        [InlineData("fe80::5", false)]
        public void Address_MatchesEntries(string address, bool expected)
        {
            var entries = new List<string> { "10.0.0.25", " 192.168.1.0/24 ", "fd00::/8" };
            Assert.Equal(expected, TrakHoundAdminAddresses.IsAllowed(IPAddress.Parse(address), entries));
        }

        [Theory]
        [InlineData("127.0.0.1")]
        [InlineData("::1")]
        [InlineData("::ffff:127.0.0.1")]
        public void Loopback_IsAlwaysAllowed(string address)
        {
            Assert.True(TrakHoundAdminAddresses.IsAllowed(IPAddress.Parse(address), new List<string> { "10.0.0.25" }));
        }

        [Fact]
        public void EmptyList_AllowsEveryAddress()
        {
            Assert.True(TrakHoundAdminAddresses.IsAllowed(IPAddress.Parse("203.0.113.7"), null));
            Assert.True(TrakHoundAdminAddresses.IsAllowed(IPAddress.Parse("203.0.113.7"), new List<string>()));
            Assert.True(TrakHoundAdminAddresses.IsAllowed(IPAddress.Parse("203.0.113.7"), new List<string> { " " }));
        }

        [Fact]
        public void OnlyInvalidEntries_StillRestrictToLoopback()
        {
            var entries = new List<string> { "admin-pc" };
            Assert.False(TrakHoundAdminAddresses.IsAllowed(IPAddress.Parse("10.0.0.25"), entries));
            Assert.True(TrakHoundAdminAddresses.IsAllowed(IPAddress.Loopback, entries));
        }

        [Fact]
        public void UnknownAddress_IsDeniedWhenRestricted()
        {
            Assert.False(TrakHoundAdminAddresses.IsAllowed(null, new List<string> { "10.0.0.25" }));
        }

        [Fact]
        public void SavedListChange_IsApplied()
        {
            var address = IPAddress.Parse("10.0.0.30");
            Assert.False(TrakHoundAdminAddresses.IsAllowed(address, new List<string> { "10.0.0.25" }));
            Assert.True(TrakHoundAdminAddresses.IsAllowed(address, new List<string> { "10.0.0.30" }));
        }

        [Fact]
        public void WouldAllow_ChecksUnsavedEntries()
        {
            var address = IPAddress.Parse("10.0.0.25");
            Assert.True(TrakHoundAdminAddresses.WouldAllow(address, new[] { "10.0.0.0/24" }));
            Assert.False(TrakHoundAdminAddresses.WouldAllow(address, new[] { "10.0.1.0/24" }));
        }


        [Theory]
        [InlineData("/_admin")]
        [InlineData("/_admin/explorer")]
        [InlineData("/_ADMIN/settings")]
        [InlineData("/_admin/session/signin")]
        [InlineData("/_download/abc123")]
        public async Task AdminRequest_FromOtherAddress_Gets404(string path)
        {
            var (passed, statusCode) = await Send(new List<string> { "10.0.0.25" }, path, IPAddress.Parse("10.0.0.99"));
            Assert.False(passed);
            Assert.Equal(StatusCodes.Status404NotFound, statusCode);
        }

        [Theory]
        [InlineData("10.0.0.25")]
        [InlineData("127.0.0.1")]
        public async Task AdminRequest_FromAllowedAddress_IsPassedOn(string address)
        {
            var (passed, _) = await Send(new List<string> { "10.0.0.25" }, "/_admin/explorer", IPAddress.Parse(address));
            Assert.True(passed);
        }

        [Theory]
        [InlineData("/")]
        [InlineData("/my-app")]
        [InlineData("/_api/my-api")]
        [InlineData("/_logs/loggers")]
        [InlineData("/_mcp")]
        [InlineData("/_blazor")]
        [InlineData("/_error")]
        [InlineData("/_administration")] // Matched by whole path segment
        public async Task OtherRequest_FromOtherAddress_IsPassedOn(string path)
        {
            var (passed, _) = await Send(new List<string> { "10.0.0.25" }, path, IPAddress.Parse("10.0.0.99"));
            Assert.True(passed);
        }
    }
}
