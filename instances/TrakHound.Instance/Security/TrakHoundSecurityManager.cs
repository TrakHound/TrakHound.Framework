// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Configurations;
using TrakHound.Instance.Security;
using TrakHound.Logging;
using TrakHound.Modules;
using TrakHound.Packages;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Security
{
    public sealed class TrakHoundSecurityManager : ITrakHoundSecurityManager
    {
        private const string _packageCategory = "identity";
        private const string _profileDirectory = "security";
        private const string _apiKeyDirectory = "api-keys";
        private const string _userDirectory = "users";
        public const string LoggerName = "security";

        private const string _wildcard = "*";

        private readonly TrakHoundConfigurationProfile _configurationProfile;
        private readonly ITrakHoundModuleManager _moduleManager;
        private readonly TrakHoundPackageManager _packageManager;
        private readonly ITrakHoundClientProvider _clientProvider;
        private readonly ITrakHoundVolumeProvider _volumeProvider;
        private readonly ITrakHoundVariableProvider _variableProvider;
        private readonly Dictionary<string, ITrakHoundIdentityProvider> _identityProviders = new Dictionary<string, ITrakHoundIdentityProvider>(StringComparer.OrdinalIgnoreCase); // ProviderId => Provider
        private readonly Dictionary<string, TrakHoundSecurityProfile> _profiles = new Dictionary<string, TrakHoundSecurityProfile>(); // ProfileId => Profile
        private readonly Dictionary<string, TrakHoundIdentityResource> _resources = new Dictionary<string, TrakHoundIdentityResource>(); // ResourceId => Resource
        private readonly TrakHoundApiKeyIdentityProvider _apiKeyProvider;
        private readonly TrakHoundUserIdentityProvider _userProvider;
        private readonly ITrakHoundLogger _logger;
        private readonly DelayEvent _delayLoadEvent = new DelayEvent(2000);
        private readonly DelayEvent _delaySaveEvent = new DelayEvent(1000);
        private readonly object _lock = new object();

        // Read on every request without the lock (replaced, never modified, whenever Profiles or Providers change)
        private volatile ProfileSnapshot _profileSnapshot = new ProfileSnapshot(Array.Empty<TrakHoundSecurityProfile>());
        private volatile ReadOnlyCollection<ITrakHoundIdentityProvider> _providers;


        private readonly Dictionary<string, string> _installedConfigurations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // Configuration.Id => Configuration.Hash
        private readonly Dictionary<string, string> _installedModuleHashes = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // Configuration.Id => Module.Hash


        public IEnumerable<ITrakHoundIdentityProvider> IdentityProviders => GetProviders();

        public event EventHandler<ITrakHoundIdentityProvider> IdentityProviderAdded;

        public event EventHandler<string> IdentityProviderRemoved;

        /// <summary>
        /// The API Keys issued by the Instance (verified by the built in API Key Identity Provider)
        /// </summary>
        public TrakHoundApiKeyStore ApiKeys => _apiKeyProvider.Store;

        /// <summary>
        /// The Users managed on the Instance (verified by the built in Users Identity Provider)
        /// </summary>
        public TrakHoundUserStore Users => _userProvider.Store;

        /// <summary>
        /// Raised when Identity Providers, Profiles, Assignments, API Keys, or Users change
        /// </summary>
        public event EventHandler Changed;


        public TrakHoundSecurityManager(
            TrakHoundConfigurationProfile configurationProfile,
            ITrakHoundModuleProvider moduleProvider,
            ITrakHoundClientProvider clientProvider,
            ITrakHoundVolumeProvider volumeProvider,
            ITrakHoundVariableProvider variableProvider,
            TrakHoundPackageManager packageManager,
            ITrakHoundLogger logger = null
            )
        {
            _logger = logger ?? new TrakHoundLogger<TrakHoundSecurityManager>();

            _configurationProfile = configurationProfile;
            _configurationProfile.ConfigurationAdded += ProviderConfigurationAdded;
            _configurationProfile.ConfigurationRemoved += ProviderConfigurationRemoved;

            _packageManager = packageManager;
            _packageManager.PackageAdded += PackageAdded;
            _packageManager.PackageRemoved += PackageRemoved;

            _moduleManager = moduleProvider.Get<ITrakHoundIdentityProvider>(_packageCategory);
            _clientProvider = clientProvider;
            _volumeProvider = volumeProvider;
            _variableProvider = variableProvider;

            _delayLoadEvent.Elapsed += LoadDelayElapsed;
            _delaySaveEvent.Elapsed += SaveDelayElapsed;

            _apiKeyProvider = new TrakHoundApiKeyIdentityProvider(new TrakHoundApiKeyStore(Path.Combine(_configurationProfile.Path, _profileDirectory, _apiKeyDirectory)));
            _apiKeyProvider.Store.Changed += (s, e) => OnChanged();
            _apiKeyProvider.Store.ReachesInstance = roles => ReachesInstance(TrakHoundApiKeyIdentityProvider.ProviderId, roles);
            _userProvider = new TrakHoundUserIdentityProvider(new TrakHoundUserStore(Path.Combine(_configurationProfile.Path, _profileDirectory, _userDirectory)));
            _userProvider.Store.Changed += (s, e) => OnChanged();
            _userProvider.Store.ReachesInstance = roles => ReachesInstance(TrakHoundUserIdentityProvider.ProviderId, roles);
            lock (_lock) RebuildProviders();

            TrakHoundSystemSecurity.RegisterResources(this);
        }


        private void PackageAdded(object sender, TrakHoundPackage package)
        {
            if (package != null && package.Category == _packageCategory)
            {
                _delayLoadEvent.Set();
            }
        }

        private void PackageRemoved(object sender, TrakHoundPackageChangedArgs args)
        {
            if (args.PackageCategory == _packageCategory)
            {
                _delayLoadEvent.Set();
            }
        }


        private void ProviderConfigurationAdded(object sender, ITrakHoundConfiguration configuration)
        {
            if (configuration.Category == TrakHoundIdentityProviderConfiguration.ConfigurationCategory)
            {
                AddProviderConfiguration((ITrakHoundIdentityProviderConfiguration)configuration);
            }
        }

        private void ProviderConfigurationRemoved(object sender, ITrakHoundConfiguration configuration)
        {
            if (configuration.Category == TrakHoundIdentityProviderConfiguration.ConfigurationCategory)
            {
                RemoveProviderConfiguration(configuration.Id);
            }
        }


        public async Task<TrakHoundIdentityResult> Authenticate(TrakHoundIdentityContext context)
        {
            if (context != null)
            {
                foreach (var provider in _providers)
                {
                    if (!provider.Features.HasFlag(TrakHoundIdentityProviderFeatures.Token)) continue;

                    TrakHoundIdentityResult result;
                    try
                    {
                        result = await provider.Authenticate(context);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Identity Provider ({provider.Id}) Authenticate Error : {ex.Message}");
                        result = TrakHoundIdentityResult.Error(ex.Message);
                    }

                    if (result.Status != TrakHoundIdentityResultStatus.NoResult)
                    {
                        // The Identity always belongs to the Identity Provider that verified it
                        if (result.IsSuccess) result.Identity.ProviderId = provider.Id;
                        return result;
                    }
                }
            }

            return TrakHoundIdentityResult.NoResult();
        }

        private const string _uncoveredMessage = "No Identity Profile Found for the Resource";
        private const string _unrestrictedMessage = "No Identity Provider specified for the Resource";

        public TrakHoundAuthorizationResult Authorize(TrakHoundIdentity identity, string resourceId, string permission = null)
        {
            // Resources that are not covered by a Profile are allowed (until they are explicitly protected).
            // The allowed results use constant messages, since they are created for most Requests (the denials, which are rare, name the Resource)
            var profileMatches = GetProfileMatches(resourceId);
            if (profileMatches.IsNullOrEmpty())
            {
                return TrakHoundAuthorizationResult.Uncovered(_uncoveredMessage);
            }

            // A Profile without an Identity Provider allows access to the Resource without Authentication
            if (profileMatches.Any(o => string.IsNullOrEmpty(o.Profile.ProviderId)))
            {
                return TrakHoundAuthorizationResult.Unrestricted(_unrestrictedMessage);
            }

            var providerIds = profileMatches.Select(o => o.Profile.ProviderId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (identity == null || identity.IsExpired)
            {
                return TrakHoundAuthorizationResult.AuthenticationRequired(providerIds, $"Authentication Required for the Resource : {resourceId}");
            }

            // Only the Profiles for the Identity's Provider apply (another Provider may still be able to sign in)
            var identityMatches = profileMatches.Where(o => string.Equals(o.Profile.ProviderId, identity.ProviderId, StringComparison.OrdinalIgnoreCase)).ToList();
            if (identityMatches.Count < 1)
            {
                return TrakHoundAuthorizationResult.AuthenticationRequired(providerIds, $"The Identity Provider ({identity.ProviderId}) is not accepted for the Resource : {resourceId}");
            }

            // An Identity must match at least one Assignment to access the Resource
            var matchedAssignments = identityMatches.SelectMany(o => o.Assignments).Where(o => IsRoleMatch(o.Roles, identity.Roles)).ToList();
            if (matchedAssignments.Count < 1)
            {
                return TrakHoundAuthorizationResult.Forbidden($"No Assignment matches the Roles of ({identity.UserId}) for the Resource : {resourceId}");
            }

            var permissions = matchedAssignments.Where(o => o.Permissions != null).SelectMany(o => o.Permissions).Distinct().ToList();

            if (!HasPermission(permissions, GetResource(resourceId)?.Permissions, permission))
            {
                return TrakHoundAuthorizationResult.Forbidden($"({identity.UserId}) does not have the required Permission for the Resource : {resourceId}");
            }

            return TrakHoundAuthorizationResult.Allowed(permissions);
        }

        private static bool HasPermission(IReadOnlyCollection<string> permissions, IEnumerable<string> resourcePermissions, string requiredPermission)
        {
            if (permissions.Contains(_wildcard)) return true;

            // The Resource requires any one of its Permissions
            if (!resourcePermissions.IsNullOrEmpty() && !resourcePermissions.Any(o => permissions.Contains(o))) return false;

            // The specific operation requires its Permission
            if (!string.IsNullOrEmpty(requiredPermission) && !permissions.Contains(requiredPermission)) return false;

            return true;
        }

        private static bool IsRoleMatch(IEnumerable<string> assignmentRoles, IEnumerable<string> identityRoles)
        {
            if (!assignmentRoles.IsNullOrEmpty())
            {
                foreach (var assignmentRole in assignmentRoles)
                {
                    // A Wildcard Role matches ANY Identity (including Identities without Roles)
                    if (assignmentRole == _wildcard) return true;

                    if (!identityRoles.IsNullOrEmpty())
                    {
                        foreach (var identityRole in identityRoles)
                        {
                            if (IsPatternMatch(assignmentRole, identityRole)) return true;
                        }
                    }
                }
            }

            return false;
        }

        // Patterns match exactly, or by prefix when they end with a Wildcard (ex. "api:my-api:*")
        private static bool IsPatternMatch(string pattern, string value)
        {
            if (!string.IsNullOrEmpty(pattern) && !string.IsNullOrEmpty(value))
            {
                if (pattern == _wildcard)
                {
                    return true;
                }
                else if (pattern.EndsWith(_wildcard))
                {
                    var matchPattern = pattern.TrimEnd('*');
                    return value.StartsWith(matchPattern, StringComparison.Ordinal);
                }
                else
                {
                    return value == pattern;
                }
            }

            return false;
        }


        public TrakHoundIdentityResource GetResource(string resourceId)
        {
            if (!string.IsNullOrEmpty(resourceId))
            {
                lock (_lock)
                {
                    return _resources.GetValueOrDefault(resourceId);
                }
            }

            return null;
        }

        public void AddResource(TrakHoundIdentityResourceType resourceType, string resourceId)
        {
            var resource = new TrakHoundIdentityResource(resourceType, resourceId);

            AddResourceInternal(resource);
        }

        public void AddResource(TrakHoundIdentityResourceType resourceType, string resourceId, string permission)
        {
            IEnumerable<string> permissions = null;
            if (!string.IsNullOrEmpty(permission)) permissions = new string[] { permission };

            var resource = new TrakHoundIdentityResource(resourceType, resourceId, permissions);

            AddResourceInternal(resource);
        }

        public void AddResource(TrakHoundIdentityResourceType resourceType, string resourceId, IEnumerable<string> permissions)
        {
            var resource = new TrakHoundIdentityResource(resourceType, resourceId, permissions);

            AddResourceInternal(resource);
        }

        private void AddResourceInternal(TrakHoundIdentityResource resource)
        {
            if (resource != null && !string.IsNullOrEmpty(resource.Id))
            {
                lock (_lock)
                {
                    _resources.Remove(resource.Id);
                    _resources.Add(resource.Id, resource);
                }
            }
        }

        public IEnumerable<TrakHoundIdentityResource> GetResources()
        {
            lock (_lock)
            {
                return _resources.Values.ToList();
            }
        }

        public void RemoveResource(string resourceId)
        {
            if (!string.IsNullOrEmpty(resourceId))
            {
                lock (_lock)
                {
                    _resources.Remove(resourceId);
                }
            }
        }


        public IEnumerable<TrakHoundSecurityProfile> GetProfiles()
        {
            lock (_lock)
            {
                return _profiles.Values.ToList();
            }
        }

        public TrakHoundSecurityProfile GetProfile(string profileId)
        {
            if (!string.IsNullOrEmpty(profileId))
            {
                lock (_lock)
                {
                    return _profiles.GetValueOrDefault(profileId);
                }
            }

            return null;
        }

        public IEnumerable<TrakHoundSecurityProfile> GetProfilesByResourceId(string resourceId)
        {
            return GetProfileMatches(resourceId)?.Select(o => o.Profile).ToList();
        }

        // Returns the Profiles (ordered by Priority) that have an Assignment matching the Resource, along with the matching Assignments
        private ProfileMatch[] GetProfileMatches(string resourceId)
        {
            if (!string.IsNullOrEmpty(resourceId))
            {
                // Matches are cached in the snapshot they were computed from, so a change (new snapshot) never sees a stale match
                var snapshot = _profileSnapshot;
                if (snapshot.Matches.TryGetValue(resourceId, out var cached)) return cached;

                var matches = new List<ProfileMatch>();
                foreach (var entry in snapshot.Profiles)
                {
                    var assignments = entry.Assignments?.Where(o => IsPatternMatch(o.ResourceId, resourceId)).ToArray();
                    if (!assignments.IsNullOrEmpty()) matches.Add(new ProfileMatch(entry.Profile, assignments));
                }

                var result = matches.ToArray();
                snapshot.AddMatch(resourceId, result);
                return result;
            }

            return null;
        }

        // Called inside the lock after every change to _profiles or a Profile's Assignments
        private void RebuildProfileSnapshot()
        {
            _profileSnapshot = new ProfileSnapshot(_profiles.Values.OrderByDescending(o => o.Priority).ToArray());
        }

        private readonly struct ProfileMatch
        {
            public readonly TrakHoundSecurityProfile Profile;
            public readonly TrakHoundIdentityAssignment[] Assignments;

            public ProfileMatch(TrakHoundSecurityProfile profile, TrakHoundIdentityAssignment[] assignments)
            {
                Profile = profile;
                Assignments = assignments;
            }
        }

        private sealed class ProfileSnapshot
        {
            // Resource IDs can come from requests, so the cache is capped (lookups beyond it are computed each time)
            private const int _maxMatches = 10000;

            private int _matchCount;

            // The Profiles ordered by Priority, with their Assignments as they were when the snapshot was built
            public readonly ProfileMatch[] Profiles;

            public readonly ConcurrentDictionary<string, ProfileMatch[]> Matches = new ConcurrentDictionary<string, ProfileMatch[]>(StringComparer.Ordinal); // ResourceId => Matches


            public ProfileSnapshot(TrakHoundSecurityProfile[] profiles)
            {
                Profiles = profiles.Select(o => new ProfileMatch(o, o.Assignments?.ToArray())).ToArray();
            }

            public void AddMatch(string resourceId, ProfileMatch[] matches)
            {
                if (Volatile.Read(ref _matchCount) < _maxMatches && Matches.TryAdd(resourceId, matches))
                {
                    Interlocked.Increment(ref _matchCount);
                }
            }
        }

        // Profiles are only changed from the Admin Interface, so each change is checked against the signed in Admin's Roles.
        // A change to Assignments that reach the System API or Services (the ones given, and the ones it replaces or removes) needs more
        private static void DemandProfileWrite(params IEnumerable<TrakHoundIdentityAssignment>[] assignments) => TrakHoundAdminProfileRules.DemandWrite(assignments);

        /// <summary>
        /// The Role names that the Profiles using an Identity Provider give Permissions to (not Wildcards or patterns), and whether each
        /// reaches the System API or Services
        /// </summary>
        public IReadOnlyList<(string Role, bool ReachesInstance)> GetProfileRoles(string providerId) => TrakHoundAdminProfileRules.GetRoles(GetProfiles().ToList(), providerId);

        /// <summary>
        /// Whether an Identity from the Identity Provider with the Roles gets Permissions on a System Resource or a Service operation from any Profile
        /// </summary>
        public bool ReachesInstance(string providerId, IEnumerable<string> roles) => TrakHoundAdminProfileRules.ReachesInstance(GetProfiles().ToList(), providerId, roles);

        public void AddProfile(string profileId, string providerId, string description = null)
        {
            DemandProfileWrite(GetProfile(profileId)?.Assignments);

            var profile = new TrakHoundSecurityProfile();
            profile.Id = profileId;
            profile.Description = description;
            profile.ProviderId = providerId;

            AddProfileInternal(profile);
            _delaySaveEvent.Set();
            OnChanged();
        }

        public void AddProfile(TrakHoundSecurityProfile profile)
        {
            DemandProfileWrite(profile?.Assignments, GetProfile(profile?.Id)?.Assignments);

            AddProfileInternal(profile);
            _delaySaveEvent.Set();
            OnChanged();
        }

        private void AddProfileInternal(TrakHoundSecurityProfile profile)
        {
            if (profile != null && !string.IsNullOrEmpty(profile.Id))
            {
                // Assignments are always replaced (never modified) so that they can be read without the lock
                profile.Assignments = profile.Assignments?.Where(IsAssignmentValid).ToList();

                if (profile.Assignments != null)
                {
                    foreach (var assignment in profile.Assignments) WarnPublicWildcard(profile, assignment);
                }

                // Profiles saved before the System API Resources were added use the previous System Api Information Resource ID (saved with the new ID)
                if (profile.Assignments != null && profile.Assignments.Any(o => TrakHoundSystemSecurity.MigrateResourceId(o.ResourceId) != o.ResourceId))
                {
                    foreach (var assignment in profile.Assignments) assignment.ResourceId = TrakHoundSystemSecurity.MigrateResourceId(assignment.ResourceId);
                    _delaySaveEvent.Set();
                }

                lock (_lock)
                {
                    _profiles.Remove(profile.Id);
                    _profiles.Add(profile.Id, profile);
                    RebuildProfileSnapshot();
                }
            }
        }

        // Any Profile without an Identity Provider makes what it matches public (whatever the other Profiles require), so a Wildcard there is usually a mistake
        private void WarnPublicWildcard(TrakHoundSecurityProfile profile, TrakHoundIdentityAssignment assignment)
        {
            if (string.IsNullOrEmpty(profile.ProviderId) && assignment.ResourceId != null && assignment.ResourceId.EndsWith(_wildcard))
            {
                _logger.LogWarning($"Security Profile ({profile.Id}) has no Identity Provider, so everything matching ({assignment.ResourceId}) doesn't require signing in");
            }
        }

        public void RemoveProfile(string profileId)
        {
            DemandProfileWrite(GetProfile(profileId)?.Assignments);

            if (!string.IsNullOrEmpty(profileId))
            {
                lock (_lock)
                {
                    _profiles.Remove(profileId);
                    RebuildProfileSnapshot();
                }

                try
                {
                    var path = Path.Combine(_configurationProfile.Path, _profileDirectory);
                    path = Path.Combine(path, $"{profileId}.config.yaml");

                    if (File.Exists(path)) File.Delete(path);
                }
                catch { }

                OnChanged();
            }
        }


        private static bool IsAssignmentValid(TrakHoundIdentityAssignment assignment)
        {
            return assignment != null && !string.IsNullOrEmpty(assignment.Id) && !string.IsNullOrEmpty(assignment.ResourceId);
        }

        public void AddAssignment(string profileId, TrakHoundIdentityAssignment assignment)
        {
            DemandProfileWrite(new[] { assignment }, GetProfile(profileId)?.Assignments?.Where(o => o.Id == assignment?.Id));

            if (!string.IsNullOrEmpty(profileId) && IsAssignmentValid(assignment))
            {
                lock (_lock)
                {
                    var profile = _profiles.GetValueOrDefault(profileId);
                    if (profile != null)
                    {
                        var profileAssignments = profile.Assignments?.ToList() ?? new List<TrakHoundIdentityAssignment>();
                        profileAssignments.RemoveAll(o => o.Id == assignment.Id);
                        profileAssignments.Add(assignment);
                        profile.Assignments = profileAssignments;
                        RebuildProfileSnapshot();
                        WarnPublicWildcard(profile, assignment);
                    }
                }

                _delaySaveEvent.Set();
                OnChanged();
            }
        }

        public void RemoveAssignment(string assignmentId)
        {
            DemandProfileWrite(GetProfiles().SelectMany(o => o.Assignments ?? Enumerable.Empty<TrakHoundIdentityAssignment>()).Where(o => o.Id == assignmentId).ToList());

            if (!string.IsNullOrEmpty(assignmentId))
            {
                lock (_lock)
                {
                    foreach (var profile in _profiles.Values)
                    {
                        if (profile.Assignments != null && profile.Assignments.Any(o => o.Id == assignmentId))
                        {
                            profile.Assignments = profile.Assignments.Where(o => o.Id != assignmentId).ToList();
                        }
                    }

                    RebuildProfileSnapshot();
                }

                _delaySaveEvent.Set();
                OnChanged();
            }
        }


        public IEnumerable<ITrakHoundIdentityProvider> GetProviders() => _providers;

        // Called inside the lock after every change to _identityProviders
        private void RebuildProviders()
        {
            var providers = new List<ITrakHoundIdentityProvider> { _apiKeyProvider, _userProvider };
            providers.AddRange(_identityProviders.Values.Where(o => !IsBuiltInProvider(o.Id)));
            _providers = providers.AsReadOnly();
        }

        public ITrakHoundIdentityProvider GetProvider(string providerId)
        {
            var builtInProvider = GetBuiltInProvider(providerId);
            if (builtInProvider != null) return builtInProvider;

            if (!string.IsNullOrEmpty(providerId))
            {
                lock (_lock)
                {
                    return _identityProviders.GetValueOrDefault(providerId);
                }
            }

            return null;
        }

        public void AddProviderConfiguration(ITrakHoundIdentityProviderConfiguration configuration)
        {
            if (configuration != null && !string.IsNullOrEmpty(configuration.Id))
            {
                var module = _moduleManager.Get(configuration.PackageId, configuration.PackageVersion);
                if (module != null)
                {
                    LoadProviderConfiguration(module, configuration);
                }
            }
        }

        public void RemoveProviderConfiguration(string configurationId)
        {
            if (!string.IsNullOrEmpty(configurationId))
            {
                lock (_lock)
                {
                    _identityProviders.Remove(configurationId);
                    RebuildProviders();

                    // Remove the Installed Hashes so that the Configuration is loaded again if it is added back
                    _installedConfigurations.Remove(configurationId);
                    _installedModuleHashes.Remove(configurationId);
                }

                if (IdentityProviderRemoved != null) IdentityProviderRemoved.Invoke(this, configurationId);
                OnChanged();
            }
        }

        private void OnChanged()
        {
            try { Changed?.Invoke(this, EventArgs.Empty); } catch { }
        }


        private static bool IsBuiltInProvider(string providerId) =>
            string.Equals(providerId, TrakHoundApiKeyIdentityProvider.ProviderId, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(providerId, TrakHoundUserIdentityProvider.ProviderId, StringComparison.OrdinalIgnoreCase);

        private ITrakHoundIdentityProvider GetBuiltInProvider(string providerId)
        {
            if (string.Equals(providerId, TrakHoundApiKeyIdentityProvider.ProviderId, StringComparison.OrdinalIgnoreCase)) return _apiKeyProvider;
            if (string.Equals(providerId, TrakHoundUserIdentityProvider.ProviderId, StringComparison.OrdinalIgnoreCase)) return _userProvider;
            return null;
        }


        private void LoadDelayElapsed(object sender, EventArgs args)
        {
            Load();
        }

        public void Load()
        {
            LoadProviders();
            LoadProfiles();
            _apiKeyProvider.Store.Load();
            _userProvider.Store.Load();
            OnChanged();
        }

        private void LoadProviders()
        {
            var configurations = _configurationProfile.Get<TrakHoundIdentityProviderConfiguration>(TrakHoundIdentityProviderConfiguration.ConfigurationCategory);
            if (!configurations.IsNullOrEmpty())
            {
                foreach (var configuration in configurations.ToList())
                {
                    if (!string.IsNullOrEmpty(configuration.Id))
                    {
                        var module = _moduleManager.Get(configuration.PackageId, configuration.PackageVersion);
                        if (module != null)
                        {
                            // Get the Installed Hash (to check if the configuration has changed)
                            string installedHash;
                            string installedModuleHash;
                            lock (_lock)
                            {
                                installedHash = _installedConfigurations.GetValueOrDefault(configuration.Id);
                                installedModuleHash = _installedModuleHashes.GetValueOrDefault(configuration.Id);
                            }

                            if (configuration.Hash != installedHash || module.Package.Hash != installedModuleHash)
                            {
                                LoadProviderConfiguration(module, configuration);
                            }
                        }
                    }
                }
            }
        }

        private void LoadProfiles()
        {
            var profilesDir = Path.Combine(_configurationProfile.Path, _profileDirectory);
            if (Directory.Exists(profilesDir))
            {
                var profilePaths = Directory.GetFiles(profilesDir);
                if (!profilePaths.IsNullOrEmpty())
                {
                    foreach (var profilePath in profilePaths)
                    {
                        var profile = TrakHoundSecurityProfile.Read(profilePath);
                        if (profile != null)
                        {
                            AddProfileInternal(profile);
                        }
                    }
                }
            }
        }

        public void LoadProvider(ITrakHoundIdentityProvider identityProvider)
        {
            if (identityProvider != null && !string.IsNullOrEmpty(identityProvider.Id))
            {
                lock (_lock)
                {
                    _identityProviders.Remove(identityProvider.Id);
                    _identityProviders.Add(identityProvider.Id, identityProvider);
                    RebuildProviders();
                }
            }
        }

        private void LoadProviderConfiguration(ITrakHoundModule module, ITrakHoundIdentityProviderConfiguration configuration)
        {
            var client = _clientProvider.GetClient(configuration.RouterId);

            var volumeId = configuration.VolumeId ?? configuration.Id;
            var volume = _volumeProvider.GetVolume(volumeId);

            var variables = _variableProvider.GetCollection(configuration.Id);

            if (IsBuiltInProvider(configuration.Id))
            {
                _logger.LogWarning($"Identity Provider ({configuration.Id}) Not Loaded : The ID is reserved for a built in Identity Provider");
                return;
            }

            var provider = CreateProviderInstance(module, configuration, client, volume, variables);
            if (provider != null && !string.IsNullOrEmpty(provider.Id))
            {
                lock (_lock)
                {
                    _identityProviders.Remove(provider.Id);
                    _identityProviders.Add(provider.Id, provider);
                    RebuildProviders();

                    // Add to installed List (so the Provider isn't created again until the Configuration or Module changes)
                    _installedConfigurations.Remove(configuration.Id);
                    _installedConfigurations.Add(configuration.Id, configuration.Hash);
                    _installedModuleHashes.Remove(configuration.Id);
                    _installedModuleHashes.Add(configuration.Id, module.Package.Hash);
                }

                if (IdentityProviderAdded != null) IdentityProviderAdded.Invoke(this, provider);
                OnChanged();
            }
        }

        public string GetProviderPackageDirectory(string providerId)
        {
            var configuration = GetProvider(providerId)?.Configuration;
            return configuration != null ? _moduleManager?.Get(configuration.PackageId, configuration.PackageVersion)?.PackageDirectory : null;
        }

        private ITrakHoundIdentityProvider CreateProviderInstance(
            ITrakHoundModule module,
            ITrakHoundIdentityProviderConfiguration configuration,
            ITrakHoundClient client,
            ITrakHoundVolume volume,
            ITrakHoundVariableCollection variables
            )
        {
            if (module != null && module.ModuleTypes != null && configuration != null)
            {
                try
                {
                    var constructor = module.ModuleTypes?.FirstOrDefault().GetConstructor(new Type[] { typeof(ITrakHoundIdentityProviderConfiguration), typeof(ITrakHoundClient), typeof(ITrakHoundVolume), typeof(ITrakHoundVariableCollection) });
                    if (constructor != null)
                    {
                        return (ITrakHoundIdentityProvider)constructor.Invoke(new object[] { configuration, client, volume, variables });
                    }
                }
                catch { }
            }

            return null;
        }


        // async void : an exception would end the process, so nothing may escape
        private async void SaveDelayElapsed(object sender, EventArgs args)
        {
            try
            {
                await Save();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error Saving Security Profiles : {ex.Message}");
            }
        }

        public async Task Save()
        {
            IEnumerable<TrakHoundSecurityProfile> profiles;
            lock (_lock) profiles = _profiles.Values.ToList();
            if (!profiles.IsNullOrEmpty())
            {
                foreach (var profile in profiles)
                {
                    await profile.Save(_configurationProfile.Path);
                }
            }
        }
    }
}
