// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Security;

namespace TrakHound.Debug
{
    /// <summary>
    /// A Request for testing an Identity Provider. Set its Headers and Query Parameters before calling the Provider
    /// </summary>
    public class TrakHoundDebugIdentityRequest : ITrakHoundIdentityRequest
    {
        public string Method { get; set; } = "GET";

        public string Path { get; set; } = "/";

        public string ClientAddress { get; set; } = "127.0.0.1";

        public Dictionary<string, string> Headers { get; } = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public Dictionary<string, string> QueryParameters { get; } = new Dictionary<string, string>();

        public Dictionary<string, string> FormValues { get; } = new Dictionary<string, string>();


        public string GetHeader(string name) => Headers.GetValueOrDefault(name);

        public string GetQueryParameter(string name) => QueryParameters.GetValueOrDefault(name);

        public Task<string> GetFormValue(string name) => Task.FromResult(FormValues.GetValueOrDefault(name));
    }

    /// <summary>
    /// An in memory State Store (values expire after their lifetime and can only be read once, as on the Instance)
    /// </summary>
    public class TrakHoundDebugIdentityStateStore : ITrakHoundIdentityStateStore
    {
        private readonly Dictionary<string, (string Value, DateTime Expires)> _values = new Dictionary<string, (string, DateTime)>();


        public void Set(string key, string value, TimeSpan lifetime) => _values[key] = (value, DateTime.UtcNow.Add(lifetime));

        public string Take(string key)
        {
            if (_values.Remove(key, out var entry) && entry.Expires > DateTime.UtcNow) return entry.Value;
            return null;
        }
    }
}
