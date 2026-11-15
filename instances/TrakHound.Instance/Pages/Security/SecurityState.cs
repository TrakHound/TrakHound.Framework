// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Pages.Configuration;
using TrakHound.Instances;
using TrakHound.Security;

namespace TrakHound.Instance.Pages.Security
{
    /// <summary>
    /// An Identity Provider shown on the Security pages (configured, or one of the built in API Keys and Users Identity Providers)
    /// </summary>
    public class SecurityProviderInfo
    {
        public string Id { get; set; }

        public string Description { get; set; }

        public string PackageId { get; set; }

        public bool IsBuiltIn { get; set; }

        public bool IsLoaded { get; set; }

        public TrakHoundIdentityProviderFeatures Features { get; set; }

        public string ConfigurationLink { get; set; }

        public List<string> ProfileIds { get; set; } = new List<string>();


        public string DisplayName => !string.IsNullOrEmpty(Description) ? Description : PackageId ?? Id;

        public bool CanSignIn => Features.HasFlag(TrakHoundIdentityProviderFeatures.Redirect) || Features.HasFlag(TrakHoundIdentityProviderFeatures.Credentials);
    }

    /// <summary>
    /// A snapshot of the Instance's Security configuration (Identity Providers, Profiles, API Keys, and Users)
    /// </summary>
    public class SecurityState
    {
        public List<SecurityProviderInfo> Providers { get; } = new List<SecurityProviderInfo>();

        public List<TrakHoundSecurityProfile> Profiles { get; } = new List<TrakHoundSecurityProfile>();

        public List<TrakHoundApiKey> Keys { get; } = new List<TrakHoundApiKey>();

        public TrakHoundApiKeyStore KeyStore { get; private set; }

        public List<TrakHoundUser> Users { get; } = new List<TrakHoundUser>();

        public TrakHoundUserStore UserStore { get; private set; }


        public int AssignmentCount => Profiles.Sum(o => o.Assignments?.Count() ?? 0);


        public SecurityProviderInfo GetProvider(string providerId)
        {
            return providerId != null ? Providers.FirstOrDefault(o => string.Equals(o.Id, providerId, StringComparison.OrdinalIgnoreCase)) : null;
        }

        public bool IsProviderLoaded(string providerId) => GetProvider(providerId)?.IsLoaded == true;


        public static SecurityState Load(ITrakHoundInstance instance)
        {
            var state = new SecurityState();
            var securityManager = instance.SecurityManager;

            state.Profiles.AddRange(securityManager.GetProfiles().OrderBy(o => o.Id));

            state.KeyStore = (securityManager as TrakHoundSecurityManager)?.ApiKeys;
            if (state.KeyStore != null) state.Keys.AddRange(state.KeyStore.GetKeys());

            state.UserStore = (securityManager as TrakHoundSecurityManager)?.Users;
            if (state.UserStore != null) state.Users.AddRange(state.UserStore.GetUsers());

            // Built in API Key Identity Provider
            var apiKeyProvider = securityManager.GetProvider(TrakHoundApiKeyIdentityProvider.ProviderId);
            if (apiKeyProvider != null)
            {
                state.Providers.Add(new SecurityProviderInfo
                {
                    Id = apiKeyProvider.Id,
                    Description = "API Keys",
                    IsBuiltIn = true,
                    IsLoaded = true,
                    Features = apiKeyProvider.Features
                });
            }

            // Built in Users Identity Provider
            var userProvider = securityManager.GetProvider(TrakHoundUserIdentityProvider.ProviderId);
            if (userProvider != null)
            {
                state.Providers.Add(new SecurityProviderInfo
                {
                    Id = userProvider.Id,
                    Description = "Users",
                    IsBuiltIn = true,
                    IsLoaded = true,
                    Features = userProvider.Features
                });
            }

            // Configured Identity Providers (including any that failed to load)
            var configurations = instance.ConfigurationProfile.Get<TrakHoundIdentityProviderConfiguration>(TrakHoundIdentityProviderConfiguration.ConfigurationCategory);
            if (configurations != null)
            {
                foreach (var configuration in configurations.OrderBy(o => o.Description ?? o.Id))
                {
                    if (state.GetProvider(configuration.Id) != null) continue;

                    var provider = securityManager.GetProvider(configuration.Id);
                    state.Providers.Add(new SecurityProviderInfo
                    {
                        Id = configuration.Id,
                        Description = configuration.Description,
                        PackageId = configuration.PackageId,
                        IsLoaded = provider != null,
                        Features = provider?.Features ?? TrakHoundIdentityProviderFeatures.None,
                        ConfigurationLink = ConfigurationCategories.GetLink(TrakHoundIdentityProviderConfiguration.ConfigurationCategory, configuration.Id)
                    });
                }
            }

            // Identity Providers loaded without a Configuration
            foreach (var provider in securityManager.GetProviders())
            {
                if (state.GetProvider(provider.Id) == null)
                {
                    state.Providers.Add(new SecurityProviderInfo
                    {
                        Id = provider.Id,
                        Description = provider.Configuration?.Description,
                        PackageId = provider.Configuration?.PackageId,
                        IsLoaded = true,
                        Features = provider.Features
                    });
                }
            }

            foreach (var profile in state.Profiles)
            {
                state.GetProvider(profile.ProviderId)?.ProfileIds.Add(profile.Id);
            }

            return state;
        }
    }

    public static class SecurityProfileValidation
    {
        /// <summary>
        /// Profile IDs are used as File names, so only letters, numbers, '-', '_', and '.' are allowed. Returns an error message (null if valid)
        /// </summary>
        public static string ValidateId(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return "An ID is required.";
            if (id.Length > 100) return "The ID must be 100 characters or less.";
            if (id.StartsWith('.') || !id.All(o => char.IsLetterOrDigit(o) || o == '-' || o == '_' || o == '.')) return "The ID can only contain letters, numbers, '-', '_', and '.' (and can't start with '.').";
            return null;
        }
    }
}
