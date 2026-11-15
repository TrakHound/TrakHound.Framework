// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;
using TrakHound.Clients;
using TrakHound.Logging;
using TrakHound.Security;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Ldap.Identity
{
    /// <summary>
    /// Signs in with a Username and Password (entered on the Instance's sign in page) verified by an LDAP server (ex. Active Directory).
    /// Group memberships are used as Roles.
    /// </summary>
    /// <remarks>
    /// Parameters:
    /// server (required) : The LDAP server host name
    /// port : The port (default 636 when useSsl, otherwise 389)
    /// useSsl : Connect with LDAPS (default false)
    /// startTls : Upgrade the connection with StartTLS (default false). Without useSsl or startTls the password is sent in clear text
    /// bindDnFormat : Bind directly as the User (ex. "{username}@corp.example.com" or "uid={username},ou=people,dc=example,dc=com")
    /// serviceBindDn, serviceBindPassword : Bind as a service account to search for the User's DN first (used instead of bindDnFormat)
    /// searchBase : The DN to search for Users under (required with serviceBindDn, optional with bindDnFormat to read Groups)
    /// userFilter : The search filter for the User (default "(sAMAccountName={username})")
    /// groupAttribute : The attribute listing the User's Groups (default "memberOf")
    /// nameAttribute : The attribute for the display name (default "displayName")
    /// roleFormat : "cn" to use the Group's CN as the Role (default) or "dn" to use the full DN
    /// </remarks>
    public class Module : TrakHoundIdentityProviderBase
    {
        private const int _invalidCredentialsErrorCode = 49;
        private static readonly TimeSpan _timeout = TimeSpan.FromSeconds(15);

        private readonly string _server;
        private readonly int _port;
        private readonly bool _useSsl;
        private readonly bool _startTls;
        private readonly string _bindDnFormat;
        private readonly string _serviceBindDn;
        private readonly string _serviceBindPassword;
        private readonly string _searchBase;
        private readonly string _userFilter;
        private readonly string _groupAttribute;
        private readonly string _nameAttribute;
        private readonly bool _roleAsDn;
        private readonly ITrakHoundLogger _logger;


        public override TrakHoundIdentityProviderFeatures Features => TrakHoundIdentityProviderFeatures.Credentials;


        public Module(ITrakHoundIdentityProviderConfiguration configuration, ITrakHoundClient client, ITrakHoundVolume volume, ITrakHoundVariableCollection variables) : base(configuration, client, volume, variables)
        {
            _logger = new TrakHoundLogger($"identity/{configuration?.Id}");

            _server = configuration.GetParameter("server");
            _useSsl = IsTrue(configuration.GetParameter("useSsl"));
            _startTls = IsTrue(configuration.GetParameter("startTls"));
            _port = int.TryParse(configuration.GetParameter("port"), out var port) && port > 0 ? port : (_useSsl ? 636 : 389);
            _bindDnFormat = configuration.GetParameter("bindDnFormat");
            _serviceBindDn = configuration.GetParameter("serviceBindDn");
            _serviceBindPassword = configuration.GetParameter("serviceBindPassword");
            _searchBase = configuration.GetParameter("searchBase");
            _userFilter = configuration.GetParameter("userFilter") ?? "(sAMAccountName={username})";
            _groupAttribute = configuration.GetParameter("groupAttribute") ?? "memberOf";
            _nameAttribute = configuration.GetParameter("nameAttribute") ?? "displayName";
            _roleAsDn = string.Equals(configuration.GetParameter("roleFormat"), "dn", StringComparison.OrdinalIgnoreCase);

            if (!_useSsl && !_startTls) _logger.LogWarning("useSsl and startTls are both disabled : Passwords are sent to the LDAP server in clear text");
        }


        protected override Task<TrakHoundIdentityResult> OnSignIn(TrakHoundIdentityContext context, TrakHoundIdentityCredentials credentials)
        {
            // An empty password performs an anonymous ("unauthenticated") bind, which many servers accept. Never treat that as a sign in
            if (string.IsNullOrEmpty(credentials?.Username) || string.IsNullOrEmpty(credentials.Password)) return Task.FromResult(TrakHoundIdentityResult.Fail("Username and Password are required"));
            if (credentials.Username.Any(char.IsControl)) return Task.FromResult(TrakHoundIdentityResult.Fail("Invalid Username"));

            if (string.IsNullOrEmpty(_server)) return Task.FromResult(TrakHoundIdentityResult.Error("The server Parameter is required"));
            if (string.IsNullOrEmpty(_serviceBindDn) && string.IsNullOrEmpty(_bindDnFormat)) return Task.FromResult(TrakHoundIdentityResult.Error("Either the bindDnFormat or serviceBindDn Parameter is required"));
            if (!string.IsNullOrEmpty(_serviceBindDn) && string.IsNullOrEmpty(_searchBase)) return Task.FromResult(TrakHoundIdentityResult.Error("The searchBase Parameter is required with serviceBindDn"));

            // System.DirectoryServices.Protocols is synchronous
            return Task.Run(() => SignIn(credentials.Username.Trim(), credentials.Password));
        }

        private TrakHoundIdentityResult SignIn(string username, string password)
        {
            try
            {
                SearchResultEntry userEntry = null;

                if (!string.IsNullOrEmpty(_serviceBindDn))
                {
                    // Find the User's DN with the service account, then verify the password by binding as the User
                    using (var serviceConnection = Connect())
                    {
                        serviceConnection.Bind(new NetworkCredential(_serviceBindDn, _serviceBindPassword));

                        var entries = Search(serviceConnection, username);
                        if (entries.Count != 1) return TrakHoundIdentityResult.Fail(entries.Count < 1 ? "User Not Found" : "More than one User matches the userFilter");
                        userEntry = entries[0];
                    }

                    using var userConnection = Connect();
                    userConnection.Bind(new NetworkCredential(userEntry.DistinguishedName, password));
                }
                else
                {
                    // Bind directly as the User, then read the User's attributes (if a searchBase is configured)
                    using var userConnection = Connect();
                    userConnection.Bind(new NetworkCredential(FormatBindDn(username), password));

                    if (!string.IsNullOrEmpty(_searchBase))
                    {
                        var entries = Search(userConnection, username);
                        if (entries.Count == 1) userEntry = entries[0];
                    }
                }

                var identity = new TrakHoundIdentity();
                identity.ProviderId = Id;
                identity.UserId = username;
                identity.Name = GetAttributeValues(userEntry, _nameAttribute).FirstOrDefault() ?? username;
                identity.Type = TrakHoundIdentityUserType.Origin;
                identity.Roles = GetAttributeValues(userEntry, _groupAttribute).Select(GetRole).Where(o => !string.IsNullOrEmpty(o)).Distinct().ToList();

                var properties = new Dictionary<string, string>();
                if (userEntry != null) properties["dn"] = userEntry.DistinguishedName;
                var email = GetAttributeValues(userEntry, "mail").FirstOrDefault();
                if (email != null) properties["email"] = email;
                identity.Properties = properties;

                return TrakHoundIdentityResult.Success(identity);
            }
            catch (LdapException ex) when (ex.ErrorCode == _invalidCredentialsErrorCode)
            {
                return TrakHoundIdentityResult.Fail("Invalid Credentials");
            }
            catch (LdapException ex)
            {
                _logger.LogError($"LDAP Error ({ex.ErrorCode}) : {ex.Message} : {ex.ServerErrorMessage}");
                return TrakHoundIdentityResult.Error($"LDAP Error ({ex.ErrorCode}) : {ex.Message}");
            }
        }

        private LdapConnection Connect()
        {
            var connection = new LdapConnection(new LdapDirectoryIdentifier(_server, _port));
            connection.AuthType = AuthType.Basic;
            connection.Timeout = _timeout;
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.ReferralChasing = ReferralChasingOptions.None;

            if (_useSsl) connection.SessionOptions.SecureSocketLayer = true;
            if (_startTls) connection.SessionOptions.StartTransportLayerSecurity(null);

            return connection;
        }

        private List<SearchResultEntry> Search(LdapConnection connection, string username)
        {
            var filter = _userFilter.Replace("{username}", EscapeFilterValue(username));
            var request = new SearchRequest(_searchBase, filter, SearchScope.Subtree, _groupAttribute, _nameAttribute, "mail");
            request.SizeLimit = 2;

            var response = (SearchResponse)connection.SendRequest(request);
            return response.Entries.Cast<SearchResultEntry>().ToList();
        }

        private string FormatBindDn(string username)
        {
            // A DN format (ex. "uid={username},ou=people,...") needs the Username escaped so it can't change the DN's structure
            var value = _bindDnFormat.Contains('=') ? EscapeDnValue(username) : username;
            return _bindDnFormat.Replace("{username}", value);
        }

        private string GetRole(string groupDn)
        {
            if (_roleAsDn) return groupDn;

            // The first RDN value (ex. "CN=Operators,OU=Groups,DC=corp" => "Operators")
            var rdn = SplitFirstRdn(groupDn);
            var separatorIndex = rdn.IndexOf('=');
            return separatorIndex > -1 ? UnescapeDnValue(rdn.Substring(separatorIndex + 1).Trim()) : groupDn;
        }

        private static string SplitFirstRdn(string dn)
        {
            for (var i = 0; i < dn.Length; i++)
            {
                if (dn[i] == '\\') i++; // Skip escaped characters
                else if (dn[i] == ',' || dn[i] == '+') return dn.Substring(0, i);
            }

            return dn;
        }

        private static IEnumerable<string> GetAttributeValues(SearchResultEntry entry, string attributeName)
        {
            if (entry != null && !string.IsNullOrEmpty(attributeName) && entry.Attributes.Contains(attributeName))
            {
                return entry.Attributes[attributeName].GetValues(typeof(string)).Cast<string>().ToList();
            }

            return Enumerable.Empty<string>();
        }

        // RFC 4515 (prevents LDAP Filter injection)
        private static string EscapeFilterValue(string value)
        {
            var builder = new StringBuilder();
            foreach (var c in value)
            {
                switch (c)
                {
                    case '\\': builder.Append(@"\5c"); break;
                    case '*': builder.Append(@"\2a"); break;
                    case '(': builder.Append(@"\28"); break;
                    case ')': builder.Append(@"\29"); break;
                    case '\0': builder.Append(@"\00"); break;
                    default: builder.Append(c); break;
                }
            }

            return builder.ToString();
        }

        // RFC 4514 (prevents DN injection)
        private static string EscapeDnValue(string value)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (c == ',' || c == '+' || c == '"' || c == '\\' || c == '<' || c == '>' || c == ';' || c == '=' ||
                    (c == '#' && i == 0) || (c == ' ' && (i == 0 || i == value.Length - 1)))
                {
                    builder.Append('\\');
                }
                builder.Append(c);
            }

            return builder.ToString();
        }

        private static string UnescapeDnValue(string value)
        {
            var builder = new StringBuilder();
            for (var i = 0; i < value.Length; i++)
            {
                if (value[i] == '\\' && i + 1 < value.Length) i++;
                builder.Append(value[i]);
            }

            return builder.ToString();
        }

        private static bool IsTrue(string value) => string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
    }
}
