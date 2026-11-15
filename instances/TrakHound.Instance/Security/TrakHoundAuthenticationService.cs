// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components.Authorization;
using TrakHound.Instances;
using TrakHound.Security;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// Provides the Session for App Pages (from the signed in Identity of the Blazor Circuit)
    /// </summary>
    public class TrakHoundAuthenticationService
    {
        private readonly ITrakHoundInstance _instance;
        private readonly AuthenticationStateProvider _authenticationStateProvider;


        public TrakHoundAuthenticationService(ITrakHoundInstance instance, AuthenticationStateProvider authenticationStateProvider)
        {
            _instance = instance;
            _authenticationStateProvider = authenticationStateProvider;
        }


        /// <summary>
        /// Gets the signed in Identity (null if not signed in or the Session has expired)
        /// </summary>
        public async Task<TrakHoundIdentity> GetIdentity()
        {
            try
            {
                var state = await _authenticationStateProvider.GetAuthenticationStateAsync();
                var identity = TrakHoundIdentityClaims.FromPrincipal(state?.User);

                // The Identity Provider may have been removed, or have ended the Session (ex. the User was disabled), since the Session was created
                if (identity != null && _instance.SecurityManager.IsSessionValid(identity)) return identity;
            }
            catch (InvalidOperationException) { } // The Authentication State is not available outside of a Circuit

            return null;
        }

        /// <summary>
        /// Authorizes the signed in Identity for the Resource and returns the Session (with the Identity's Permissions for the Resource)
        /// </summary>
        public async Task<(ITrakHoundSession Session, TrakHoundAuthorizationResult Authorization)> Authorize(string resourceId)
        {
            var identity = await GetIdentity();
            var authorization = _instance.SecurityManager.Authorize(identity, resourceId);

            return (CreateSession(identity, authorization.IsAllowed ? authorization.Permissions : Array.Empty<string>()), authorization);
        }

        /// <summary>
        /// Gets the URL to sign in with the first Identity Provider that supports interactive sign in (null if none do)
        /// </summary>
        public string GetSignInUrl(IEnumerable<string> providerIds, string returnPath)
        {
            var providerId = providerIds?.FirstOrDefault(o =>
            {
                var features = _instance.SecurityManager.GetProvider(o)?.Features ?? TrakHoundIdentityProviderFeatures.None;
                return features.HasFlag(TrakHoundIdentityProviderFeatures.Redirect) || features.HasFlag(TrakHoundIdentityProviderFeatures.Credentials);
            });

            if (providerId != null)
            {
                var basePath = _instance.Configuration.BasePath;
                var returnUrl = TrakHoundIdentityHttp.GetExternalPath(basePath, returnPath);
                var signInPath = TrakHoundIdentityHttp.GetExternalPath(basePath, $"{TrakHoundIdentityHttp.SignInPrefix}/{Uri.EscapeDataString(providerId)}");

                return $"{signInPath}?returnUrl={Uri.EscapeDataString(returnUrl)}";
            }

            return null;
        }

        private static ITrakHoundSession CreateSession(TrakHoundIdentity identity, IReadOnlyList<string> permissions)
        {
            if (identity != null)
            {
                var session = new TrakHoundAuthenticatedSession();
                session.SessionId = $"{identity.ProviderId}:{identity.UserId}";
                session.ProviderId = identity.ProviderId;
                session.User = identity.ToUser();
                session.Roles = permissions;
                session.ValidFrom = DateTime.UtcNow;
                session.ValidTo = identity.Expires ?? DateTime.MaxValue;
                return session;
            }
            else
            {
                var session = new TrakHoundAnnonymousSession();
                session.SessionId = Guid.NewGuid().ToString();
                session.Roles = permissions;
                return session;
            }
        }
    }
}
