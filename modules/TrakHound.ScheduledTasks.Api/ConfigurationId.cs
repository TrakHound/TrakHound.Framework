// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace TrakHound.ScheduledTasks
{
    /// <summary>
    /// The IDs of Scheduled Tasks and Jobs : the same on every Instance (so an exported configuration can be imported anywhere),
    /// and safe to use as a File name and a Route segment. Shared with TrakHound.Functions.Api
    /// </summary>
    internal static class ConfigurationId
    {
        public const int MaxLength = 100;

        private static readonly Regex _pattern = new Regex("^[A-Za-z0-9][A-Za-z0-9._-]*$", RegexOptions.Compiled);


        /// <summary>
        /// Returns an error message when the ID is empty, too long, or has characters other than letters, digits, '.', '_', and '-' (null when valid)
        /// </summary>
        public static string Validate(string id, string label = "ID")
        {
            if (string.IsNullOrWhiteSpace(id)) return $"{label} is required";
            if (id.Length > MaxLength) return $"{label} is longer than {MaxLength} characters : {id}";
            if (!_pattern.IsMatch(id)) return $"Invalid {label} : {id} (use letters, digits, '.', '_', and '-', starting with a letter or digit)";
            return null;
        }

        public static bool IsValid(string id) => Validate(id) == null;

        /// <summary>
        /// True when the ID can't name a File outside its Volume directory (a looser check than Validate, for IDs saved before the ID rules)
        /// </summary>
        public static bool IsSafeFileName(string id)
        {
            if (string.IsNullOrWhiteSpace(id)) return false;
            if (id.Contains("..") || id.IndexOfAny(new[] { '/', '\\', ':' }) >= 0) return false;
            return id.IndexOfAny(System.IO.Path.GetInvalidFileNameChars()) < 0;
        }

        /// <summary>
        /// An ID made from a Name : lowercase letters and digits separated by '-' (ex. "Plant 1 Daily" => "plant-1-daily")
        /// </summary>
        public static string FromName(string name, string fallback = "item")
        {
            var builder = new StringBuilder();
            foreach (var c in (name ?? "").Trim().ToLowerInvariant())
            {
                if (c < 128 && char.IsLetterOrDigit(c)) builder.Append(c);
                else if (builder.Length > 0 && builder[builder.Length - 1] != '-') builder.Append('-');
            }

            var id = builder.ToString().Trim('-');
            if (id.Length > MaxLength) id = id.Substring(0, MaxLength).TrimEnd('-');
            return id.Length > 0 ? id : fallback;
        }

        /// <summary>
        /// The ID, or the ID with the first free "-2", "-3", .. suffix when it's already used (compared ignoring case)
        /// </summary>
        public static string MakeUnique(string id, IEnumerable<string> existingIds)
        {
            var existing = new HashSet<string>(existingIds?.Where(o => o != null) ?? Enumerable.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(id)) return id;

            for (var i = 2; ; i++)
            {
                var suffix = $"-{i}";
                var candidate = (id.Length + suffix.Length > MaxLength ? id.Substring(0, MaxLength - suffix.Length) : id) + suffix;
                if (!existing.Contains(candidate)) return candidate;
            }
        }
    }
}
