// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Security;

namespace TrakHound.Instance.Controllers
{
    /// <summary>
    /// Signs out of the Instance ("/_identity/signout?returnUrl=..."). Removes the Session Cookie, then signs out of the
    /// Identity Provider's external server if it supports it (ex. OpenID Connect end session)
    /// </summary>
    public class IdentityRevokationController : ControllerBase
    {
        private readonly ITrakHoundInstance _instance;
        private readonly ITrakHoundSecurityManager _securityManager;
        private readonly TrakHoundIdentityStateStore _stateStore;
        private readonly ITrakHoundLogger _logger = new TrakHoundLogger<IdentityRevokationController>();


        public IdentityRevokationController(ITrakHoundInstance instance, TrakHoundIdentityStateStore stateStore)
        {
            _instance = instance;
            _securityManager = instance.SecurityManager;
            _stateStore = stateStore;
        }


        [AcceptVerbs("GET", "POST", Route = "_identity/signout")]
        public async Task<IActionResult> ProcessRevoke()
        {
            var basePath = _instance.Configuration.BasePath;
            var returnUrl = TrakHoundIdentityHttp.GetSafeReturnUrl(Request.Query["returnUrl"], basePath);

            var authenticateResult = await HttpContext.AuthenticateAsync(TrakHoundIdentityClaims.AuthenticationScheme);
            var identity = authenticateResult.Succeeded ? TrakHoundIdentityClaims.FromPrincipal(authenticateResult.Principal) : null;

            await HttpContext.SignOutAsync(TrakHoundIdentityClaims.AuthenticationScheme);

            if (identity != null)
            {
                _logger.LogInformation($"Signed Out : {identity.ProviderId} : {identity.UserId} : {HttpContext.Connection.RemoteIpAddress}");

                var provider = _securityManager.GetProvider(identity.ProviderId);
                if (provider != null)
                {
                    try
                    {
                        var context = TrakHoundIdentityHttp.CreateContext(HttpContext, _stateStore, basePath, provider.Id);
                        var absoluteReturnUrl = $"{Request.Scheme}://{Request.Host.Value}{returnUrl}";

                        var redirect = await provider.SignOut(context, identity, absoluteReturnUrl);
                        if (redirect.IsRedirect) return Redirect(redirect.Location);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Identity Provider ({provider.Id}) Sign Out Error : {ex.Message}");
                    }
                }
            }

            return Redirect(returnUrl);
        }
    }
}
