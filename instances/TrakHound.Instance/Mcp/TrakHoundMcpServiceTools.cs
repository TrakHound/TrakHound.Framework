// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using TrakHound.Instances;
using TrakHound.Services;

namespace TrakHound.Instance.Mcp
{
    /// <summary>
    /// Key Value, Table and Message Service Tools. Service operations are authorized by the Service (see TrakHoundServiceSecurity),
    /// so these Tools don't declare a System Resource
    /// </summary>
    [McpServerToolType]
    public class TrakHoundMcpServiceTools : TrakHoundMcpTools
    {
        private const int MaxRows = 1000;
        private const int MaxWaitSeconds = 60;
        private const string RouterDescription = "The Service Router (leave empty for the default)";


        public TrakHoundMcpServiceTools(ITrakHoundInstance instance) : base(instance) { }


        // Key Values

        [McpServerTool(Name = "keyvalues_collections", Title = "List Key Value Collections", ReadOnly = true)]
        [Description("Lists the Key Value Collections.")]
        [TrakHoundMcpTool]
        public async Task<string> KeyValueCollections([Description(RouterDescription)] string routerId = null, CancellationToken cancellationToken = default)
        {
            var response = await GetClient().Services.KeyValues.ListCollections(Router(routerId), cancellationToken);
            return ToJson(Check(response, "List Collections"));
        }

        [McpServerTool(Name = "keyvalues_keys", Title = "List Keys", ReadOnly = true)]
        [Description("Lists the Keys in a Key Value Collection.")]
        [TrakHoundMcpTool]
        public async Task<string> KeyValueKeys(
            [Description("The Collection (from keyvalues_collections)")] string collection,
            [Description("A Key pattern ('*' matches anything)")] string pattern = "*",
            [Description("Keys to skip (for paging)")] int skip = 0,
            [Description("The most Keys to return (up to 1000)")] int take = 100,
            [Description(RouterDescription)] string routerId = null,
            CancellationToken cancellationToken = default)
        {
            var response = await GetClient().Services.KeyValues.ListKeys(collection, string.IsNullOrWhiteSpace(pattern) ? "*" : pattern, Math.Max(0, skip), Clamp(take, 1, MaxRows), Router(routerId), cancellationToken);
            return ToJson(Check(response, "List Keys"));
        }

        [McpServerTool(Name = "keyvalues_get", Title = "Get Key Values", ReadOnly = true)]
        [Description("Reads the values of one or more Keys in a Key Value Collection.")]
        [TrakHoundMcpTool]
        public async Task<string> KeyValueGet(
            [Description("The Collection")] string collection,
            [Description("The Keys to read")] string[] keys,
            [Description(RouterDescription)] string routerId = null,
            CancellationToken cancellationToken = default)
        {
            if (keys == null || keys.Length == 0) throw new McpException("At least one Key is required");

            var response = await GetClient().Services.KeyValues.Read(collection, keys, Router(routerId), cancellationToken);
            var values = Check(response, "Read")?.Values;
            return ToJson(values?.ToDictionary(o => o.Key, o => Truncate(o.Value)));
        }

        [McpServerTool(Name = "keyvalues_set", Title = "Set Key Value", Destructive = true, Idempotent = true)]
        [Description("Sets the value of a Key in a Key Value Collection (Apps and Workers may react to the change).")]
        [TrakHoundMcpTool(Write = true)]
        public async Task<string> KeyValueSet(
            [Description("The Collection")] string collection,
            [Description("The Key")] string key,
            [Description("The value")] string value,
            [Description(RouterDescription)] string routerId = null,
            CancellationToken cancellationToken = default)
        {
            Check(await GetClient().Services.KeyValues.Publish(collection, key, value, Router(routerId), cancellationToken), "Set");
            return ToJson(new { success = true });
        }


        // Tables

        [McpServerTool(Name = "tables_list", Title = "List Tables", ReadOnly = true)]
        [Description("Lists the Tables (schema, name and type) of the Table Service.")]
        [TrakHoundMcpTool]
        public async Task<string> TablesList([Description(RouterDescription)] string routerId = null, CancellationToken cancellationToken = default)
        {
            var tables = Check(await GetClient().Services.Tables.List(Router(routerId), cancellationToken), "List Tables");
            return ToJson(tables?.Select(o => new { schema = o.Schema, name = o.Name, type = o.Type.ToString() }));
        }

        [McpServerTool(Name = "tables_dialects", Title = "Get SQL Dialects", ReadOnly = true)]
        [Description("Lists the SQL Dialects the Table Service runs, in order of preference (ex. postgres, ansi). The first one is the database's own SQL, which tables_query runs.")]
        [TrakHoundMcpTool]
        public async Task<string> TablesDialects([Description(RouterDescription)] string routerId = null, CancellationToken cancellationToken = default)
        {
            return ToJson(Check(await GetClient().Services.Tables.GetDialects(Router(routerId), cancellationToken), "Get Dialects"));
        }

        [McpServerTool(Name = "tables_columns", Title = "Get Table Columns", ReadOnly = true)]
        [Description("Lists the Columns of a Table (name, data type, length and whether it allows null).")]
        [TrakHoundMcpTool]
        public async Task<string> TablesColumns(
            [Description("The Table name (as listed by tables_list, ex. schema.table)")] string table,
            [Description(RouterDescription)] string routerId = null,
            CancellationToken cancellationToken = default)
        {
            var columns = Check(await GetClient().Services.Tables.GetColumns(table, Router(routerId), cancellationToken), "Get Columns");
            return ToJson(columns?.Select(o => new { name = o.Name, dataType = o.DataType.ToString(), dataLength = o.DataLength, allowNull = o.AllowNull }));
        }

        [McpServerTool(Name = "tables_query", Title = "Run SQL", Destructive = true)]
        [Description("Runs SQL on the Table Service and returns each result's columns and rows. Any SQL is run as given, in the database's own SQL, including statements that change or delete data, so prefer SELECT and check with the user before changing anything. Use tables_dialects to find which SQL to write, and tables_list and tables_columns to find the schema first.")]
        [TrakHoundMcpTool(Write = true)]
        public async Task<string> TablesQuery(
            [Description("The SQL to run")] string sql,
            [Description("The most rows to return per result (up to 1000)")] int maxRows = 200,
            [Description("Timeout in seconds (empty for the Service default)")] int? timeoutSeconds = null,
            [Description(RouterDescription)] string routerId = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(sql)) throw new McpException("SQL is required");

            maxRows = Clamp(maxRows, 1, MaxRows);
            var results = Check(await GetClient().Services.Tables.ExecuteQuery(sql, timeoutSeconds, null, Router(routerId), cancellationToken), "Query");

            return ToJson(results?.Select(o => new
            {
                message = o.Message,
                columns = o.Columns?.Select(c => c.Name),
                rowCount = o.Rows?.Length ?? 0,
                truncated = (o.Rows?.Length ?? 0) > maxRows,
                rows = o.Rows?.Take(maxRows)
            }));
        }


        // Messages

        [McpServerTool(Name = "messages_publish", Title = "Publish Message", Destructive = true, OpenWorld = true)]
        [Description("Publishes a Message to a Topic (subscribers, ex. Workers or devices, may act on it).")]
        [TrakHoundMcpTool(Write = true)]
        public async Task<string> MessagesPublish(
            [Description("The Topic")] string topic,
            [Description("The Message content (text or JSON)")] string content,
            [Description("Retain the Message for new subscribers")] bool retain = false,
            [Description(RouterDescription)] string routerId = null,
            CancellationToken cancellationToken = default)
        {
            Check(await GetClient().Services.Messages.Publish(topic, content ?? "", retain, 0, Router(routerId), cancellationToken), "Publish");
            return ToJson(new { success = true });
        }

        [McpServerTool(Name = "messages_wait", Title = "Wait for Messages", ReadOnly = true)]
        [Description("Subscribes to a Topic and returns the Messages that arrive: after the given number of seconds, or as soon as maxMessages have arrived. Retained Messages arrive right away.")]
        [TrakHoundMcpTool]
        public async Task<string> MessagesWait(
            [Description("The Topic (wildcards depend on the Message Service, ex. MQTT '+' and '#')")] string topic,
            [Description("How long to wait (up to 60 seconds)")] int seconds = 10,
            [Description("Return as soon as this many Messages have arrived")] int maxMessages = 20,
            [Description(RouterDescription)] string routerId = null,
            CancellationToken cancellationToken = default)
        {
            maxMessages = Clamp(maxMessages, 1, MaxRows);
            var messages = new List<object>();
            var full = new TaskCompletionSource();

            var stream = Check(await GetClient().Services.Messages.Subscribe(topic, routerId: Router(routerId), cancellationToken: cancellationToken), "Subscribe");
            if (stream == null) return ToJson(messages);

            using (stream)
            {
                stream.OnReceived = message =>
                {
                    var item = new
                    {
                        topic = message.Topic,
                        timestamp = FormatTimestamp(message.Timestamp),
                        retain = message.Retain,
                        content = ReadContent(message.Content)
                    };

                    lock (messages)
                    {
                        if (messages.Count < maxMessages) messages.Add(item);
                        if (messages.Count >= maxMessages) full.TrySetResult();
                    }
                };
                stream.Start();

                await Task.WhenAny(full.Task, Task.Delay(TimeSpan.FromSeconds(Clamp(seconds, 1, MaxWaitSeconds)), cancellationToken));
            }

            lock (messages)
            {
                return ToJson(messages.ToList());
            }
        }


        private static string Router(string routerId) => string.IsNullOrWhiteSpace(routerId) ? null : routerId;

        private static string ReadContent(Stream content)
        {
            if (content == null) return null;

            try
            {
                if (content.CanSeek) content.Seek(0, SeekOrigin.Begin);
                using var reader = new StreamReader(content, leaveOpen: true);
                return Truncate(reader.ReadToEnd(), 20_000);
            }
            catch
            {
                return null;
            }
        }
    }
}
