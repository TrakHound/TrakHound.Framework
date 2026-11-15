// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace TrakHound
{
    /// <summary>
    /// Helpers for working with TrakHound Object paths ("/" separated)
    /// </summary>
    public static class TrakHoundPath
    {
        /// <summary>
        /// The character used to separate the parts of a path ('/')
        /// </summary>
        public const char PathSeparator = '/';


        /// <summary>
        /// Combines the paths into a single path, trimming separators between parts (null if no paths are specified)
        /// </summary>
        public static string Combine(params string[] paths)
		{
			if (!paths.IsNullOrEmpty())
			{
				var p = new List<string>();

                for (var i = 0; i < paths.Length; i++)
                {
                    var path = paths[i];
                    if (!string.IsNullOrEmpty(path))
                    {
						if (i == 0) p.Add(path.TrimEnd(PathSeparator));
						else p.Add(path.Trim(PathSeparator));
					}
                }

				return string.Join(PathSeparator, p).TrimEnd(PathSeparator);
			}

			return null;
		}

        /// <summary>
        /// Returns true if the path is a descendant of (below) the parent path
        /// </summary>
        public static bool IsChildOf(string parentPath, string path)
        {
            if (!string.IsNullOrEmpty(parentPath) && !string.IsNullOrEmpty(path))
            {
                var parentPaths = GetPaths(parentPath).ToArray();
                var paths = GetPaths(path).ToArray();

                if (parentPaths.Count() < paths.Count())
                {
                    for (var i = 0; i < parentPaths.Count(); i++)
                    {
                        var parent = parentPaths[i];
                        var target = paths[i];

                        if (target != parent) return false;
                    }

                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets the first part of the path (the path itself if it contains no separator)
        /// </summary>
        public static string GetRoot(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                if (path.Contains(PathSeparator))
                {
                    var i = path.IndexOf(PathSeparator);
                    if (i > 0 && i < path.Length - 1)
                    {
                        return path.Substring(0, i);
                    }
                }
            }

            return path;
        }

        /// <summary>
        /// Gets the parent path by removing the last part of the path (null if the path has no parent)
        /// </summary>
        public static string GetParentPath(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                if (path.Contains(PathSeparator))
                {
                    var i = path.LastIndexOf(PathSeparator);
                    if (i > 0 && i < path.Length - 1)
                    {
                        return path.Substring(0, i);
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the ancestor path at the specified depth (0 is the root), only if it is above the path itself (null otherwise)
        /// </summary>
        public static string GetParentPath(string path, int index)
        {
            if (!string.IsNullOrEmpty(path) && index > -1)
            {
                var parts = GetPaths(path).ToArray();
                if (index < parts.Length - 1)
                {
                    return parts[index];
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the last part of the path (the Object name)
        /// </summary>
        public static string GetObject(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                if (path.Contains(PathSeparator))
                {
                    var i = path.LastIndexOf(PathSeparator);
                    if (i > 0 && i < path.Length - 1)
                    {
                        return path.Substring(i + 1).Trim('/');
                    }
                }

                return path.Trim('/');
            }

            return null;
        }

        /// <summary>
        /// Gets the path relative to the specified base path (the path is returned unchanged if either is empty)
        /// </summary>
        public static string GetRelativeTo(string relativePath, string path)
        {
            if (!string.IsNullOrEmpty(relativePath) && !string.IsNullOrEmpty(path))
            {
                var result = System.IO.Path.GetRelativePath(relativePath, path);
                return result?.Replace('\\', '/');
			}

			return path;
		}

        /// <summary>
        /// Gets a shortened version of the target path that keeps only the parts needed to distinguish it from the other paths ("/../" marks removed parts)
        /// </summary>
        public static string GetTruncatedPath(IEnumerable<string> paths, string targetPath)
        {
            if (!paths.IsNullOrEmpty() && !string.IsNullOrEmpty(targetPath))
            {
                var pathPairs = new Dictionary<string, string[]>();
                var dPaths = paths.Where(o => !string.IsNullOrEmpty(o))?.Distinct();

                if (!dPaths.IsNullOrEmpty())
                {
                    foreach (var path in dPaths)
                    {
                        // Get a list of the Part Parts
                        var parts = path.Split(PathSeparator);
                        if (!parts.IsNullOrEmpty()) pathPairs.Add(path, parts.ToArray());
                    }

                    if (pathPairs.ContainsKey(targetPath))
                    {
                        var targetParts = pathPairs[targetPath];
                        var otherPairs = pathPairs.Where(o => o.Key != targetPath);

                        var truncatedPath = "";
                        var prevMatch = false;
                        var prevPart = "";

                        for (var i = 0; i < targetParts.Length; i++)
                        {
                            var targetPart = targetParts[i];
                            var match = true;

                            if (i < targetParts.Length - 1)
                            {
                                foreach (var otherPair in otherPairs)
                                {
                                    if (otherPair.Value.Length > i)
                                    {
                                        var otherPart = otherPair.Value[i];

                                        if (targetPart != otherPart)
                                        {
                                            if (prevMatch) truncatedPath += "/../";
                                            truncatedPath = Combine(truncatedPath, prevPart, targetPart);
                                            match = false;
                                            break;
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // Always add the last Part
                                truncatedPath = Combine(truncatedPath, targetPart);
                            }

                            prevPart = targetPart;
                            prevMatch = match;
                        }

                        return truncatedPath;
                    }
                }
            }

            return targetPath;
        }

        /// <summary>
        /// Gets the path and each of its ancestor paths, starting from the root (ex. "a/b/c" returns "a", "a/b", "a/b/c")
        /// </summary>
        public static IEnumerable<string> GetPaths(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var paths = new List<string>();

                var parts = path.Split(PathSeparator, System.StringSplitOptions.RemoveEmptyEntries);
                if (!parts.IsNullOrEmpty())
                {
                    for (var i = 0; i < parts.Length; i++)
                    {
                        // Add Parent Paths
                        var partpaths = new List<string>();
                        for (var j = 0; j <= i; j++) partpaths.Add(parts[j]);

                        paths.Add(string.Join(PathSeparator, partpaths));
                    }
                }

                return paths;
            }

            return null;
        }

        /// <summary>
        /// Gets the value of a parameter from the path using a route pattern ("{name}" matches one part and "{*name}" matches the rest of the path). Returns null if not found
        /// </summary>
        public static string GetPathParameter(string path, string pattern, string parameterName)
        {
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(pattern) && !string.IsNullOrEmpty(parameterName))
            {
                var pathParts = path.Trim('/').Split('/');
                var patternParts = pattern.Trim('/').Split('/');

                if (pathParts != null && patternParts != null && pathParts.Length >= patternParts.Length)
                {
                    for (var i = 0; i < pathParts.Length; i++)
                    {
                        if (i < patternParts.Length)
                        {
                            var pathPart = pathParts[i];
                            var patternPart = patternParts[i];

                            // Check for Slug Match
                            var slugRegex = new Regex(@"(\{\*(.*?)\})");
                            if (slugRegex.IsMatch(patternPart))
                            {
                                var match = slugRegex.Match(patternPart);
                                if (match != null)
                                {
                                    var matchText = match.Groups[1].ToString();
                                    var pathParameterName = match.Groups[2].ToString();

                                    if (pathParameterName == parameterName)
                                    {
                                        var slugParts = new List<string>();
                                        for (var j = i; j < pathParts.Length; j++) slugParts.Add(pathParts[j]);

                                        return string.Join('/', slugParts);
                                    }
                                }
                            }

                            // Check for Exact Match
                            var exactRegex = new Regex(@"(\{(.*?)\})");
                            if (exactRegex.IsMatch(patternPart))
                            {
                                var match = exactRegex.Match(patternPart);
                                if (match != null)
                                {
                                    var matchText = match.Groups[1].ToString();
                                    var pathParameterName = match.Groups[2].ToString();

                                    if (pathParameterName == parameterName) return pathPart;
                                }
                            }
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Replaces the "{name}" parameter in the pattern with the value and returns the resulting path
        /// </summary>
        public static string SetPathParameter(string path, string pattern, string parameterName, string parameterValue)
        {
            if (!string.IsNullOrEmpty(path) && !string.IsNullOrEmpty(pattern) && !string.IsNullOrEmpty(parameterName))
            {
                var pathParts = path.Split('/');
                var patternParts = pattern.Split('/');

                if (pathParts != null && patternParts != null && pathParts.Length >= patternParts.Length)
                {
                    for (var i = 0; i < pathParts.Length; i++)
                    {
                        if (i < patternParts.Length)
                        {
                            var pathPart = pathParts[i];
                            var patternPart = patternParts[i];
                            if (!string.IsNullOrEmpty(patternPart) && !string.IsNullOrEmpty(parameterValue))
                            {
                                var regexPattern = "({" + parameterName + "})";

                                var regex = new Regex(regexPattern);
                                if (regex.IsMatch(patternPart))
                                {
                                    pathParts[i] = regex.Replace(patternPart, parameterValue);
                                }
                            }
                        }
                    }
                }

                return string.Join('/', pathParts);
            }

            return path;
        }
    }
}
