// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Http;

namespace TrakHound.Clients
{
    /// <summary>
    /// A Token issued by the Instance to a Module (an Api, App, Function or Worker) so that code running outside of the Instance's process
    /// (ex. a Python or Node.js worker) can call the System API as the Module. The Module's Zone applies to those calls, the same as to calls
    /// made through the Module's Client.
    /// </summary>
    /// <remarks>
    /// Module Tokens are kept in memory only (they are invalid after the Instance restarts), are only accepted from the same machine, and are
    /// sent in the "X-TrakHound-Module-Token" Header (or as a Bearer Token).
    /// </remarks>
    public class TrakHoundModuleToken
    {
        /// <summary>
        /// The Token to send with each Request ("thm_" followed by a random secret)
        /// </summary>
        public string Token { get; set; }

        /// <summary>
        /// The URL of the Instance's System API (ex. http://127.0.0.1:8472/)
        /// </summary>
        public string InstanceUrl { get; set; }

        /// <summary>
        /// The ID of the Service Router that the Module's Client uses (null for the default Router)
        /// </summary>
        public string RouterId { get; set; }

        /// <summary>
        /// When the Token expires (UTC). Null if it is valid until it is revoked (or the Instance restarts)
        /// </summary>
        public DateTime? Expires { get; set; }


        /// <summary>
        /// Gets the Environment Variables to start a Module process with ("TRAKHOUND_URL", "TRAKHOUND_MODULE_TOKEN" and "TRAKHOUND_ROUTER_ID").
        /// The process can then connect with <see cref="TrakHoundHttpClient.FromModuleEnvironment"/> (or the equivalent in its own language)
        /// </summary>
        public IDictionary<string, string> GetEnvironmentVariables()
        {
            var variables = new Dictionary<string, string>();
            if (!string.IsNullOrEmpty(InstanceUrl)) variables[TrakHoundHttpConstants.InstanceUrlVariable] = InstanceUrl;
            if (!string.IsNullOrEmpty(Token)) variables[TrakHoundHttpConstants.ModuleTokenVariable] = Token;
            if (!string.IsNullOrEmpty(RouterId)) variables[TrakHoundHttpConstants.RouterIdVariable] = RouterId;
            return variables;
        }

        /// <summary>
        /// Creates a Client Configuration that connects to the Instance as the Module. Null if the Instance URL is not valid
        /// </summary>
        public TrakHoundHttpClientConfiguration GetClientConfiguration()
        {
            var configuration = TrakHoundHttpClientConfiguration.FromUrl(InstanceUrl);
            if (configuration != null) configuration.ModuleToken = Token;
            return configuration;
        }
    }

    /// <summary>
    /// A Client that can issue Module Tokens for the Module it belongs to (the Client given to a Module by the Instance)
    /// </summary>
    public interface ITrakHoundModuleTokenIssuer
    {
        /// <summary>
        /// Issues a Token that makes Requests as the Module this Client belongs to. Returns null if the Client doesn't belong to a Module
        /// </summary>
        /// <param name="lifetime">How long the Token is valid for (null until it is revoked, the Module is unloaded, or the Instance restarts)</param>
        Task<TrakHoundModuleToken> CreateModuleToken(TimeSpan? lifetime = null);

        /// <summary>
        /// Revokes a Token issued for the Module this Client belongs to. Returns false if it is not found
        /// </summary>
        Task<bool> RevokeModuleToken(string token);
    }

    /// <summary>
    /// Extension methods for issuing Module Tokens
    /// </summary>
    public static class TrakHoundModuleTokenExtensions
    {
        /// <summary>
        /// Issues a Token that makes Requests as the Module the Client belongs to. Returns null if the Client can't issue Module Tokens
        /// (ex. a TrakHoundHttpClient, or a Client that doesn't belong to a Module)
        /// </summary>
        public static Task<TrakHoundModuleToken> CreateModuleToken(this ITrakHoundClient client, TimeSpan? lifetime = null)
        {
            if (client is ITrakHoundModuleTokenIssuer issuer) return issuer.CreateModuleToken(lifetime);
            return Task.FromResult<TrakHoundModuleToken>(null);
        }

        /// <summary>
        /// Revokes a Module Token issued through the Client. Returns false if the Client can't issue Module Tokens or the Token is not found
        /// </summary>
        public static Task<bool> RevokeModuleToken(this ITrakHoundClient client, string token)
        {
            if (client is ITrakHoundModuleTokenIssuer issuer) return issuer.RevokeModuleToken(token);
            return Task.FromResult(false);
        }
    }
}
