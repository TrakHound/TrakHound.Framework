// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// Admin Interface Sessions. A Session is an HttpOnly Cookie (encrypted with the Instance's persisted Data Protection keys, so it
    /// survives restarts and can't be read by scripts on the page). Each Session has an ID that can be revoked (by signing out), and the
    /// Session Stamp of its Admin Account, so changing the Account's Password (or disabling or deleting it) ends its Sessions.
    /// </summary>
    /// <remarks>
    /// The Admin Login checks the Password in the Blazor Circuit, which can't set a Cookie. It gets a one time Code instead, and the
    /// browser exchanges it for the Cookie at /_admin/session/signin (see AdminSessionController)
    /// </remarks>
    public sealed class TrakHoundAdminSessions
    {
        public const string AuthenticationScheme = "TrakHound.Admin";
        public static readonly TimeSpan SessionLifetime = TimeSpan.FromDays(7);
        public static readonly TimeSpan CodeLifetime = TimeSpan.FromSeconds(60);

        private const string _accountIdClaim = "trakhound.admin.id";
        private const string _usernameClaim = "trakhound.admin.username";
        private const string _sessionIdClaim = "trakhound.admin.sid";
        private const string _stampClaim = "trakhound.admin.stamp";
        private const string _expiresClaim = "trakhound.admin.expires";
        private const string _clientAddressClaim = "trakhound.connection.address";

        private readonly TrakHoundAdminAccountStore _accounts;
        private readonly string _revokedPath;
        private readonly Func<DateTime> _now;
        private readonly Dictionary<string, (string AccountId, string Stamp, DateTime Expires)> _codes = new Dictionary<string, (string, string, DateTime)>();
        private readonly Dictionary<string, DateTime> _revoked = new Dictionary<string, DateTime>(); // Session ID => when it would have expired
        private readonly object _lock = new object();


        /// <summary>
        /// The Admin Accounts that Sessions are for
        /// </summary>
        public TrakHoundAdminAccountStore Accounts => _accounts;


        public TrakHoundAdminSessions(string configurationProfilePath, TrakHoundAdminAccountStore accounts, Func<DateTime> now = null)
        {
            _accounts = accounts ?? throw new ArgumentNullException(nameof(accounts));
            _revokedPath = TrakHoundInstanceLocalFiles.GetRevokedSessionsPath(configurationProfilePath);
            _now = now ?? (() => DateTime.UtcNow);
            LoadRevoked();
        }


        /// <summary>
        /// Create a one time Code for a successful Login (exchanged for the Session Cookie)
        /// </summary>
        public string CreateCode(TrakHoundAdminAccount account)
        {
            if (account == null) throw new ArgumentNullException(nameof(account));

            var code = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(32));
            var now = _now();

            lock (_lock)
            {
                foreach (var expired in _codes.Where(o => o.Value.Expires <= now).Select(o => o.Key).ToList()) _codes.Remove(expired);
                _codes[code] = (account.Id, account.SessionStamp, now + CodeLifetime);
            }

            return code;
        }

        /// <summary>
        /// Exchange a Code for the Session's Principal (null if the Code is unknown, expired, already used, or the Account's Sessions were ended)
        /// </summary>
        public ClaimsPrincipal TakeCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return null;

            (string AccountId, string Stamp, DateTime Expires) entry;
            lock (_lock)
            {
                if (!_codes.Remove(code, out entry)) return null;
            }

            if (entry.Expires <= _now() || !_accounts.IsSessionValid(entry.AccountId, entry.Stamp)) return null;

            var account = _accounts.GetAccount(entry.AccountId);
            if (account == null) return null;

            var expires = _now() + SessionLifetime;
            var claims = new[]
            {
                new Claim(_accountIdClaim, account.Id),
                new Claim(_usernameClaim, account.Username),
                new Claim(_sessionIdClaim, Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16))),
                new Claim(_stampClaim, entry.Stamp),
                new Claim(_expiresClaim, expires.Ticks.ToString())
            };

            return new ClaimsPrincipal(new ClaimsIdentity(claims, AuthenticationScheme));
        }

        /// <summary>
        /// The Admin Account of a valid Session in the Principal (null if there is none, or it expired, was revoked, or the Account's Sessions were ended)
        /// </summary>
        public TrakHoundAdminAccount Validate(ClaimsPrincipal principal)
        {
            var identity = principal?.Identities?.FirstOrDefault(o => o.AuthenticationType == AuthenticationScheme && o.IsAuthenticated);
            if (identity == null) return null;

            var sessionId = identity.FindFirst(_sessionIdClaim)?.Value;
            var stamp = identity.FindFirst(_stampClaim)?.Value;
            var accountId = identity.FindFirst(_accountIdClaim)?.Value;
            if (string.IsNullOrEmpty(sessionId) || string.IsNullOrEmpty(stamp) || string.IsNullOrEmpty(accountId)) return null;

            // A Circuit keeps the Principal it started with, so expiry is also checked here (not only by the Cookie)
            if (!long.TryParse(identity.FindFirst(_expiresClaim)?.Value, out var expiresTicks) || new DateTime(expiresTicks, DateTimeKind.Utc) <= _now()) return null;

            lock (_lock)
            {
                if (_revoked.ContainsKey(sessionId)) return null;
            }

            if (!_accounts.IsSessionValid(accountId, stamp)) return null;

            return _accounts.GetAccount(accountId);
        }

        /// <summary>
        /// End the Session in the Principal (also in other Browser Tabs using it, and after a restart)
        /// </summary>
        public void Revoke(ClaimsPrincipal principal)
        {
            var identity = principal?.Identities?.FirstOrDefault(o => o.AuthenticationType == AuthenticationScheme);
            var sessionId = identity?.FindFirst(_sessionIdClaim)?.Value;
            if (string.IsNullOrEmpty(sessionId)) return;

            var expires = long.TryParse(identity.FindFirst(_expiresClaim)?.Value, out var ticks) ? new DateTime(ticks, DateTimeKind.Utc) : _now() + SessionLifetime;

            lock (_lock)
            {
                var now = _now();
                foreach (var expired in _revoked.Where(o => o.Value <= now).Select(o => o.Key).ToList()) _revoked.Remove(expired);

                _revoked[sessionId] = expires;
                SaveRevoked();
            }
        }


        /// <summary>
        /// An Identity with the client address of a connection. It has no Authentication Type, so it is never treated as signed in
        /// </summary>
        public static ClaimsIdentity CreateConnectionIdentity(string clientAddress)
        {
            return new ClaimsIdentity(new[] { new Claim(_clientAddressClaim, clientAddress) });
        }

        /// <summary>
        /// Whether the Principal has an Admin Session (which may no longer be valid, see Validate)
        /// </summary>
        public static bool HasSession(ClaimsPrincipal principal)
        {
            return principal?.Identities?.Any(o => o.AuthenticationType == AuthenticationScheme) ?? false;
        }

        /// <summary>
        /// The Username of the Admin Account the Principal signed in as (null if it didn't). Not checked, see Validate
        /// </summary>
        public static string GetUsername(ClaimsPrincipal principal)
        {
            return principal?.Identities?.Select(o => o.FindFirst(_usernameClaim)?.Value).FirstOrDefault(o => o != null);
        }

        /// <summary>
        /// The client address of the connection the Principal is for (null if unknown)
        /// </summary>
        public static string GetClientAddress(ClaimsPrincipal principal)
        {
            return principal?.Identities?.Select(o => o.FindFirst(_clientAddressClaim)?.Value).FirstOrDefault(o => o != null);
        }


        private void LoadRevoked()
        {
            try
            {
                if (!File.Exists(_revokedPath)) return;

                var revoked = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(File.ReadAllText(_revokedPath));
                if (revoked == null) return;

                var now = _now();
                foreach (var entry in revoked.Where(o => o.Value > now)) _revoked[entry.Key] = entry.Value;
            }
            catch { } // A missing or unreadable file only means earlier sign outs aren't known (the Sessions still expire)
        }

        private void SaveRevoked()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_revokedPath));

                var tempPath = $"{_revokedPath}.{Guid.NewGuid():N}.tmp";
                File.WriteAllText(tempPath, JsonSerializer.Serialize(_revoked));
                File.Move(tempPath, _revokedPath, true);
            }
            catch { } // Still revoked until the Instance restarts
        }
    }
}
