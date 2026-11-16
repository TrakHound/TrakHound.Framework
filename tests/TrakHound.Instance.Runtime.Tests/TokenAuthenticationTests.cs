// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Security;
using TrakHound.Security;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// Credentials sent with a Request are checked by every Token Identity Provider until one accepts them. A Token Identity Provider only
    /// knows its own Token, so it rejects every other one, and that mustn't stop a later Provider from accepting it
    /// </summary>
    public class TokenAuthenticationTests
    {
        private class Request : ITrakHoundIdentityRequest
        {
            private readonly Dictionary<string, string> _headers = new Dictionary<string, string>();

            public string Method => "GET";
            public string Path => "/test";
            public string ClientAddress => "127.0.0.1";

            public Request(string token = null)
            {
                if (token != null) _headers["token"] = token;
            }

            public string GetHeader(string name) => _headers.GetValueOrDefault(name);
            public string GetQueryParameter(string name) => null;
            public Task<string> GetFormValue(string name) => Task.FromResult<string>(null);
        }

        private static ITrakHoundIdentityProvider TokenProvider(string id, string token, string roles)
        {
            var configuration = new TrakHoundIdentityProviderConfiguration { Id = id };
            configuration.SetParameter("token", token);
            configuration.SetParameter("userId", id);
            configuration.SetParameter("roles", roles);
            return new TrakHound.Token.Identity.Module(configuration, null, null, null);
        }

        private static readonly ITrakHoundIdentityProvider[] _providers =
        {
            TokenProvider("machines", "token-a", "operators"),
            TokenProvider("dashboards", "token-b", "viewers")
        };

        private static Task<TrakHoundIdentityResult> Authenticate(string token)
        {
            return TrakHoundSecurityManager.Authenticate(_providers, new TrakHoundIdentityContext(new Request(token), null, "http://localhost", null));
        }


        [Fact]
        public async Task FirstProvidersToken_SignsIn()
        {
            var result = await Authenticate("token-a");

            Assert.True(result.IsSuccess);
            Assert.Equal("machines", result.Identity.ProviderId);
            Assert.Equal(new[] { "operators" }, result.Identity.Roles);
        }

        [Fact]
        public async Task SecondProvidersToken_SignsIn()
        {
            var result = await Authenticate("token-b");

            Assert.True(result.IsSuccess);
            Assert.Equal("dashboards", result.Identity.ProviderId);
            Assert.Equal(new[] { "viewers" }, result.Identity.Roles);
        }

        [Fact]
        public async Task UnknownToken_IsRejected()
        {
            // Still a Fail (not NoResult), so the Instance answers 401 invalid_token instead of sending the client to sign in
            var result = await Authenticate("token-c");

            Assert.Equal(TrakHoundIdentityResultStatus.Fail, result.Status);
        }

        [Fact]
        public async Task NoToken_IsNoResult()
        {
            var result = await Authenticate(null);

            Assert.Equal(TrakHoundIdentityResultStatus.NoResult, result.Status);
        }
    }
}
