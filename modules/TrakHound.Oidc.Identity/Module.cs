// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using TrakHound.Clients;
using TrakHound.Security;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Oidc.Identity
{
    /// <summary>
    /// Signs in with an OpenID Connect server using the Authorization Code flow (with PKCE), and accepts the server's
    /// JWT Access Tokens sent as "Authorization: Bearer {token}" (for API clients)
    /// </summary>
    /// <remarks>
    /// Parameters:
    /// authority (required) : The Issuer URL (ex. https://login.microsoftonline.com/{tenant}/v2.0 or https://keycloak/realms/{realm})
    /// clientId (required) : The Client ID registered with the server. The Redirect URI to register is {instance}/_identity/callback/{providerId}
    /// clientSecret : The Client Secret (omit for public clients, which use PKCE only)
    /// scopes : Space or comma separated Scopes (default "openid profile email")
    /// rolesClaim : Comma separated Claims to read Roles from. Nested values use dots (ex. "realm_access.roles"). Default "roles,groups"
    /// userIdClaim : The Claim for the User ID (default "sub")
    /// nameClaim : The Claim for the display name (default "name", falls back to "preferred_username" and "email")
    /// audience : Comma separated Audiences accepted for Bearer Tokens (default the clientId)
    /// requireHttpsMetadata : Require HTTPS for the server's metadata (default true)
    /// </remarks>
    public class Module : TrakHoundIdentityProviderBase
    {
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // How long a sign in can take (between the Challenge and the Callback)
        private static readonly TimeSpan _signInLifetime = TimeSpan.FromMinutes(15);

        private static readonly TimeSpan _clockSkew = TimeSpan.FromMinutes(2);

        // How long a failed first metadata fetch is remembered (so an unreachable server doesn't make every Request wait on it)
        private static readonly TimeSpan _metadataRetryDelay = TimeSpan.FromSeconds(30);

        // How long a validated Bearer Token's Identity is reused (never past the Token's expiration)
        private static readonly TimeSpan _tokenCacheLifetime = TimeSpan.FromMinutes(5);
        private const int _tokenCacheLimit = 10000;

        private readonly string _authority;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private readonly string _scopes;
        private readonly string[] _rolesClaims;
        private readonly string _userIdClaim;
        private readonly string _nameClaim;
        private readonly string[] _audiences;
        private readonly ConfigurationManager<OpenIdConnectConfiguration> _configurationManager;
        private readonly JsonWebTokenHandler _tokenHandler = new JsonWebTokenHandler();

        // Bearer Tokens that validated successfully, keyed by the Token's SHA-256 hash. A changed configuration creates a new Module (and cache)
        private readonly ConcurrentDictionary<string, CachedIdentity> _tokenCache = new ConcurrentDictionary<string, CachedIdentity>();

        private volatile OpenIdConnectConfiguration _metadata; // The last metadata retrieved (null until the first successful fetch)
        private long _metadataRetryAfter; // UTC Ticks before which a failed first fetch is not tried again
        private Task<OpenIdConnectConfiguration> _metadataFetch; // The first fetch in progress (shared by concurrent callers)
        private readonly object _metadataLock = new object();


        public override TrakHoundIdentityProviderFeatures Features => TrakHoundIdentityProviderFeatures.Redirect | TrakHoundIdentityProviderFeatures.Token;


        public Module(ITrakHoundIdentityProviderConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume, ITrakHoundVariableCollection variables) : base(configuration, client, volume, variables)
        {
            _authority = configuration.GetParameter("authority")?.TrimEnd('/');
            _clientId = configuration.GetParameter("clientId");
            _clientSecret = configuration.GetParameter("clientSecret");

            var scopes = configuration.GetParameter("scopes");
            _scopes = !string.IsNullOrWhiteSpace(scopes) ? string.Join(' ', scopes.Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries)) : "openid profile email";
            if (!_scopes.Split(' ').Contains("openid")) _scopes = "openid " + _scopes;

            _rolesClaims = GetListParameter("rolesClaim");
            if (_rolesClaims.Length < 1) _rolesClaims = new[] { "roles", "groups" };

            _userIdClaim = configuration.GetParameter("userIdClaim") ?? "sub";
            _nameClaim = configuration.GetParameter("nameClaim") ?? "name";

            _audiences = GetListParameter("audience");
            if (_audiences.Length < 1 && _clientId != null) _audiences = new[] { _clientId };

            if (!string.IsNullOrEmpty(_authority))
            {
                var requireHttps = !string.Equals(configuration.GetParameter("requireHttpsMetadata"), "false", StringComparison.OrdinalIgnoreCase);
                var retriever = new HttpDocumentRetriever(_httpClient) { RequireHttps = requireHttps };
                _configurationManager = new ConfigurationManager<OpenIdConnectConfiguration>($"{_authority}/.well-known/openid-configuration", new OpenIdConnectConfigurationRetriever(), retriever);
            }
        }


        protected override async Task<TrakHoundIdentityRedirect> OnChallenge(TrakHoundIdentityContext context, string state)
        {
            if (!IsConfigured()) return TrakHoundIdentityRedirect.None("The authority and clientId Parameters are required");

            var metadata = await GetMetadataAsync();

            // The Nonce binds the ID Token to this sign in. The PKCE Verifier binds the Authorization Code to this sign in
            var nonce = CreateRandom();
            var codeVerifier = CreateRandom();
            var codeChallenge = Base64UrlEncoder.Encode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));

            context.State.Set(GetStateKey(state), JsonSerializer.Serialize(new SignInState { Nonce = nonce, CodeVerifier = codeVerifier }), _signInLifetime);

            var query = new Dictionary<string, string>
            {
                ["response_type"] = "code",
                ["client_id"] = _clientId,
                ["redirect_uri"] = context.CallbackUrl,
                ["scope"] = _scopes,
                ["state"] = state,
                ["nonce"] = nonce,
                ["code_challenge"] = codeChallenge,
                ["code_challenge_method"] = "S256",
                ["response_mode"] = "query"
            };

            return TrakHoundIdentityRedirect.To(AppendQuery(metadata.AuthorizationEndpoint, query));
        }

        protected override async Task<TrakHoundIdentityResult> OnHandleCallback(TrakHoundIdentityContext context)
        {
            if (!IsConfigured()) return TrakHoundIdentityResult.Error("The authority and clientId Parameters are required");

            var state = context.Request.GetQueryParameter("state");
            if (string.IsNullOrEmpty(state)) return TrakHoundIdentityResult.Fail("No State in Callback");

            // The State can only be used once (prevents replaying a Callback)
            var signInStateJson = context.State.Take(GetStateKey(state));
            if (signInStateJson == null) return TrakHoundIdentityResult.Fail("Unknown or Expired State", state);
            var signInState = JsonSerializer.Deserialize<SignInState>(signInStateJson);

            var error = context.Request.GetQueryParameter("error");
            if (!string.IsNullOrEmpty(error)) return TrakHoundIdentityResult.Fail($"{error} : {context.Request.GetQueryParameter("error_description")}", state);

            var code = context.Request.GetQueryParameter("code");
            if (string.IsNullOrEmpty(code)) return TrakHoundIdentityResult.Fail("No Authorization Code in Callback", state);

            var metadata = await GetMetadataAsync();

            // Exchange the Authorization Code for Tokens
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["redirect_uri"] = context.CallbackUrl,
                ["client_id"] = _clientId,
                ["code_verifier"] = signInState.CodeVerifier
            };
            if (!string.IsNullOrEmpty(_clientSecret)) form["client_secret"] = _clientSecret;

            using var response = await _httpClient.PostAsync(metadata.TokenEndpoint, new FormUrlEncodedContent(form));
            var responseBody = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode) return TrakHoundIdentityResult.Error($"Token Request Failed ({(int)response.StatusCode}) : {responseBody}", state);

            string idToken = null;
            using (var document = JsonDocument.Parse(responseBody))
            {
                if (document.RootElement.TryGetProperty("id_token", out var idTokenElement)) idToken = idTokenElement.GetString();
            }
            if (string.IsNullOrEmpty(idToken)) return TrakHoundIdentityResult.Error("No ID Token in Token Response", state);

            // Validate the ID Token (Signature, Issuer, Audience = Client ID, Lifetime)
            var validation = await _tokenHandler.ValidateTokenAsync(idToken, CreateValidationParameters(metadata, new[] { _clientId }));
            if (!validation.IsValid) return TrakHoundIdentityResult.Fail($"Invalid ID Token : {validation.Exception?.Message}", state);

            var token = (JsonWebToken)validation.SecurityToken;
            if (!token.TryGetPayloadValue<string>("nonce", out var nonce) || !CryptographicOperations.FixedTimeEquals(Encoding.UTF8.GetBytes(nonce ?? ""), Encoding.UTF8.GetBytes(signInState.Nonce)))
            {
                return TrakHoundIdentityResult.Fail("Invalid ID Token Nonce", state);
            }

            // The Instance's Session lifetime is used (not the ID Token's, which is usually short)
            TrakHoundIdentity identity;
            using (var payload = ReadPayload(token)) identity = CreateIdentity(payload.RootElement, null);
            if (string.IsNullOrEmpty(identity.UserId)) return TrakHoundIdentityResult.Fail($"The ID Token does not contain the ({_userIdClaim}) Claim", state);

            return TrakHoundIdentityResult.Success(identity, state);
        }

        protected override async Task<TrakHoundIdentityResult> OnAuthenticate(TrakHoundIdentityContext context)
        {
            // Only JWT Bearer Tokens (other Bearer Tokens, such as Instance API Keys, are for other Identity Providers)
            var authorization = context.Request.GetHeader("Authorization");
            if (authorization == null || !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase)) return TrakHoundIdentityResult.NoResult();

            var value = authorization.Substring("Bearer ".Length).Trim();
            if (!_tokenHandler.CanReadToken(value)) return TrakHoundIdentityResult.NoResult();
            if (!IsConfigured()) return TrakHoundIdentityResult.NoResult();

            // A Token that already validated is reused until it expires (a JWT can't be revoked before then anyway)
            var cacheKey = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
            if (_tokenCache.TryGetValue(cacheKey, out var cached))
            {
                if (cached.Until > DateTime.UtcNow) return TrakHoundIdentityResult.Success(CopyIdentity(cached.Identity));
                _tokenCache.TryRemove(cacheKey, out _);
            }

            JsonWebToken unvalidatedToken;
            try
            {
                unvalidatedToken = _tokenHandler.ReadJsonWebToken(value);
            }
            catch
            {
                return TrakHoundIdentityResult.NoResult();
            }

            // Tokens from other Issuers are left for other Identity Providers. Once the metadata is known its Issuer is compared without
            // fetching anything, so Requests meant for other Providers never wait on this server
            var metadata = _metadata;
            if (metadata != null)
            {
                if (!string.Equals(unvalidatedToken.Issuer, metadata.Issuer, StringComparison.Ordinal)) return TrakHoundIdentityResult.NoResult();
            }
            else if (!IsAuthorityIssuer(unvalidatedToken.Issuer))
            {
                // The Issuer can legitimately differ from the authority (ex. a server reached by an internal address), so the metadata is
                // fetched in the background and the Token is left for other Providers until it is known
                FetchMetadataInBackground();
                return TrakHoundIdentityResult.NoResult();
            }

            metadata = await GetMetadataAsync();
            if (!string.Equals(unvalidatedToken.Issuer, metadata.Issuer, StringComparison.Ordinal)) return TrakHoundIdentityResult.NoResult();

            // Validate the Token that was already read (instead of parsing it again)
            var validation = await _tokenHandler.ValidateTokenAsync(unvalidatedToken, CreateValidationParameters(metadata, _audiences));
            if (!validation.IsValid) return TrakHoundIdentityResult.Fail($"Invalid Bearer Token : {validation.Exception?.Message}");

            var token = (JsonWebToken)validation.SecurityToken;

            TrakHoundIdentity identity;
            using (var payload = ReadPayload(token))
            {
                identity = CreateIdentity(payload.RootElement, token.ValidTo);

                // Tokens issued to an application without a User (Client Credentials) are identified by the application
                if (string.IsNullOrEmpty(identity.UserId))
                {
                    identity.UserId = GetClaimValues(payload.RootElement, "azp").FirstOrDefault() ?? GetClaimValues(payload.RootElement, "client_id").FirstOrDefault();
                    identity.Type = TrakHoundIdentityUserType.Delegate;
                }
            }
            if (string.IsNullOrEmpty(identity.UserId)) return TrakHoundIdentityResult.Fail($"The Bearer Token does not contain the ({_userIdClaim}) Claim");

            AddCachedIdentity(cacheKey, identity, token.ValidTo);

            // Callers can change the Identity (ex. the Security Manager sets its ProviderId), so the cached one is never returned
            return TrakHoundIdentityResult.Success(CopyIdentity(identity));
        }

        protected override async Task<TrakHoundIdentityRedirect> OnSignOut(TrakHoundIdentityContext context, TrakHoundIdentity identity, string returnUrl)
        {
            if (!IsConfigured()) return TrakHoundIdentityRedirect.None();

            var metadata = await GetMetadataAsync();
            if (string.IsNullOrEmpty(metadata.EndSessionEndpoint)) return TrakHoundIdentityRedirect.None();

            var query = new Dictionary<string, string>
            {
                ["client_id"] = _clientId,
                ["post_logout_redirect_uri"] = returnUrl
            };

            return TrakHoundIdentityRedirect.To(AppendQuery(metadata.EndSessionEndpoint, query));
        }


        private bool IsConfigured() => _configurationManager != null && !string.IsNullOrEmpty(_clientId);

        private string GetStateKey(string state) => $"oidc:{Id}:{state}";

        private async Task<OpenIdConnectConfiguration> GetMetadataAsync()
        {
            // After the first fetch the Configuration Manager returns the cached metadata and refreshes it in the background
            if (_metadata != null)
            {
                var metadata = await _configurationManager.GetConfigurationAsync(CancellationToken.None);
                _metadata = metadata;
                return metadata;
            }

            // Until then every call waits on a fetch, so a failure is remembered briefly (Requests fail fast instead of each waiting on an
            // unreachable server) and concurrent callers share one fetch (instead of queuing a fetch, and its timeout, each)
            var retryAfter = Interlocked.Read(ref _metadataRetryAfter);
            if (DateTime.UtcNow.Ticks < retryAfter)
            {
                throw new InvalidOperationException($"The OpenID Connect metadata for ({_authority}) could not be retrieved (retrying after {new DateTime(retryAfter, DateTimeKind.Utc):o})");
            }

            Task<OpenIdConnectConfiguration> fetch;
            lock (_metadataLock)
            {
                if (_metadataFetch == null || _metadataFetch.IsCompleted) _metadataFetch = FetchFirstMetadataAsync();
                fetch = _metadataFetch;
            }
            return await fetch;
        }

        private async Task<OpenIdConnectConfiguration> FetchFirstMetadataAsync()
        {
            try
            {
                var metadata = await _configurationManager.GetConfigurationAsync(CancellationToken.None);
                _metadata = metadata;
                return metadata;
            }
            catch
            {
                Interlocked.Exchange(ref _metadataRetryAfter, (DateTime.UtcNow + _metadataRetryDelay).Ticks);
                throw;
            }
        }

        private void FetchMetadataInBackground()
        {
            _ = Task.Run(async () =>
            {
                try { await GetMetadataAsync(); }
                catch { } // Remembered by GetMetadataAsync (and reported when a Token for this Provider needs it)
            });
        }

        // Compares an unvalidated Token's Issuer with the configured authority (before the metadata, and its exact Issuer, is known)
        private bool IsAuthorityIssuer(string issuer)
        {
            return issuer != null && string.Equals(issuer.TrimEnd('/'), _authority, StringComparison.OrdinalIgnoreCase);
        }

        private void AddCachedIdentity(string key, TrakHoundIdentity identity, DateTime tokenExpires)
        {
            var now = DateTime.UtcNow;
            var until = now + _tokenCacheLifetime;
            if (tokenExpires < until) until = tokenExpires;
            if (until <= now) return;

            if (_tokenCache.Count >= _tokenCacheLimit)
            {
                // Remove expired entries first, then everything if the cache is still full (a full cache is only a performance concern)
                foreach (var entry in _tokenCache)
                {
                    if (entry.Value.Until <= now) _tokenCache.TryRemove(entry.Key, out _);
                }
                if (_tokenCache.Count >= _tokenCacheLimit) _tokenCache.Clear();
            }

            _tokenCache[key] = new CachedIdentity(CopyIdentity(identity), until);
        }

        private static TrakHoundIdentity CopyIdentity(TrakHoundIdentity identity)
        {
            var copy = new TrakHoundIdentity();
            copy.ProviderId = identity.ProviderId;
            copy.UserId = identity.UserId;
            copy.Name = identity.Name;
            copy.Type = identity.Type;
            copy.Roles = identity.Roles?.ToList();
            copy.Properties = identity.Properties != null ? new Dictionary<string, string>(identity.Properties) : null;
            copy.Expires = identity.Expires;
            return copy;
        }

        private TokenValidationParameters CreateValidationParameters(OpenIdConnectConfiguration metadata, IEnumerable<string> audiences)
        {
            return new TokenValidationParameters
            {
                ValidIssuer = metadata.Issuer,
                ValidAudiences = audiences,
                IssuerSigningKeys = metadata.SigningKeys,

                // A Token signed with a Key that isn't in the cached metadata (the server rotated its Keys) makes the handler refresh the
                // metadata (limited by the Configuration Manager's RefreshInterval) instead of rejecting new Keys until the next automatic refresh
                ConfigurationManager = _configurationManager,
                ValidateIssuer = true,
                ValidateAudience = true,
                ValidateLifetime = true,
                ValidateIssuerSigningKey = true,
                RequireSignedTokens = true,
                RequireExpirationTime = true,
                ClockSkew = _clockSkew
            };
        }

        // The Payload is parsed once and every Claim is read from it
        private static JsonDocument ReadPayload(JsonWebToken token) => JsonDocument.Parse(Base64UrlEncoder.DecodeBytes(token.EncodedPayload));

        private TrakHoundIdentity CreateIdentity(JsonElement payload, DateTime? expires)
        {
            var email = GetClaimValues(payload, "email").FirstOrDefault();
            var username = GetClaimValues(payload, "preferred_username").FirstOrDefault();

            var identity = new TrakHoundIdentity();
            identity.ProviderId = Id;
            identity.UserId = GetClaimValues(payload, _userIdClaim).FirstOrDefault();
            identity.Name = GetClaimValues(payload, _nameClaim).FirstOrDefault() ?? username ?? email;
            identity.Type = TrakHoundIdentityUserType.Origin;
            identity.Roles = _rolesClaims.SelectMany(o => GetClaimValues(payload, o)).Distinct().ToList();
            identity.Expires = expires?.ToUniversalTime();

            var properties = new Dictionary<string, string>();
            if (email != null) properties["email"] = email;
            if (username != null) properties["username"] = username;
            identity.Properties = properties;

            return identity;
        }

        // Reads a Claim's values from the Token Payload. Nested values use dots (ex. "realm_access.roles"). Arrays return each value
        private static IEnumerable<string> GetClaimValues(JsonElement payload, string claimPath)
        {
            if (string.IsNullOrEmpty(claimPath)) return Enumerable.Empty<string>();

            var element = payload;
            foreach (var segment in claimPath.Split('.'))
            {
                if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(segment, out element)) return Enumerable.Empty<string>();
            }

            switch (element.ValueKind)
            {
                case JsonValueKind.String: return new[] { element.GetString() };
                case JsonValueKind.Array: return element.EnumerateArray().Where(o => o.ValueKind == JsonValueKind.String).Select(o => o.GetString()).ToList();
                case JsonValueKind.Number:
                case JsonValueKind.True:
                case JsonValueKind.False: return new[] { element.GetRawText() };
                default: return Enumerable.Empty<string>();
            }
        }

        private static string CreateRandom() => Base64UrlEncoder.Encode(RandomNumberGenerator.GetBytes(32));

        private static string AppendQuery(string url, IDictionary<string, string> parameters)
        {
            var query = string.Join("&", parameters.Where(o => o.Value != null).Select(o => $"{Uri.EscapeDataString(o.Key)}={Uri.EscapeDataString(o.Value)}"));
            return url + (url.Contains('?') ? "&" : "?") + query;
        }


        private sealed record CachedIdentity(TrakHoundIdentity Identity, DateTime Until);

        private class SignInState
        {
            public string Nonce { get; set; }

            public string CodeVerifier { get; set; }
        }
    }
}
