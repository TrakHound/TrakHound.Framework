// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace TrakHound
{
    /// <summary>
    /// Adjusts how Health Check results count towards the overall Health. Entries are a Check ID (ex. "workers") to match the whole Check,
    /// or "{Check ID}/{Item}" where Item is the item's Name or ID (ex. "workers/TrakHound.Example.Worker"). "*" matches any characters.
    /// </summary>
    public class TrakHoundInstanceHealthConfiguration
    {
        /// <summary>
        /// Problems with these are reported as Unhealthy (instead of Degraded)
        /// </summary>
        [JsonPropertyName("critical")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitEmptyCollections)]
        public List<string> Critical { get; set; } = new List<string>();

        /// <summary>
        /// These are shown but not included in the overall Health (ex. a Worker that is expected to fail)
        /// </summary>
        [JsonPropertyName("ignore")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitEmptyCollections)]
        public List<string> Ignore { get; set; } = new List<string>();


        public bool IsCritical(string checkId, TrakHoundHealthCheckItemReference item = default) => Matches(Critical, checkId, item);

        public bool IsIgnored(string checkId, TrakHoundHealthCheckItemReference item = default) => Matches(Ignore, checkId, item);


        private static bool Matches(IEnumerable<string> patterns, string checkId, TrakHoundHealthCheckItemReference item)
        {
            if (patterns == null || string.IsNullOrEmpty(checkId)) return false;

            foreach (var raw in patterns)
            {
                var pattern = raw?.Trim();
                if (string.IsNullOrEmpty(pattern)) continue;

                var separator = pattern.IndexOf('/');
                if (separator < 0)
                {
                    // Whole Check (only when matching the Check itself)
                    if (item.IsEmpty && IsMatch(pattern, checkId)) return true;
                }
                else if (!item.IsEmpty)
                {
                    var checkPattern = pattern.Substring(0, separator);
                    var itemPattern = pattern.Substring(separator + 1);

                    if (IsMatch(checkPattern, checkId) && (IsMatch(itemPattern, item.Name) || IsMatch(itemPattern, item.Id))) return true;
                }
            }

            return false;
        }

        // Case-insensitive match where '*' matches any characters
        private static bool IsMatch(string pattern, string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            if (!pattern.Contains('*')) return string.Equals(pattern, value, StringComparison.OrdinalIgnoreCase);

            var regex = "^" + System.Text.RegularExpressions.Regex.Escape(pattern).Replace("\\*", ".*") + "$";
            return System.Text.RegularExpressions.Regex.IsMatch(value, regex, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        }
    }

    /// <summary>
    /// The Name and ID of a Health Check item (used to match the Health settings)
    /// </summary>
    public readonly struct TrakHoundHealthCheckItemReference
    {
        public string Id { get; }

        public string Name { get; }

        public bool IsEmpty => Id == null && Name == null;

        public TrakHoundHealthCheckItemReference(string id, string name)
        {
            Id = id;
            Name = name;
        }
    }
}
