// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Security.Cryptography;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// An Admin Role held by an Admin Account
    /// </summary>
    public class TrakHoundAdminRoleAssignment
    {
        public string RoleId { get; set; }

        /// <summary>
        /// The Zones the Role is limited to (everything when empty)
        /// </summary>
        public List<string> Zones { get; set; }


        public TrakHoundAdminRoleAssignment Clone()
        {
            return new TrakHoundAdminRoleAssignment { RoleId = RoleId, Zones = Zones?.ToList() };
        }
    }

    /// <summary>
    /// An account that signs in to the Admin Interface. Only a Hash of the Password is stored
    /// </summary>
    public class TrakHoundAdminAccount
    {
        public string Id { get; set; }

        public string Username { get; set; }

        /// <summary>
        /// The display name (the Username is shown if not set)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Admin Roles the Account holds. Its Permissions are those of all of them
        /// </summary>
        public List<TrakHoundAdminRoleAssignment> Assignments { get; set; }

        // Always serialized (YamlDotNet writes default values unless told to omit them), so a disabled Account stays disabled
        public bool Enabled { get; set; } = true;

        public DateTime Created { get; set; }

        public DateTime? PasswordChanged { get; set; }

        /// <summary>
        /// Changed whenever the Account's Password or Enabled change, which ends the Account's existing Sessions.
        /// Roles aren't kept in the Session, so changing them applies without ending it
        /// </summary>
        public string SessionStamp { get; set; }

        public string Salt { get; set; }

        /// <summary>
        /// The PBKDF2 Hash (hex) of the Password (see TrakHoundPasswordHash)
        /// </summary>
        public string Hash { get; set; }

        public int Iterations { get; set; }


        [YamlIgnore]
        public string DisplayName => !string.IsNullOrEmpty(Name) ? Name : Username;

        [YamlIgnore]
        public IEnumerable<string> RoleIds => Assignments?.Select(o => o.RoleId).Where(o => !string.IsNullOrEmpty(o)) ?? Enumerable.Empty<string>();

        [YamlIgnore]
        public bool IsOwner => Assignments != null && Assignments.Any(o => o != null && o.Zones.IsNullOrEmpty() && string.Equals(o.RoleId, TrakHoundAdminSecurity.OwnerRoleId, StringComparison.OrdinalIgnoreCase));


        public TrakHoundAdminAccount Clone()
        {
            var clone = (TrakHoundAdminAccount)MemberwiseClone();
            clone.Assignments = Assignments?.Select(o => o.Clone()).ToList();
            return clone;
        }
    }

    /// <summary>
    /// Stores the Admin Accounts (one file per Account in the "security/admins" directory of the Configuration Profile).
    /// The files are named by the Account's ID, so a Username is never used as a path.
    /// </summary>
    /// <remarks>
    /// The first Account can't be created from the Admin Interface (anyone who reached a new Instance first could claim it), only from
    /// the machine the Instance runs on (see TrakHoundAdminAccountCommand and TrakHoundStartupAdminUser). Those write the files from
    /// another process (or before the Instance's store is created), so the store reads the directory again when its files change
    /// </remarks>
    public sealed class TrakHoundAdminAccountStore
    {
        public const string DirectoryName = "admins";
        public const int UsernameMinLength = 4;
        public const int UsernameMaxLength = 100;
        public const int PasswordMinLength = 4;
        private const string _securityDirectory = "security";
        private const string _fileExtension = ".yaml";
        private static readonly TimeSpan _refreshInterval = TimeSpan.FromSeconds(2);

        // Hashed for unknown Usernames, so a sign in takes as long whether or not the Username exists
        private static readonly string _unknownSalt = TrakHoundPasswordHash.CreateSalt();

        private readonly string _directory;
        private readonly Dictionary<string, TrakHoundAdminAccount> _accounts = new Dictionary<string, TrakHoundAdminAccount>(); // Id => Account
        private readonly object _lock = new object();
        private string _signature;
        private long _refreshedTicks;


        public string Directory => _directory;

        /// <summary>
        /// Raised when an Account is created, changed, or deleted
        /// </summary>
        public event EventHandler Changed;


        public TrakHoundAdminAccountStore(string directory)
        {
            _directory = directory;
            Load();
        }

        /// <summary>
        /// The directory the Accounts of a Configuration Profile are kept in
        /// </summary>
        public static string GetDirectory(string configurationProfilePath)
        {
            return Path.Combine(configurationProfilePath, _securityDirectory, DirectoryName);
        }


        public void Load()
        {
            var accounts = new List<TrakHoundAdminAccount>();
            var signature = ReadSignature();

            if (System.IO.Directory.Exists(_directory))
            {
                var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

                foreach (var path in System.IO.Directory.GetFiles(_directory, $"*{_fileExtension}"))
                {
                    try
                    {
                        var account = deserializer.Deserialize<TrakHoundAdminAccount>(File.ReadAllText(path));
                        if (account != null && !string.IsNullOrEmpty(account.Id) && !string.IsNullOrEmpty(account.Username)) accounts.Add(account);
                    }
                    catch { }
                }
            }

            lock (_lock)
            {
                _accounts.Clear();
                foreach (var account in accounts) _accounts[account.Id] = account;
                _signature = signature;
                _refreshedTicks = Environment.TickCount64;
            }
        }

        /// <summary>
        /// Whether there is any Account. Until there is, the Admin Interface shows how to create one instead of the Login form
        /// </summary>
        public bool Any()
        {
            Refresh();
            lock (_lock) return _accounts.Count > 0;
        }

        public IEnumerable<TrakHoundAdminAccount> GetAccounts()
        {
            Refresh();
            lock (_lock) return _accounts.Values.OrderBy(o => o.Username, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public TrakHoundAdminAccount GetAccount(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;

            Refresh();
            lock (_lock) return _accounts.GetValueOrDefault(id);
        }

        /// <summary>
        /// Usernames match without regard to case
        /// </summary>
        public TrakHoundAdminAccount GetAccountByUsername(string username)
        {
            if (string.IsNullOrEmpty(username)) return null;

            Refresh();
            lock (_lock) return FindByUsername(username);
        }


        /// <summary>
        /// Returns the reason the Username can't be used (null if it can)
        /// </summary>
        public static string ValidateUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return "A Username is required";
            if (username != username.Trim()) return "The Username can't start or end with a space";
            if (username.Length < UsernameMinLength) return $"The Username must be a minimum of {UsernameMinLength} characters";
            if (username.Length > UsernameMaxLength) return $"The Username must be {UsernameMaxLength} characters or less";
            if (username.Any(char.IsControl)) return "The Username can't contain control characters";
            return null;
        }

        /// <summary>
        /// Returns the reason the Password can't be used (null if it can)
        /// </summary>
        public static string ValidatePassword(string password)
        {
            if (string.IsNullOrEmpty(password)) return "A Password is required";
            if (password.Length < PasswordMinLength) return $"The Password must be a minimum of {PasswordMinLength} characters";
            return null;
        }

        /// <summary>
        /// Returns the reason the Roles can't be used (null if they can)
        /// </summary>
        public static string ValidateRoles(IEnumerable<string> roleIds)
        {
            var ids = NormalizeRoles(roleIds);
            if (ids.Count == 0) return "At least one Role is required";

            var unknown = ids.FirstOrDefault(o => TrakHoundAdminSecurity.GetRole(o) == null);
            return unknown != null ? $"The Role '{unknown}' doesn't exist" : null;
        }

        /// <summary>
        /// Returns the reason the Roles can't be limited to the Zones given for them (null if they can). A Role is only limited to Zones
        /// when it has something that is about a Module (see TrakHoundAdminScope), and the Owner Role never is
        /// </summary>
        public static string ValidateZones(IDictionary<string, IEnumerable<string>> zones)
        {
            if (zones == null) return null;

            foreach (var entry in zones.Where(o => !NormalizeZones(o.Value).IsNullOrEmpty()))
            {
                var role = TrakHoundAdminSecurity.GetRole(entry.Key);
                if (role == null) continue;

                if (string.Equals(role.Id, TrakHoundAdminSecurity.OwnerRoleId, StringComparison.OrdinalIgnoreCase)) return "The Owner Role can't be limited to Zones";

                if (!TrakHoundAdminScope.Resources.Any(o => role.Allows(o, TrakHoundAdminSecurity.ReadPermission)))
                {
                    return $"The Role '{role.Name}' has nothing that can be limited to Zones (Modules, Services, Volumes, or App, Api, Function and Worker Configurations)";
                }
            }

            return null;
        }

        private static List<string> NormalizeZones(IEnumerable<string> zones)
        {
            var list = zones?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            return list.IsNullOrEmpty() ? null : list;
        }

        private static List<string> GetZones(IDictionary<string, IEnumerable<string>> zones, string roleId)
        {
            if (zones == null) return null;

            var entry = zones.FirstOrDefault(o => string.Equals(o.Key?.Trim(), roleId, StringComparison.OrdinalIgnoreCase));
            return NormalizeZones(entry.Value);
        }


        /// <summary>
        /// Creates an Account. Returns the reason it wasn't created (null when it was)
        /// </summary>
        /// <param name="zones">The Zones each Role is limited to, by Role ID (a Role that isn't listed applies everywhere)</param>
        public string Create(string username, string name, IEnumerable<string> roleIds, string password, IDictionary<string, IEnumerable<string>> zones = null)
        {
            var message = TrakHoundAdminContext.GetDenial(TrakHoundAdminSecurity.Admins, TrakHoundAdminSecurity.WritePermission);
            message ??= ValidateUsername(username) ?? ValidatePassword(password) ?? ValidateRoles(roleIds) ?? ValidateZones(zones);
            if (message != null) return message;

            Refresh();
            lock (_lock)
            {
                if (FindByUsername(username) != null) return $"The Username '{username}' is already used";

                var account = NewAccount(username, name, roleIds, password);
                foreach (var assignment in account.Assignments) assignment.Zones = GetZones(zones, assignment.RoleId);
                message = Write(account);
                if (message != null) return message;

                _accounts[account.Id] = account;
            }

            OnChanged();
            return null;
        }

        /// <summary>
        /// Creates an Owner with the Username, or (if the Username is already used) sets its Password, enables it and makes it an Owner.
        /// Only for the machine the Instance runs on (the set-admin command and the startup files) : it isn't checked against the signed in Admin.
        /// Returns the reason it wasn't written (null when it was)
        /// </summary>
        public string SetOwner(string username, string password, out bool created)
        {
            created = false;

            var message = ValidateUsername(username) ?? ValidatePassword(password);
            if (message != null) return message;

            Load();
            lock (_lock)
            {
                var existing = FindByUsername(username);
                created = existing == null;

                var account = existing?.Clone() ?? NewAccount(username, null, null, password);
                if (existing != null) SetPasswordHash(account, password);
                account.Enabled = true;
                account.SessionStamp = CreateSessionStamp();
                if (!account.IsOwner)
                {
                    account.Assignments ??= new List<TrakHoundAdminRoleAssignment>();
                    account.Assignments.RemoveAll(o => string.Equals(o?.RoleId, TrakHoundAdminSecurity.OwnerRoleId, StringComparison.OrdinalIgnoreCase));
                    account.Assignments.Insert(0, new TrakHoundAdminRoleAssignment { RoleId = TrakHoundAdminSecurity.OwnerRoleId });
                }

                message = Write(account);
                if (message != null) return message;

                _accounts[account.Id] = account;
            }

            OnChanged();
            return null;
        }

        /// <summary>
        /// Changes an Account's display name and Roles. Returns the reason it wasn't changed (null when it was)
        /// </summary>
        /// <param name="zones">The Zones each Role is limited to, by Role ID (a Role that isn't listed applies everywhere). When null, the Roles the Account already holds keep their Zones</param>
        public string Update(string id, string name, IEnumerable<string> roleIds, IDictionary<string, IEnumerable<string>> zones = null)
        {
            var message = TrakHoundAdminContext.GetDenial(TrakHoundAdminSecurity.Admins, TrakHoundAdminSecurity.WritePermission) ?? ValidateRoles(roleIds) ?? ValidateZones(zones);
            if (message != null) return message;

            var ids = NormalizeRoles(roleIds);

            return Change(id, account =>
            {
                var existing = account.Assignments ?? new List<TrakHoundAdminRoleAssignment>();
                account.Name = NormalizeName(name);
                account.Assignments = ids.Select(roleId =>
                {
                    var assignment = existing.FirstOrDefault(o => string.Equals(o.RoleId, roleId, StringComparison.OrdinalIgnoreCase))?.Clone() ?? new TrakHoundAdminRoleAssignment { RoleId = roleId };
                    if (zones != null) assignment.Zones = GetZones(zones, roleId);
                    return assignment;
                }).ToList();
            });
        }

        /// <summary>
        /// Enables or disables an Account. Disabling ends the Account's Sessions. Returns the reason it wasn't changed (null when it was)
        /// </summary>
        public string SetEnabled(string id, bool enabled)
        {
            var message = TrakHoundAdminContext.GetDenial(TrakHoundAdminSecurity.Admins, TrakHoundAdminSecurity.WritePermission);
            if (message != null) return message;

            return Change(id, account =>
            {
                if (account.Enabled != enabled) account.SessionStamp = CreateSessionStamp();
                account.Enabled = enabled;
            });
        }

        /// <summary>
        /// Sets an Account's Password, which ends the Account's Sessions. An Admin can always set their own.
        /// Returns the reason it wasn't set (null when it was)
        /// </summary>
        public string SetPassword(string id, string password)
        {
            var message = TrakHoundAdminContext.Current?.AccountId == id ? null : TrakHoundAdminContext.GetDenial(TrakHoundAdminSecurity.Admins, TrakHoundAdminSecurity.WritePermission);
            message ??= ValidatePassword(password);
            if (message != null) return message;

            return Change(id, account =>
            {
                SetPasswordHash(account, password);
                account.SessionStamp = CreateSessionStamp();
            });
        }

        /// <summary>
        /// Ends an Account's Sessions (they sign in again). Returns the reason they weren't ended (null when they were)
        /// </summary>
        public string EndSessions(string id)
        {
            var message = TrakHoundAdminContext.GetDenial(TrakHoundAdminSecurity.Admins, TrakHoundAdminSecurity.WritePermission);
            if (message != null) return message;

            return Change(id, account => account.SessionStamp = CreateSessionStamp());
        }

        /// <summary>
        /// Deletes an Account. Returns the reason it wasn't deleted (null when it was)
        /// </summary>
        public string Delete(string id)
        {
            var message = TrakHoundAdminContext.GetDenial(TrakHoundAdminSecurity.Admins, TrakHoundAdminSecurity.WritePermission);
            if (message != null) return message;

            Refresh();
            lock (_lock)
            {
                var existing = _accounts.GetValueOrDefault(id ?? "");
                if (existing == null) return "Admin Account Not Found";
                if (IsLastOwner(existing)) return "This is the only enabled Owner, so it can't be deleted";

                try
                {
                    var path = GetPath(id);
                    if (File.Exists(path)) File.Delete(path);
                }
                catch (Exception ex)
                {
                    return $"The Admin Account could not be deleted : {ex.Message}";
                }

                _accounts.Remove(id);
                _signature = ReadSignature();
            }

            OnChanged();
            return null;
        }


        /// <summary>
        /// Returns the Account if the Username and Password match an enabled Account (null otherwise)
        /// </summary>
        public TrakHoundAdminAccount Verify(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return null;

            var account = GetAccountByUsername(username);
            if (account == null)
            {
                TrakHoundPasswordHash.Hash(password, _unknownSalt, TrakHoundPasswordHash.Iterations);
                return null;
            }

            var hash = TrakHoundPasswordHash.Hash(password, account.Salt, account.Iterations);
            if (!TrakHoundPasswordHash.IsMatch(hash, account.Hash) || !account.Enabled) return null;

            return account;
        }

        /// <summary>
        /// Whether a Session created for the Account is still valid (the Account exists, is enabled, and its Session Stamp hasn't changed)
        /// </summary>
        public bool IsSessionValid(string id, string sessionStamp)
        {
            var account = GetAccount(id);
            return account != null && account.Enabled && sessionStamp != null && string.Equals(account.SessionStamp, sessionStamp, StringComparison.Ordinal);
        }


        // Changes a copy of the Account, so a failed write leaves the stored Account unchanged (and readers never see a partial change)
        private string Change(string id, Action<TrakHoundAdminAccount> change)
        {
            Refresh();
            lock (_lock)
            {
                var existing = _accounts.GetValueOrDefault(id ?? "");
                if (existing == null) return "Admin Account Not Found";

                var account = existing.Clone();
                change(account);

                // There must always be an Owner who can sign in, or the Admin Accounts could only be managed with the set-admin command
                if (IsLastOwner(existing) && (!account.IsOwner || !account.Enabled)) return "This is the only enabled Owner, so it must stay an enabled Owner";

                var message = Write(account);
                if (message != null) return message;

                _accounts[account.Id] = account;
            }

            OnChanged();
            return null;
        }

        private bool IsLastOwner(TrakHoundAdminAccount account)
        {
            return account.IsOwner && account.Enabled && !_accounts.Values.Any(o => o.Id != account.Id && o.IsOwner && o.Enabled);
        }

        private TrakHoundAdminAccount FindByUsername(string username)
        {
            return _accounts.Values.FirstOrDefault(o => string.Equals(o.Username, username, StringComparison.OrdinalIgnoreCase));
        }

        private static TrakHoundAdminAccount NewAccount(string username, string name, IEnumerable<string> roleIds, string password)
        {
            var account = new TrakHoundAdminAccount();
            account.Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            account.Username = username;
            account.Name = NormalizeName(name);
            account.Assignments = NormalizeRoles(roleIds).Select(o => new TrakHoundAdminRoleAssignment { RoleId = o }).ToList();
            account.Created = DateTime.UtcNow;
            account.SessionStamp = CreateSessionStamp();
            SetPasswordHash(account, password);
            return account;
        }

        // Written to a temporary file first (readable only by its owner on Linux and macOS), so a failed write never leaves a partial file
        private string Write(TrakHoundAdminAccount account)
        {
            var path = GetPath(account.Id);
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                // RestrictDirectory creates the directory only on Linux and macOS
                System.IO.Directory.CreateDirectory(_directory);
                TrakHoundFilePermissions.RestrictDirectory(_directory);

                var serializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
                using (var stream = new FileStream(tempPath, TrakHoundFilePermissions.CreateNewOptions()))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(serializer.Serialize(account));
                }

                File.Move(tempPath, path, true);
                _signature = ReadSignature();
                return null;
            }
            catch (Exception ex)
            {
                try { File.Delete(tempPath); } catch { }
                return $"The Admin Account could not be saved : {ex.Message}";
            }
        }

        // Reads the directory again if its files changed (ex. the set-admin command was run while the Instance is running). Checked at most
        // every few seconds, since Accounts are read on every Admin Interface event
        private void Refresh()
        {
            lock (_lock)
            {
                var now = Environment.TickCount64;
                if (now - _refreshedTicks < _refreshInterval.TotalMilliseconds) return;
                _refreshedTicks = now;

                if (ReadSignature() == _signature) return;
            }

            Load();
            OnChanged();
        }

        private string ReadSignature()
        {
            try
            {
                if (!System.IO.Directory.Exists(_directory)) return "";

                var files = new DirectoryInfo(_directory).GetFiles($"*{_fileExtension}").OrderBy(o => o.Name, StringComparer.Ordinal);
                return string.Join('|', files.Select(o => $"{o.Name}:{o.LastWriteTimeUtc.Ticks}:{o.Length}"));
            }
            catch
            {
                return "";
            }
        }

        private static void SetPasswordHash(TrakHoundAdminAccount account, string password)
        {
            account.Salt = TrakHoundPasswordHash.CreateSalt();
            account.Iterations = TrakHoundPasswordHash.Iterations;
            account.Hash = TrakHoundPasswordHash.Hash(password, account.Salt, account.Iterations);
            account.PasswordChanged = DateTime.UtcNow;
        }

        private static string CreateSessionStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        private static string NormalizeName(string name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        private static List<string> NormalizeRoles(IEnumerable<string> roleIds)
        {
            return roleIds?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim().ToLowerInvariant()).Distinct().ToList() ?? new List<string>();
        }

        /// <summary>
        /// The file an Account is kept in (ex. to record a change to it in the Audit Log)
        /// </summary>
        public string GetAccountPath(string id) => GetPath(id);

        private string GetPath(string id)
        {
            // IDs are created by the store (hex), but a file could have been edited, so never let one leave the directory
            if (string.IsNullOrEmpty(id) || id.Any(o => !char.IsAsciiLetterOrDigit(o))) throw new ArgumentException("Invalid Admin Account ID");
            return Path.Combine(_directory, $"{id}{_fileExtension}");
        }

        private void OnChanged()
        {
            try { Changed?.Invoke(this, EventArgs.Empty); } catch { }
        }
    }
}
