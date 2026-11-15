// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Security.Cryptography;
using System.Text;
using TrakHound.Clients;
using TrakHound.Instance.Security;
using TrakHound.Volumes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.Security
{
    /// <summary>
    /// An API Key issued by the Instance. Only a Hash of the Key's secret is stored
    /// </summary>
    public class TrakHoundApiKey
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// The Roles of the Identity created for the Key (mapped to Permissions by Security Profile Assignments)
        /// </summary>
        public List<string> Roles { get; set; }

        public DateTime Created { get; set; }

        /// <summary>
        /// When the Key expires (UTC). Null if the Key does not expire
        /// </summary>
        public DateTime? Expires { get; set; }

        /// <summary>
        /// The SHA-256 Hash (hex) of the Key's secret
        /// </summary>
        public string Hash { get; set; }


        [YamlIgnore]
        public bool IsExpired => Expires.HasValue && Expires.Value.ToUniversalTime() < DateTime.UtcNow;
    }

    /// <summary>
    /// Stores the API Keys issued by the Instance (one file per Key in the "security/api-keys" directory of the Configuration Profile).
    /// Keys have the format "thk_{id}_{secret}"
    /// </summary>
    public sealed class TrakHoundApiKeyStore
    {
        private const string _keyPrefix = "thk_";
        private const string _fileExtension = ".yaml";

        private readonly string _directory;
        private readonly Dictionary<string, TrakHoundApiKey> _keys = new Dictionary<string, TrakHoundApiKey>(); // Id => Key
        private readonly object _lock = new object();


        /// <summary>
        /// Raised when a Key is created or revoked
        /// </summary>
        public event EventHandler Changed;


        public TrakHoundApiKeyStore(string directory)
        {
            _directory = directory;
        }


        /// <summary>
        /// Whether a Key with the Roles gets access to the System API or Services from a Security Profile (set by the Security Manager, which has the Profiles)
        /// </summary>
        public Func<IEnumerable<string>, bool> ReachesInstance { get; set; }

        /// <summary>
        /// Returns why the signed in Admin can't create a Key with the Roles, or revoke a Key that has them (null when they can). An
        /// Admin who manages API Keys can't hand out access to the System API or Services unless they can also change the Profiles that give it
        /// </summary>
        public string GetRoleDenial(IEnumerable<string> roles)
        {
            if (TrakHoundAdminContext.Current == null) return null;

            // The Admin's own Roles can limit which Roles they give
            var denial = TrakHoundAdminContext.GetGrantDenial(TrakHoundAdminSecurity.ApiKeys, roles);
            if (denial != null) return denial;

            if (ReachesInstance == null || !ReachesInstance(roles ?? Enumerable.Empty<string>())) return null;

            return TrakHoundAdminContext.IsAllowed(TrakHoundAdminSecurity.InstanceSecurityProfiles, TrakHoundAdminSecurity.WritePermission) ? null : TrakHoundAdminSecurity.InstanceRoleDeniedMessage;
        }

        private void DemandRoles(IEnumerable<string> roles)
        {
            if (GetRoleDenial(roles?.ToList()) != null) throw new TrakHoundAdminAccessException(TrakHoundAdminSecurity.InstanceSecurityProfiles, TrakHoundAdminSecurity.WritePermission);
        }


        public void Load()
        {
            var keys = new List<TrakHoundApiKey>();

            if (Directory.Exists(_directory))
            {
                var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

                foreach (var path in Directory.GetFiles(_directory, $"*{_fileExtension}"))
                {
                    try
                    {
                        var key = deserializer.Deserialize<TrakHoundApiKey>(File.ReadAllText(path));
                        if (key != null && !string.IsNullOrEmpty(key.Id) && !string.IsNullOrEmpty(key.Hash)) keys.Add(key);
                    }
                    catch { }
                }
            }

            lock (_lock)
            {
                _keys.Clear();
                foreach (var key in keys) _keys[key.Id] = key;
            }
        }

        public IEnumerable<TrakHoundApiKey> GetKeys()
        {
            lock (_lock) return _keys.Values.OrderBy(o => o.Name).ToList();
        }

        /// <summary>
        /// Creates a new API Key and returns the Key value. The value is not stored and cannot be retrieved again
        /// </summary>
        public string Create(string name, string description, IEnumerable<string> roles, DateTime? expires)
        {
            TrakHoundAdminContext.DemandWrite(TrakHoundAdminSecurity.ApiKeys);
            DemandRoles(roles?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()));

            var id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            var secret = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

            var key = new TrakHoundApiKey();
            key.Id = id;
            key.Name = name;
            key.Description = description;
            key.Roles = roles?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).Distinct().ToList();
            key.Created = DateTime.UtcNow;
            key.Expires = expires?.ToUniversalTime();
            key.Hash = GetHash(secret);

            Directory.CreateDirectory(_directory);
            var serializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
            File.WriteAllText(GetPath(id), serializer.Serialize(key));

            lock (_lock) _keys[id] = key;
            Changed?.Invoke(this, EventArgs.Empty);

            return $"{_keyPrefix}{id}_{secret}";
        }

        public void Revoke(string id)
        {
            TrakHoundAdminContext.DemandWrite(TrakHoundAdminSecurity.ApiKeys);

            TrakHoundApiKey existing;
            lock (_lock) existing = _keys.GetValueOrDefault(id ?? "");
            DemandRoles(existing?.Roles);

            if (!string.IsNullOrEmpty(id))
            {
                lock (_lock) _keys.Remove(id);

                var path = GetPath(id);
                if (File.Exists(path)) File.Delete(path);

                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        /// <summary>
        /// Returns the Key matching the value (null if the value is not a valid, unexpired Key)
        /// </summary>
        public TrakHoundApiKey Verify(string value)
        {
            if (!IsApiKey(value)) return null;

            // Format = thk_{id}_{secret} (the secret can contain '_')
            var separatorIndex = value.IndexOf('_', _keyPrefix.Length);
            if (separatorIndex < 0) return null;

            var id = value.Substring(_keyPrefix.Length, separatorIndex - _keyPrefix.Length);
            var secret = value.Substring(separatorIndex + 1);

            TrakHoundApiKey key;
            lock (_lock) _keys.TryGetValue(id, out key);

            if (key != null && !key.IsExpired)
            {
                // Constant time comparison (prevents timing attacks)
                var expected = Encoding.ASCII.GetBytes(key.Hash);
                var actual = Encoding.ASCII.GetBytes(GetHash(secret));
                if (CryptographicOperations.FixedTimeEquals(expected, actual)) return key;
            }

            return null;
        }

        public static bool IsApiKey(string value) => value != null && value.StartsWith(_keyPrefix, StringComparison.Ordinal);


        private string GetPath(string id)
        {
            // IDs are generated hex strings, but never allow a path outside of the directory
            if (id.Any(o => !char.IsLetterOrDigit(o))) throw new ArgumentException("Invalid API Key ID");
            return Path.Combine(_directory, $"{id}{_fileExtension}");
        }

        private static string GetHash(string secret) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(secret))).ToLowerInvariant();

        private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    /// <summary>
    /// The built in Identity Provider for API Keys issued by the Instance. Keys are sent in the "Authorization: Bearer {key}" or "X-API-Key: {key}" header
    /// </summary>
    public sealed class TrakHoundApiKeyIdentityProvider : ITrakHoundIdentityProvider
    {
        public const string ProviderId = "api-keys";
        public const string ApiKeyHeader = "X-API-Key";

        private readonly TrakHoundApiKeyStore _store;
        private readonly TrakHoundIdentityProviderConfiguration _configuration;


        public string Id => ProviderId;

        public ITrakHoundIdentityProviderConfiguration Configuration => _configuration;

        public ITrakHoundClient Client => null;

        public ITrakHoundVolume Volume => null;

        public TrakHoundIdentityProviderFeatures Features => TrakHoundIdentityProviderFeatures.Token;

        public TrakHoundApiKeyStore Store => _store;


        public TrakHoundApiKeyIdentityProvider(TrakHoundApiKeyStore store)
        {
            _store = store;

            _configuration = new TrakHoundIdentityProviderConfiguration();
            _configuration.Id = ProviderId;
            _configuration.Description = "API Keys issued by the Instance";
        }


        public Task<TrakHoundIdentityResult> Authenticate(TrakHoundIdentityContext context)
        {
            var value = context.Request.GetHeader(ApiKeyHeader);
            if (string.IsNullOrEmpty(value))
            {
                var authorization = context.Request.GetHeader("Authorization");
                if (authorization != null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    value = authorization.Substring("Bearer ".Length).Trim();
                }
            }

            // Bearer Tokens that are not API Keys are left for other Identity Providers (ex. OpenID Connect)
            if (!TrakHoundApiKeyStore.IsApiKey(value)) return Task.FromResult(TrakHoundIdentityResult.NoResult());

            var key = _store.Verify(value);
            if (key == null) return Task.FromResult(TrakHoundIdentityResult.Fail("Invalid or Expired API Key"));

            var identity = new TrakHoundIdentity();
            identity.ProviderId = ProviderId;
            identity.UserId = key.Id;
            identity.Name = key.Name;
            identity.Type = TrakHoundIdentityUserType.Delegate;
            identity.Roles = key.Roles ?? new List<string>();
            identity.Expires = key.Expires;

            return Task.FromResult(TrakHoundIdentityResult.Success(identity));
        }

        public Task<TrakHoundIdentityRedirect> Challenge(TrakHoundIdentityContext context, string state) => Task.FromResult(TrakHoundIdentityRedirect.None("API Keys do not support Sign In"));

        public Task<TrakHoundIdentityResult> HandleCallback(TrakHoundIdentityContext context) => Task.FromResult(TrakHoundIdentityResult.Error("API Keys do not support Sign In"));

        public Task<TrakHoundIdentityResult> SignIn(TrakHoundIdentityContext context, TrakHoundIdentityCredentials credentials) => Task.FromResult(TrakHoundIdentityResult.Error("API Keys do not support Sign In"));

        public Task<TrakHoundIdentityRedirect> SignOut(TrakHoundIdentityContext context, TrakHoundIdentity identity, string returnUrl) => Task.FromResult(TrakHoundIdentityRedirect.None());
    }
}
