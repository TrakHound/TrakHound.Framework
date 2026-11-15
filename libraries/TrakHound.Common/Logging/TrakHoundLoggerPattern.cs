// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace TrakHound.Logging
{
    /// <summary>
    /// Matches Logger Names (paths such as "api/TrakHound.Packages.Api") against a Pattern.
    /// Used for both Log Queries and Log Subscriptions so that a Pattern selects the same Loggers in both.
    /// </summary>
    /// <remarks>
    /// Patterns are case-insensitive :
    /// <list type="bullet">
    /// <item>"*" or "**" (or an empty Pattern) matches every Logger</item>
    /// <item>"*" matches any characters within a single path segment (ex. "api/*" matches "api/a" but not "api/a/b")</item>
    /// <item>"**" matches any characters across path segments (ex. "api/**" matches "api", "api/a" and "api/a/b")</item>
    /// <item>A Pattern without wildcards matches the Logger Name exactly</item>
    /// </list>
    /// </remarks>
    public static class TrakHoundLoggerPattern
    {
        private static readonly ConcurrentDictionary<string, Regex> _regexCache = new ConcurrentDictionary<string, Regex>();


        /// <summary>
        /// Returns true if the Pattern matches every Logger (null, empty, "*", "**", or "/**")
        /// </summary>
        public static bool IsMatchAll(string pattern)
        {
            return string.IsNullOrEmpty(pattern) || pattern == "*" || pattern == "**" || pattern == "/**";
        }

        /// <summary>
        /// Returns true if the Patterns match every Logger (null or empty, or any Pattern matches every Logger)
        /// </summary>
        public static bool IsMatchAll(IEnumerable<string> patterns)
        {
            if (patterns == null) return true;

            var any = false;
            foreach (var pattern in patterns)
            {
                any = true;
                if (IsMatchAll(pattern)) return true;
            }

            return !any;
        }

        /// <summary>
        /// Returns true if the Logger Name matches the Pattern
        /// </summary>
        public static bool IsMatch(string pattern, string name)
        {
            if (IsMatchAll(pattern)) return true;
            if (string.IsNullOrEmpty(name)) return false;

            if (pattern.IndexOf('*') < 0)
            {
                return string.Equals(pattern.Trim('/'), name.Trim('/'), StringComparison.OrdinalIgnoreCase);
            }

            return GetRegex(pattern).IsMatch(name.Trim('/'));
        }

        /// <summary>
        /// Returns true if the Logger Name matches any of the Patterns
        /// </summary>
        public static bool IsMatch(IEnumerable<string> patterns, string name)
        {
            if (patterns == null) return true;

            var any = false;
            foreach (var pattern in patterns)
            {
                any = true;
                if (IsMatch(pattern, name)) return true;
            }

            return !any;
        }

        /// <summary>
        /// Returns true if either the Logger Name or the Entry Sender (the Logger Name plus an optional sub path) matches any of the Patterns
        /// </summary>
        public static bool IsMatch(IEnumerable<string> patterns, string loggerName, string sender)
        {
            return IsMatch(patterns, loggerName) || (!string.IsNullOrEmpty(sender) && sender != loggerName && IsMatch(patterns, sender));
        }


        /// <summary>
        /// Returns true if the Pattern could match a path under the Logger Name (ex. an Entry Sender such as "{loggerName}/Devices/d-1").
        /// Used to find the Loggers whose entries may match by Sender. It can return true for paths that don't match,
        /// so each Sender is still checked with <see cref="IsMatch(string, string)"/>.
        /// </summary>
        public static bool CanMatchUnder(string pattern, string name)
        {
            if (IsMatchAll(pattern)) return true;
            if (string.IsNullOrEmpty(name)) return false;

            var patternSegments = pattern.Trim('/').Split('/');
            var nameSegments = name.Trim('/').Split('/');

            for (var i = 0; i < nameSegments.Length; i++)
            {
                // The Pattern ends before the Logger Name does, so it can't match anything under it
                if (i >= patternSegments.Length) return false;

                // "**" can match the rest of the Logger Name and anything under it
                if (patternSegments[i].Contains("**")) return true;

                if (!GetRegex(patternSegments[i]).IsMatch(nameSegments[i])) return false;
            }

            // Segments are left in the Pattern for the path under the Logger Name
            return patternSegments.Length > nameSegments.Length;
        }


        private static Regex GetRegex(string pattern)
        {
            return _regexCache.GetOrAdd(pattern.Trim('/').ToLowerInvariant(), p =>
            {
                var builder = new StringBuilder("^");

                for (var i = 0; i < p.Length; i++)
                {
                    var c = p[i];
                    if (c == '*')
                    {
                        if (i + 1 < p.Length && p[i + 1] == '*')
                        {
                            i++;

                            // "a/**" also matches "a" itself
                            if (builder.Length > 1 && builder[builder.Length - 1] == '/' && i == p.Length - 1)
                            {
                                builder.Length -= 1;
                                builder.Append("(/.*)?");
                            }
                            else
                            {
                                builder.Append(".*");
                            }
                        }
                        else
                        {
                            builder.Append("[^/]*");
                        }
                    }
                    else
                    {
                        builder.Append(Regex.Escape(c.ToString()));
                    }
                }

                builder.Append('$');
                return new Regex(builder.ToString(), RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);
            });
        }
    }
}
