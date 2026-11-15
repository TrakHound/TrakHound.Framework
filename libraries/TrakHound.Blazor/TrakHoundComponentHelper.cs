// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Blazor.Services;
using TrakHound.Configurations;
using TrakHound.Logging;

namespace TrakHound.Blazor
{
    /// <summary>
    /// Shared logic for TrakHoundComponentBase and TrakHoundLayoutBase (which can't share a base class)
    /// </summary>
    internal static class TrakHoundComponentHelper
    {
        public static string GetUrl(string baseUrl, string destination, TrakHoundAppService appService)
        {
            var url = EnsureLeadingSlash(Url.Combine(baseUrl, destination));

            if (appService != null)
            {
                url = appService.RewriteUrl(url);
                url = EnsureLeadingSlash(Url.Combine(baseUrl, url));
            }

            return url;
        }

        private static string EnsureLeadingSlash(string url)
        {
            if (url == null) return "/";
            return url.StartsWith('/') ? url : '/' + url;
        }

        public static string GetParameter(Dictionary<string, object> parameters, string name)
        {
            if (parameters != null && !string.IsNullOrEmpty(name) && parameters.TryGetValue(name, out var obj))
            {
                // Encrypted secrets (ex. a password) are decrypted
                return TrakHoundSecrets.Resolve(obj, "app", null, name)?.ToString();
            }

            return null;
        }

        public static T GetParameter<T>(Dictionary<string, object> parameters, string name, ITrakHoundLogger logger, string componentName)
        {
            if (parameters == null || string.IsNullOrEmpty(name) || !parameters.TryGetValue(name, out var obj) || obj == null) return default;
            obj = TrakHoundSecrets.Resolve(obj, "app", componentName, name);
            if (obj == null) return default;
            if (obj is T typed) return typed;

            if (obj is JsonElement json)
            {
                try
                {
                    return json.Deserialize<T>();
                }
                catch { }

                // Fall back to converting the raw text (ex. "42" stored as a JSON String)
                obj = json.ValueKind == JsonValueKind.String ? json.GetString() : json.GetRawText();
                if (obj == null) return default;
            }

            var targetType = Nullable.GetUnderlyingType(typeof(T)) ?? typeof(T);

            try
            {
                if (targetType.IsEnum) return (T)Enum.Parse(targetType, obj.ToString(), true);
                return (T)Convert.ChangeType(obj, targetType);
            }
            catch (Exception ex)
            {
                logger?.LogDebug($"GetParameter : {componentName} : {name} : Unable to convert to {typeof(T).Name} : {ex.Message}");
                return default;
            }
        }

        public static void SetParameter(Dictionary<string, object> parameters, string name, object value)
        {
            if (parameters != null && !string.IsNullOrEmpty(name) && value != null)
            {
                parameters[name] = value.ToString();
            }
        }
    }
}
