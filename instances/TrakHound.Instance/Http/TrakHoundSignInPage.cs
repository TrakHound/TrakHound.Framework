// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Blazor.Apps;
using TrakHound.Instances;
using TrakHound.Security;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// Whether a signInRoute can be used
    /// </summary>
    public enum TrakHoundSignInRouteStatus
    {
        Ok,
        AppsNotServed, // Apps aren't served over HTTP
        NotFound, // No App Page matches the route
        RequiresSignIn // A Profile requires signing in to open the Page (signing in would redirect back to it, forever)
    }

    /// <summary>
    /// The page an Identity Provider with the Credentials Feature uses for its Username and Password form. It only displays the form:
    /// the form is always posted to the Instance, which verifies it (with the antiforgery check, delay and lockout) for every page.
    /// Set with the Parameters of the provider's configuration, otherwise the Instance's own form is shown :
    /// signInRoute : The route of an App Page, which an anonymous Identity must be able to open (ex. /sign-in)
    /// signInPage : A folder in the provider's Package (under dist) with an index.html, whose files are also served at /_identity/assets/{providerId}/...
    /// </summary>
    public static class TrakHoundSignInPage
    {
        public const string RouteParameter = "signInRoute";
        public const string PageParameter = "signInPage";
        public const string ProductParameter = "signInProduct"; // Shown above the Instance's own form
        public const string LogoParameter = "signInLogo"; // A path in the Instance that doesn't require signing in, shown above the Instance's own form
        public const string AssetsRoute = "assets";

        // Sent to a signInRoute page (never as text, so a link can't put its own words on the page)
        public const string ErrorInvalid = "invalid";
        public const string ErrorLocked = "locked";
        public const string ErrorUnavailable = "unavailable";

        private const string _packageFilesDirectory = "dist";
        private const string _pageFilename = "index.html";


        /// <summary>
        /// The page's folder with a trailing separator. Null unless it's a folder inside of the Package's files, so
        /// the Package's Assemblies (which are in the root of those files) are never served as assets
        /// </summary>
        public static string GetFolder(string packageDirectory, string page)
        {
            if (string.IsNullOrWhiteSpace(packageDirectory) || string.IsNullOrWhiteSpace(page)) return null;

            try
            {
                // The same on every OS : '\' separates folders (as on Windows), and a drive (ex. "C:\Windows") is never inside the Package
                var relativePath = page.Trim().Replace('\\', '/');
                if (relativePath.Contains(':')) return null;

                var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(packageDirectory, _packageFilesDirectory))) + Path.DirectorySeparatorChar;
                var folder = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.Combine(root, relativePath))) + Path.DirectorySeparatorChar;

                var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
                return folder.Length > root.Length && folder.StartsWith(root, comparison) ? folder : null;
            }
            catch
            {
                return null; // ex. invalid path characters
            }
        }

        /// <summary>
        /// Whether the route of an App Page can be used to sign in : the Page must exist, and an anonymous Identity must be able to open it.
        /// The Resource ID of the matched Page is returned for messages
        /// </summary>
        public static TrakHoundSignInRouteStatus CheckRoute(ITrakHoundInstance instance, string route, out string resourceId)
        {
            resourceId = null;

            var routeManager = instance.Configuration.AppsHttpEnabled ? (instance.AppProvider as TrakHoundAppProvider)?.RouteManager : null;
            if (routeManager == null) return TrakHoundSignInRouteStatus.AppsNotServed;

            var match = routeManager.Match(route);
            if (match == null || !match.IsMatch) return TrakHoundSignInRouteStatus.NotFound;

            resourceId = TrakHoundAppProvider.GetSecurityResourceId(match.MatchedRoute.AppId, match.MatchedRoute.Handler);
            return instance.SecurityManager.Authorize(null, resourceId).IsAllowed ? TrakHoundSignInRouteStatus.Ok : TrakHoundSignInRouteStatus.RequiresSignIn;
        }

        /// <summary>
        /// The page's HTML. Null when it can't be read, so the Instance's own form is shown instead
        /// </summary>
        public static string ReadPage(string folder)
        {
            if (folder == null) return null;

            try
            {
                var file = Path.Combine(folder, _pageFilename);
                return File.Exists(file) ? File.ReadAllText(file) : null;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// The values a page can use (ex. {{action}}). Error is empty when there isn't one
        /// </summary>
        public static Dictionary<string, string> GetTokens(string action, string antiforgeryName, string antiforgeryToken, string returnUrl, string username, string error, string assets)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["action"] = action,
                ["antiforgeryName"] = antiforgeryName,
                ["antiforgeryToken"] = antiforgeryToken,
                ["returnUrl"] = returnUrl,
                ["username"] = username ?? "",
                ["error"] = error ?? "",
                ["assets"] = assets
            };
        }
    }
}
