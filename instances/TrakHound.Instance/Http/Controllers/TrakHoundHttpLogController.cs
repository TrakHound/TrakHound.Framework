// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Controllers.Http;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Instance.Security;

namespace TrakHound.Http
{
    [ApiController]
    [Route(TrakHoundHttpConstants.LogsPrefix)]
    [TrakHoundSystemResource(TrakHoundSystemSecurity.Logs)]
    public class TrakHoundHttpLogController : ControllerBase
    {
        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundHttpWebSocketManager _webSocketManager;


        public TrakHoundHttpLogController(ITrakHoundInstance instance, TrakHoundHttpWebSocketManager webSocketManager)
        {
            _instance = instance;
            _webSocketManager = webSocketManager;
        }


        [HttpGet("list")]
        [RouteDescription("Lists the IDs of the Loggers that match a pattern.")]
        [RouteParameterDescription("pattern", "The Logger ID pattern to match (* matches every Logger)")]
        [RouteResponse(200, typeof(IEnumerable<string>))]
        [RouteResponse(404)]
        public IActionResult List([FromQuery] string pattern = "*")
        {
            var loggerIds = _instance.LogProvider.QueryLoggerIds(pattern);
            if (loggerIds != null)
            {
                return Ok(loggerIds);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet("list/directories")]
        [RouteDescription("Lists the Log directories that match a pattern.")]
        [RouteParameterDescription("pattern", "The directory pattern to match (* matches every directory)")]
        [RouteResponse(200, typeof(IEnumerable<string>))]
        [RouteResponse(404)]
        public IActionResult ListDirectories([FromQuery] string pattern = "*")
        {
            var directories = _instance.LogProvider.QueryDirectories(pattern);
            if (directories != null)
            {
                return Ok(directories);
            }
            else
            {
                return NotFound();
            }
        }

        [HttpGet]
        [RouteDescription("Lists the Log entries for the Loggers that match a pattern, between two times.")]
        [RouteParameterDescription("pattern", "The Logger ID pattern to match")]
        [RouteParameterDescription("from", "The start of the time range")]
        [RouteParameterDescription("to", "The end of the time range")]
        [RouteParameterDescription("minimumLogLevel", "The lowest Log Level to include (Trace when empty)")]
        [RouteParameterDescription("skip", "The number of entries to skip")]
        [RouteParameterDescription("take", "The most entries to return")]
        [RouteParameterDescription("sortOrder", "1 for oldest first, -1 for newest first")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundLogItem>))]
        [RouteResponse(404)]
        public IActionResult Query(
            [FromQuery] string pattern,
            [FromQuery] string from,
            [FromQuery] string to,
            [FromQuery] string minimumLogLevel,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 1000,
            [FromQuery] int sortOrder = (int)TrakHoundSortOrder.Ascending
            )
        {
            var fromTimestamp = from.ToDateTime();
            var toTimestamp = to.ToDateTime();
            var level = !string.IsNullOrEmpty(minimumLogLevel) ? minimumLogLevel.ConvertEnum<TrakHoundLogLevel>() : TrakHoundLogLevel.Trace;

            var logs = _instance.LogProvider.QueryLogsByMinimumLevel(pattern, level, fromTimestamp, toTimestamp, skip, take, (TrakHoundSortOrder)sortOrder);
            if (logs != null)
            {
                return Ok(logs);
            }
            else
            {
                return NotFound();
            }
        }

        /// <summary>
        /// Query Log History with filtering and paging. Returns { items, hasMore, nextCursor }
        /// </summary>
        [HttpGet("query")]
        [RouteDescription("Queries Log History with filters and paging, and returns a page of entries with a cursor for the next page.")]
        [RouteParameterDescription("patterns", "The Logger Name patterns to include (every Logger when empty)")]
        [RouteParameterDescription("from", "The start of the time range (inclusive)")]
        [RouteParameterDescription("to", "The end of the time range (inclusive)")]
        [RouteParameterDescription("minimumLogLevel", "The lowest Log Level to include when levels is not set (Trace when empty)")]
        [RouteParameterDescription("levels", "A comma separated list of the exact Log Levels to include (overrides minimumLogLevel)")]
        [RouteParameterDescription("search", "Case-insensitive text that the Message must contain")]
        [RouteParameterDescription("code", "The Code that entries must have")]
        [RouteParameterDescription("sender", "The Sender (Source) that entries must have")]
        [RouteParameterDescription("skip", "The number of entries to skip (after the cursor, if set)")]
        [RouteParameterDescription("take", "The most entries to return")]
        [RouteParameterDescription("sortOrder", "1 for oldest first, -1 for newest first")]
        [RouteParameterDescription("cursor", "The nextCursor from a previous result, to continue from")]
        [RouteResponse(200, typeof(TrakHoundLogQueryResultJson))]
        [RouteResponse(404)]
        public IActionResult QueryPaged(
            [FromQuery(Name = "pattern")] string[] patterns,
            [FromQuery] string from,
            [FromQuery] string to,
            [FromQuery] string minimumLogLevel,
            [FromQuery] string levels = null,
            [FromQuery] string search = null,
            [FromQuery] string code = null,
            [FromQuery] string sender = null,
            [FromQuery] int skip = 0,
            [FromQuery] int take = 1000,
            [FromQuery] int sortOrder = (int)TrakHoundSortOrder.Ascending,
            [FromQuery] string cursor = null
            )
        {
            var query = CreateQuery(patterns, from, to, minimumLogLevel, levels, search, code, sender);
            query.Skip = skip;
            query.Take = take;
            query.SortOrder = (TrakHoundSortOrder)sortOrder;
            query.Cursor = cursor;

            var result = _instance.LogProvider.Query(query);
            if (result != null)
            {
                return Ok(new TrakHoundLogQueryResultJson(result));
            }
            else
            {
                return NotFound();
            }
        }

        /// <summary>
        /// Count Log History entries by Logger and Level
        /// </summary>
        [HttpGet("counts")]
        [RouteDescription("Counts the Log History entries for each Logger and Level.")]
        [RouteParameterDescription("patterns", "The Logger Name patterns to include (every Logger when empty)")]
        [RouteParameterDescription("from", "The start of the time range (inclusive)")]
        [RouteParameterDescription("to", "The end of the time range (inclusive)")]
        [RouteParameterDescription("minimumLogLevel", "The lowest Log Level to include when levels is not set (Trace when empty)")]
        [RouteParameterDescription("levels", "A comma separated list of the exact Log Levels to include (overrides minimumLogLevel)")]
        [RouteParameterDescription("search", "Case-insensitive text that the Message must contain")]
        [RouteParameterDescription("code", "The Code that entries must have")]
        [RouteParameterDescription("sender", "The Sender (Source) that entries must have")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundLoggerLogCountJson>))]
        [RouteResponse(404)]
        public IActionResult QueryCounts(
            [FromQuery(Name = "pattern")] string[] patterns,
            [FromQuery] string from,
            [FromQuery] string to,
            [FromQuery] string minimumLogLevel,
            [FromQuery] string levels = null,
            [FromQuery] string search = null,
            [FromQuery] string code = null,
            [FromQuery] string sender = null
            )
        {
            var query = CreateQuery(patterns, from, to, minimumLogLevel, levels, search, code, sender);

            var counts = _instance.LogProvider.QueryCounts(query);
            if (counts != null)
            {
                return Ok(counts.Select(o => new TrakHoundLoggerLogCountJson(o)));
            }
            else
            {
                return NotFound();
            }
        }

        /// <summary>
        /// List the Loggers known to Log History (including Loggers from previous runs)
        /// </summary>
        [HttpGet("loggers")]
        [RouteDescription("Lists the Loggers known to Log History, including Loggers from previous runs.")]
        [RouteParameterDescription("pattern", "The Logger Name pattern to match (* matches every Logger)")]
        [RouteResponse(200, typeof(IEnumerable<TrakHoundLoggerInformation>))]
        [RouteResponse(404)]
        public IActionResult QueryLoggers([FromQuery] string pattern = "*")
        {
            var loggers = _instance.LogProvider.QueryLoggers(pattern);
            if (loggers != null)
            {
                return Ok(loggers);
            }
            else
            {
                return NotFound();
            }
        }

        private static TrakHoundLogQuery CreateQuery(string[] patterns, string from, string to, string minimumLogLevel, string levels, string search, string code, string sender)
        {
            var query = new TrakHoundLogQuery();
            query.Patterns = patterns?.Where(o => !string.IsNullOrEmpty(o)).ToArray();
            query.From = !string.IsNullOrEmpty(from) ? from.ToDateTime() : default;
            query.To = !string.IsNullOrEmpty(to) ? to.ToDateTime() : default;
            query.MinimumLevel = !string.IsNullOrEmpty(minimumLogLevel) ? minimumLogLevel.ConvertEnum<TrakHoundLogLevel>() : TrakHoundLogLevel.Trace;
            query.Search = search;
            query.Code = code;
            query.Sender = sender;

            if (!string.IsNullOrEmpty(levels))
            {
                query.Levels = levels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Select(o => Enum.TryParse<TrakHoundLogLevel>(o, true, out var level) ? (TrakHoundLogLevel?)level : null)
                    .Where(o => o.HasValue)
                    .Select(o => o.Value)
                    .ToArray();
            }

            return query;
        }

        [Route("subscribe")]
        [RouteDescription("Streams new Log entries for the Loggers that match a pattern over a WebSocket.")]
        [RouteParameterDescription("pattern", "The Logger ID pattern to match")]
        [RouteParameterDescription("minimumLogLevel", "The lowest Log Level to include")]
        [RouteParameterDescription("routerId", "The Service Router to use (the default Router when empty)")]
        public async Task SubscribeLog(
            [FromQuery] string pattern,
            [FromQuery] TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace,
            [FromQuery] string routerId = null
            )
        {
            if (!string.IsNullOrEmpty(pattern))
            {
                var client = _instance.ClientProvider.GetClient(routerId).WithModuleRequestSource(HttpContext);
                if (client != null)
                {
                    Func<Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>>> getConsumer = async () =>
                    {
                        var clientConsumer = await client.Logs.Subscribe(pattern, minimumLogLevel);
                        if (clientConsumer != null)
                        {
                            return clientConsumer;
                        }
                        else
                        {
                            return null;
                        }
                    };

                    var formatResponse = (IEnumerable<TrakHoundLogItem> items) =>
                    {
                        if (!items.IsNullOrEmpty())
                        {
                            var jsonItems = new List<TrakHoundLogItemJson>();
                            foreach (var item in items)
                            {
                                jsonItems.Add(new TrakHoundLogItemJson(item));
                            }
                            return jsonItems.ToJsonBytes(true);
                        }

                        return null;
                    };

                    await _webSocketManager.Create<IEnumerable<TrakHoundLogItem>>(HttpContext, getConsumer, null, formatResponse);
                }
            }
        }
    }
}
