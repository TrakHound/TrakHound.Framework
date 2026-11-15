// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Extensions.FileProviders;
using System.Collections.Concurrent;
using System.Reflection;
using System.Security.Cryptography;
using TrakHound.Blazor;

namespace TrakHound.Instance
{
    /// <summary>
    /// The Stylesheets and Scripts shared by the App document (AppDocument.razor) and the Admin interface (InstanceApp.razor).
    /// A hash of each file's contents is added to its URL so that a changed file is never served stale.
    /// The TrakHound Version isn't enough, since it doesn't change between builds of the same Version
    /// </summary>
    public static class InstanceAssets
    {
        private const string _hostingContent = "_content/TrakHound.Blazor.Hosting";

        private static readonly ConcurrentDictionary<string, string> _versions = new();
        private static IFileProvider _files;

        public static readonly string TrakHoundVersion = Assembly.GetEntryAssembly()?.GetName().Version?.ToString();
        public static readonly string RadzenVersion = typeof(Radzen.Colors).Assembly.GetName().Version?.ToString();

        public static string[] IconStylesheets { get; private set; } = Array.Empty<string>();

        // Preloaded, otherwise not discovered until the stylesheet is parsed and an icon is rendered
        public static string[] IconFonts { get; private set; } = Array.Empty<string>();

        public static readonly string RadzenStylesheet = $"_content/Radzen.Blazor/css/material-base.css?v={RadzenVersion}";
        public static readonly string RadzenScript = $"_content/Radzen.Blazor/Radzen.Blazor.js?v={RadzenVersion}";

        public static string TrakHoundStylesheet => GetInstanceAsset($"{_hostingContent}/css/trakhound/trakhound.css");
        public static string TokensStylesheet => GetInstanceAsset("css/tokens.css");
        public static string InstanceStylesheet => GetInstanceAsset("TrakHound.Instance.styles.css");

        public static string BlazorScript => GetInstanceAsset("_framework/blazor.web.js");
        public static string TrakHoundBlazorScript => GetInstanceAsset($"{_hostingContent}/js/trakhound-blazor.js");
        public static string TrakHoundScript => GetInstanceAsset($"{_hostingContent}/js/trakhound.js");


        /// <summary>
        /// Sets where the files are read from, so that their contents can be hashed
        /// </summary>
        public static void Initialize(IFileProvider files)
        {
            _files = files;
            _versions.Clear();

            IconStylesheets = TrakHoundIconAssets.GetStylesheets(files).Select(o => $"{o}?v={TrakHoundVersion}").ToArray();
            IconFonts = TrakHoundIconAssets.GetFonts(files);

            TrakHoundTheme.ResolveStylesheet = GetInstanceAsset;
        }

        /// <summary>
        /// The base href for the Instance's configured Base Path (ex. "/trakhound/")
        /// </summary>
        public static string GetBasePath(string basePath) => !string.IsNullOrEmpty(basePath) ? $"/{basePath.Trim('/')}/" : "/";

        /// <summary>
        /// A URL served by the Instance itself, with its version added
        /// </summary>
        public static string GetInstanceAsset(string path) => $"{path}?v={_versions.GetOrAdd(path, GetVersion)}";

        private static string GetVersion(string path)
        {
            // Some files (ex. _framework) aren't in the Web Root, so they use the TrakHound Version
            var file = _files?.GetFileInfo(path);
            if (file == null || !file.Exists || file.IsDirectory) return TrakHoundVersion;

            using var stream = file.CreateReadStream();
            return Convert.ToHexString(SHA256.HashData(stream))[..12].ToLowerInvariant();
        }
    }
}
