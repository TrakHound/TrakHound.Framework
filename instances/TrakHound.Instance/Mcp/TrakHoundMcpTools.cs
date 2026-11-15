// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using ModelContextProtocol;
using System.Globalization;
using TrakHound.Clients;
using TrakHound.Logging;
using TrakHound.Instances;
using TrakHound.Services;

namespace TrakHound.Instance.Mcp
{
    /// <summary>
    /// Shared helpers for the MCP Tool classes. Tools return JSON text, and throw McpException for errors the AI client should see
    /// </summary>
    public abstract class TrakHoundMcpTools
    {
        /// <summary>
        /// Text values (ex. a Volume file or a Message) are cut to this length so a single result can't flood the client's context
        /// </summary>
        protected const int MaxTextLength = 100_000;

        protected readonly ITrakHoundInstance Instance;


        protected TrakHoundMcpTools(ITrakHoundInstance instance)
        {
            Instance = instance;
        }


        protected ITrakHoundClient GetClient()
        {
            return Instance.ClientProvider.GetClient() ?? throw new McpException("The Instance is not running");
        }

        protected static string ToJson(object value) => Json.Convert(value, indented: false);

        /// <summary>
        /// Throws (with the Service's message) when a Service operation failed
        /// </summary>
        protected static T Check<T>(TrakHoundServiceResponse<T> response, string operation)
        {
            if (!response.Success) throw new McpException($"{operation} failed ({response.StatusCode}){(string.IsNullOrEmpty(response.Message) ? "" : $" : {response.Message}")}");
            return response.Result;
        }

        protected static void Check(TrakHoundServiceResponse response, string operation)
        {
            if (!response.Success) throw new McpException($"{operation} failed ({response.StatusCode}){(string.IsNullOrEmpty(response.Message) ? "" : $" : {response.Message}")}");
        }

        /// <summary>
        /// A time as ISO 8601 (ex. 2026-10-20T08:00:00Z) or relative to now (ex. 15m, 2h, 1d). Null uses the default
        /// </summary>
        protected static DateTime ParseTime(string value, DateTime defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;

            value = value.Trim();
            if (value.Length > 1 && double.TryParse(value[..^1], NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
            {
                switch (char.ToLowerInvariant(value[^1]))
                {
                    case 's': return DateTime.UtcNow.AddSeconds(-amount);
                    case 'm': return DateTime.UtcNow.AddMinutes(-amount);
                    case 'h': return DateTime.UtcNow.AddHours(-amount);
                    case 'd': return DateTime.UtcNow.AddDays(-amount);
                }
            }

            if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out var timestamp)) return timestamp;

            throw new McpException($"'{value}' is not a valid time (use ISO 8601, ex. 2026-10-20T08:00:00Z, or a relative time, ex. 15m, 2h, 1d)");
        }

        protected static string Truncate(string value, int maxLength = MaxTextLength)
        {
            if (value == null || value.Length <= maxLength) return value;
            return value[..maxLength] + $"... ({value.Length - maxLength:N0} more characters)";
        }

        protected static string FormatTimestamp(long timestamp) => timestamp > 0 ? timestamp.ToDateTime().ToString("o") : null;

        protected static object FormatLogItem(TrakHoundLogItem item) => new
        {
            timestamp = FormatTimestamp(item.Timestamp),
            level = item.LogLevel.ToString(),
            sender = item.Sender,
            code = item.Code,
            message = item.Message
        };

        protected static TrakHoundLogLevel ParseLogLevel(string value, TrakHoundLogLevel defaultValue)
        {
            if (string.IsNullOrWhiteSpace(value)) return defaultValue;
            if (Enum.TryParse<TrakHoundLogLevel>(value, true, out var level)) return level;
            throw new McpException($"'{value}' is not a Log Level (Critical, Error, Warning, Information, Debug or Trace)");
        }

        protected static int Clamp(int value, int min, int max) => Math.Min(max, Math.Max(min, value));
    }
}
