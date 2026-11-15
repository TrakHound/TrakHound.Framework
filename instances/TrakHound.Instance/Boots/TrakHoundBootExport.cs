// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Globalization;
using System.Text;
using System.Text.Json;
using TrakHound.Instances;

namespace TrakHound.Instance.Boots
{
    /// <summary>
    /// Writes Boots to a file to download : JSON (everything recorded) or CSV (one summary row per Boot)
    /// </summary>
    public static class TrakHoundBootExport
    {
        public static readonly string[] CsvColumns = new[]
        {
            "id", "status", "started", "ready", "ended", "bootDurationMs", "uptimeMs", "version", "machineName", "processId", "user",
            "operatingSystem", "runtime", "warningCount", "errorCount", "runtimeStarts", "failedRuntimeStarts", "reason", "exitCode"
        };

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { WriteIndented = true };


        public static string ToJson(IEnumerable<TrakHoundInstanceBoot> boots) => JsonSerializer.Serialize(boots ?? Enumerable.Empty<TrakHoundInstanceBoot>(), _jsonOptions);

        public static string ToJson(TrakHoundInstanceBoot boot) => JsonSerializer.Serialize(boot, _jsonOptions);

        /// <summary>
        /// One row per Boot, without its Steps, Runtime Starts, Log entries or Exception Details (those are only in the JSON)
        /// </summary>
        public static string ToCsv(IEnumerable<TrakHoundInstanceBoot> boots, DateTime? now = null)
        {
            var utcNow = now ?? DateTime.UtcNow;
            var csv = new StringBuilder();
            csv.Append(string.Join(",", CsvColumns)).Append("\r\n");

            foreach (var boot in boots ?? Enumerable.Empty<TrakHoundInstanceBoot>())
            {
                if (boot == null) continue;

                var values = new string[]
                {
                    Text(boot.Id),
                    boot.Status.ToString(),
                    Timestamp(boot.Started),
                    Timestamp(boot.Ready),
                    Timestamp(boot.Ended),
                    Number(boot.BootDuration?.TotalMilliseconds),
                    Number(GetUptime(boot, utcNow)?.TotalMilliseconds),
                    Text(boot.Version),
                    Text(boot.MachineName),
                    boot.ProcessId.ToString(CultureInfo.InvariantCulture),
                    Text(boot.User),
                    Text(boot.OperatingSystem),
                    Text(boot.Runtime),
                    (boot.Log?.WarningCount ?? 0).ToString(CultureInfo.InvariantCulture),
                    (boot.Log?.ErrorCount ?? 0).ToString(CultureInfo.InvariantCulture),
                    (boot.Starts?.Count ?? 0).ToString(CultureInfo.InvariantCulture),
                    (boot.Starts?.Count(o => !o.Success) ?? 0).ToString(CultureInfo.InvariantCulture),
                    Text(boot.Reason),
                    boot.ExitCode?.ToString(CultureInfo.InvariantCulture)
                };

                csv.Append(string.Join(",", values)).Append("\r\n");
            }

            return csv.ToString();
        }

        /// <summary>
        /// Until now for a Boot that is still running, until it ended otherwise (the last time it was known to be running for an Unclean Boot)
        /// </summary>
        public static TimeSpan? GetUptime(TrakHoundInstanceBoot boot, DateTime utcNow)
        {
            if (boot == null) return null;

            var running = boot.Status == TrakHoundInstanceBootStatus.Running || boot.Status == TrakHoundInstanceBootStatus.Starting;
            var end = boot.Ended ?? (running ? utcNow : (DateTime?)null);
            return end != null ? end.Value - boot.Started : null;
        }


        // UTC in ISO 8601, so the file reads the same in every time zone
        private static string Timestamp(DateTime? timestamp)
        {
            if (timestamp == null || timestamp.Value == DateTime.MinValue) return null;

            var utc = timestamp.Value.Kind == DateTimeKind.Local ? timestamp.Value.ToUniversalTime() : DateTime.SpecifyKind(timestamp.Value, DateTimeKind.Utc);
            return utc.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
        }

        private static string Number(double? value) => value?.ToString("0", CultureInfo.InvariantCulture);

        private static string Text(string value)
        {
            if (string.IsNullOrEmpty(value)) return value;

            // A leading = + - @ would be read as a Formula by a spreadsheet (ex. a Reason from an Exception message)
            if ("=+-@".IndexOf(value[0]) >= 0) value = "'" + value;

            if (value.IndexOfAny(new[] { ',', '"', '\r', '\n' }) >= 0)
            {
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            }

            return value;
        }
    }
}
