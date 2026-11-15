// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Controllers;
using TrakHound.Api;
using TrakHound.Clients;
using TrakHound.Instance.Api;
using TrakHound.Instance.Http;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Security;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// Identifies the caller of each Request (from the Session Cookie, an API Key, a Bearer Token, or a Module Token), authorizes access to the
    /// requested Resource, and sets the Security Context so that Service calls made while handling the Request are authorized too
    /// </summary>
    public class TrakHoundAuthenticationMiddleware
    {
        private readonly ITrakHoundInstance _instance;
        private readonly ITrakHoundSecurityManager _securityManager;
        private readonly TrakHoundIdentityStateStore _stateStore;
        private readonly RequestDelegate _next;
        private readonly ITrakHoundLogger _logger = new TrakHoundLogger<TrakHoundAuthenticationMiddleware>();


        public TrakHoundAuthenticationMiddleware(ITrakHoundInstance instance, ITrakHoundSecurityManager securityManager, TrakHoundIdentityStateStore stateStore, RequestDelegate next)
        {
            _instance = instance;
            _securityManager = securityManager;
            _stateStore = stateStore;
            _next = next;
        }


        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path;
            if (TrakHoundInstanceRoutes.IsStaticRoute(path) || TrakHoundInstanceRoutes.IsInitializationRoute(path) || TrakHoundInstanceRoutes.IsAdminRoute(path) || TrakHoundInstanceRoutes.IsIdentityRoute(path))
            {
                await _next(context);
                return;
            }

            // A Request made with a Module Token is made as the Module (ex. by a worker process that an Api started). Like a call through the Module's
            // own Client, it is checked against the Module's Zone (see WithModuleRequestSource) rather than a Security Profile
            var moduleToken = TrakHoundModuleTokenManager.GetRequestToken(context.Request);
            if (moduleToken != null && AcceptsModuleToken(context))
            {
                var moduleTokenEntry = TrakHoundModuleTokenManager.IsLocalRequest(context) ? _instance.ModuleTokens?.Validate(moduleToken) : null;
                if (moduleTokenEntry == null)
                {
                    _logger.LogDebug($"Module Token Rejected : {context.Connection.RemoteIpAddress}");
                    context.Response.StatusCode = 401;
                    context.Response.Headers.Append("WWW-Authenticate", "Bearer error=\"invalid_token\"");
                    return;
                }

                context.SetModuleToken(moduleTokenEntry);

                using (TrakHoundSecurityContext.Begin(null, null))
                using (TrakHound.Instance.Audit.TrakHoundAuditContext.Begin(TrakHound.Instance.Audit.TrakHoundAuditSources.Module, null, context.Connection.RemoteIpAddress?.ToString()))
                {
                    await _next(context);
                }
                return;
            }

            // Identify the caller (null for an anonymous Request). Rejected credentials leave the Request anonymous, so they only
            // matter for protected Resources (a Query Parameter that happens to share a Token's name can't break public pages)
            var (identity, credentialsRejected) = await GetIdentity(context);

            // A System API Controller must say how it is authorized, so one that doesn't is never served unprotected by mistake
            if (IsUnclassifiedSystemEndpoint(context))
            {
                _logger.LogError($"System API Endpoint has no System Resource : {context.Request.Path}");
                context.Response.StatusCode = 403;
                return;
            }

            // Resources that no Profile covers can only be used from the same machine (unless the Instance allows it). Service operations
            // and MCP Tools called by the Request are authorized through the Security Context, so it uses the same Authorizer
            var protectionRequired = TrakHoundSystemApiProtection.IsRequired(context, _instance.Configuration);
            ITrakHoundAuthorizer authorizer = protectionRequired ? new TrakHoundProtectedResourceAuthorizer(_securityManager) : _securityManager;

            using (TrakHoundSecurityContext.Begin(authorizer, identity))
            using (TrakHound.Instance.Audit.TrakHoundAuditContext.Begin(TrakHound.Instance.Audit.TrakHoundAuditSources.SystemApi, identity?.Name, context.Connection.RemoteIpAddress?.ToString()))
            {
                var (resourceId, permission) = GetResourceId(_instance, context);
                if (resourceId != null)
                {
                    if (_securityManager.GetResource(resourceId) == null)
                    {
                        context.Response.StatusCode = 404;
                        return;
                    }

                    var authorization = _securityManager.Authorize(identity, resourceId, permission);
                    if (protectionRequired && authorization.IsUncovered)
                    {
                        _logger.LogDebug($"Remote Request to an unprotected Resource Denied : {context.Connection.RemoteIpAddress} : {resourceId}");
                        context.Response.StatusCode = 403;
                        await context.Response.WriteAsync(TrakHoundSystemApiProtection.DeniedMessage);
                        return;
                    }

                    switch (authorization.Status)
                    {
                        case TrakHoundAuthorizationStatus.Allowed:
                            break;

                        case TrakHoundAuthorizationStatus.AuthenticationRequired:
                            _logger.LogDebug(authorization.Message);
                            if (credentialsRejected)
                            {
                                // Credentials were sent but are not valid (don't redirect a script or API client to sign in)
                                context.Response.StatusCode = 401;
                                context.Response.Headers.Append("WWW-Authenticate", "Bearer error=\"invalid_token\"");
                            }
                            else
                            {
                                Challenge(context, authorization.ProviderIds);
                            }
                            return;

                        default:
                            _logger.LogDebug(authorization.Message);
                            context.Response.StatusCode = 403;
                            return;
                    }
                }

                await _next(context);
            }
        }

        /// <summary>
        /// Module Tokens are only for the System API (and its Api Catalog) and Apis, not for App pages or the MCP Server (whose Tools don't make calls as the Module)
        /// </summary>
        private bool AcceptsModuleToken(HttpContext context)
        {
            var path = context.Request.Path;
            return TrakHoundInstanceRoutes.IsSystemApiRoute(path) ||
                TrakHoundInstanceRoutes.IsDeveloperRoute(path) ||
                _instance.ApiProvider.IsRouteValid(context);
        }

        private async Task<(TrakHoundIdentity Identity, bool CredentialsRejected)> GetIdentity(HttpContext context)
        {
            // Session Cookie (set after signing in with an Identity Provider)
            var authenticateResult = await context.AuthenticateAsync(TrakHoundIdentityClaims.AuthenticationScheme);
            if (authenticateResult.Succeeded)
            {
                var identity = TrakHoundIdentityClaims.FromPrincipal(authenticateResult.Principal);

                // The Identity Provider may have been removed, or have ended the Session (ex. the User was disabled), since the Session was created
                if (identity != null && _securityManager.IsSessionValid(identity)) return (identity, false);
            }

            // Credentials sent with the Request (ex. an API Key or Bearer Token). Identity Providers return NoResult when the Request has none for them
            var identityContext = TrakHoundIdentityHttp.CreateContext(context, _stateStore, _instance.Configuration.BasePath, null);
            var result = await _securityManager.Authenticate(identityContext);
            switch (result.Status)
            {
                case TrakHoundIdentityResultStatus.Success:
                    return (result.Identity, false);

                case TrakHoundIdentityResultStatus.Fail:
                case TrakHoundIdentityResultStatus.Error:
                    _logger.LogDebug($"Request Credentials Rejected : {context.Connection.RemoteIpAddress} : {result.Message}");
                    return (null, true);
            }

            return (null, false);
        }

        private void Challenge(HttpContext context, IReadOnlyList<string> providerIds)
        {
            // Browsers are sent to sign in with the first Identity Provider that supports interactive sign in
            if (TrakHoundIdentityHttp.IsBrowserNavigation(context.Request))
            {
                var signInProviderId = providerIds.FirstOrDefault(o =>
                {
                    var features = _securityManager.GetProvider(o)?.Features ?? TrakHoundIdentityProviderFeatures.None;
                    return features.HasFlag(TrakHoundIdentityProviderFeatures.Redirect) || features.HasFlag(TrakHoundIdentityProviderFeatures.Credentials);
                });

                if (signInProviderId != null)
                {
                    var basePath = _instance.Configuration.BasePath;
                    var returnUrl = TrakHoundIdentityHttp.GetExternalPath(basePath, $"{context.Request.Path}{context.Request.QueryString}");
                    var signInPath = TrakHoundIdentityHttp.GetExternalPath(basePath, $"{TrakHoundIdentityHttp.SignInPrefix}/{Uri.EscapeDataString(signInProviderId)}");

                    context.Response.Redirect($"{signInPath}?returnUrl={Uri.EscapeDataString(returnUrl)}");
                    return;
                }
            }

            context.Response.StatusCode = 401;
            context.Response.Headers.Append("WWW-Authenticate", "Bearer");
        }


        /// <summary>
        /// The Resource (and the Permission it requires) that a Request is for. Null for Requests that are not authorized here
        /// (ex. App pages, which are authorized by the Router, and Service operations, which are authorized when the Service is called)
        /// </summary>
        private (string ResourceId, string Permission) GetResourceId(ITrakHoundInstance instance, HttpContext httpContext)
        {
            var systemResource = GetSystemResource(httpContext);
            if (systemResource.ResourceId != null) return systemResource;

            return (GetApiResourceId(instance, httpContext), null);
        }

        /// <summary>
        /// System API Controllers declare their Resource with TrakHoundSystemResourceAttribute (the Action's takes precedence over the Controller's)
        /// and changes with TrakHoundSystemWriteAttribute
        /// </summary>
        private static (string ResourceId, string Permission) GetSystemResource(HttpContext httpContext)
        {
            var metadata = httpContext.GetEndpoint()?.Metadata;
            var resource = metadata?.GetMetadata<TrakHoundSystemResourceAttribute>();
            if (resource == null) return (null, null);

            var write = metadata.GetMetadata<TrakHoundSystemWriteAttribute>() != null;
            return (resource.ResourceId, TrakHoundSystemSecurity.GetPermission(write));
        }

        /// <summary>
        /// A System API Controller Action with neither a TrakHoundSystemResourceAttribute nor a TrakHoundServiceOperationsAttribute
        /// </summary>
        public static bool IsUnclassifiedSystemEndpoint(HttpContext httpContext)
        {
            if (!TrakHoundInstanceRoutes.IsSystemApiRoute(httpContext.Request.Path)) return false;

            var metadata = httpContext.GetEndpoint()?.Metadata;
            if (metadata?.GetMetadata<ControllerActionDescriptor>() == null) return false; // Not a Controller (ex. the MCP Server, which declares its Resource)

            return metadata.GetMetadata<TrakHoundSystemResourceAttribute>() == null && metadata.GetMetadata<TrakHoundServiceOperationsAttribute>() == null;
        }

        private string GetApiResourceId(ITrakHoundInstance instance, HttpContext httpContext)
        {
            var httpMethod = httpContext.Request.Method;
            var requestMethod = TrakHoundApi.GetApiMethod(httpMethod);

            // An Api that isn't served over HTTP is answered with a 404 by the TrakHoundApiMiddleware (don't Challenge first, which would show that it exists)
            if (instance.ApiProvider.IsRouteValid(httpContext) && instance.ApiProvider.IsHttpEnabled(httpContext))
            {
                // WebSocket and Server-Sent Events Requests are handled by Stream Endpoints (see TrakHoundApiMiddleware), so they are authorized for the Stream Endpoint
                var endpointType = TrakHoundApiMiddleware.IsStreamRequest(instance, httpContext) ? TrakHoundApiEndpointType.Stream : TrakHoundApiEndpointType.Request;

                var endpointInformation = instance.ApiProvider.GetEndpointInformation(httpContext, endpointType, requestMethod);
                if (endpointInformation != null)
                {
                    return TrakHoundApiInformation.GetEndpointResourceId(endpointInformation.Api.Id, endpointInformation.Controller.Id, endpointInformation.Id);
                }
            }

            return null;
        }
    }
}
