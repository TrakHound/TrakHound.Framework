// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Net;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.WebUtilities;
using TrakHound.Blazor.Apps;
using TrakHound.Instance.Http;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Security;

namespace TrakHound.Instance.Controllers
{
    /// <summary>
    /// Interactive sign in with an Identity Provider: "/_identity/signin/{providerId}" starts the sign in (a Redirect to an external server,
    /// or a Username and Password form, see TrakHoundSignInPage) and "/_identity/callback/{providerId}" completes a Redirect sign in
    /// </summary>
    public class IdentityCallbackController : ControllerBase
    {
        // How long a sign in can take (between starting it and completing it)
        private static readonly TimeSpan _signInLifetime = TimeSpan.FromMinutes(15);

        // How long a Session lasts when the Identity doesn't specify when it expires
        private static readonly TimeSpan _sessionLifetime = TimeSpan.FromHours(8);

        // Slows down repeated guessing of passwords
        private static readonly TimeSpan _failedSignInDelay = TimeSpan.FromSeconds(1);

        private const string _returnUrlStatePrefix = "returnUrl:";

        private readonly ITrakHoundInstance _instance;
        private readonly ITrakHoundSecurityManager _securityManager;
        private readonly TrakHoundIdentityStateStore _stateStore;
        private readonly IAntiforgery _antiforgery;
        private readonly TrakHoundSignInThrottle _throttle;
        private readonly ITrakHoundLogger _logger;


        public IdentityCallbackController(ITrakHoundInstance instance, TrakHoundIdentityStateStore stateStore, IAntiforgery antiforgery, TrakHoundSignInThrottle throttle)
        {
            _instance = instance;
            _securityManager = instance.SecurityManager;
            _stateStore = stateStore;
            _antiforgery = antiforgery;
            _throttle = throttle;
            _logger = instance.LogProvider?.GetLoggerByName(TrakHoundSecurityManager.LoggerName) ?? new TrakHoundLogger<IdentityCallbackController>();
        }


        [AcceptVerbs("GET", "POST", Route = "_identity/signin/{**slug}")]
        public async Task<IActionResult> SignIn()
        {
            var providerId = GetProviderId(TrakHoundIdentityHttp.SignInPrefix);
            var provider = _securityManager.GetProvider(providerId);
            if (provider == null) return NotFound();

            var basePath = _instance.Configuration.BasePath;
            var context = TrakHoundIdentityHttp.CreateContext(HttpContext, _stateStore, basePath, provider.Id);

            if (provider.Features.HasFlag(TrakHoundIdentityProviderFeatures.Credentials))
            {
                if (HttpMethods.IsPost(Request.Method))
                {
                    if (!await _antiforgery.IsRequestValidAsync(HttpContext)) return BadRequest();

                    var username = await context.Request.GetFormValue("username");
                    var password = await context.Request.GetFormValue("password");
                    var returnUrl = TrakHoundIdentityHttp.GetSafeReturnUrl(await context.Request.GetFormValue("returnUrl"), basePath);

                    // Checked before the Identity Provider is called, so a locked out client can't keep guessing (or load the provider's server)
                    var clientAddress = HttpContext.Connection.RemoteIpAddress?.ToString();
                    var lockout = _throttle.GetLockout(provider.Id, clientAddress, username);
                    if (lockout.HasValue)
                    {
                        _logger.LogInformation($"Sign In Locked Out : {provider.Id} : {username} : {clientAddress}");
                        Response.Headers.RetryAfter = ((int)Math.Ceiling(lockout.Value.TotalSeconds)).ToString();
                        return ShowSignIn(provider, returnUrl, username, TrakHoundSignInPage.ErrorLocked, TrakHoundSignInThrottle.GetMessage(lockout.Value), 429);
                    }

                    TrakHoundIdentityResult result;
                    if (!string.IsNullOrEmpty(username) && !string.IsNullOrEmpty(password))
                    {
                        result = await RunProvider(provider, () => provider.SignIn(context, new TrakHoundIdentityCredentials(username, password)));
                    }
                    else
                    {
                        result = TrakHoundIdentityResult.Fail("Username and Password are required");
                    }

                    if (result.IsSuccess)
                    {
                        _throttle.RecordSuccess(provider.Id, clientAddress, username);
                        await StartSession(provider, result.Identity);
                        return Redirect(returnUrl);
                    }

                    // An Error (ex. the LDAP server is down) isn't a wrong password, so it isn't counted
                    if (result.Status != TrakHoundIdentityResultStatus.Error) _throttle.RecordFailure(provider.Id, clientAddress, username);

                    _logger.LogInformation($"Sign In Failed : {provider.Id} : {username} : {clientAddress} : {result.Message}");
                    await Task.Delay(_failedSignInDelay);

                    return result.Status == TrakHoundIdentityResultStatus.Error ?
                        ShowSignIn(provider, returnUrl, username, TrakHoundSignInPage.ErrorUnavailable, "Sign in is not available. Please try again later.") :
                        ShowSignIn(provider, returnUrl, username, TrakHoundSignInPage.ErrorInvalid, "Invalid username or password.");
                }

                return ShowSignIn(provider, TrakHoundIdentityHttp.GetSafeReturnUrl(Request.Query["returnUrl"], basePath), null, null, null);
            }

            if (provider.Features.HasFlag(TrakHoundIdentityProviderFeatures.Redirect))
            {
                // The State links the Callback back to this sign in (and the URL to return to)
                var state = Convert.ToHexString(System.Security.Cryptography.RandomNumberGenerator.GetBytes(16)).ToLowerInvariant();
                _stateStore.Set(_returnUrlStatePrefix + state, TrakHoundIdentityHttp.GetSafeReturnUrl(Request.Query["returnUrl"], basePath), _signInLifetime);

                TrakHoundIdentityRedirect redirect;
                try
                {
                    redirect = await provider.Challenge(context, state);
                }
                catch (Exception ex)
                {
                    redirect = TrakHoundIdentityRedirect.None(ex.Message);
                }

                if (redirect.IsRedirect) return Redirect(redirect.Location);

                _logger.LogError($"Sign In Error : {provider.Id} : {redirect.Message}");
                return StatusCode(503);
            }

            return NotFound();
        }

        [AcceptVerbs("GET", "POST", Route = "_identity/callback/{**slug}")]
        public async Task<IActionResult> ProcessCallback()
        {
            var providerId = GetProviderId(TrakHoundIdentityHttp.CallbackPrefix);
            var provider = _securityManager.GetProvider(providerId);
            if (provider == null || !provider.Features.HasFlag(TrakHoundIdentityProviderFeatures.Redirect)) return NotFound();

            var context = TrakHoundIdentityHttp.CreateContext(HttpContext, _stateStore, _instance.Configuration.BasePath, provider.Id);
            var result = await RunProvider(provider, () => provider.HandleCallback(context));

            // The Return URL can only be used once (the State is removed when read)
            var returnUrl = result.State != null ? _stateStore.Take(_returnUrlStatePrefix + result.State) : null;

            if (result.IsSuccess && returnUrl != null)
            {
                await StartSession(provider, result.Identity);
                return Redirect(TrakHoundIdentityHttp.GetSafeReturnUrl(returnUrl, _instance.Configuration.BasePath));
            }

            _logger.LogInformation($"Sign In Failed : {provider.Id} : {HttpContext.Connection.RemoteIpAddress} : {(result.IsSuccess ? "Unknown or Expired State" : result.Message)}");
            return StatusCode(result.Status == TrakHoundIdentityResultStatus.Error ? 503 : 401);
        }


        [AcceptVerbs("GET", "HEAD", Route = "_identity/" + TrakHoundSignInPage.AssetsRoute + "/{providerId}/{**path}")]
        public async Task Asset(string providerId, string path)
        {
            var provider = _securityManager.GetProvider(providerId);
            await TrakHoundInitializationPage.SendAsset(HttpContext, provider != null ? GetSignInPageFolder(provider) : null, "/" + path);
        }


        private async Task<TrakHoundIdentityResult> RunProvider(ITrakHoundIdentityProvider provider, Func<Task<TrakHoundIdentityResult>> function)
        {
            try
            {
                return await function();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Identity Provider ({provider.Id}) Error : {ex.Message}");
                return TrakHoundIdentityResult.Error(ex.Message);
            }
        }

        private async Task StartSession(ITrakHoundIdentityProvider provider, TrakHoundIdentity identity)
        {
            // The Identity always belongs to the Identity Provider that verified it
            identity.ProviderId = provider.Id;

            var expires = DateTime.UtcNow.Add(_sessionLifetime);
            if (identity.Expires.HasValue && identity.Expires.Value.ToUniversalTime() < expires) expires = identity.Expires.Value.ToUniversalTime();

            var properties = new AuthenticationProperties();
            properties.ExpiresUtc = expires;
            properties.IsPersistent = false;
            properties.AllowRefresh = false;

            await HttpContext.SignInAsync(TrakHoundIdentityClaims.AuthenticationScheme, TrakHoundIdentityClaims.ToPrincipal(identity), properties);

            _logger.LogInformation($"Signed In : {provider.Id} : {identity.UserId} : {HttpContext.Connection.RemoteIpAddress}");
        }

        private string GetProviderId(string prefix)
        {
            return TrakHound.Url.GetRouteParameter(HttpContext.Request.Path, $"{prefix}/{{providerId}}", "providerId");
        }

        /// <summary>
        /// Shows the provider's sign in page. A page that isn't available falls back to the Instance's own form, so a mistake can't stop everyone from signing in
        /// </summary>
        private IActionResult ShowSignIn(ITrakHoundIdentityProvider provider, string returnUrl, string username, string error, string message, int? statusCode = null)
        {
            var route = GetSignInRoute(provider);
            if (route != null) return RedirectToSignInRoute(provider, route, returnUrl, error);

            var folder = GetSignInPageFolder(provider);
            var html = TrakHoundSignInPage.ReadPage(folder);
            if (html != null) return SignInPage(provider, html, returnUrl, username, message, statusCode);

            if (!string.IsNullOrEmpty(provider.Configuration?.GetParameter(TrakHoundSignInPage.PageParameter)))
            {
                _logger.LogWarning($"Sign In Page Not Found : {provider.Id} : {provider.Configuration.GetParameter(TrakHoundSignInPage.PageParameter)}");
            }

            return SignInForm(provider, returnUrl, username, message, statusCode);
        }

        /// <summary>
        /// The route of the provider's App sign in page. Null if there isn't one, or if the Page can't be used : it must exist,
        /// and an anonymous Identity must be able to open it (otherwise signing in would redirect back here, forever)
        /// </summary>
        private string GetSignInRoute(ITrakHoundIdentityProvider provider)
        {
            var parameter = provider.Configuration?.GetParameter(TrakHoundSignInPage.RouteParameter);
            if (string.IsNullOrWhiteSpace(parameter)) return null;

            var route = "/" + parameter.Trim().TrimStart('/');

            // Apps are only served on the HTTP Port (not a separate Admin Port)
            string resourceId = null;
            var status = TrakHoundAdminPort.IsHttpConnection(HttpContext.Connection.LocalPort) ? TrakHoundSignInPage.CheckRoute(_instance, route, out resourceId) : TrakHoundSignInRouteStatus.AppsNotServed;
            switch (status)
            {
                case TrakHoundSignInRouteStatus.Ok: return route;
                case TrakHoundSignInRouteStatus.RequiresSignIn: _logger.LogWarning($"Sign In Route Requires Signing In : {provider.Id} : {route} : {resourceId}"); break;
                default: _logger.LogWarning($"Sign In Route Not Found : {provider.Id} : {route}"); break;
            }

            return null;
        }

        private string GetSignInPageFolder(ITrakHoundIdentityProvider provider)
        {
            var page = provider.Configuration?.GetParameter(TrakHoundSignInPage.PageParameter);
            return !string.IsNullOrWhiteSpace(page) ? TrakHoundSignInPage.GetFolder(_securityManager.GetProviderPackageDirectory(provider.Id), page) : null;
        }

        // Sent to the App Page as Query Parameters, since a Page running in a Circuit doesn't have a Request to get them from. Sets the antiforgery cookie for the form
        private RedirectResult RedirectToSignInRoute(ITrakHoundIdentityProvider provider, string route, string returnUrl, string error)
        {
            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
            var basePath = _instance.Configuration.BasePath;

            var query = new Dictionary<string, string>
            {
                ["action"] = GetSignInAction(provider),
                ["tokenName"] = tokens.FormFieldName,
                ["token"] = tokens.RequestToken,
                ["returnUrl"] = returnUrl
            };
            if (error != null) query["error"] = error;

            Response.Headers.CacheControl = "no-store";
            Response.Headers["Referrer-Policy"] = "no-referrer";
            return Redirect(QueryHelpers.AddQueryString(TrakHoundIdentityHttp.GetExternalPath(basePath, route), query));
        }

        private ContentResult SignInPage(ITrakHoundIdentityProvider provider, string html, string returnUrl, string username, string message, int? statusCode)
        {
            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
            var assets = TrakHoundIdentityHttp.GetExternalPath(_instance.Configuration.BasePath, $"{TrakHoundIdentityHttp.IdentityPrefix}/{TrakHoundSignInPage.AssetsRoute}/{Uri.EscapeDataString(provider.Id)}");
            var pageTokens = TrakHoundSignInPage.GetTokens(GetSignInAction(provider), tokens.FormFieldName, tokens.RequestToken, returnUrl, username, message, assets);

            Response.Headers.CacheControl = "no-store";
            return new ContentResult { Content = TrakHoundInitializationPage.ReplaceTokens(html, pageTokens), ContentType = "text/html; charset=utf-8", StatusCode = statusCode ?? (message != null ? 401 : 200) };
        }

        private string GetSignInAction(ITrakHoundIdentityProvider provider)
        {
            return TrakHoundIdentityHttp.GetExternalPath(_instance.Configuration.BasePath, $"{TrakHoundIdentityHttp.SignInPrefix}/{Uri.EscapeDataString(provider.Id)}");
        }

        private ContentResult SignInForm(ITrakHoundIdentityProvider provider, string returnUrl, string username, string message, int? statusCode = null)
        {
            var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
            var action = GetSignInAction(provider);
            var title = provider.Configuration?.Description ?? provider.Id;

            // Optional branding, from the Identity Provider's Parameters. The Logo is a path in the Instance that doesn't require signing in, ex. _init/assets/logo.svg
            var logo = provider.Configuration?.GetParameter(TrakHoundSignInPage.LogoParameter);
            var product = provider.Configuration?.GetParameter(TrakHoundSignInPage.ProductParameter);
            var hasBrand = !string.IsNullOrEmpty(logo) || !string.IsNullOrEmpty(product);
            var logoPath = !string.IsNullOrEmpty(logo) ? TrakHoundIdentityHttp.GetExternalPath(_instance.Configuration.BasePath, "/" + logo.TrimStart('/')) : null;
            var logoHtml = logoPath != null ? $"<img src=\"{E(logoPath)}\" alt=\"\" />" : "";
            var productHtml = !string.IsNullOrEmpty(product) ? $"<span>{E(product)}</span>" : "";
            var brand = $"<div class=\"brand\">{logoHtml}{productHtml}</div>";

            static string E(string value) => WebUtility.HtmlEncode(value ?? "");

            var html = $@"<!DOCTYPE html>
<html lang=""en"">
<head>
<meta charset=""utf-8"" />
<meta name=""viewport"" content=""width=device-width, initial-scale=1"" />
<title>{(!string.IsNullOrEmpty(product) ? $"Sign In - {E(product)}" : "Sign In")}</title>
<style>
:root {{ color-scheme: light; --bg: #eef1f5; --bg-glow: rgba(43, 108, 176, 0.10); --panel: #ffffff; --text: #1d2129; --muted: #5c6370; --border: #d9dde3; --input: #f8f9fb; --accent: #2b6cb0; --accent-hover: #245d99; --accent-soft: rgba(43, 108, 176, 0.12); --focus: rgba(43, 108, 176, 0.35); --error: #c53030; --error-bg: rgba(197, 48, 48, 0.08); --error-border: rgba(197, 48, 48, 0.35); --shadow: 0 20px 40px -16px rgba(15, 23, 42, 0.25), 0 2px 6px rgba(15, 23, 42, 0.06); }}
@media (prefers-color-scheme: dark) {{ :root {{ color-scheme: dark; --bg: #12151b; --bg-glow: rgba(99, 164, 232, 0.10); --panel: #1c2028; --text: #e6e8eb; --muted: #9aa1ac; --border: #2e343e; --input: #151920; --accent: #3d82cc; --accent-hover: #4b90da; --accent-soft: rgba(99, 164, 232, 0.14); --focus: rgba(99, 164, 232, 0.4); --error: #f07171; --error-bg: rgba(240, 113, 113, 0.10); --error-border: rgba(240, 113, 113, 0.35); --shadow: 0 24px 48px -16px rgba(0, 0, 0, 0.6), 0 2px 6px rgba(0, 0, 0, 0.3); }} }}
* {{ box-sizing: border-box; }}
body {{ margin: 0; min-height: 100vh; display: flex; align-items: center; justify-content: center; padding: 24px 16px; background: radial-gradient(ellipse at 50% 0%, var(--bg-glow), transparent 65%), var(--bg); color: var(--text); font-family: system-ui, -apple-system, 'Segoe UI', Roboto, sans-serif; font-size: 15px; line-height: 1.4; }}
.card {{ width: 100%; max-width: 380px; background: var(--panel); border: 1px solid var(--border); border-radius: 12px; box-shadow: var(--shadow); overflow: hidden; animation: fade-in 0.3s ease-out; }}
@keyframes fade-in {{ from {{ opacity: 0; transform: translateY(6px); }} to {{ opacity: 1; transform: none; }} }}
@media (prefers-reduced-motion: reduce) {{ .card {{ animation: none; }} }}
.brand {{ display: flex; align-items: center; justify-content: center; gap: 12px; padding: 20px 32px; color: #fff; background: linear-gradient(180deg, #2464a8 0%, #1b4b7e 100%); }}
.brand img {{ display: block; height: 30px; }}
.brand span {{ font-size: 1.2rem; font-weight: 500; }}
form {{ padding: 32px; }}
.heading {{ display: flex; flex-direction: column; align-items: center; margin-bottom: 24px; text-align: center; }}
.heading-icon {{ display: flex; align-items: center; justify-content: center; width: 44px; height: 44px; margin-bottom: 14px; color: var(--accent); background: var(--accent-soft); border-radius: 50%; }}
.heading-icon svg {{ width: 20px; height: 20px; }}
h1 {{ margin: 0; font-size: 1.4rem; font-weight: 600; letter-spacing: -0.01em; }}
.provider {{ margin: 4px 0 0; color: var(--muted); font-size: 0.9rem; }}
.error {{ display: flex; align-items: flex-start; gap: 10px; margin: 0 0 20px; padding: 10px 12px; color: var(--error); font-size: 0.9rem; background: var(--error-bg); border: 1px solid var(--error-border); border-left-width: 3px; border-radius: 6px; }}
.status {{ position: absolute; width: 1px; height: 1px; overflow: hidden; clip: rect(0 0 0 0); white-space: nowrap; }}
.error svg {{ flex: none; width: 16px; height: 16px; margin-top: 2px; }}
.field {{ margin-bottom: 16px; }}
label {{ display: block; margin-bottom: 6px; font-size: 0.85rem; font-weight: 500; }}
input[type=text], input[type=password] {{ width: 100%; height: 42px; padding: 0 12px; font: inherit; color: var(--text); background: var(--input); border: 1px solid var(--border); border-radius: 6px; transition: border-color 0.15s, box-shadow 0.15s; }}
input[type=text]:focus, input[type=password]:focus {{ outline: none; border-color: var(--accent); box-shadow: 0 0 0 3px var(--focus); }}
button {{ display: flex; align-items: center; justify-content: center; gap: 10px; width: 100%; height: 42px; margin-top: 8px; font: inherit; font-weight: 600; color: #fff; background: var(--accent); border: 0; border-radius: 6px; cursor: pointer; transition: background-color 0.15s, box-shadow 0.15s; }}
button:hover {{ background: var(--accent-hover); }}
button:focus-visible {{ outline: none; box-shadow: 0 0 0 3px var(--focus); }}
.spinner {{ display: none; width: 16px; height: 16px; border: 2px solid rgba(255, 255, 255, 0.35); border-top-color: #fff; border-radius: 50%; animation: spin 0.7s linear infinite; }}
@keyframes spin {{ to {{ transform: rotate(360deg); }} }}
@media (prefers-reduced-motion: reduce) {{ .spinner {{ animation-duration: 2s; }} }}
form.loading button {{ cursor: progress; background: var(--accent-hover); }}
form.loading .spinner {{ display: block; }}
form.loading input {{ opacity: 0.7; }}
@media (max-width: 420px) {{ form {{ padding: 24px 20px; }} }}
</style>
</head>
<body>
<div class=""card"">
{(hasBrand ? brand : "")}
<form method=""post"" action=""{E(action)}"">
<div class=""heading"">
<span class=""heading-icon"" aria-hidden=""true""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round""><rect x=""4"" y=""11"" width=""16"" height=""10"" rx=""2"" /><path d=""M8 11V7a4 4 0 0 1 8 0v4"" /></svg></span>
<h1>Sign In</h1>
<p class=""provider"">{E(title)}</p>
</div>
{(message != null ? $@"<div class=""error"" role=""alert""><svg viewBox=""0 0 24 24"" fill=""none"" stroke=""currentColor"" stroke-width=""2"" stroke-linecap=""round"" stroke-linejoin=""round"" aria-hidden=""true""><circle cx=""12"" cy=""12"" r=""10"" /><path d=""M12 8v4M12 16h.01"" /></svg><span>{E(message)}</span></div>" : "")}
<input type=""hidden"" name=""{E(tokens.FormFieldName)}"" value=""{E(tokens.RequestToken)}"" />
<input type=""hidden"" name=""returnUrl"" value=""{E(returnUrl)}"" />
<div class=""field"">
<label for=""username"">Username</label>
<input type=""text"" id=""username"" name=""username"" value=""{E(username)}"" autocomplete=""username"" autocapitalize=""none"" spellcheck=""false"" {(string.IsNullOrEmpty(username) ? "autofocus " : "")}required />
</div>
<div class=""field"">
<label for=""password"">Password</label>
<input type=""password"" id=""password"" name=""password"" autocomplete=""current-password"" {(!string.IsNullOrEmpty(username) ? "autofocus " : "")}required />
</div>
<button type=""submit""><span class=""spinner"" aria-hidden=""true""></span><span class=""label"">Sign In</span></button>
<p class=""status"" role=""status"" aria-live=""polite"" hidden></p>
</form>
</div>
<script>
(function () {{
  var form = document.querySelector('form');
  if (!form) return;
  var button = form.querySelector('button');
  var label = button.querySelector('.label');
  var status = form.querySelector('.status');
  var inputs = form.querySelectorAll('input:not([type=hidden])');
  function reset() {{
    form.classList.remove('loading');
    button.removeAttribute('aria-busy');
    label.textContent = 'Sign In';
    status.hidden = true;
    status.textContent = '';
    inputs.forEach(function (input) {{ input.readOnly = false; }});
  }}
  form.addEventListener('submit', function (e) {{
    if (form.classList.contains('loading')) {{ e.preventDefault(); return; }}
    form.classList.add('loading');
    button.setAttribute('aria-busy', 'true');
    label.textContent = 'Signing In…';
    status.textContent = 'Signing in';
    status.hidden = false;
    inputs.forEach(function (input) {{ input.readOnly = true; }});
  }});
  // Going Back to the page (from the browser's cache) shows the form ready again
  window.addEventListener('pageshow', function (e) {{ if (e.persisted) reset(); }});
}})();
</script>
</body>
</html>";

            Response.Headers.CacheControl = "no-store";
            return new ContentResult { Content = html, ContentType = "text/html; charset=utf-8", StatusCode = statusCode ?? (message != null ? 401 : 200) };
        }
    }
}
