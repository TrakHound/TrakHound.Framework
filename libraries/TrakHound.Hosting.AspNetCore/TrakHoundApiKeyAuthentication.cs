// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;

namespace TrakHound.Hosting
{
    /// <summary>
    /// Signs in Requests that send one of the API Keys in the host's settings ("TrakHound:ApiKeys"), the same headers the Instance reads:
    /// "X-API-Key: {key}" or "Authorization: Bearer {key}". A Request without a Key isn't signed in (so other schemes can sign it in);
    /// a Request with a Key that doesn't match is refused
    /// </summary>
    public class TrakHoundApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public const string SchemeName = "TrakHoundApiKey";
        public const string ApiKeyHeader = "X-API-Key";

        private readonly IOptionsMonitor<TrakHoundHostOptions> _hostOptions;


        public TrakHoundApiKeyAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder, IOptionsMonitor<TrakHoundHostOptions> hostOptions)
            : base(options, logger, encoder)
        {
            _hostOptions = hostOptions;
        }


        /// <summary>
        /// The SHA-256 hash (hex) of a Key, as set in a Key's Hash
        /// </summary>
        public static string GetHash(string key) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key))).ToLowerInvariant();


        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var value = Request.Headers[ApiKeyHeader].FirstOrDefault();
            if (string.IsNullOrEmpty(value))
            {
                var authorization = Request.Headers.Authorization.FirstOrDefault();
                if (authorization != null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) value = authorization.Substring(7).Trim();
            }
            if (string.IsNullOrEmpty(value)) return Task.FromResult(AuthenticateResult.NoResult());

            var apiKey = Find(value);
            if (apiKey == null)
            {
                // A Bearer Token may be for another scheme (ex. a JWT), so only an X-API-Key that doesn't match is a failure
                if (Request.Headers.ContainsKey(ApiKeyHeader)) return Task.FromResult(AuthenticateResult.Fail("Invalid API Key"));
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var name = !string.IsNullOrEmpty(apiKey.Name) ? apiKey.Name : "api-key";
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, name), new Claim(ClaimTypes.NameIdentifier, name) }, SchemeName);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), SchemeName)));
        }


        private TrakHoundHostApiKey Find(string value)
        {
            var actual = Encoding.ASCII.GetBytes(GetHash(value));

            foreach (var apiKey in _hostOptions.CurrentValue.ApiKeys ?? new List<TrakHoundHostApiKey>())
            {
                var hash = !string.IsNullOrEmpty(apiKey.Hash) ? apiKey.Hash.ToLowerInvariant() : !string.IsNullOrEmpty(apiKey.Key) ? GetHash(apiKey.Key) : null;
                if (hash == null) continue;

                // Compare in constant time so the time taken doesn't reveal how much of a Key matched
                if (CryptographicOperations.FixedTimeEquals(Encoding.ASCII.GetBytes(hash), actual)) return apiKey;
            }

            return null;
        }
    }

    public static class TrakHoundApiKeyAuthenticationExtensions
    {
        /// <summary>
        /// Adds authentication with the API Keys in the host's settings ("TrakHound:ApiKeys") as the default scheme, and authorization.
        /// Protect the Api with RequireAuthorization (ex. app.MapTrakHoundApi(...).Group.RequireAuthorization())
        /// </summary>
        public static AuthenticationBuilder AddTrakHoundApiKeyAuthentication(this IServiceCollection services)
        {
            services.AddAuthorization();
            return services.AddAuthentication(TrakHoundApiKeyAuthenticationHandler.SchemeName)
                .AddScheme<AuthenticationSchemeOptions, TrakHoundApiKeyAuthenticationHandler>(TrakHoundApiKeyAuthenticationHandler.SchemeName, null);
        }
    }
}
