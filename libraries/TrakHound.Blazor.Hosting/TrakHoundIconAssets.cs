// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Extensions.FileProviders;

namespace TrakHound.Blazor
{
    /// <summary>
    /// The Stylesheets and Fonts for icons. Font Awesome Pro is licensed, so it's only in official builds (it's ignored by git).
    /// Other builds load a Stylesheet that shows a generic placeholder wherever an icon would be
    /// </summary>
    public static class TrakHoundIconAssets
    {
        private const string _hostingContent = "_content/TrakHound.Blazor.Hosting";
        private const string _fontAwesome = $"{_hostingContent}/css/font-awesome";

        private static readonly string[] _fontAwesomeStylesheets = new[] { "fontawesome", "regular", "duotone", "solid", "brands", "sharp-regular", "sharp-solid" };
        private static readonly string[] _fontAwesomeFonts = new[] { "fa-sharp-solid-900", "fa-solid-900", "fa-regular-400" };


        /// <summary>
        /// Whether Font Awesome is in the Web Root that is being served
        /// </summary>
        public static bool HasFontAwesome(IFileProvider files) => files.GetFileInfo($"{_fontAwesome}/css/fontawesome.min.css").Exists;

        public static string[] GetStylesheets(IFileProvider files)
        {
            if (!HasFontAwesome(files)) return new[] { $"{_hostingContent}/css/trakhound/icons-placeholder.css" };

            return _fontAwesomeStylesheets.Select(o => $"{_fontAwesome}/css/{o}.min.css").ToArray();
        }

        public static string[] GetFonts(IFileProvider files)
        {
            if (!HasFontAwesome(files)) return Array.Empty<string>();

            return _fontAwesomeFonts.Select(o => $"{_fontAwesome}/webfonts/{o}.woff2").ToArray();
        }
    }
}
