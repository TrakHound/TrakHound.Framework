// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using System.Collections.Concurrent;
using System.Globalization;
using System.Reflection;

namespace TrakHound.Blazor.Routing
{
    /// <summary>
    /// Sets a Page's [Parameter] Properties from the Route Values, the way Blazor's Router does
    /// </summary>
    public static class TrakHoundRouteParameters
    {
        // Parameter Properties are cached per Page Type so that Reflection is only performed once per Type
        private static readonly ConcurrentDictionary<Type, PropertyInfo[]> _parameterProperties = new();
        private static readonly ConcurrentDictionary<Type, (PropertyInfo Property, string Name)[]> _queryProperties = new();
        private static readonly ConcurrentDictionary<Type, HashSet<string>> _routeParameterNames = new();


        /// <summary>
        /// The value of each [Parameter] Property named in the Template (case-insensitively). A Parameter missing from the Path
        /// (an optional Parameter) or whose value can't be converted is set to null (the default for value types), so a value
        /// from a previous Navigation to the same Page isn't kept. The same goes for a Parameter that is only in another of the
        /// Page's Routes (ex. "/orders" after "/orders/{id}"), as with Blazor's Router
        /// </summary>
        public static Dictionary<string, object> GetRouteParameters(Type pageType, MatchResult matchResult)
        {
            var parameterValues = new Dictionary<string, object>();

            var parameterNames = matchResult?.Template?.ParameterNames;
            if (pageType != null)
            {
                // The Parameters of the Page's other Routes : the same Page is kept when navigating between its Routes
                var otherParameterNames = GetRouteParameterNames(pageType);

                if (!parameterNames.IsNullOrEmpty() || otherParameterNames.Count > 0)
                {
                    foreach (var property in GetParameterProperties(pageType))
                    {
                        if (!parameterNames.IsNullOrEmpty() && parameterNames.Contains(property.Name, StringComparer.OrdinalIgnoreCase))
                        {
                            object parameterValue = null;
                            if (matchResult.RouteValues != null && matchResult.RouteValues.TryGetValue(property.Name, out var routeValue))
                            {
                                TryConvert(routeValue, property.PropertyType, out parameterValue);
                            }

                            parameterValues[property.Name] = parameterValue;
                        }
                        else if (otherParameterNames.Contains(property.Name))
                        {
                            parameterValues[property.Name] = null;
                        }
                    }
                }
            }

            return parameterValues;
        }

        /// <summary>
        /// The value of each [Parameter, SupplyParameterFromQuery] Property from the query string. Only for a Route that isn't the
        /// browser's location (ex. TrakHoundApp's Route) : for the location, Blazor supplies [SupplyParameterFromQuery] itself
        /// (a Property without [Parameter] only accepts that cascading value, so it can't be set here)
        /// </summary>
        public static Dictionary<string, object> GetQueryParameters(Type pageType, string query)
        {
            var parameterValues = new Dictionary<string, object>();

            if (pageType != null && !string.IsNullOrEmpty(query))
            {
                var queryValues = ParseQuery(query);
                if (queryValues.Count > 0)
                {
                    foreach (var (property, name) in GetQueryProperties(pageType))
                    {
                        if (queryValues.TryGetValue(name, out var values) && values.Count > 0)
                        {
                            if (property.PropertyType.IsArray && property.PropertyType != typeof(string))
                            {
                                var elementType = property.PropertyType.GetElementType();
                                var array = Array.CreateInstance(elementType, values.Count);
                                for (var i = 0; i < values.Count; i++)
                                {
                                    if (TryConvert(values[i], elementType, out var element)) array.SetValue(element, i);
                                }

                                parameterValues[property.Name] = array;
                            }
                            else if (TryConvert(values[values.Count - 1], property.PropertyType, out var parameterValue))
                            {
                                parameterValues[property.Name] = parameterValue;
                            }
                        }
                    }
                }
            }

            return parameterValues;
        }

        /// <summary>
        /// Converts a Route or Query value to the Property's type (invariant culture, as Blazor does). Nullable types, enums
        /// (case-insensitive) and Guids are supported
        /// </summary>
        public static bool TryConvert(string value, Type type, out object result)
        {
            result = null;

            if (value == null || type == null) return false;

            var convertType = Nullable.GetUnderlyingType(type) ?? type;

            try
            {
                if (convertType == typeof(string) || convertType == typeof(object))
                {
                    result = value;
                    return true;
                }
                else if (convertType == typeof(Guid))
                {
                    if (Guid.TryParse(value, out var guid))
                    {
                        result = guid;
                        return true;
                    }
                }
                else if (convertType == typeof(DateTime))
                {
                    if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTime))
                    {
                        result = dateTime;
                        return true;
                    }

                    // Other formats TrakHound accepts (ex. Unix milliseconds)
                    result = value.ToDateTime();
                    return true;
                }
                else if (convertType == typeof(DateTimeOffset))
                {
                    if (DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var dateTimeOffset))
                    {
                        result = dateTimeOffset;
                        return true;
                    }
                }
                else if (convertType.IsEnum)
                {
                    if (Enum.TryParse(convertType, value, true, out var enumResult))
                    {
                        result = enumResult;
                        return true;
                    }
                }
                else
                {
                    result = Convert.ChangeType(value, convertType, CultureInfo.InvariantCulture);
                    return true;
                }
            }
            catch { }

            result = null;
            return false;
        }

        private static Dictionary<string, List<string>> ParseQuery(string query)
        {
            var values = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

            var queryIndex = query.IndexOf('?');
            if (queryIndex > -1) query = query.Substring(queryIndex + 1);

            foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                // Split at the first '=' only, so a value can contain '='
                var separatorIndex = pair.IndexOf('=');
                var key = Decode(separatorIndex > -1 ? pair.Substring(0, separatorIndex) : pair);
                var value = separatorIndex > -1 ? Decode(pair.Substring(separatorIndex + 1)) : string.Empty;

                if (!string.IsNullOrEmpty(key))
                {
                    if (!values.TryGetValue(key, out var keyValues))
                    {
                        keyValues = new List<string>();
                        values[key] = keyValues;
                    }

                    keyValues.Add(value);
                }
            }

            return values;
        }

        private static string Decode(string value)
        {
            try
            {
                return Uri.UnescapeDataString(value.Replace('+', ' '));
            }
            catch
            {
                return value;
            }
        }

        // The names of the Parameters in any of the Page's Routes (its @page directives)
        private static HashSet<string> GetRouteParameterNames(Type pageType)
        {
            return _routeParameterNames.GetOrAdd(pageType, type =>
            {
                var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var route in type.GetCustomAttributes<RouteAttribute>(false))
                {
                    var template = route.Template;
                    if (string.IsNullOrEmpty(template)) continue;

                    var start = template.IndexOf('{');
                    while (start > -1)
                    {
                        var end = template.IndexOf('}', start);
                        if (end < 0) break;

                        // "{id:int}", "{id?}", "{*path}", "{**path}"
                        var name = template.Substring(start + 1, end - start - 1).TrimStart('*');
                        var nameEnd = name.IndexOfAny(new[] { ':', '?', '=' });
                        if (nameEnd > -1) name = name.Substring(0, nameEnd);
                        if (name.Length > 0) names.Add(name);

                        start = template.IndexOf('{', end);
                    }
                }

                return names;
            });
        }

        private static PropertyInfo[] GetParameterProperties(Type pageType)
        {
            return _parameterProperties.GetOrAdd(pageType, type =>
                type.GetProperties()
                    .Where(o => o.GetCustomAttribute<ParameterAttribute>() != null)
                    .ToArray());
        }

        private static (PropertyInfo Property, string Name)[] GetQueryProperties(Type pageType)
        {
            return _queryProperties.GetOrAdd(pageType, type =>
                type.GetProperties()
                    .Where(o => o.GetCustomAttribute<ParameterAttribute>() != null)
                    .Select(o => (Property: o, Attribute: o.GetCustomAttribute<SupplyParameterFromQueryAttribute>()))
                    .Where(o => o.Attribute != null)
                    .Select(o => (o.Property, !string.IsNullOrEmpty(o.Attribute.Name) ? o.Attribute.Name : o.Property.Name))
                    .ToArray());
        }
    }
}
