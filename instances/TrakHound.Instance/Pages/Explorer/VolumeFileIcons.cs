// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Pages.Explorer
{
    /// <summary>
    /// Font Awesome icons and display names for files and directories shown in the Volume Explorer
    /// </summary>
    public static class VolumeFileIcons
    {
        public const string Directory = "fa-solid fa-sharp fa-folder";

        private const string _default = "fa-regular fa-sharp fa-file";

        private static readonly Dictionary<string, string> _icons = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { ".json", "fa-regular fa-sharp fa-file-code" },
            { ".yaml", "fa-regular fa-sharp fa-file-code" },
            { ".yml", "fa-regular fa-sharp fa-file-code" },
            { ".xml", "fa-regular fa-sharp fa-file-code" },
            { ".js", "fa-regular fa-sharp fa-file-code" },
            { ".css", "fa-regular fa-sharp fa-file-code" },
            { ".html", "fa-regular fa-sharp fa-file-code" },
            { ".txt", "fa-regular fa-sharp fa-file-lines" },
            { ".md", "fa-regular fa-sharp fa-file-lines" },
            { ".log", "fa-regular fa-sharp fa-file-lines" },
            { ".csv", "fa-regular fa-sharp fa-file-csv" },
            { ".png", "fa-regular fa-sharp fa-file-image" },
            { ".jpg", "fa-regular fa-sharp fa-file-image" },
            { ".jpeg", "fa-regular fa-sharp fa-file-image" },
            { ".gif", "fa-regular fa-sharp fa-file-image" },
            { ".svg", "fa-regular fa-sharp fa-file-image" },
            { ".zip", "fa-regular fa-sharp fa-file-zipper" },
            { ".pdf", "fa-regular fa-sharp fa-file-pdf" }
        };


        public static string Get(string path)
        {
            var extension = System.IO.Path.GetExtension(path ?? "");
            return !string.IsNullOrEmpty(extension) && _icons.TryGetValue(extension, out var icon) ? icon : _default;
        }

        /// <summary>
        /// Returns the last segment of a Volume path (Volumes can return either '/' or '\' separators)
        /// </summary>
        public static string GetName(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            var trimmed = path.TrimEnd('/', '\\');
            var index = trimmed.LastIndexOfAny(new[] { '/', '\\' });
            return index >= 0 ? trimmed.Substring(index + 1) : trimmed;
        }
    }
}
