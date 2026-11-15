// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using TrakHound.Api;
using TrakHound.Clients;
using TrakHound.Http;
using TrakHound.Instance.Http;
using TrakHound.Instances;
using TrakHound.Zones;

namespace TrakHound.Security
{
    /// <summary>
    /// A Module Token that was issued by the Instance
    /// </summary>
    public sealed class TrakHoundModuleTokenEntry
    {
        /// <summary>
        /// The Module that Requests made with the Token are made as
        /// </summary>
        public TrakHoundApiRequestSource Source { get; init; }

        /// <summary>
        /// The Member ID of the Module (ex. "api:my-api")
        /// </summary>
        public string MemberId { get; init; }

        /// <summary>
        /// The ID of the Service Router used when a Request doesn't specify one (null for the default Router)
        /// </summary>
        public string RouterId { get; init; }

        public DateTime Created { get; init; }

        /// <summary>
        /// When the Token expires (UTC). Null if it is valid until it is revoked
        /// </summary>
        public DateTime? Expires { get; init; }

        public bool IsExpired => Expires.HasValue && Expires.Value < DateTime.UtcNow;
    }

    /// <summary>
    /// Issues and verifies Module Tokens, which let code running outside of the Instance's process (ex. a Python worker started by an Api)
    /// call the System API as the Module that started it, so that the Module's Zone applies. Tokens are kept in memory only (as a Hash of the Token)
    /// </summary>
    public sealed class TrakHoundModuleTokenManager
    {
        private const int _secretBytes = 32;

        private readonly ConcurrentDictionary<string, TrakHoundModuleTokenEntry> _tokens = new ConcurrentDictionary<string, TrakHoundModuleTokenEntry>(); // Hash => Entry
        private readonly Func<string> _getInstanceUrl;


        /// <summary>
        /// Creates a Token Manager. The Instance URL given to Modules is read when each Token is issued (the listening Ports are set at startup)
        /// </summary>
        public TrakHoundModuleTokenManager(Func<string> getInstanceUrl = null)
        {
            _getInstanceUrl = getInstanceUrl;
        }


        /// <summary>
        /// Issues a Token for the Module. Returns null if the Source is not a Module (ex. the Instance or an HTTP Request)
        /// </summary>
        public TrakHoundModuleToken Issue(TrakHoundApiRequestSource source, string routerId = null, TimeSpan? lifetime = null)
        {
            var memberId = TrakHoundZoneResources.GetMemberId(source);
            if (memberId == null) return null;

            RemoveExpired();

            var token = TrakHoundHttpConstants.ModuleTokenPrefix + Base64UrlEncode(RandomNumberGenerator.GetBytes(_secretBytes));
            var now = DateTime.UtcNow;
            var expires = lifetime.HasValue && lifetime.Value > TimeSpan.Zero ? now + lifetime.Value : (DateTime?)null;

            _tokens[GetHash(token)] = new TrakHoundModuleTokenEntry
            {
                Source = source,
                MemberId = memberId,
                RouterId = !string.IsNullOrEmpty(routerId) ? routerId : null,
                Created = now,
                Expires = expires
            };

            return new TrakHoundModuleToken
            {
                Token = token,
                InstanceUrl = _getInstanceUrl?.Invoke(),
                RouterId = !string.IsNullOrEmpty(routerId) ? routerId : null,
                Expires = expires
            };
        }

        /// <summary>
        /// Gets the Entry for a Token. Returns null if the Token was not issued, was revoked, or has expired
        /// </summary>
        public TrakHoundModuleTokenEntry Validate(string token)
        {
            if (!IsModuleToken(token)) return null;

            var hash = GetHash(token);
            if (_tokens.TryGetValue(hash, out var entry))
            {
                if (!entry.IsExpired) return entry;
                _tokens.TryRemove(hash, out _);
            }

            return null;
        }

        /// <summary>
        /// Revokes a Token. When a Source is given, the Token is only revoked if it was issued to that Module (so a Module can't revoke another Module's Token)
        /// </summary>
        public bool Revoke(string token, TrakHoundApiRequestSource source = null)
        {
            if (!IsModuleToken(token)) return false;

            var hash = GetHash(token);
            if (_tokens.TryGetValue(hash, out var entry))
            {
                if (source != null && !string.Equals(entry.MemberId, TrakHoundZoneResources.GetMemberId(source), StringComparison.OrdinalIgnoreCase)) return false;
                return _tokens.TryRemove(hash, out _);
            }

            return false;
        }

        /// <summary>
        /// Revokes every Token issued to the Module (ex. when it is unloaded). Returns the number of Tokens revoked
        /// </summary>
        public int RevokeAll(string memberId)
        {
            if (string.IsNullOrEmpty(memberId)) return 0;

            var count = 0;
            foreach (var item in _tokens)
            {
                if (string.Equals(item.Value.MemberId, memberId, StringComparison.OrdinalIgnoreCase) && _tokens.TryRemove(item.Key, out _)) count++;
            }

            return count;
        }

        /// <summary>
        /// Gets the Tokens that are currently valid (for display. The Tokens themselves are not stored)
        /// </summary>
        public IEnumerable<TrakHoundModuleTokenEntry> GetEntries()
        {
            RemoveExpired();
            return _tokens.Values.ToList();
        }


        /// <summary>
        /// Whether the value has the format of a Module Token ("thm_{secret}")
        /// </summary>
        public static bool IsModuleToken(string value) => value != null && value.Length > TrakHoundHttpConstants.ModuleTokenPrefix.Length && value.StartsWith(TrakHoundHttpConstants.ModuleTokenPrefix, StringComparison.Ordinal);

        /// <summary>
        /// Gets the Module Token sent with a Request (in the "X-TrakHound-Module-Token" Header, or as a Bearer Token). Null if there is none
        /// </summary>
        public static string GetRequestToken(HttpRequest request)
        {
            if (request == null) return null;

            var value = request.Headers[TrakHoundHttpConstants.ModuleTokenHeader].FirstOrDefault();
            if (!string.IsNullOrEmpty(value)) return value.Trim();

            // A Bearer Token is only a Module Token if it has the prefix (other Bearer Tokens are for the Identity Providers)
            var authorization = request.Headers.Authorization.FirstOrDefault();
            if (authorization != null && authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                var bearer = authorization.Substring("Bearer ".Length).Trim();
                if (IsModuleToken(bearer)) return bearer;
            }

            return null;
        }

        /// <summary>
        /// Whether the Request was made from the same machine as the Instance (Module Tokens are only accepted from the same machine)
        /// </summary>
        public static bool IsLocalRequest(HttpContext context)
        {
            var remoteAddress = context?.Connection?.RemoteIpAddress;
            if (remoteAddress == null) return false;
            if (IPAddress.IsLoopback(remoteAddress)) return true;

            // The Instance may only listen on a specific address (ex. 192.168.1.10), which a local process connects from too
            var localAddress = context.Connection.LocalIpAddress;
            return localAddress != null && NormalizeAddress(remoteAddress).Equals(NormalizeAddress(localAddress));
        }

        /// <summary>
        /// Gets the URL that a process on the same machine reaches the System API at (the Admin Port when one is configured)
        /// </summary>
        public static string GetInstanceUrl(TrakHoundInstanceConfiguration configuration)
        {
            if (configuration == null) return null;

            var port = TrakHoundAdminPort.IsEnabled ? TrakHoundAdminPort.Port : configuration.HttpPort;
            var address = TrakHoundAdminPort.IsEnabled && !string.IsNullOrEmpty(configuration.AdminHttpAddress) ? configuration.AdminHttpAddress : configuration.HttpAddress;

            // Listening on every address (or none set) is reached through the loopback address
            var host = "127.0.0.1";
            if (!string.IsNullOrEmpty(address) && IPAddress.TryParse(address, out var ipAddress) && !ipAddress.Equals(IPAddress.Any) && !ipAddress.Equals(IPAddress.IPv6Any))
            {
                host = ipAddress.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? $"[{ipAddress}]" : ipAddress.ToString();
            }

            return $"http://{host}:{port}/";
        }


        private void RemoveExpired()
        {
            foreach (var item in _tokens)
            {
                if (item.Value.IsExpired) _tokens.TryRemove(item.Key, out _);
            }
        }

        private static IPAddress NormalizeAddress(IPAddress address) => address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;

        private static string GetHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token))).ToLowerInvariant();

        private static string Base64UrlEncode(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
