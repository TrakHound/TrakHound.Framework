// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Http
{
    /// <summary>
    /// Hides the values of sensitive Query parameters (ex. token, apiKey, password) so they aren't kept in the Recent HTTP and API Requests
    /// </summary>
    public static class TrakHoundHttpQueryMask
    {
        public const string Mask = "***";

        // Names that are sensitive on their own (compared without '_' and '-')
        private static readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase)
        {
            "key", "code", "sig", "auth", "pwd", "pass", "session", "sessionid", "jwt"
        };

        // Names that contain any of these are sensitive (ex. access_token, client_secret, x-api-key)
        private static readonly string[] _contains = new[]
        {
            "token", "secret", "password", "apikey", "signature", "authorization", "credential"
        };


        /// <summary>
        /// The Query String (including the leading '?') with the values of sensitive parameters replaced with ***
        /// </summary>
        public static string Apply(string queryString)
        {
            if (string.IsNullOrEmpty(queryString)) return queryString;

            var query = queryString.StartsWith('?') ? queryString.Substring(1) : queryString;
            var parameters = query.Split('&');
            var masked = false;

            for (var i = 0; i < parameters.Length; i++)
            {
                var parameter = parameters[i];
                var separator = parameter.IndexOf('=');
                if (separator < 1 || separator == parameter.Length - 1) continue;

                var name = parameter.Substring(0, separator);
                if (IsSensitive(name))
                {
                    parameters[i] = $"{name}={Mask}";
                    masked = true;
                }
            }

            return masked ? "?" + string.Join('&', parameters) : queryString;
        }

        public static bool IsSensitive(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;

            string normalized;
            try
            {
                normalized = Uri.UnescapeDataString(name.Replace('+', ' '));
            }
            catch
            {
                normalized = name;
            }

            normalized = normalized.Replace("_", "").Replace("-", "").Replace(" ", "");
            if (_names.Contains(normalized)) return true;

            foreach (var value in _contains)
            {
                if (normalized.Contains(value, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
