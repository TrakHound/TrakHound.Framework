// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Security
{
    /// <summary>
    /// Base class for implementing an Identity Provider. Set Features and override the matching methods
    /// (OnAuthenticate for Token, OnChallenge and OnHandleCallback for Redirect, OnSignIn for Credentials)
    /// </summary>
    public abstract class TrakHoundIdentityProviderBase : ITrakHoundIdentityProvider
    {
        private readonly ITrakHoundIdentityProviderConfiguration _configuration;
        private readonly ITrakHoundClient _client;
        private readonly ITrakHoundVolume _volume;
        private readonly ITrakHoundVariableCollection _variables;


        /// <summary>
        /// The Configuration the Identity Provider was created from
        /// </summary>
        public ITrakHoundIdentityProviderConfiguration Configuration => _configuration;

        /// <summary>
        /// The Client used to access the Instance
        /// </summary>
        public ITrakHoundClient Client => _client;

        /// <summary>
        /// The Volume used to store Files for the Identity Provider
        /// </summary>
        public ITrakHoundVolume Volume => _volume;

        /// <summary>
        /// The Variables available to the Identity Provider
        /// </summary>
        public ITrakHoundVariableCollection Variables => _variables;

        private string _id;
        /// <summary>
        /// The unique Identifier of the Identity Provider (the Configuration Id)
        /// </summary>
        public string Id
        {
            get
            {
                if (_id == null) _id = _configuration?.Id;
                return _id;
            }
        }

        /// <summary>
        /// The name of the Identity Provider (the Type name)
        /// </summary>
        public string Name => GetType().Name;

        /// <summary>
        /// The ways the Identity Provider can verify an Identity
        /// </summary>
        public abstract TrakHoundIdentityProviderFeatures Features { get; }


        public TrakHoundIdentityProviderBase(
            ITrakHoundIdentityProviderConfiguration configuration,
            ITrakHoundClient client,
            ITrakHoundVolume volume,
            ITrakHoundVariableCollection variables
            )
        {
            _configuration = configuration;
            _client = client;
            _volume = volume;
            _variables = variables;
        }


        /// <summary>
        /// Verifies credentials sent with the Request (returns NoResult if not overridden)
        /// </summary>
        public Task<TrakHoundIdentityResult> Authenticate(TrakHoundIdentityContext context) => OnAuthenticate(context);

        /// <summary>
        /// Override to verify credentials sent with each Request (ex. an Authorization header)
        /// </summary>
        protected virtual Task<TrakHoundIdentityResult> OnAuthenticate(TrakHoundIdentityContext context) => Task.FromResult(TrakHoundIdentityResult.NoResult());


        /// <summary>
        /// Starts an interactive sign in (returns no Redirect if not overridden)
        /// </summary>
        public Task<TrakHoundIdentityRedirect> Challenge(TrakHoundIdentityContext context, string state) => OnChallenge(context, state);

        /// <summary>
        /// Override to start an interactive sign in by returning a Redirect to an external sign in page. The State must be returned by OnHandleCallback
        /// </summary>
        protected virtual Task<TrakHoundIdentityRedirect> OnChallenge(TrakHoundIdentityContext context, string state) => Task.FromResult(TrakHoundIdentityRedirect.None("Challenge Not Supported"));


        /// <summary>
        /// Completes an interactive sign in (returns Error if not overridden)
        /// </summary>
        public Task<TrakHoundIdentityResult> HandleCallback(TrakHoundIdentityContext context) => OnHandleCallback(context);

        /// <summary>
        /// Override to complete an interactive sign in when the external server redirects back to the Callback URL
        /// </summary>
        protected virtual Task<TrakHoundIdentityResult> OnHandleCallback(TrakHoundIdentityContext context) => Task.FromResult(TrakHoundIdentityResult.Error("Callback Not Supported"));


        /// <summary>
        /// Verifies a Username and Password (returns Error if not overridden)
        /// </summary>
        public Task<TrakHoundIdentityResult> SignIn(TrakHoundIdentityContext context, TrakHoundIdentityCredentials credentials) => OnSignIn(context, credentials);

        /// <summary>
        /// Override to verify a Username and Password entered on the Instance's sign in page
        /// </summary>
        protected virtual Task<TrakHoundIdentityResult> OnSignIn(TrakHoundIdentityContext context, TrakHoundIdentityCredentials credentials) => Task.FromResult(TrakHoundIdentityResult.Error("Sign In Not Supported"));


        /// <summary>
        /// Signs out of the external server (returns no Redirect if not overridden)
        /// </summary>
        public Task<TrakHoundIdentityRedirect> SignOut(TrakHoundIdentityContext context, TrakHoundIdentity identity, string returnUrl) => OnSignOut(context, identity, returnUrl);

        /// <summary>
        /// Override to sign out of the external server (ex. return a Redirect to an end session endpoint)
        /// </summary>
        protected virtual Task<TrakHoundIdentityRedirect> OnSignOut(TrakHoundIdentityContext context, TrakHoundIdentity identity, string returnUrl) => Task.FromResult(TrakHoundIdentityRedirect.None());


        /// <summary>
        /// Splits a comma separated Configuration Parameter into its trimmed values (empty if not set)
        /// </summary>
        protected string[] GetListParameter(string name)
        {
            var value = _configuration?.GetParameter(name);
            if (!string.IsNullOrEmpty(value))
            {
                return value.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            }

            return Array.Empty<string>();
        }
    }
}
