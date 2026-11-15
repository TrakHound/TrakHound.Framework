// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Threading.Tasks;

namespace TrakHound.Security
{
    /// <summary>
    /// The incoming Request an Identity Provider is verifying (independent of the HTTP framework)
    /// </summary>
    public interface ITrakHoundIdentityRequest
    {
        /// <summary>
        /// The HTTP Method (ex. GET or POST)
        /// </summary>
        string Method { get; }

        /// <summary>
        /// The Path of the Request (relative to the Instance's Base Path)
        /// </summary>
        string Path { get; }

        /// <summary>
        /// The Address of the Client that sent the Request
        /// </summary>
        string ClientAddress { get; }

        /// <summary>
        /// Gets the value of a Request Header (null if not present)
        /// </summary>
        string GetHeader(string name);

        /// <summary>
        /// Gets the value of a Query Parameter (null if not present)
        /// </summary>
        string GetQueryParameter(string name);

        /// <summary>
        /// Gets the value of a Form field from the Request Body (null if not present or not a Form)
        /// </summary>
        Task<string> GetFormValue(string name);
    }

    /// <summary>
    /// Short lived, server side storage for Identity Provider sign in flows (ex. OpenID Connect nonce and PKCE values)
    /// </summary>
    public interface ITrakHoundIdentityStateStore
    {
        /// <summary>
        /// Stores a value that is removed after the specified lifetime
        /// </summary>
        void Set(string key, string value, TimeSpan lifetime);

        /// <summary>
        /// Gets and removes a value (null if not found or expired). Values can only be read once to prevent replay
        /// </summary>
        string Take(string key);
    }

    /// <summary>
    /// The context given to an Identity Provider for each operation
    /// </summary>
    public sealed class TrakHoundIdentityContext
    {
        /// <summary>
        /// The incoming Request
        /// </summary>
        public ITrakHoundIdentityRequest Request { get; }

        /// <summary>
        /// Short lived storage for sign in flows
        /// </summary>
        public ITrakHoundIdentityStateStore State { get; }

        /// <summary>
        /// The absolute URL the Identity Provider's external server must redirect back to (ex. OpenID Connect redirect_uri)
        /// </summary>
        public string CallbackUrl { get; }

        /// <summary>
        /// The absolute URL of the Instance (including the Base Path)
        /// </summary>
        public string BaseUrl { get; }


        public TrakHoundIdentityContext(ITrakHoundIdentityRequest request, ITrakHoundIdentityStateStore state, string baseUrl, string callbackUrl)
        {
            Request = request;
            State = state;
            BaseUrl = baseUrl;
            CallbackUrl = callbackUrl;
        }
    }

    /// <summary>
    /// A Username and Password entered on the Instance's sign in page
    /// </summary>
    public sealed class TrakHoundIdentityCredentials
    {
        public string Username { get; }

        public string Password { get; }


        public TrakHoundIdentityCredentials(string username, string password)
        {
            Username = username;
            Password = password;
        }
    }

    /// <summary>
    /// The ways an Identity Provider can verify an Identity
    /// </summary>
    [Flags]
    public enum TrakHoundIdentityProviderFeatures
    {
        None = 0,

        /// <summary>
        /// Verifies credentials sent with each Request (ex. an API Key or Bearer Token in the Authorization header)
        /// </summary>
        Token = 1,

        /// <summary>
        /// Signs in by redirecting to an external server, which redirects back to the Callback (ex. OpenID Connect)
        /// </summary>
        Redirect = 2,

        /// <summary>
        /// Signs in with a Username and Password entered on the Instance's sign in page (ex. LDAP)
        /// </summary>
        Credentials = 4
    }
}
