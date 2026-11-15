// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http.Extensions;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using TrakHound.Instances;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// The page shown (at /_init) while the Instance is not Started (starting, restarting, stopped or in an error state), so that clients
    /// (ex. kiosk displays) requesting an App route don't get a 404 before the Apps are loaded. The page reloads the requested route every
    /// 5 seconds (with a Refresh header) until the Instance is Started. It is plain HTML (no Blazor connection required) so it can't get stuck.
    /// Requests that don't accept HTML (ex. API clients) get a 503 with Retry-After instead.
    /// A Custom Page (ex. a branded loading screen) can be shown instead : the index.html file in the folder set by the Instance Configuration
    /// (InitializationPagePath), whose files are also served at /_init/assets/... It is reloaded by a Refresh header, so it doesn't need its
    /// own reload script, and its {{tokens}} are replaced (see GetTokens).
    /// </summary>
    public static class TrakHoundInitializationPage
    {
        public const string Route = "_init";
        public const string AssetsRoute = "assets";
        public const string PageFilename = "index.html";
        public const int RetrySeconds = 5;

        private static readonly Regex _tokenRegex = new Regex(@"\{\{\s*([A-Za-z]+)\s*\}\}", RegexOptions.Compiled);


        /// <summary>
        /// Returns true if the path is the Initialization Page (with or without the Base Path)
        /// </summary>
        public static bool IsRequest(PathString path, string basePath)
        {
            var s = path.Value?.TrimEnd('/');
            if (string.Equals(s, "/" + Route, StringComparison.OrdinalIgnoreCase)) return true;
            if (!string.IsNullOrEmpty(basePath) && string.Equals(s, GetPath(basePath), StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static string GetPath(string basePath) => "/" + Url.Combine(basePath, Route);

        /// <summary>
        /// The path of a Custom Page file (ex. "/logo.png" for /_init/assets/logo.png, with or without the Base Path). Null if the path isn't an asset
        /// </summary>
        public static PathString? GetAssetPath(PathString path, string basePath)
        {
            if (path.StartsWithSegments("/" + Route + "/" + AssetsRoute, StringComparison.OrdinalIgnoreCase, out var remaining)) return remaining;
            if (!string.IsNullOrEmpty(basePath) && path.StartsWithSegments(GetPath(basePath) + "/" + AssetsRoute, StringComparison.OrdinalIgnoreCase, out remaining)) return remaining;
            return null;
        }

        /// <summary>
        /// The Custom Page's folder (relative to the Configuration Profile's folder, or absolute) with a trailing separator. Null when no Custom Page is set
        /// </summary>
        public static string GetCustomPageFolder(string configurationPath, string pagePath)
        {
            if (string.IsNullOrWhiteSpace(pagePath)) return null;

            try
            {
                var folder = Path.GetFullPath(Path.Combine(configurationPath ?? AppContext.BaseDirectory, pagePath.Trim()));
                return Path.TrimEndingDirectorySeparator(folder) + Path.DirectorySeparatorChar;
            }
            catch
            {
                return null; // ex. invalid path characters
            }
        }

        /// <summary>
        /// The Custom Page's file (index.html in its folder). Null when no Custom Page is set
        /// </summary>
        public static string GetCustomPageFile(string configurationPath, string pagePath)
        {
            var folder = GetCustomPageFolder(configurationPath, pagePath);
            return folder != null ? Path.Combine(folder, PageFilename) : null;
        }

        /// <summary>
        /// Respond to a request for a route that isn't available while the Instance is not Started
        /// (a 503 for all requests when the Initialization Page is disabled)
        /// </summary>
        public static async Task HandleUnavailable(HttpContext context, string basePath, bool pageEnabled = true)
        {
            context.Response.Headers.CacheControl = "no-store";

            if (pageEnabled && AcceptsHtml(context.Request))
            {
                var redirect = Url.AddQueryParameter(GetPath(basePath), "route", context.Request.GetEncodedPathAndQuery());
                context.Response.Redirect(redirect);
            }
            else
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                context.Response.Headers.RetryAfter = RetrySeconds.ToString();
            }

            await context.Response.CompleteAsync();
        }

        /// <summary>
        /// Write the Initialization Page, or redirect to the requested route if the Instance is Started
        /// </summary>
        public static async Task Handle(HttpContext context, ITrakHoundInstance instance)
        {
            var route = GetLocalRoute(context.Request.Query["route"]);

            // Shows the page while the Instance is Started, without reloading (ex. to check a Custom Page from the Settings)
            var preview = context.Request.Query.ContainsKey("preview");
            var status = instance?.Status ?? TrakHoundInstanceStatus.Stopped;

            context.Response.Headers.CacheControl = "no-store";

            if (status == TrakHoundInstanceStatus.Started && !preview)
            {
                context.Response.Redirect(route);
                await context.Response.CompleteAsync();
                return;
            }

            if (status == TrakHoundInstanceStatus.Started) status = TrakHoundInstanceStatus.Starting;

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = "text/html; charset=utf-8";
            if (!preview) context.Response.Headers["Refresh"] = GetRefreshHeader(route);

            if (!HttpMethods.IsHead(context.Request.Method))
            {
                var html = ReadCustomPage(instance?.ConfigurationPath, instance?.Configuration?.InitializationPagePath);
                html = html != null ? ReplaceTokens(html, GetTokens(route, status)) : GetHtml(route, status);

                await context.Response.WriteAsync(html);
            }
        }

        /// <summary>
        /// Serve a file from the Custom Page's folder (/_init/assets/...). A 404 when there is no Custom Page or the file isn't in its folder
        /// </summary>
        public static async Task HandleAsset(HttpContext context, ITrakHoundInstance instance, PathString assetPath)
        {
            var folder = GetCustomPageFolder(instance?.ConfigurationPath, instance?.Configuration?.InitializationPagePath);
            await SendAsset(context, folder, assetPath);
        }

        /// <summary>
        /// Serve a file from a folder of a page's assets. A 404 when the file isn't in the folder
        /// </summary>
        public static async Task SendAsset(HttpContext context, string folder, PathString assetPath)
        {
            var path = folder != null ? TrakHoundPackageFilesMiddleware.GetFilePath(folder, assetPath) : null;
            var file = path != null ? new FileInfo(path) : null;
            if (file == null || !file.Exists || (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)))
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                await context.Response.CompleteAsync();
                return;
            }

            // Checked on every load (the files can be edited while the Instance is running), but not sent again if unchanged
            var etag = $"\"{file.LastWriteTimeUtc.Ticks:x}-{file.Length:x}\"";
            context.Response.Headers.ETag = etag;
            context.Response.Headers.CacheControl = "no-cache";

            if (context.Request.Headers.IfNoneMatch == etag)
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = MimeTypes.GetMimeType(file.FullName);
            context.Response.ContentLength = file.Length;

            if (!HttpMethods.IsHead(context.Request.Method)) await context.Response.SendFileAsync(file.FullName, context.RequestAborted);
        }

        /// <summary>
        /// The Custom Page's HTML. Null when no Custom Page is set or it can't be read, so the built-in page is shown instead
        /// </summary>
        public static string ReadCustomPage(string configurationPath, string pagePath)
        {
            var file = GetCustomPageFile(configurationPath, pagePath);
            if (file == null) return null;

            try
            {
                return File.Exists(file) ? File.ReadAllText(file) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The values a Custom Page can use (ex. {{title}})
        /// </summary>
        public static Dictionary<string, string> GetTokens(string route, TrakHoundInstanceStatus status)
        {
            var (title, description) = GetText(status);

            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["title"] = title,
                ["description"] = description,
                ["status"] = status.ToString(),
                ["route"] = route,
                ["timestamp"] = DateTime.Now.ToString("ddd M/d/yy h:mm:ss tt"),
                ["retrySeconds"] = RetrySeconds.ToString(),
                ["assets"] = Route + "/" + AssetsRoute // Relative (like the built-in page's images), so it also works under a Base Path
            };
        }

        /// <summary>
        /// Replace each {{name}} with its HTML encoded value. Unknown tokens are left as they are
        /// </summary>
        public static string ReplaceTokens(string html, IReadOnlyDictionary<string, string> tokens)
        {
            if (string.IsNullOrEmpty(html)) return html;

            return _tokenRegex.Replace(html, match => tokens.TryGetValue(match.Groups[1].Value, out var value) ? WebUtility.HtmlEncode(value) : match.Value);
        }

        /// <summary>
        /// The Refresh header that reloads the requested route. Header values must be ASCII, so other characters are percent encoded
        /// </summary>
        public static string GetRefreshHeader(string route)
        {
            var url = new StringBuilder();
            foreach (var b in Encoding.UTF8.GetBytes(route ?? "/"))
            {
                if (b < 128) url.Append((char)b);
                else url.Append('%').Append(b.ToString("X2"));
            }
            return $"{RetrySeconds};url={url}";
        }

        /// <summary>
        /// Only allow local paths (ex. '/app/page?x=1') so the page can't be used to redirect to another site
        /// </summary>
        public static string GetLocalRoute(string route)
        {
            if (string.IsNullOrEmpty(route)) return "/";
            if (!route.StartsWith('/') || route.StartsWith("//") || route.StartsWith("/\\")) return "/";
            if (route.Any(char.IsControl)) return "/";
            return route;
        }

        private static bool AcceptsHtml(HttpRequest request)
        {
            if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method)) return false;
            return request.Headers.Accept.Any(o => o != null && o.Contains("text/html", StringComparison.OrdinalIgnoreCase));
        }

        private static (string Title, string Description) GetText(TrakHoundInstanceStatus status)
        {
            switch (status)
            {
                case TrakHoundInstanceStatus.Error:
                    return ("TrakHound Instance Unavailable", "The Instance did not start. This page will reload when it is running.");

                case TrakHoundInstanceStatus.Stopping:
                case TrakHoundInstanceStatus.Stopped:
                    return ("TrakHound Instance Restarting...", "This page will reload when the Instance is running.");

                default:
                    return ("TrakHound Instance Initializing...", "This page will reload when the Instance is running.");
            }
        }

        private static string GetHtml(string route, TrakHoundInstanceStatus status)
        {
            var (title, description) = GetText(status);

            var encodedRoute = WebUtility.HtmlEncode(route);
            var timestamp = WebUtility.HtmlEncode(DateTime.Now.ToString("ddd M/d/yy h:mm:ss tt"));

            return $$"""
                <!DOCTYPE html>
                <html lang="en">
                <head>
                <meta charset="utf-8" />
                <meta name="viewport" content="width=device-width, initial-scale=1.0" />
                <title>Starting | TrakHound</title>
                <style>
                    html, body { height: 100%; margin: 0; }
                    body { background: linear-gradient(180deg, #2464a8, #1f5793); font-family: "Segoe UI", system-ui, -apple-system, Roboto, "Helvetica Neue", Arial, sans-serif; }
                    .panel { display: grid; grid-template-rows: 0.2fr max-content 1.8fr; grid-template-columns: minmax(16px, 1fr) minmax(0, 760px) minmax(16px, 1fr); height: 100%; }
                    .container { grid-row: 2; grid-column: 2; display: grid; grid-template-columns: max-content minmax(0, 1fr); border: 1px solid rgba(0, 0, 0, 0.25); }
                    .header { grid-column: 1 / 3; display: flex; justify-content: center; align-items: center; gap: 15px; padding: 15px 20px 10px; border-bottom: 1px solid rgba(0, 0, 0, 0.25); }
                    .header .logo { display: inline-block; height: 40px; aspect-ratio: 1027 / 717; background-color: rgba(0, 0, 0, 0.25); -webkit-mask: url("img/trakhound-logo.svg") center / contain no-repeat; mask: url("img/trakhound-logo.svg") center / contain no-repeat; }
                    .header span { font-size: 1.875rem; font-weight: 600; color: rgba(0, 0, 0, 0.25); }
                    .icon { padding: 30px 0 25px 50px; }
                    .spinner { width: 42px; height: 42px; border: 6px solid rgba(0, 0, 0, 0.12); border-top-color: rgba(0, 0, 0, 0.3); border-radius: 50%; animation: spin 1.5s linear infinite; }
                    @keyframes spin { to { transform: rotate(360deg); } }
                    .information { padding: 25px 50px; }
                    h1 { margin: 0 0 8px; font-size: 1.5rem; font-weight: bold; color: rgba(0, 0, 0, 0.5); }
                    h2 { margin: 0 0 16px; font-size: 1.125rem; font-weight: normal; color: rgba(0, 0, 0, 0.5); }
                    p { margin: 0 0 6px; font-size: 1rem; color: rgba(0, 0, 0, 0.75); word-break: break-all; }
                    @media (max-width: 600px) { .icon { padding-left: 25px; } .information { padding: 25px; } }
                </style>
                </head>
                <body>
                <div class="panel">
                    <div class="container">
                        <div class="header">
                            <span class="logo" aria-hidden="true"></span>
                            <span>TrakHound</span>
                        </div>
                        <div class="icon"><div class="spinner"></div></div>
                        <div class="information">
                            <h1>{{title}}</h1>
                            <h2>{{timestamp}}</h2>
                            <p>{{description}}</p>
                            <p>{{encodedRoute}}</p>
                        </div>
                    </div>
                </div>
                </body>
                </html>
                """;
        }
    }
}
