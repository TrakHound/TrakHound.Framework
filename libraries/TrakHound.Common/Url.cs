// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Web;

namespace TrakHound
{
    /// <summary>
    /// Helpers for building and parsing Urls, routes, and query strings
    /// </summary>
    public static class Url
    {
        /// <summary>
        /// The character used to separate the parts of a Url path ('/')
        /// </summary>
        public const char PathSeparator = '/';

        // Matches a route parameter such as {name}, {name:constraint}, or {*slug}
        // Group 1 = '*' (if slug), Group 2 = parameter name
        private static readonly Regex _routeParameterRegex = new Regex(@"\{(\*)?([^}:]+)(?::.+)?\}", RegexOptions.Compiled);


        /// <summary>
        /// Combines the paths into a single Url path, trimming separators from each part and skipping empty parts (null if no paths are specified)
        /// </summary>
        public static string Combine(params string[] paths)
        {
            if (!paths.IsNullOrEmpty())
            {
                var p = new List<string>();

                foreach (var path in paths)
                {
                    if (!string.IsNullOrEmpty(path))
                    {
                        var trimmed = path.Trim(PathSeparator);
                        if (trimmed.Length > 0) p.Add(trimmed);
                    }
                }

                return string.Join(PathSeparator, p);
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
        /// Rewrites the route using a Regex pattern and a replacement where "{R:n}" is replaced with capture group n. Query parameters are preserved and the route is returned unchanged if the pattern does not match
        /// </summary>
        public static string Rewrite(string route, string pattern, string replacement)
        {
            if (!string.IsNullOrEmpty(route) && !string.IsNullOrEmpty(pattern))
            {
                var queryParameters = GetQueryParameters(route);
                var fRoute = RemoveQueryParameters(route);

                var match = Regex.Match(fRoute, pattern);
                if (match.Success)
                {
                    if (replacement == "/") return "/";

                    var result = replacement ?? "";
                    for (int i = 1; i < match.Groups.Count; i++)
                    {
                        result = result.Replace("{R:" + i + "}", match.Groups[i].Value);
                    }
                    fRoute = result;
                }

                return AddQueryParameters(fRoute, queryParameters);
            }

            return route;
        }

        /// <summary>
        /// Url encodes the string (null if empty)
        /// </summary>
        public static string Encode(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                return HttpUtility.UrlEncode(url);
            }

            return null;
        }

        /// <summary>
        /// Url decodes the string (null if empty)
        /// </summary>
        public static string Decode(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                return HttpUtility.UrlDecode(url);
            }

            return null;
        }

        /// <summary>
        /// Adds a query parameter to the Url, encoding the name and value (the Url is returned unchanged if the value is null or empty)
        /// </summary>
        public static string AddQueryParameter(string url, string parameterName, object parameterValue)
        {
            if (!string.IsNullOrEmpty(parameterName) && parameterValue != null)
            {
                var lUrl = !string.IsNullOrEmpty(url) ? url : "";

                var valueStr = parameterValue.ToString();
                if (!string.IsNullOrEmpty(valueStr))
                {
                    string path;
                    string query = null;

                    var i = lUrl.IndexOf('?');
                    if (i >= 0)
                    {
                        path = lUrl.Substring(0, i);
                        query = lUrl.Substring(i);
                    }
                    else
                    {
                        path = lUrl;
                    }

                    var parameter = HttpUtility.UrlEncode(parameterName) + '=' + HttpUtility.UrlEncode(valueStr);

                    if (!string.IsNullOrEmpty(query) && query != "?")
                    {
                        query += '&' + parameter;
                    }
                    else
                    {
                        query = '?' + parameter;
                    }

                    return path + query;
                }
            }

            return url;
        }

        /// <summary>
        /// Adds each of the parameters as a query parameter to the Url
        /// </summary>
        public static string AddQueryParameters(string url, IReadOnlyDictionary<string, string> parameters)
        {
            var result = url;

            if (!parameters.IsNullOrEmpty())
            {
                foreach (var parameter in parameters)
                {
                    result = AddQueryParameter(result, parameter.Key, parameter.Value);
                }
            }

            return result;
        }

        /// <summary>
        /// Serializes the value to JSON and adds it as a query parameter to the Url
        /// </summary>
        public static string AddQueryParameterJson(string url, string parameterName, object parameterValue)
        {
            if (parameterValue != null)
            {
                var json = Json.Convert(parameterValue);
                if (json != null)
                {
                    return AddQueryParameter(url, parameterName, json);
                }
            }

            return url;
        }

        /// <summary>
        /// Removes the query string from the Url (null if the Url is empty)
        /// </summary>
        public static string RemoveQueryParameters(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                var i = url.IndexOf('?');
                if (i >= 0)
                {
                    return url.Substring(0, i);
                }
                else
                {
                    return url;
                }
            }

            return null;
        }
       
        /// <summary>
        /// Gets the first segment of the Uri path (null if empty)
        /// </summary>
        public static string GetFirstFragment(string uri)
        {
            if (!string.IsNullOrEmpty(uri))
            {
                var s = uri.Trim('/');
                if (!string.IsNullOrEmpty(s))
                {
                    var i = s.IndexOf('/');
                    if (i >= 0)
                    {
                        return s.Substring(0, i);
                    }
                    else return s;
                }
            }

            return null;
        }

        /// <summary>
        /// Removes the first segment of the Uri path and trims the leading and trailing separators (null if the path has only one segment)
        /// </summary>
        public static string RemoveFirstFragment(string uri)
        {
            if (!string.IsNullOrEmpty(uri))
            {
                var s = uri.Trim('/');
                if (!string.IsNullOrEmpty(s))
                {
                    var i = s.IndexOf('/');
                    if (i >= 0)
                    {
                        return s.Substring(i).TrimStart('/');
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the scheme and authority of the Url (ex. "https://host:port") or null if the Url does not contain "://"
        /// </summary>
        public static string GetLeftPart(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                var p = "://";

                var i = url.IndexOf(p, StringComparison.Ordinal);
                if (i >= 0)
                {
                    // Authority ends at the first path, query, or fragment delimiter
                    var end = url.IndexOfAny(new[] { '/', '?', '#' }, i + p.Length);
                    return end >= 0 ? url.Substring(0, end) : url;
                }
            }

            return null;
        }

        /// <summary>
        /// Removes the base path from the Url, matching only on a full segment boundary (empty if the Url equals the base, null if it does not start with the base)
        /// </summary>
        public static string RemoveBase(string url, string baseUrl)
        {
            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(baseUrl))
            {
                var b = baseUrl.Trim('/');
                var s = url.Trim('/');
                if (!string.IsNullOrEmpty(s))
                {
                    if (b.Length == 0) return s;
                    if (s == b) return "";

                    // Only match on a full segment boundary (ex. "api" should not match "apiv2")
                    if (s.StartsWith(b + '/', StringComparison.Ordinal))
                    {
                        return s.Substring(b.Length).TrimStart('/');
                    }
                }
            }

            return null;
        }


        /// <summary>
        /// Gets the last segment of the Uri path (null if empty)
        /// </summary>
        public static string GetLastFragment(string uri)
        {
            if (!string.IsNullOrEmpty(uri))
            {
                var s = uri.Trim('/');
                if (!string.IsNullOrEmpty(s))
                {
                    var i = s.LastIndexOf('/');
                    return i >= 0 ? s.Substring(i + 1) : s;
                }
            }

            return null;
        }

        /// <summary>
        /// Removes the last segment of the Uri path and trims the leading and trailing separators (null if the path has only one segment)
        /// </summary>
        public static string RemoveLastFragment(string uri)
        {
            if (!string.IsNullOrEmpty(uri))
            {
                var s = uri.Trim('/');
                if (!string.IsNullOrEmpty(s))
                {
                    var i = s.LastIndexOf('/');
                    if (i >= 0)
                    {
                        return s.Substring(0, i);
                    }
                }
            }

            return null;
        }


        /// <summary>
        /// Gets the value of a parameter from the Url using a route template ("{name}", "{name:constraint}", or "{*slug}" which captures the rest of the path). Returns null if not found
        /// </summary>
        public static string GetRouteParameter(string url, string route, string parameterName)
        {
            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(route) && !string.IsNullOrEmpty(parameterName))
            {
                url = RemoveQueryParameters(url);

                var urlParts = url.Trim('/').Split('/');
                var routeParts = route.Trim('/').Split('/');

                if (urlParts.Length >= routeParts.Length)
                {
                    for (var i = 0; i < routeParts.Length; i++)
                    {
                        var match = _routeParameterRegex.Match(routeParts[i]);
                        if (match.Success && match.Groups[2].Value == parameterName)
                        {
                            var isSlug = match.Groups[1].Success;
                            if (isSlug)
                            {
                                // Slug captures all remaining segments
                                return string.Join('/', urlParts, i, urlParts.Length - i);
                            }

                            return urlParts[i];
                        }
                    }
                }
            }

            return null;
        }

        /// <summary>
        /// Replaces the segment of the Url that matches the parameter in the route template with the value, preserving the query string (null if the Url has fewer segments than the route)
        /// </summary>
        public static string AddRouteParameter(string url, string route, string parameterName, object parameterValue)
        {
            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(route) && !string.IsNullOrEmpty(parameterName))
            {
                string query = null;
                var q = url.IndexOf('?');
                if (q >= 0)
                {
                    query = url.Substring(q);
                    url = url.Substring(0, q);
                }

                var urlParts = url.Split('/');
                var routeParts = route.Split('/');

                if (urlParts.Length >= routeParts.Length)
                {
                    for (var i = 0; i < routeParts.Length; i++)
                    {
                        var match = _routeParameterRegex.Match(routeParts[i]);
                        if (match.Success && match.Groups[2].Value == parameterName)
                        {
                            urlParts[i] = parameterValue?.ToString();
                        }
                    }

                    // Segments beyond the route (ex. from a previously filled slug) are preserved
                    return string.Join('/', urlParts) + query;
                }
            }

            return null;
        }

        /// <summary>
        /// Gets the value of a query parameter from the Url (null if not found)
        /// </summary>
        public static string GetQueryParameter(string url, string parameterName)
        {
            if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(parameterName))
            {
                try
                {
                    var i = url.IndexOf('?');
                    if (i >= 0)
                    {
                        var query = url.Substring(i);
                        var parameters = HttpUtility.ParseQueryString(query);
                        return parameters.Get(parameterName);
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Gets the query parameters of the Url as a Dictionary (null if the Url has no query string)
        /// </summary>
        public static Dictionary<string, string> GetQueryParameters(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                try
                {
                    var i = url.IndexOf('?');
                    if (i >= 0)
                    {
                        var query = url.Substring(i);
                        var parameters = HttpUtility.ParseQueryString(query);
                        if (parameters != null && !parameters.AllKeys.IsNullOrEmpty())
                        {
                            var results = new Dictionary<string, string>();
                            foreach (var key in parameters.AllKeys)
                            {
                                if (!string.IsNullOrEmpty(key))
                                {
                                    var value = parameters[key];
                                    if (value != null)
                                    {
                                        if (!results.ContainsKey(key)) results.Add(key, value);
                                    }
                                }
                            }
                            return results;
                        }
                    }
                }
                catch { }
            }

            return null;
        }
    }
}
