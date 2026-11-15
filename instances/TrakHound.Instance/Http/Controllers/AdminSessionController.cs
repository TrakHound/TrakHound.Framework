// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using TrakHound.Instance.Security;
using TrakHound.Instance.Services;
using TrakHound.Instances;
using TrakHound.Logging;

namespace TrakHound.Instance.Controllers
{
    /// <summary>
    /// Sets and removes the Admin Interface Session Cookie (see TrakHoundAdminSessions). The Login page checks the Password and
    /// sends the browser here with a one time Code, since a Blazor Circuit can't set an HttpOnly Cookie itself
    /// </summary>
    [ApiController]
    [Route("_admin/session")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public class AdminSessionController : ControllerBase
    {
        private const string _loginPath = "_admin/login";

        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundAdminSessions _sessions;
        private readonly ITrakHoundLogger _logger = new TrakHoundLogger<AdminSessionController>();


        public AdminSessionController(ITrakHoundInstance instance, TrakHoundAdminSessions sessions)
        {
            _instance = instance;
            _sessions = sessions;
        }


        [HttpGet("signin")]
        public async Task<IActionResult> SignIn([FromQuery] string code, [FromQuery] string linkId = null)
        {
            Response.Headers.CacheControl = "no-store";

            var principal = _sessions.TakeCode(code);
            if (principal == null)
            {
                _logger.LogInformation($"Admin Sign In Code Rejected : {HttpContext.Connection.RemoteIpAddress}");
                return RedirectTo(_loginPath);
            }

            var properties = new AuthenticationProperties
            {
                IsPersistent = true,
                ExpiresUtc = DateTimeOffset.UtcNow.Add(TrakHoundAdminSessions.SessionLifetime),
                AllowRefresh = false
            };
            await HttpContext.SignInAsync(TrakHoundAdminSessions.AuthenticationScheme, principal, properties);

            _logger.LogInformation($"Admin Signed In : {HttpContext.Connection.RemoteIpAddress}");

            // The page that was asked for, or the first area the Admin's Roles let them view
            var link = LinkStore.Get(linkId);
            return RedirectTo(!string.IsNullOrEmpty(link) ? link : GetHomePath(principal));
        }

        [HttpGet("signout")]
        public async Task<IActionResult> SignOutAdmin()
        {
            Response.Headers.CacheControl = "no-store";

            // The Session was revoked by the Admin Interface before it sent the browser here, so this only removes the Cookie
            await HttpContext.SignOutAsync(TrakHoundAdminSessions.AuthenticationScheme);
            return RedirectTo(_loginPath);
        }


        private string GetHomePath(System.Security.Claims.ClaimsPrincipal principal)
        {
            var context = new TrakHoundAdminContext(_sessions.Validate(principal));
            return TrakHoundAdminSecurity.GetHomePath(resourceId => context.CanSome(resourceId, TrakHoundAdminSecurity.ReadPermission));
        }

        private IActionResult RedirectTo(string path)
        {
            var basePath = _instance.Configuration.BasePath;
            return Redirect(TrakHoundIdentityHttp.GetSafeReturnUrl(TrakHoundIdentityHttp.GetExternalPath(basePath, path), basePath));
        }
    }
}
