// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Logging;

namespace TrakHound.Instance.Health
{
    /// <summary>
    /// Watches for errors that mean a Package was built against a different (incompatible) version of the TrakHound libraries,
    /// ex. "Method not found" (MissingMethodException) or "Could not load type" (TypeLoadException).
    /// Errors are read from the Log Provider (Apis, Workers, ...) and can be reported directly (ex. Function Run results).
    /// </summary>
    public class TrakHoundPackageCompatibilityTracker : IDisposable
    {
        /// <summary>
        /// How long a Package is reported after its most recent error
        /// </summary>
        public static readonly TimeSpan Retention = TimeSpan.FromHours(1);

        private const int _maxMessageLength = 300;

        // Exception messages (and HResult codes logged as the Code) that indicate a binary incompatibility
        private static readonly string[] _messagePatterns =
        {
            "Method not found",
            "Field not found",
            "Could not load type",
            "Could not load file or assembly",
            "MissingMethodException",
            "MissingFieldException",
            "TypeLoadException"
        };

        private static readonly string[] _codes =
        {
            "-2146233069", // MissingMethodException
            "-2146233071", // MissingFieldException
            "-2146233054"  // TypeLoadException
        };

        // Logger names start with the Category followed by the Package ID (ex. "api/TrakHound.Functions.Api/...", "workers/{packageId}/{workerId}/Engine")
        private static readonly HashSet<string> _packageCategories = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "api", "apps", "workers", "functions", "services" };

        private readonly ITrakHoundLogProvider _logProvider;
        private readonly Dictionary<string, Incident> _incidents = new Dictionary<string, Incident>(StringComparer.OrdinalIgnoreCase); // Package ID => Incident
        private readonly object _lock = new object();


        public class Incident
        {
            public string PackageId { get; set; }
            public string Source { get; set; }
            public string Message { get; set; }
            public DateTime FirstSeen { get; set; }
            public DateTime LastSeen { get; set; }
            public long Count { get; set; }
        }


        public TrakHoundPackageCompatibilityTracker(ITrakHoundLogProvider logProvider)
        {
            _logProvider = logProvider;
            if (_logProvider != null) _logProvider.LogEntryReceived += LogEntryReceived;
        }

        public void Dispose()
        {
            if (_logProvider != null) _logProvider.LogEntryReceived -= LogEntryReceived;
        }


        /// <summary>
        /// Whether the message (or code) is from an error caused by an incompatible Package
        /// </summary>
        public static bool IsIncompatibilityError(string message, string code = null)
        {
            if (!string.IsNullOrEmpty(code) && _codes.Contains(code)) return true;
            if (string.IsNullOrEmpty(message)) return false;

            foreach (var pattern in _messagePatterns)
            {
                var index = message.IndexOf(pattern, StringComparison.Ordinal);
                while (index >= 0)
                {
                    // Ignore quoted text : another error that includes the message (ex. a database error while storing a log message) is not itself an incompatibility
                    if (index == 0 || (message[index - 1] != '\'' && message[index - 1] != '"')) return true;
                    index = message.IndexOf(pattern, index + pattern.Length, StringComparison.Ordinal);
                }
            }

            return false;
        }

        /// <summary>
        /// Record an incompatibility error for a Package
        /// </summary>
        public void Report(string packageId, string source, string message, DateTime timestamp)
        {
            if (string.IsNullOrEmpty(packageId)) return;

            lock (_lock)
            {
                if (!_incidents.TryGetValue(packageId, out var incident))
                {
                    incident = new Incident { PackageId = packageId, FirstSeen = timestamp };
                    _incidents.Add(packageId, incident);
                }

                // The same error may be reported again (ex. read from the Function Metrics on each evaluation) : only count newer errors
                if (timestamp > incident.LastSeen)
                {
                    incident.Source = source;
                    incident.Message = GetFirstLine(message);
                    incident.LastSeen = timestamp;
                    incident.Count++;
                }

                if (timestamp < incident.FirstSeen) incident.FirstSeen = timestamp;
            }
        }

        /// <summary>
        /// Forget the errors for a Package (ex. a new version was installed)
        /// </summary>
        public void Clear(string packageId)
        {
            if (string.IsNullOrEmpty(packageId)) return;
            lock (_lock) _incidents.Remove(packageId);
        }

        /// <summary>
        /// The Packages with errors within the Retention period (most recent first)
        /// </summary>
        public IEnumerable<Incident> GetIncidents(DateTime now)
        {
            lock (_lock)
            {
                // Remove expired
                foreach (var expired in _incidents.Values.Where(o => now - o.LastSeen > Retention).Select(o => o.PackageId).ToList())
                {
                    _incidents.Remove(expired);
                }

                return _incidents.Values
                    .OrderByDescending(o => o.LastSeen)
                    .Select(o => new Incident { PackageId = o.PackageId, Source = o.Source, Message = o.Message, FirstSeen = o.FirstSeen, LastSeen = o.LastSeen, Count = o.Count })
                    .ToList();
            }
        }


        private void LogEntryReceived(object sender, TrakHoundLogItem item)
        {
            try
            {
                if (item.LogLevel != TrakHoundLogLevel.Error && item.LogLevel != TrakHoundLogLevel.Critical) return;
                if (!IsIncompatibilityError(item.Message, item.Code)) return;

                var loggerName = (sender as ITrakHoundLogger)?.Name ?? item.Sender;
                var packageId = GetPackageId(loggerName);
                if (packageId != null)
                {
                    Report(packageId, loggerName, item.Message, DateTime.UtcNow);
                }
            }
            catch { } // Never affect logging
        }

        private static string GetPackageId(string loggerName)
        {
            if (string.IsNullOrEmpty(loggerName)) return null;

            var segments = loggerName.Split('/', StringSplitOptions.RemoveEmptyEntries);
            if (segments.Length >= 2 && _packageCategories.Contains(segments[0])) return segments[1];

            return null;
        }

        private static string GetFirstLine(string message)
        {
            if (string.IsNullOrEmpty(message)) return message;

            var line = message.Split('\n', 2)[0].TrimEnd('\r');

            // Start at the Exception message (ex. API Request errors are logged as "Request Response : {Request ID} : Exception : {Code} : {Message}")
            var index = _messagePatterns.Select(o => line.IndexOf(o, StringComparison.Ordinal)).Where(o => o >= 0).DefaultIfEmpty(0).Min();
            if (index > 0) line = line.Substring(index);

            return line.Length > _maxMessageLength ? line.Substring(0, _maxMessageLength) + "..." : line;
        }
    }
}
