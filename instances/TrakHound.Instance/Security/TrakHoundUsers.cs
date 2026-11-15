// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Security.Cryptography;
using TrakHound.Clients;
using TrakHound.Instance.Http;
using TrakHound.Instance.Security;
using TrakHound.Volumes;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.Security
{
    /// <summary>
    /// A User of the built in Users Identity Provider. Only a Hash of the Password is stored
    /// </summary>
    public class TrakHoundUser
    {
        public string Id { get; set; }

        public string Username { get; set; }

        /// <summary>
        /// The display name (the Username is shown if not set)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// The Roles of the Identity created when the User signs in (mapped to Permissions by Security Profile Assignments)
        /// </summary>
        public List<string> Roles { get; set; }

        // Always serialized (YamlDotNet writes default values unless told to omit them), so a disabled User stays disabled
        public bool Enabled { get; set; } = true;

        public DateTime Created { get; set; }

        public DateTime? PasswordChanged { get; set; }

        /// <summary>
        /// Changed whenever the User's Password, Roles, or Enabled change, which ends the User's existing Sessions
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


        public TrakHoundUser Clone()
        {
            var clone = (TrakHoundUser)MemberwiseClone();
            clone.Roles = Roles?.ToList();
            return clone;
        }
    }

    /// <summary>
    /// How the built in Users Identity Provider's sign in page looks: the same Parameters a configured Identity Provider uses (see TrakHoundSignInPage)
    /// </summary>
    public class TrakHoundUserSettings
    {
        public const int ProductMaxLength = 100;

        /// <summary>
        /// The route of a public App Page to sign in on (ex. /sign-in). The Instance's own form is used if not set (or the Page can't be used)
        /// </summary>
        public string SignInRoute { get; set; }

        /// <summary>
        /// A product name shown above the Instance's own form
        /// </summary>
        public string SignInProduct { get; set; }

        /// <summary>
        /// A logo shown above the Instance's own form : a path in the Instance that doesn't require signing in (ex. _init/assets/logo.svg)
        /// </summary>
        public string SignInLogo { get; set; }


        /// <summary>
        /// Returns the reason the Settings can't be used (null if they can)
        /// </summary>
        public string Validate()
        {
            if (!IsLocalPath(SignInRoute)) return "The Sign In Route must be a route in this Instance (ex. /sign-in)";
            if (!IsLocalPath(SignInLogo)) return "The Logo must be a path in this Instance (ex. _init/assets/logo.svg)";
            if (SignInProduct != null && SignInProduct.Length > ProductMaxLength) return $"The Product Name must be {ProductMaxLength} characters or less";
            if (SignInProduct != null && SignInProduct.Any(char.IsControl)) return "The Product Name can't contain control characters";
            return null;
        }

        // Both are used as paths in the Instance (the base path is added), so a scheme, a host ("//") or characters that change what's loaded aren't allowed
        private static bool IsLocalPath(string value)
        {
            if (string.IsNullOrEmpty(value)) return true;
            return !value.Contains("://") && !value.StartsWith("//") && !value.Contains('\\') && !value.Any(o => char.IsControl(o) || char.IsWhiteSpace(o) || o == '"' || o == '<' || o == '>');
        }

        /// <summary>
        /// A copy with blank values removed, the Route starting with '/', and the Logo without one. Only one '/' is added or removed,
        /// so a value starting with "//" (another host) is still rejected by Validate
        /// </summary>
        public TrakHoundUserSettings Normalize()
        {
            var settings = new TrakHoundUserSettings();

            var route = SignInRoute?.Trim();
            settings.SignInRoute = string.IsNullOrEmpty(route) ? null : (route.StartsWith('/') ? route : "/" + route);

            settings.SignInProduct = string.IsNullOrWhiteSpace(SignInProduct) ? null : SignInProduct.Trim();

            var logo = SignInLogo?.Trim();
            if (logo != null && logo.StartsWith('/') && !logo.StartsWith("//")) logo = logo.Substring(1);
            settings.SignInLogo = string.IsNullOrEmpty(logo) ? null : logo;

            return settings;
        }
    }

    /// <summary>
    /// Stores the Users of the built in Users Identity Provider (one file per User in the "security/users" directory of the Configuration Profile).
    /// The files are named by the User's ID, so a Username is never used as a path. The provider's Settings are in "_settings.yaml" there
    /// </summary>
    public sealed class TrakHoundUserStore
    {
        public const int UsernameMaxLength = 100;
        public const int PasswordMinLength = 8;
        private const string _fileExtension = ".yaml";
        private const string _settingsFilename = "_settings.yaml"; // User IDs are only letters and numbers, so this is never a User's file

        // Hashed for unknown Usernames, so a sign in takes as long whether or not the Username exists
        private static readonly string _unknownUserSalt = TrakHoundPasswordHash.CreateSalt();

        private readonly string _directory;
        private readonly Dictionary<string, TrakHoundUser> _users = new Dictionary<string, TrakHoundUser>(); // Id => User
        private readonly object _lock = new object();
        private volatile TrakHoundUserSettings _settings = new TrakHoundUserSettings();


        /// <summary>
        /// The provider's Settings (replaced, never modified, when they change)
        /// </summary>
        public TrakHoundUserSettings Settings => _settings;

        /// <summary>
        /// Raised when a User is created, changed, or deleted, or the Settings change
        /// </summary>
        public event EventHandler Changed;


        public TrakHoundUserStore(string directory)
        {
            _directory = directory;
        }


        public void Load()
        {
            var users = new List<TrakHoundUser>();

            if (Directory.Exists(_directory))
            {
                var deserializer = new DeserializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).IgnoreUnmatchedProperties().Build();

                try
                {
                    var settingsPath = Path.Combine(_directory, _settingsFilename);
                    if (File.Exists(settingsPath)) _settings = (deserializer.Deserialize<TrakHoundUserSettings>(File.ReadAllText(settingsPath)) ?? new TrakHoundUserSettings()).Normalize();
                }
                catch { }

                foreach (var path in Directory.GetFiles(_directory, $"*{_fileExtension}"))
                {
                    if (string.Equals(Path.GetFileName(path), _settingsFilename, StringComparison.OrdinalIgnoreCase)) continue;

                    try
                    {
                        var user = deserializer.Deserialize<TrakHoundUser>(File.ReadAllText(path));
                        if (user != null && !string.IsNullOrEmpty(user.Id) && !string.IsNullOrEmpty(user.Username)) users.Add(user);
                    }
                    catch { }
                }
            }

            lock (_lock)
            {
                _users.Clear();
                foreach (var user in users) _users[user.Id] = user;
            }
        }

        public IEnumerable<TrakHoundUser> GetUsers()
        {
            lock (_lock) return _users.Values.OrderBy(o => o.Username, StringComparer.OrdinalIgnoreCase).ToList();
        }

        public TrakHoundUser GetUser(string id)
        {
            if (string.IsNullOrEmpty(id)) return null;
            lock (_lock) return _users.GetValueOrDefault(id);
        }

        /// <summary>
        /// Usernames match without regard to case
        /// </summary>
        public TrakHoundUser GetUserByUsername(string username)
        {
            if (string.IsNullOrEmpty(username)) return null;
            lock (_lock) return _users.Values.FirstOrDefault(o => string.Equals(o.Username, username, StringComparison.OrdinalIgnoreCase));
        }


        /// <summary>
        /// Returns the reason the Username can't be used (null if it can)
        /// </summary>
        public static string ValidateUsername(string username)
        {
            if (string.IsNullOrWhiteSpace(username)) return "A Username is required";
            if (username != username.Trim()) return "The Username can't start or end with a space";
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
        /// Creates a User. Returns the reason it wasn't created (null when it was)
        /// </summary>
        public string Create(string username, string name, IEnumerable<string> roles, string password)
        {
            var message = GetDenial() ?? GetRoleDenial(NormalizeRoles(roles)) ?? ValidateUsername(username) ?? ValidatePassword(password);
            if (message != null) return message;

            var user = new TrakHoundUser();
            user.Id = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            user.Username = username;
            user.Name = NormalizeName(name);
            user.Roles = NormalizeRoles(roles);
            user.Created = DateTime.UtcNow;
            user.SessionStamp = CreateSessionStamp();
            SetPasswordHash(user, password);

            lock (_lock)
            {
                if (GetUserByUsername(username) != null) return $"The Username '{username}' is already used";

                message = Write(user);
                if (message != null) return message;

                _users[user.Id] = user;
            }

            OnChanged();
            return null;
        }

        /// <summary>
        /// Changes a User's display name and Roles. Returns the reason it wasn't changed (null when it was)
        /// </summary>
        public string Update(string id, string name, IEnumerable<string> roles)
        {
            // The Roles being given (the Roles the User already has are checked for every change)
            var message = GetRoleDenial(NormalizeRoles(roles));
            if (message != null) return message;

            return Change(id, user =>
            {
                var newRoles = NormalizeRoles(roles);

                // Roles are kept in the Session, so a change ends the User's Sessions (they sign in again with the new Roles)
                if (!(user.Roles ?? new List<string>()).SequenceEqual(newRoles ?? new List<string>())) user.SessionStamp = CreateSessionStamp();

                user.Name = NormalizeName(name);
                user.Roles = newRoles;
            });
        }

        /// <summary>
        /// Enables or disables a User. Disabling ends the User's Sessions. Returns the reason it wasn't changed (null when it was)
        /// </summary>
        public string SetEnabled(string id, bool enabled)
        {
            return Change(id, user =>
            {
                if (user.Enabled != enabled) user.SessionStamp = CreateSessionStamp();
                user.Enabled = enabled;
            });
        }

        /// <summary>
        /// Sets a User's Password, which ends the User's Sessions. Returns the reason it wasn't set (null when it was)
        /// </summary>
        public string SetPassword(string id, string password)
        {
            var message = GetDenial() ?? ValidatePassword(password);
            if (message != null) return message;

            return Change(id, user =>
            {
                SetPasswordHash(user, password);
                user.SessionStamp = CreateSessionStamp();
            });
        }

        /// <summary>
        /// Ends a User's Sessions (they sign in again). Returns the reason they weren't ended (null when they were)
        /// </summary>
        public string EndSessions(string id)
        {
            return Change(id, user => user.SessionStamp = CreateSessionStamp());
        }

        /// <summary>
        /// Ends every User's Sessions. Returns the reason any weren't ended (null when all were)
        /// </summary>
        public string EndAllSessions()
        {
            var denial = GetDenial();
            if (denial != null) return denial;

            var failed = new List<string>();

            lock (_lock)
            {
                foreach (var existing in _users.Values.ToList())
                {
                    var user = existing.Clone();
                    user.SessionStamp = CreateSessionStamp();

                    if (Write(user) == null) _users[user.Id] = user;
                    else failed.Add(user.Username);
                }
            }

            OnChanged();
            return failed.Count > 0 ? $"These Users could not be saved, so they are still signed in : {string.Join(", ", failed)}" : null;
        }

        public void Delete(string id)
        {
            TrakHoundAdminContext.DemandWrite(TrakHoundAdminSecurity.Users);
            if (GetRoleDenial(GetUser(id)?.Roles) != null) throw new TrakHoundAdminAccessException(TrakHoundAdminSecurity.InstanceSecurityProfiles, TrakHoundAdminSecurity.WritePermission);

            if (!string.IsNullOrEmpty(id))
            {
                lock (_lock)
                {
                    _users.Remove(id);

                    var path = GetPath(id);
                    if (File.Exists(path)) File.Delete(path);
                }

                OnChanged();
            }
        }


        /// <summary>
        /// Saves the provider's Settings. Returns the reason they weren't saved (null when they were)
        /// </summary>
        public string SaveSettings(TrakHoundUserSettings settings)
        {
            var normalized = (settings ?? new TrakHoundUserSettings()).Normalize();

            var message = GetDenial() ?? normalized.Validate();
            if (message != null) return message;

            lock (_lock)
            {
                message = WriteFile(Path.Combine(_directory, _settingsFilename), normalized);
                if (message != null) return message;

                _settings = normalized;
            }

            OnChanged();
            return null;
        }


        /// <summary>
        /// Returns the User if the Username and Password match an enabled User (null otherwise)
        /// </summary>
        public TrakHoundUser Verify(string username, string password)
        {
            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password)) return null;

            var user = GetUserByUsername(username);
            if (user == null)
            {
                TrakHoundPasswordHash.Hash(password, _unknownUserSalt, TrakHoundPasswordHash.Iterations);
                return null;
            }

            var hash = TrakHoundPasswordHash.Hash(password, user.Salt, user.Iterations);
            if (!TrakHoundPasswordHash.IsMatch(hash, user.Hash) || !user.Enabled) return null;

            // Hashes made with fewer Iterations are upgraded (the Password is only known now). The Session Stamp is kept, so other Sessions continue
            if (user.Iterations < TrakHoundPasswordHash.Iterations)
            {
                Change(user.Id, o => SetPasswordHash(o, password), false, false);
            }

            return GetUser(user.Id);
        }

        /// <summary>
        /// Whether a Session created for the User is still valid (the User exists, is enabled, and its Session Stamp hasn't changed)
        /// </summary>
        public bool IsSessionValid(string username, string sessionStamp)
        {
            var user = GetUserByUsername(username);
            return user != null && user.Enabled && sessionStamp != null && string.Equals(user.SessionStamp, sessionStamp, StringComparison.Ordinal);
        }


        // Changes a copy of the User, so a failed write leaves the stored User unchanged (and readers never see a partial change)
        // Users are only changed from the Admin Interface, so each change is checked against the signed in Admin's Roles
        private static string GetDenial() => TrakHoundAdminContext.GetDenial(TrakHoundAdminSecurity.Users, TrakHoundAdminSecurity.WritePermission);

        /// <summary>
        /// Whether a User with the Roles gets access to the System API or Services from a Security Profile (set by the Security Manager, which has the Profiles)
        /// </summary>
        public Func<IEnumerable<string>, bool> ReachesInstance { get; set; }

        /// <summary>
        /// Returns why the signed in Admin can't give a User the Roles, or change a User who has them (null when they can). An Admin who
        /// manages Users can't hand out (or take over, ex. by setting a Password) access to the System API or Services unless they can
        /// also change the Profiles that give it
        /// </summary>
        public string GetRoleDenial(IEnumerable<string> roles)
        {
            if (TrakHoundAdminContext.Current == null) return null;

            // The Admin's own Roles can limit which Roles they give
            var denial = TrakHoundAdminContext.GetGrantDenial(TrakHoundAdminSecurity.Users, roles);
            if (denial != null) return denial;

            if (ReachesInstance == null || !ReachesInstance(roles ?? Enumerable.Empty<string>())) return null;

            return TrakHoundAdminContext.IsAllowed(TrakHoundAdminSecurity.InstanceSecurityProfiles, TrakHoundAdminSecurity.WritePermission) ? null : TrakHoundAdminSecurity.InstanceRoleDeniedMessage;
        }

        // "checkAdmin" is false for a change the store makes itself (upgrading a Password Hash when a User signs in)
        private string Change(string id, Action<TrakHoundUser> change, bool raiseChanged = true, bool checkAdmin = true)
        {
            var denial = checkAdmin ? GetDenial() : null;
            if (denial != null) return denial;

            lock (_lock)
            {
                var existing = GetUser(id);
                if (existing == null) return "User Not Found";

                denial = checkAdmin ? GetRoleDenial(existing.Roles) : null;
                if (denial != null) return denial;

                var user = existing.Clone();
                change(user);

                var message = Write(user);
                if (message != null) return message;

                _users[user.Id] = user;
            }

            if (raiseChanged) OnChanged();
            return null;
        }

        private string Write(TrakHoundUser user) => WriteFile(GetPath(user.Id), user, "The User");

        // Written to a temporary file first (readable only by its owner on Linux and macOS), so a failed write never leaves a partial file
        private string WriteFile(string path, object value, string description = "The Settings")
        {
            var tempPath = $"{path}.{Guid.NewGuid():N}.tmp";
            try
            {
                // RestrictDirectory creates the directory only on Linux and macOS
                Directory.CreateDirectory(_directory);
                TrakHoundFilePermissions.RestrictDirectory(_directory);

                var serializer = new SerializerBuilder().WithNamingConvention(CamelCaseNamingConvention.Instance).Build();
                using (var stream = new FileStream(tempPath, TrakHoundFilePermissions.CreateNewOptions()))
                using (var writer = new StreamWriter(stream))
                {
                    writer.Write(serializer.Serialize(value));
                }

                File.Move(tempPath, path, true);
                return null;
            }
            catch (Exception ex)
            {
                try { File.Delete(tempPath); } catch { }
                return $"{description} could not be saved : {ex.Message}";
            }
        }

        private static void SetPasswordHash(TrakHoundUser user, string password)
        {
            user.Salt = TrakHoundPasswordHash.CreateSalt();
            user.Iterations = TrakHoundPasswordHash.Iterations;
            user.Hash = TrakHoundPasswordHash.Hash(password, user.Salt, user.Iterations);
            user.PasswordChanged = DateTime.UtcNow;
        }

        private static string CreateSessionStamp() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();

        private static string NormalizeName(string name) => string.IsNullOrWhiteSpace(name) ? null : name.Trim();

        private static List<string> NormalizeRoles(IEnumerable<string> roles)
        {
            var list = roles?.Where(o => !string.IsNullOrWhiteSpace(o)).Select(o => o.Trim()).Distinct().ToList();
            return list.IsNullOrEmpty() ? null : list;
        }

        private string GetPath(string id)
        {
            // IDs are created by the store (hex), but a file could have been edited, so never let one leave the directory
            if (string.IsNullOrEmpty(id) || id.Any(o => !char.IsAsciiLetterOrDigit(o))) throw new ArgumentException("Invalid User ID");
            return Path.Combine(_directory, $"{id}{_fileExtension}");
        }

        private void OnChanged()
        {
            try { Changed?.Invoke(this, EventArgs.Empty); } catch { }
        }
    }

    /// <summary>
    /// Implemented by Identity Providers that can end Sessions they created (ex. when a User is disabled)
    /// </summary>
    public interface ITrakHoundSessionValidator
    {
        /// <summary>
        /// Whether a Session for the Identity (created by this Identity Provider) is still valid
        /// </summary>
        bool IsSessionValid(TrakHoundIdentity identity);
    }

    public static class TrakHoundSessionValidation
    {
        /// <summary>
        /// Whether a Session for the Identity can still be used: its Identity Provider is still loaded, and the Provider hasn't ended it (ex. the User was disabled)
        /// </summary>
        public static bool IsSessionValid(this ITrakHoundSecurityManager securityManager, TrakHoundIdentity identity)
        {
            var provider = identity != null ? securityManager?.GetProvider(identity.ProviderId) : null;
            if (provider == null) return false;

            return provider is not ITrakHoundSessionValidator validator || validator.IsSessionValid(identity);
        }
    }

    /// <summary>
    /// The built in Identity Provider for Users managed on the Instance (signed in with a Username and Password on the Instance's sign in page)
    /// </summary>
    public sealed class TrakHoundUserIdentityProvider : ITrakHoundIdentityProvider, ITrakHoundSessionValidator
    {
        public const string ProviderId = "users";
        private const string _sessionStampProperty = "sessionStamp";

        private const string _description = "Users managed on the Instance";

        private readonly TrakHoundUserStore _store;
        private volatile ConfigurationSnapshot _configuration;


        public string Id => ProviderId;

        /// <summary>
        /// Built from the Store's Settings, so the sign in page uses them the same as a configured Identity Provider's Parameters
        /// </summary>
        public ITrakHoundIdentityProviderConfiguration Configuration
        {
            get
            {
                var settings = _store.Settings;
                var snapshot = _configuration;
                if (snapshot == null || snapshot.Settings != settings)
                {
                    snapshot = new ConfigurationSnapshot(settings, CreateConfiguration(settings));
                    _configuration = snapshot;
                }

                return snapshot.Configuration;
            }
        }

        public ITrakHoundClient Client => null;

        public ITrakHoundVolume Volume => null;

        public TrakHoundIdentityProviderFeatures Features => TrakHoundIdentityProviderFeatures.Credentials;

        public TrakHoundUserStore Store => _store;


        public TrakHoundUserIdentityProvider(TrakHoundUserStore store)
        {
            _store = store;
        }


        private static TrakHoundIdentityProviderConfiguration CreateConfiguration(TrakHoundUserSettings settings)
        {
            var parameters = new Dictionary<string, object>();
            if (settings?.SignInRoute != null) parameters[TrakHoundSignInPage.RouteParameter] = settings.SignInRoute;
            if (settings?.SignInProduct != null) parameters[TrakHoundSignInPage.ProductParameter] = settings.SignInProduct;
            if (settings?.SignInLogo != null) parameters[TrakHoundSignInPage.LogoParameter] = settings.SignInLogo;

            var configuration = new TrakHoundIdentityProviderConfiguration();
            configuration.Id = ProviderId;
            configuration.Description = _description;
            configuration.Parameters = parameters;
            return configuration;
        }

        private sealed class ConfigurationSnapshot
        {
            public readonly TrakHoundUserSettings Settings;
            public readonly TrakHoundIdentityProviderConfiguration Configuration;

            public ConfigurationSnapshot(TrakHoundUserSettings settings, TrakHoundIdentityProviderConfiguration configuration)
            {
                Settings = settings;
                Configuration = configuration;
            }
        }


        public Task<TrakHoundIdentityResult> SignIn(TrakHoundIdentityContext context, TrakHoundIdentityCredentials credentials)
        {
            if (string.IsNullOrEmpty(credentials?.Username) || string.IsNullOrEmpty(credentials.Password)) return Task.FromResult(TrakHoundIdentityResult.Fail("Username and Password are required"));

            // PBKDF2 is CPU bound, so it isn't run on the request's thread
            return Task.Run(() =>
            {
                var user = _store.Verify(credentials.Username.Trim(), credentials.Password);
                if (user == null) return TrakHoundIdentityResult.Fail("Invalid Credentials");

                var identity = new TrakHoundIdentity();
                identity.ProviderId = ProviderId;
                identity.UserId = user.Username;
                identity.Name = user.DisplayName;
                identity.Type = TrakHoundIdentityUserType.Origin;
                identity.Roles = user.Roles?.ToList() ?? new List<string>();
                identity.Properties = new Dictionary<string, string> { { _sessionStampProperty, user.SessionStamp } };

                return TrakHoundIdentityResult.Success(identity);
            });
        }

        public bool IsSessionValid(TrakHoundIdentity identity)
        {
            return identity != null && _store.IsSessionValid(identity.UserId, identity.Properties?.GetValueOrDefault(_sessionStampProperty));
        }

        public Task<TrakHoundIdentityResult> Authenticate(TrakHoundIdentityContext context) => Task.FromResult(TrakHoundIdentityResult.NoResult());

        public Task<TrakHoundIdentityRedirect> Challenge(TrakHoundIdentityContext context, string state) => Task.FromResult(TrakHoundIdentityRedirect.None("Users sign in with a Username and Password"));

        public Task<TrakHoundIdentityResult> HandleCallback(TrakHoundIdentityContext context) => Task.FromResult(TrakHoundIdentityResult.Error("Users sign in with a Username and Password"));

        public Task<TrakHoundIdentityRedirect> SignOut(TrakHoundIdentityContext context, TrakHoundIdentity identity, string returnUrl) => Task.FromResult(TrakHoundIdentityRedirect.None());
    }
}
