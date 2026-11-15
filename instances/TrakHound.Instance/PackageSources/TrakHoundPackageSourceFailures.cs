// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instance.Configurations;

namespace TrakHound.Instance.PackageSources
{
    /// <summary>
    /// Logs a request to a Package Source that failed once for each kind of failure (by Status Code, 0 if it couldn't be reached), rather than on every attempt,
    /// and logs when the Source answers again
    /// </summary>
    internal class TrakHoundPackageSourceFailures
    {
        private readonly Dictionary<string, int> _failures = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase); // Source ID => Status Code of the last failure
        private readonly object _lock = new object();


        public static string GetName(RemoteConfiguration source) => source?.Name ?? source?.Id ?? source?.BaseUrl;

        /// <summary>
        /// Record a failure. Returns true if it is a different kind of failure than the last one for the Source, so it should be logged
        /// </summary>
        public bool Failed(RemoteConfiguration source, int statusCode)
        {
            var key = GetKey(source);
            lock (_lock)
            {
                if (_failures.TryGetValue(key, out var previous) && previous == statusCode) return false;

                _failures[key] = statusCode;
                return true;
            }
        }

        /// <summary>
        /// Record a success. Returns true if the Source had failed before, so its recovery should be logged
        /// </summary>
        public bool Succeeded(RemoteConfiguration source)
        {
            lock (_lock) return _failures.Remove(GetKey(source));
        }

        /// <summary>
        /// Describe a failed request in a log line, with what to check for an Access Token that is refused
        /// </summary>
        public static string Describe(int statusCode, string message)
        {
            switch (statusCode)
            {
                case 0: return $"it couldn't be reached ({message})";
                case 401: return "the Access Token was refused (401 Unauthorized). Check the Source's Access Token";
                case 403: return "the Access Token isn't allowed (403 Forbidden). Check that the Instance is registered and approved on the Package Manager";
                default: return $"Status {statusCode}{(!string.IsNullOrEmpty(message) && message != $"Status {statusCode}" ? $" ({message})" : null)}";
            }
        }

        private static string GetKey(RemoteConfiguration source) => source?.Id ?? source?.BaseUrl ?? string.Empty;
    }
}
