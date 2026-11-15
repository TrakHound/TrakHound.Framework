// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http;
using System.Net;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Security;

namespace TrakHound.Instance.Http.Tests
{
    /// <summary>
    /// System Resources and Service operations that no Profile covers can only be used from the same machine, unless the Instance allows
    /// unprotected remote access
    /// </summary>
    public class SystemApiProtectionTests
    {
        private static readonly IPAddress Instance = IPAddress.Parse("10.0.0.10");
        private static readonly IPAddress Remote = IPAddress.Parse("10.0.0.20");


        private static HttpContext CreateContext(string path, IPAddress remoteAddress)
        {
            var context = new DefaultHttpContext();
            context.Request.Path = path;
            context.Connection.RemoteIpAddress = remoteAddress;
            context.Connection.LocalIpAddress = Instance;
            return context;
        }

        private class TestAuthorizer : ITrakHoundAuthorizer
        {
            private readonly TrakHoundAuthorizationResult _result;

            public TestAuthorizer(TrakHoundAuthorizationResult result) => _result = result;

            public TrakHoundAuthorizationResult Authorize(TrakHoundIdentity identity, string resourceId, string permission = null) => _result;
        }


        [Theory]
        [InlineData("/_logs/loggers")]
        [InlineData("/_LOGS/loggers")]
        [InlineData("/_services/keyvalues/default")]
        [InlineData("/_mcp")]
        public void RemoteSystemApiRequest_RequiresProtection(string path)
        {
            Assert.True(TrakHoundSystemApiProtection.IsRequired(CreateContext(path, Remote), new TrakHoundInstanceConfiguration()));
        }

        [Fact]
        public void LoopbackRequest_DoesNotRequireProtection()
        {
            Assert.False(TrakHoundSystemApiProtection.IsRequired(CreateContext("/_logs/loggers", IPAddress.Loopback), new TrakHoundInstanceConfiguration()));
            Assert.False(TrakHoundSystemApiProtection.IsRequired(CreateContext("/_logs/loggers", IPAddress.IPv6Loopback), new TrakHoundInstanceConfiguration()));
        }

        [Fact]
        public void RequestFromInstanceAddress_DoesNotRequireProtection()
        {
            // A local process connecting to the address the Instance listens on
            Assert.False(TrakHoundSystemApiProtection.IsRequired(CreateContext("/_logs/loggers", Instance), new TrakHoundInstanceConfiguration()));
        }

        [Fact]
        public void UnprotectedRemoteAccessAllowed_DoesNotRequireProtection()
        {
            var configuration = new TrakHoundInstanceConfiguration { SystemApiUnprotectedRemoteAccess = true };

            Assert.False(TrakHoundSystemApiProtection.IsRequired(CreateContext("/_logs/loggers", Remote), configuration));
        }

        [Theory]
        [InlineData("/orders")]
        [InlineData("/_developer/catalog")]
        [InlineData("/llms.txt")]
        public void RemoteRequestOutsideSystemApi_DoesNotRequireProtection(string path)
        {
            // Apis and the Api Catalog are public until protected (they are what the Instance serves)
            Assert.False(TrakHoundSystemApiProtection.IsRequired(CreateContext(path, Remote), new TrakHoundInstanceConfiguration()));
        }

        [Fact]
        public void DefaultConfiguration_DeniesUnprotectedRemoteAccess()
        {
            Assert.False(new TrakHoundInstanceConfiguration().SystemApiUnprotectedRemoteAccess);
        }


        [Fact]
        public void ProtectedResourceAuthorizer_DeniesUncoveredResource()
        {
            var authorizer = new TrakHoundProtectedResourceAuthorizer(new TestAuthorizer(TrakHoundAuthorizationResult.Uncovered()));

            var result = authorizer.Authorize(null, "system:logs", "read");

            Assert.Equal(TrakHoundAuthorizationStatus.Forbidden, result.Status);
        }

        [Fact]
        public void ProtectedResourceAuthorizer_AllowsProfileWithoutProvider()
        {
            // A Profile without an Identity Provider deliberately makes the Resource public
            var authorizer = new TrakHoundProtectedResourceAuthorizer(new TestAuthorizer(TrakHoundAuthorizationResult.Unrestricted()));

            Assert.True(authorizer.Authorize(null, "system:logs", "read").IsAllowed);
        }

        [Fact]
        public void ProtectedResourceAuthorizer_AllowsAuthorizedIdentity()
        {
            var authorizer = new TrakHoundProtectedResourceAuthorizer(new TestAuthorizer(TrakHoundAuthorizationResult.Allowed(new[] { "read" })));

            Assert.True(authorizer.Authorize(null, "system:logs", "read").IsAllowed);
        }

        [Fact]
        public void ProtectedResourceAuthorizer_KeepsAuthenticationRequired()
        {
            var authorizer = new TrakHoundProtectedResourceAuthorizer(new TestAuthorizer(TrakHoundAuthorizationResult.AuthenticationRequired(new[] { "api-keys" })));

            var result = authorizer.Authorize(null, "system:logs", "read");

            Assert.Equal(TrakHoundAuthorizationStatus.AuthenticationRequired, result.Status);
            Assert.Equal(new[] { "api-keys" }, result.ProviderIds);
        }

        [Fact]
        public void UncoveredResult_IsAllowed()
        {
            // On its own (ex. from the same machine), a Resource that no Profile covers is still allowed
            var result = TrakHoundAuthorizationResult.Uncovered();

            Assert.True(result.IsAllowed);
            Assert.True(result.IsUncovered);
            Assert.False(TrakHoundAuthorizationResult.Unrestricted().IsUncovered);
        }
    }
}
