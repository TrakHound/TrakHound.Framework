// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Logging;

namespace TrakHound.Clients
{
    /// <summary>
    /// HTTP implementation of ITrakHoundLogsClient that uses the Logs Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpLogsClient : TrakHoundHttpClientBase, ITrakHoundLogsClient
    {
        private const string _apiRoute = TrakHoundHttpConstants.LogsPrefix;


        /// <summary>
        /// Creates a new Logs Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpLogsClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<IEnumerable<string>> List(string pattern)
        {
            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute, "list");
            url = Url.AddQueryParameter(url, "pattern", pattern);

            return await Requests.Get<IEnumerable<string>>(url);
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<string>> ListDirectories(string pattern)
        {
            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute, "list/directories");
            url = Url.AddQueryParameter(url, "pattern", pattern);

            return await Requests.Get<IEnumerable<string>>(url);
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundLogItem>> Query(string pattern, DateTime from, DateTime to, TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute);
            url = Url.AddQueryParameter(url, "pattern", pattern);
            url = Url.AddQueryParameter(url, "minimumLogLevel", minimumLogLevel);
            url = Url.AddQueryParameter(url, "from", from.ToUnixTime());
            url = Url.AddQueryParameter(url, "to", to.ToUnixTime());
            url = Url.AddQueryParameter(url, "skip", skip);
            url = Url.AddQueryParameter(url, "take", take);
            url = Url.AddQueryParameter(url, "sortOrder", sortOrder);

            var jsonItems = await Requests.Get<IEnumerable<TrakHoundLogItemJson>>(url);
            if (!jsonItems.IsNullOrEmpty())
            {
                var items = new List<TrakHoundLogItem>(jsonItems.Count());
                foreach (var jsonItem in jsonItems)
                {
                    var item = new TrakHoundLogItem();
                    item.Sender = jsonItem.Sender;
                    item.LogLevel = jsonItem.LogLevel;
                    item.Message = jsonItem.Message;
                    item.Code = jsonItem.Code;
                    item.Timestamp = jsonItem.Timestamp;
                    items.Add(item);
                }
                return items;
            }
            
            return null;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundLogQueryResult> Query(TrakHoundLogQuery query)
        {
            if (query == null) return null;

            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute, "query");
            url = AddQueryParameters(url, query, true);

            var result = await Requests.Get<TrakHoundLogQueryResultJson>(url);
            return result?.ToResult();
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundLoggerLogCount>> QueryCounts(TrakHoundLogQuery query)
        {
            if (query == null) return null;

            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute, "counts");
            url = AddQueryParameters(url, query, false);

            var counts = await Requests.Get<IEnumerable<TrakHoundLoggerLogCountJson>>(url);
            return counts?.Select(o => o.ToCount()).ToList();
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundLoggerInformation>> QueryLoggers(string pattern)
        {
            var url = Url.Combine(BaseClient.ClientConfiguration.GetHttpBaseUrl(), _apiRoute, "loggers");
            url = Url.AddQueryParameter(url, "pattern", pattern);

            return await Requests.Get<IEnumerable<TrakHoundLoggerInformation>>(url);
        }

        private static string AddQueryParameters(string url, TrakHoundLogQuery query, bool includePaging)
        {
            var parameters = new List<string>();

            if (query.Patterns != null)
            {
                foreach (var pattern in query.Patterns)
                {
                    if (!string.IsNullOrEmpty(pattern)) parameters.Add($"pattern={Uri.EscapeDataString(pattern)}");
                }
            }

            parameters.Add($"from={query.From.ToUnixTime()}");
            parameters.Add($"to={query.To.ToUnixTime()}");
            parameters.Add($"minimumLogLevel={query.MinimumLevel}");
            if (query.Levels != null && query.Levels.Any()) parameters.Add($"levels={string.Join(',', query.Levels)}");
            if (!string.IsNullOrEmpty(query.Search)) parameters.Add($"search={Uri.EscapeDataString(query.Search)}");
            if (!string.IsNullOrEmpty(query.Code)) parameters.Add($"code={Uri.EscapeDataString(query.Code)}");
            if (!string.IsNullOrEmpty(query.Sender)) parameters.Add($"sender={Uri.EscapeDataString(query.Sender)}");

            if (includePaging)
            {
                parameters.Add($"skip={query.Skip}");
                parameters.Add($"take={query.Take}");
                parameters.Add($"sortOrder={(int)query.SortOrder}");
                if (!string.IsNullOrEmpty(query.Cursor)) parameters.Add($"cursor={Uri.EscapeDataString(query.Cursor)}");
            }

            return url + (url.Contains('?') ? "&" : "?") + string.Join('&', parameters);
        }

        /// <inheritdoc/>
        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> Subscribe(string pattern, TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace)
        {
            // Relative: the WebSocket Stream adds the ws:// base URL
            var url = Url.Combine(_apiRoute, "subscribe");
            url = Url.AddQueryParameter(url, "pattern", pattern);
            url = Url.AddQueryParameter(url, "minimumLogLevel", minimumLogLevel.ToString());

            var websocketStream = new TrakHoundWebSocketsStream<IEnumerable<TrakHoundLogItem>>(BaseClient.ClientConfiguration, url);
            websocketStream.OnInputReceived = (o) =>
            {
                var json = StringFunctions.GetUtf8String(o);
                var jsonItems = Json.Convert<IEnumerable<TrakHoundLogItemJson>>(json);
                if (jsonItems != null)
                {
                    var items = new List<TrakHoundLogItem>(jsonItems.Count());
                    foreach (var jsonItem in jsonItems)
                    {
                        var item = new TrakHoundLogItem();
                        item.Sender = jsonItem.Sender;
                        item.LogLevel = jsonItem.LogLevel;
                        item.Message = jsonItem.Message;
                        item.Code = jsonItem.Code;
                        item.Timestamp = jsonItem.Timestamp;
                        items.Add(item);
                    }
                    return items;
                }

                return default;
            };
            return websocketStream;
        }
    }
}
