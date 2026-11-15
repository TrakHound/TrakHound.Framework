// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Net.Http.Headers;
using TrakHound.Instance.Services;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// Serves the static files of App Packages (/_packages/{packageId}/{version}/..., ex. stylesheets, scripts and images) from the
    /// folder the App Provider copies them to. Served here rather than by the Static Files Middleware because of the .NET debug wwwroot
    /// folder (loading .js files from it makes the build try to map JavaScript exports to a .razor file)
    /// </summary>
    public class TrakHoundPackageFilesMiddleware
    {
        // Small files are kept in memory (most are stylesheets and scripts read on every page load). Larger files are sent from disk
        private const long _maxCachedFileSize = 1024 * 1024;

        // Package paths include the Package Version, so the content can be cached by the browser
        private const string _cacheControl = "max-age=604800";

        private readonly RequestDelegate _next;
        private readonly TrakHoundFileCache _cache = new TrakHoundFileCache();
        private readonly string _root;


        public TrakHoundPackageFilesMiddleware(RequestDelegate next)
        {
            _next = next;
            _root = GetRoot(AppDomain.CurrentDomain.BaseDirectory);
        }


        public async Task Invoke(HttpContext context)
        {
            if (!TrakHoundInstanceRoutes.IsPackagesRoute(context.Request.Path))
            {
                await _next(context);
                return;
            }

            var path = GetFilePath(_root, context.Request.Path);
            var file = path != null ? new FileInfo(path) : null;
            if (file == null || !file.Exists)
            {
                context.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            // Changes when a Package is rebuilt with the same Version, so the browser can check it after max-age
            var etag = $"\"{file.LastWriteTimeUtc.Ticks:x}-{file.Length:x}\"";
            var headers = context.Response.GetTypedHeaders();
            headers.ETag = new EntityTagHeaderValue(etag);
            headers.LastModified = file.LastWriteTimeUtc;
            context.Response.Headers.CacheControl = _cacheControl;

            if (context.Request.Headers.IfNoneMatch == etag)
            {
                context.Response.StatusCode = StatusCodes.Status304NotModified;
                return;
            }

            context.Response.StatusCode = StatusCodes.Status200OK;
            context.Response.ContentType = MimeTypes.GetMimeType(file.FullName);
            context.Response.ContentLength = file.Length;

            if (HttpMethods.IsHead(context.Request.Method)) return;

            if (file.Length <= _maxCachedFileSize)
            {
                var content = _cache.Get(file.FullName);
                if (content == null)
                {
                    content = await File.ReadAllBytesAsync(file.FullName, context.RequestAborted);
                    _cache.Add(file.FullName, content);
                }

                await context.Response.Body.WriteAsync(content, context.RequestAborted);
            }
            else
            {
                await context.Response.SendFileAsync(file.FullName, context.RequestAborted);
            }
        }


        /// <summary>
        /// The folder the Package files are served from (with a trailing separator)
        /// </summary>
        public static string GetRoot(string baseDirectory)
        {
            return Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(baseDirectory, "_temp", "wwwroot"))) + Path.DirectorySeparatorChar;
        }

        /// <summary>
        /// The file a Request Path is for. Null when it would be outside of the Package files folder (ex. "..", or "\" on Windows),
        /// which the server's own path normalization would usually prevent, but this doesn't depend on it
        /// </summary>
        public static string GetFilePath(string root, PathString requestPath)
        {
            var relativePath = requestPath.Value?.TrimStart('/');
            if (string.IsNullOrEmpty(relativePath)) return null;

            try
            {
                var fullPath = Path.GetFullPath(Path.Combine(root, relativePath));
                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return fullPath.StartsWith(root, comparison) ? fullPath : null;
            }
            catch
            {
                return null; // ex. invalid path characters
            }
        }
    }
}
