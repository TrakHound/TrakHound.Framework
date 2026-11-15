// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics.Eventing.Reader;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;

namespace TrakHound.Instance
{
    /// <summary>
    /// Reads the Windows Event Log entries that explain why a previous run of the Instance stopped.
    /// Used for terminations the Instance can't log itself (ex. Stack Overflow, Access Violation, or IIS terminating the Worker Process)
    /// </summary>
    internal static class TrakHoundWindowsEventLog
    {
        public class Entry
        {
            public DateTime Timestamp { get; init; }
            public string Log { get; init; }
            public string Source { get; init; }
            public int EventId { get; init; }
            public string Level { get; init; }
            public string Message { get; init; }

            /// <summary>
            /// The lines of the Message that describe the cause (ex. the Exception or Faulting Module)
            /// </summary>
            public string Summary { get; init; }

            // Lower is a more specific cause (used to pick the Reason)
            internal int Priority { get; init; }
        }

        private class Source
        {
            public string Log { get; init; }
            public string Provider { get; init; }
            public int[] EventIds { get; init; } // Null for every Event ID
            public int Priority { get; init; }
        }


        private const int _maxEntries = 50;

        // The process may have crashed up to one Heartbeat after the Run Marker was last touched
        private static readonly TimeSpan _crashWindow = TimeSpan.FromSeconds(45);

        private static readonly Source[] _sources = new[]
        {
            // Fatal Execution Engine Error (1023), Native Crash (1025), Unhandled Exception or Stack Overflow (1026).
            // Other Applications' ASP.NET Core Event Log entries also use this source (1000), so only these are read
            new Source { Log = "Application", Provider = ".NET Runtime", EventIds = new[] { 1023, 1025, 1026 }, Priority = 1 },
            new Source { Log = "Application", Provider = "Application Error", EventIds = new[] { 1000 }, Priority = 2 },  // Faulting Module and Exception Code (ex. Access Violation)
            new Source { Log = "Application", Provider = "IIS AspNetCore Module V2", Priority = 3 },  // ASP.NET Core Module (IIS)
            new Source { Log = "System", Provider = "WAS", Priority = 4 }                             // IIS Worker Process terminated, unresponsive or Rapid-Fail Protection
        };


        /// <summary>
        /// Read the Error and Warning entries (written between from and to) that refer to the previous process
        /// </summary>
        /// <param name="processId">The ID of the previous process</param>
        /// <param name="lastAlive">When the previous process was last known to be running</param>
        /// <param name="errors">Logs that couldn't be read (ex. Access Denied)</param>
        [SupportedOSPlatform("windows")]
        public static List<Entry> Read(int processId, DateTime lastAlive, DateTime from, DateTime to, out List<string> errors)
        {
            errors = new List<string>();
            var entries = new List<Entry>();

            var matcher = new Matcher(processId, lastAlive);

            foreach (var log in _sources.GroupBy(o => o.Log))
            {
                try
                {
                    var providers = string.Join(" or ", log.Select(GetProviderQuery));
                    var query = $"*[System[({providers}) and (Level=1 or Level=2 or Level=3) and TimeCreated[@SystemTime>='{ToSystemTime(from)}' and @SystemTime<='{ToSystemTime(to)}']]]";

                    using var reader = new EventLogReader(new EventLogQuery(log.Key, PathType.LogName, query));

                    EventRecord record;
                    while (entries.Count < _maxEntries && (record = reader.ReadEvent()) != null)
                    {
                        using (record)
                        {
                            var source = log.FirstOrDefault(o => o.Provider == record.ProviderName);
                            var message = GetMessage(record);

                            if (source != null && matcher.IsMatch(record, message))
                            {
                                entries.Add(new Entry
                                {
                                    Timestamp = record.TimeCreated ?? DateTime.MinValue,
                                    Log = log.Key,
                                    Source = record.ProviderName,
                                    EventId = record.Id,
                                    Level = GetLevel(record.Level),
                                    Message = message,
                                    Summary = GetSummary(record.ProviderName, message),
                                    Priority = source.Priority
                                });
                            }
                        }
                    }
                }
                catch (EventLogNotFoundException) { } // ex. IIS isn't installed
                catch (Exception ex)
                {
                    errors.Add($"{log.Key} Log : {ex.Message}");
                }
            }

            return entries.OrderBy(o => o.Timestamp).ToList();
        }

        /// <summary>
        /// The entry that best explains the termination (null if none)
        /// </summary>
        public static Entry GetCause(IEnumerable<Entry> entries)
        {
            return entries?.OrderBy(o => o.Priority).ThenBy(o => o.Timestamp).FirstOrDefault();
        }


        /// <summary>
        /// Decides if an entry refers to the previous process (other Applications and App Pools write to the same Logs)
        /// </summary>
        [SupportedOSPlatform("windows")]
        private class Matcher
        {
            private readonly Regex _processIdRegex;
            private readonly string _processName;
            private readonly string _baseDirectory;
            private readonly string _appPoolId;
            private readonly DateTime _lastAlive;


            public Matcher(int processId, DateTime lastAlive)
            {
                // Decimal (ex. WAS "The process id was '1234'") or Hex (ex. Application Error "Faulting process id: 0x4d2")
                _processIdRegex = new Regex($@"(?<![0-9A-Za-z]){processId}(?![0-9])|0x0*{processId:x}(?![0-9A-Fa-f])", RegexOptions.IgnoreCase);
                _processName = $"{System.Diagnostics.Process.GetCurrentProcess().ProcessName}.exe";
                _baseDirectory = AppContext.BaseDirectory.TrimEnd('\\', '/');
                _appPoolId = Environment.GetEnvironmentVariable("APP_POOL_ID"); // Set by IIS
                _lastAlive = lastAlive;
            }

            public bool IsMatch(EventRecord record, string message)
            {
                if (string.IsNullOrEmpty(message)) return false;

                if (_processIdRegex.IsMatch(message)) return true;

                // ASP.NET Core Module entries name the Application's physical root
                if (message.Contains(_baseDirectory, StringComparison.OrdinalIgnoreCase)) return true;

                // WAS entries about the App Pool itself (ex. Rapid-Fail Protection) don't include a process ID
                if (record.ProviderName == "WAS" && !string.IsNullOrEmpty(_appPoolId) && message.Contains($"'{_appPoolId}'", StringComparison.OrdinalIgnoreCase)) return true;

                // .NET Runtime entries only name the process (ex. "Application: w3wp.exe"), so they must also be at the time of the crash
                if (record.ProviderName == ".NET Runtime" && record.TimeCreated != null &&
                    record.TimeCreated.Value >= _lastAlive.AddSeconds(-5) && record.TimeCreated.Value <= _lastAlive + _crashWindow &&
                    message.Contains(_processName, StringComparison.OrdinalIgnoreCase)) return true;

                return false;
            }
        }


        [SupportedOSPlatform("windows")]
        private static string GetMessage(EventRecord record)
        {
            string message = null;
            try { message = record.FormatDescription(); } catch { }

            // The Provider's message resources may not be installed (ex. a removed ASP.NET Core Module version)
            if (string.IsNullOrEmpty(message) && record.Properties != null)
            {
                message = string.Join(Environment.NewLine, record.Properties.Select(o => o.Value?.ToString()).Where(o => !string.IsNullOrEmpty(o)));
            }

            return message?.Trim();
        }

        private static string GetSummary(string provider, string message)
        {
            if (string.IsNullOrEmpty(message)) return null;

            var lines = message.Split('\n').Select(o => o.Trim()).Where(o => o.Length > 0).ToList();

            switch (provider)
            {
                case ".NET Runtime":
                    // ex. "Description: The process was terminated due to stack overflow." and "Exception Info: System.Exception: ..."
                    var description = lines.FirstOrDefault(o => o.StartsWith("Description:"));
                    var exceptionIndex = lines.FindIndex(o => o.StartsWith("Exception Info:"));
                    string exception = null;
                    if (exceptionIndex >= 0)
                    {
                        exception = lines[exceptionIndex].Substring("Exception Info:".Length).Trim();
                        if (exception.Length == 0 && exceptionIndex + 1 < lines.Count) exception = lines[exceptionIndex + 1];
                    }
                    var runtimeSummary = string.Join(" ", new[] { description?.Substring("Description:".Length).Trim(), exception }.Where(o => !string.IsNullOrEmpty(o)));
                    if (runtimeSummary.Length > 0) return Truncate(runtimeSummary);
                    break;

                case "Application Error":
                    // ex. "Faulting module name: coreclr.dll, version: ..." and "Exception code: 0xc00000fd"
                    var module = lines.FirstOrDefault(o => o.StartsWith("Faulting module name:"));
                    var code = lines.FirstOrDefault(o => o.StartsWith("Exception code:"));
                    var errorSummary = string.Join(", ", new[] { module?.Split(',')[0], code }.Where(o => !string.IsNullOrEmpty(o)));
                    if (errorSummary.Length > 0) return Truncate(errorSummary);
                    break;
            }

            return Truncate(lines.FirstOrDefault());
        }

        private static string GetLevel(byte? level)
        {
            switch (level)
            {
                case 1: return "Critical";
                case 2: return "Error";
                case 3: return "Warning";
                default: return "Information";
            }
        }

        private static string GetProviderQuery(Source source)
        {
            var provider = $"Provider[@Name='{source.Provider}']";
            return source.EventIds != null ? $"({provider} and ({string.Join(" or ", source.EventIds.Select(o => $"EventID={o}"))}))" : provider;
        }

        private static string ToSystemTime(DateTime timestamp) => timestamp.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        private static string Truncate(string value) => value != null && value.Length > 300 ? value.Substring(0, 300) + ".." : value;
    }
}
