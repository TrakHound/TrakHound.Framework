// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Http;
using TrakHound.Instances;
using TrakHound.Security;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// Keeps the System Resources (and Service operations) that no Identity Profile covers from being used from other machines, so a new
    /// Instance isn't open to its network before it is protected. Requests from the same machine (ex. the Admin Interface or a local Client)
    /// are not affected, and SystemApiUnprotectedRemoteAccess turns it off
    /// </summary>
    public static class TrakHoundSystemApiProtection
    {
        /// <summary>
        /// Sent with the 403 response, since an unprotected Resource is otherwise reachable and the reason isn't obvious from the client
        /// </summary>
        public const string DeniedMessage = "Remote access to an unprotected System Resource is not allowed. Protect it with a Security Profile (Config > Security Profiles) or allow unprotected remote access (Settings > Interfaces).";


        /// <summary>
        /// Whether Resources that no Profile covers are denied for the Request
        /// </summary>
        public static bool IsRequired(HttpContext context, TrakHoundInstanceConfiguration configuration)
        {
            if (context == null) return false;
            if (configuration != null && configuration.SystemApiUnprotectedRemoteAccess) return false;
            if (!TrakHoundInstanceRoutes.IsSystemApiRoute(context.Request.Path)) return false;

            return !TrakHoundModuleTokenManager.IsLocalRequest(context);
        }
    }

    /// <summary>
    /// Authorizes with another Authorizer, but denies the Resources that no Profile covers (see TrakHoundSystemApiProtection). Used as the
    /// Security Context's Authorizer so that Service operations and MCP Tools called by the Request are denied the same way
    /// </summary>
    public sealed class TrakHoundProtectedResourceAuthorizer : ITrakHoundAuthorizer
    {
        private readonly ITrakHoundAuthorizer _authorizer;


        public TrakHoundProtectedResourceAuthorizer(ITrakHoundAuthorizer authorizer)
        {
            _authorizer = authorizer ?? throw new ArgumentNullException(nameof(authorizer));
        }


        public TrakHoundAuthorizationResult Authorize(TrakHoundIdentity identity, string resourceId, string permission = null)
        {
            var result = _authorizer.Authorize(identity, resourceId, permission);
            // The client gets the same fixed message as the System API routes (it names no Resource), and the log gets the Resource
            if (result.IsUncovered) return TrakHoundAuthorizationResult.Forbidden($"Remote access to an unprotected Resource : {resourceId}", TrakHoundSystemApiProtection.DeniedMessage);

            return result;
        }
    }
}
