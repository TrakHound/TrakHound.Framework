// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http.Features;
using TrakHound.Security;

namespace TrakHound.Instance.Security
{
    internal static class TrakHoundIdentityHttp
    {
        public const string IdentityPrefix = "/_identity";
        public const string SignInPrefix = "/_identity/signin";
        public const string CallbackPrefix = "/_identity/callback";
        public const string SignOutPrefix = "/_identity/signout";


        /// <summary>
        /// Gets the Path as seen by the client (including the Instance's Base Path, used when behind a reverse proxy)
        /// </summary>
        public static string GetExternalPath(string basePath, string path)
        {
            return "/" + Url.Combine(basePath, path?.TrimStart('/'))?.TrimStart('/');
        }

        /// <summary>
        /// Gets the absolute URL of the Instance (including the Base Path)
        /// </summary>
        public static string GetBaseUrl(HttpContext context, string basePath)
        {
            return $"{context.Request.Scheme}://{context.Request.Host.Value}{GetExternalPath(basePath, null).TrimEnd('/')}";
        }

        /// <summary>
        /// Creates the Context given to Identity Providers for a Request
        /// </summary>
        public static TrakHoundIdentityContext CreateContext(HttpContext context, ITrakHoundIdentityStateStore stateStore, string basePath, string providerId)
        {
            var baseUrl = GetBaseUrl(context, basePath);
            var callbackUrl = providerId != null ? $"{baseUrl}{CallbackPrefix}/{Uri.EscapeDataString(providerId)}" : null;

            return new TrakHoundIdentityContext(new HttpIdentityRequest(context), stateStore, baseUrl, callbackUrl);
        }

        /// <summary>
        /// Returns the Return URL if it is a local Path (prevents redirecting to other sites after signing in). Otherwise returns the Instance root
        /// </summary>
        public static string GetSafeReturnUrl(string returnUrl, string basePath)
        {
            if (!string.IsNullOrEmpty(returnUrl) && returnUrl.StartsWith('/') && !returnUrl.StartsWith("//") && !returnUrl.StartsWith("/\\"))
            {
                return returnUrl;
            }

            return GetExternalPath(basePath, null);
        }

        /// <summary>
        /// True if the Request was made by a browser navigating to a page (as opposed to a script or API client)
        /// </summary>
        public static bool IsBrowserNavigation(HttpRequest request)
        {
            return HttpMethods.IsGet(request.Method) && request.Headers.Accept.Any(o => o != null && o.Contains("text/html", StringComparison.OrdinalIgnoreCase));
        }


        private sealed class HttpIdentityRequest : ITrakHoundIdentityRequest
        {
            private readonly HttpContext _context;

            public string Method => _context.Request.Method;

            public string Path => _context.Request.Path.Value;

            public string ClientAddress => _context.Connection.RemoteIpAddress?.ToString();


            public HttpIdentityRequest(HttpContext context)
            {
                _context = context;
            }


            public string GetHeader(string name)
            {
                if (!string.IsNullOrEmpty(name) && _context.Request.Headers.TryGetValue(name, out var values)) return values.FirstOrDefault();
                return null;
            }

            public string GetQueryParameter(string name)
            {
                if (!string.IsNullOrEmpty(name) && _context.Request.Query.TryGetValue(name, out var values)) return values.FirstOrDefault();
                return null;
            }

            public async Task<string> GetFormValue(string name)
            {
                if (!string.IsNullOrEmpty(name) && _context.Request.HasFormContentType)
                {
                    var form = await _context.Request.ReadFormAsync();
                    if (form.TryGetValue(name, out var values)) return values.FirstOrDefault();
                }

                return null;
            }
        }
    }

    /// <summary>
    /// In memory storage for Identity Provider sign in flows. Values expire and can only be read once
    /// </summary>
    public sealed class TrakHoundIdentityStateStore : ITrakHoundIdentityStateStore
    {
        private const int _maxEntries = 10000;

        private readonly Dictionary<string, (string Value, DateTime Expires)> _values = new Dictionary<string, (string, DateTime)>();
        private readonly object _lock = new object();


        public void Set(string key, string value, TimeSpan lifetime)
        {
            if (!string.IsNullOrEmpty(key))
            {
                lock (_lock)
                {
                    // Remove expired values (and limit the size, since sign in flows can be started by anyone)
                    var now = DateTime.UtcNow;
                    foreach (var expiredKey in _values.Where(o => o.Value.Expires < now).Select(o => o.Key).ToList()) _values.Remove(expiredKey);
                    if (_values.Count >= _maxEntries)
                    {
                        foreach (var oldestKey in _values.OrderBy(o => o.Value.Expires).Take(_values.Count - _maxEntries + 1).Select(o => o.Key).ToList()) _values.Remove(oldestKey);
                    }

                    _values[key] = (value, now.Add(lifetime));
                }
            }
        }

        public string Take(string key)
        {
            if (!string.IsNullOrEmpty(key))
            {
                lock (_lock)
                {
                    if (_values.Remove(key, out var entry) && entry.Expires >= DateTime.UtcNow) return entry.Value;
                }
            }

            return null;
        }
    }
}
