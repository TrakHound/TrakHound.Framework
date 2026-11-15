// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using ModelContextProtocol.Server;
using System.ComponentModel;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Logging;

namespace TrakHound.Instance.Mcp
{
    [McpServerToolType]
    public class TrakHoundMcpLogTools : TrakHoundMcpTools
    {
        private const int MaxTake = 1000;
        private const int MaxTailSeconds = 60;


        public TrakHoundMcpLogTools(ITrakHoundInstance instance) : base(instance) { }


        [McpServerTool(Name = "logs_loggers", Title = "List Loggers", ReadOnly = true)]
        [Description("Lists the Loggers that have written logs (ex. 'functions/my-function', 'api', 'instance'), with when each was first and last seen.")]
        [TrakHoundMcpTool(Resource = TrakHoundSystemSecurity.Logs)]
        public async Task<string> Loggers([Description("A Logger name pattern ('*' matches anything)")] string pattern = "*")
        {
            var loggers = await GetClient().Logs.QueryLoggers(string.IsNullOrWhiteSpace(pattern) ? "*" : pattern);
            return ToJson(loggers?.OrderBy(o => o.Name).Select(o => new
            {
                id = o.Id,
                name = o.Name,
                firstSeen = FormatTimestamp(o.FirstSeen),
                lastSeen = FormatTimestamp(o.LastSeen)
            }));
        }

        [McpServerTool(Name = "logs_query", Title = "Query Logs", ReadOnly = true)]
        [Description("Reads logs from the log history (newest first by default).")]
        [TrakHoundMcpTool(Resource = TrakHoundSystemSecurity.Logs)]
        public async Task<string> Query(
            [Description("A Logger name pattern ('*' matches anything)")] string pattern = "*",
            [Description("Start time: ISO 8601 (ex. 2026-10-20T08:00:00Z) or relative (ex. 15m, 2h, 1d). Default 1h")] string from = null,
            [Description("End time: ISO 8601 or relative. Default now")] string to = null,
            [Description("The lowest level to include: Critical, Error, Warning, Information, Debug or Trace")] string minimumLevel = "Information",
            [Description("The most items to return (up to 1000)")] int take = 200,
            [Description("Return the newest items first")] bool newestFirst = true)
        {
            var items = await GetClient().Logs.Query(
                string.IsNullOrWhiteSpace(pattern) ? "*" : pattern,
                ParseTime(from, DateTime.UtcNow.AddHours(-1)),
                ParseTime(to, DateTime.UtcNow),
                ParseLogLevel(minimumLevel, TrakHoundLogLevel.Information),
                0,
                Clamp(take, 1, MaxTake),
                newestFirst ? TrakHoundSortOrder.Descending : TrakHoundSortOrder.Ascending);

            return ToJson(items?.Select(FormatLogItem));
        }

        [McpServerTool(Name = "logs_tail", Title = "Tail Logs", ReadOnly = true)]
        [Description("Waits for new logs and returns them: after the given number of seconds, or as soon as maxItems have arrived.")]
        [TrakHoundMcpTool(Resource = TrakHoundSystemSecurity.Logs)]
        public async Task<string> Tail(
            [Description("A Logger name pattern ('*' matches anything)")] string pattern = "*",
            [Description("The lowest level to include: Critical, Error, Warning, Information, Debug or Trace")] string minimumLevel = "Information",
            [Description("How long to wait (up to 60 seconds)")] int seconds = 10,
            [Description("Return as soon as this many items have arrived")] int maxItems = 100,
            CancellationToken cancellationToken = default)
        {
            var level = ParseLogLevel(minimumLevel, TrakHoundLogLevel.Information);
            maxItems = Clamp(maxItems, 1, MaxTake);

            var items = new List<TrakHoundLogItem>();
            var full = new TaskCompletionSource();

            using var stream = await GetClient().Logs.Subscribe(string.IsNullOrWhiteSpace(pattern) ? "*" : pattern, level);
            if (stream == null) return ToJson(items);

            stream.OnReceived = received =>
            {
                lock (items)
                {
                    if (received != null) items.AddRange(received);
                    if (items.Count >= maxItems) full.TrySetResult();
                }
            };
            stream.Start();

            try
            {
                await Task.WhenAny(full.Task, Task.Delay(TimeSpan.FromSeconds(Clamp(seconds, 1, MaxTailSeconds)), cancellationToken));
            }
            catch (OperationCanceledException) { }

            lock (items)
            {
                return ToJson(items.Take(maxItems).Select(FormatLogItem).ToList());
            }
        }
    }
}
