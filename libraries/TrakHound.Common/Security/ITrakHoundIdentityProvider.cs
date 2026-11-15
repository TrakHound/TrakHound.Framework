// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Volumes;

namespace TrakHound.Security
{
    /// <summary>
    /// Verifies Identities (ex. API Keys, OpenID Connect, or LDAP). Verified Identities are turned into Sessions by the Instance
    /// </summary>
    public interface ITrakHoundIdentityProvider
    {
        /// <summary>
        /// The unique Identifier of the Identity Provider (the Configuration Id)
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The Configuration the Identity Provider was created from
        /// </summary>
        ITrakHoundIdentityProviderConfiguration Configuration { get; }

        /// <summary>
        /// The Client used to access the Instance
        /// </summary>
        ITrakHoundClient Client { get; }

        /// <summary>
        /// The Volume used to store Files for the Identity Provider
        /// </summary>
        ITrakHoundVolume Volume { get; }

        /// <summary>
        /// The ways the Identity Provider can verify an Identity
        /// </summary>
        TrakHoundIdentityProviderFeatures Features { get; }


        /// <summary>
        /// Verifies credentials sent with the Request (ex. an Authorization header). Returns NoResult if the Request doesn't contain credentials for this Identity Provider
        /// </summary>
        Task<TrakHoundIdentityResult> Authenticate(TrakHoundIdentityContext context);

        /// <summary>
        /// Starts an interactive sign in (ex. returns a Redirect to an external sign in page). The State must be returned unchanged by HandleCallback
        /// </summary>
        Task<TrakHoundIdentityRedirect> Challenge(TrakHoundIdentityContext context, string state);

        /// <summary>
        /// Completes an interactive sign in when the external server redirects back to the Callback URL
        /// </summary>
        Task<TrakHoundIdentityResult> HandleCallback(TrakHoundIdentityContext context);

        /// <summary>
        /// Verifies a Username and Password entered on the Instance's sign in page
        /// </summary>
        Task<TrakHoundIdentityResult> SignIn(TrakHoundIdentityContext context, TrakHoundIdentityCredentials credentials);

        /// <summary>
        /// Signs out of the external server (ex. returns a Redirect to an OpenID Connect end session endpoint). The Instance's Session is removed regardless
        /// </summary>
        Task<TrakHoundIdentityRedirect> SignOut(TrakHoundIdentityContext context, TrakHoundIdentity identity, string returnUrl);
    }
}
