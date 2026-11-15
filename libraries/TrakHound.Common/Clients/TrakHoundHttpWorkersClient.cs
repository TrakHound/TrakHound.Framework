// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Logging;
using TrakHound.Workers;

namespace TrakHound.Clients
{
    /// <summary>
    /// HTTP implementation of ITrakHoundWorkersClient that uses the Workers Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpWorkersClient : TrakHoundHttpClientBase, ITrakHoundWorkersClient
    {
        private const string _apiRoute = "services";


        /// <summary>
        /// Creates a new Workers Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpWorkersClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundWorkerInformation>> GetInformation()
        {
            var route = Url.Combine(_apiRoute, "list");
            return await BaseClient.Api.GetJson<IEnumerable<TrakHoundWorkerInformation>>(route);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundWorkerInformation> GetInformation(string packageId)
        {
            var route = Url.Combine(_apiRoute, "list", "package", packageId);
            return await BaseClient.Api.GetJson<TrakHoundWorkerInformation>(route);
        }

        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundWorkerInformation>> GetInformation(IEnumerable<string> packageIds)
        {
            var route = Url.Combine(_apiRoute, "list", "packages");
            return await BaseClient.Api.PostJson<IEnumerable<TrakHoundWorkerInformation>>(route, packageIds);
        }

        /// <summary>
        /// Not supported by the HTTP Client (always returns null)
        /// </summary>
        public async Task<ITrakHoundStream<TrakHoundWorkerInformation>> Subscribe()
        {
            //return await BaseClient.Api.SubscribeJson<TrakHoundWorkerInformation>(_apiRoute);
            return null;
        }

        /// <summary>
        /// Not supported by the HTTP Client (always returns null)
        /// </summary>
        public async Task<ITrakHoundStream<TrakHoundWorkerInformation>> Subscribe(string serviceId)
        {
            var route = Url.Combine(_apiRoute, serviceId);
            //return await BaseClient.Api.SubscribeJson<TrakHoundWorkerInformation>(route);
            return null;
        }


        /// <summary>
        /// Gets the Status of the specified Worker (Started if the Request succeeds, otherwise Error with the Response content as the Message)
        /// </summary>
        public async Task<TrakHoundWorkerStatus> GetStatus(string serviceId)
        {
            var route = Url.Combine(_apiRoute, serviceId, "status");

            var response = await BaseClient.Api.Get(route);

            var result = new TrakHoundWorkerStatus();
            result.WorkerId = serviceId;
            result.Status = response.Success ? TrakHoundWorkerStatusType.Started : TrakHoundWorkerStatusType.Error;
            result.Message = !response.Success ? response.GetContentUtf8String() : null;
            return result;
        }

        /// <summary>
        /// Not supported by the HTTP Client (always returns null)
        /// </summary>
        public async Task<IEnumerable<TrakHoundWorkerStatus>> GetStatus(IEnumerable<string> serviceIds)
        {
            return null;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundWorkerStatus> StartWorker(string serviceId)
        {
            var route = Url.Combine(_apiRoute, serviceId, "start");

            var response = await BaseClient.Api.Put(route);

            var result = new TrakHoundWorkerStatus();
            result.WorkerId = serviceId;
            result.Status = response.Success ? TrakHoundWorkerStatusType.Started : TrakHoundWorkerStatusType.Error;
            result.Message = !response.Success ? response.GetContentUtf8String() : null;
            return result;
        }

        /// <inheritdoc/>
        public async Task<TrakHoundWorkerStatus> StopWorker(string serviceId)
        {
            var route = Url.Combine(_apiRoute, serviceId, "stop");

            var response = await BaseClient.Api.Put(route);

            var result = new TrakHoundWorkerStatus();
            result.WorkerId = serviceId;
            result.Status = response.Success ? TrakHoundWorkerStatusType.Stopped : TrakHoundWorkerStatusType.Error;
            result.Message = !response.Success ? response.GetContentUtf8String() : null;
            return result;
        }


        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundWorkerCpuSample>> GetCpuHistory(string workerId, DateTime? from = null)
        {
            var route = Url.Combine(_apiRoute, workerId, "cpu");
            if (from.HasValue) route = Url.AddQueryParameter(route, "from", from.Value.ToUnixTime());

            return await BaseClient.Api.GetJson<IEnumerable<TrakHoundWorkerCpuSample>>(route);
        }


        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundWorkerMetrics>> GetMetrics()
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.WorkersPrefix, "metrics");
            return await Requests.Get<IEnumerable<TrakHoundWorkerMetrics>>(url);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundWorkerMetrics> GetMetrics(string workerId)
        {
            if (string.IsNullOrEmpty(workerId)) return null;

            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.WorkersPrefix, "metrics", workerId);
            return await Requests.Get<TrakHoundWorkerMetrics>(url);
        }


        /// <inheritdoc/>
        public async Task<IEnumerable<string>> QueryLogNames(string serviceId)
        {
            var route = Url.Combine(_apiRoute, serviceId, "logs");
            return await BaseClient.Api.GetJson<IEnumerable<string>>(route);
        }

        /// <summary>
        /// Not supported by the HTTP Client (always returns null)
        /// </summary>
        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeToLog(string serviceId, TrakHoundLogLevel minimumLevel = TrakHoundLogLevel.Information)
        {
            return null;
        }

        /// <summary>
        /// Not supported by the HTTP Client (always returns null)
        /// </summary>
        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeToLog(string serviceId, string logName, TrakHoundLogLevel minimumLevel = TrakHoundLogLevel.Information)
        {
            return null;
        }


        /// <summary>
        /// Not supported by the HTTP Client (always returns null)
        /// </summary>
        public async Task<IEnumerable<TrakHoundLogItem>> QueryLogs(string serviceId, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            return null;
        }

        /// <summary>
        /// Not supported by the HTTP Client (always returns null)
        /// </summary>
        public async Task<IEnumerable<TrakHoundLogItem>> QueryLogsByMinimumLevel(string serviceId, TrakHoundLogLevel minimumLevel, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            return null;
        }

        /// <summary>
        /// Not supported by the HTTP Client (always returns null)
        /// </summary>
        public async Task<IEnumerable<TrakHoundLogItem>> QueryLogsByLevel(string serviceId, TrakHoundLogLevel level, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            return null;
        }


        /// <summary>
        /// Not supported by the HTTP Client (always returns null)
        /// </summary>
        public async Task<IEnumerable<TrakHoundLogItem>> QueryLogs(string serviceId, string logName, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            return null;
        }

        /// <summary>
        /// Not supported by the HTTP Client (always returns null)
        /// </summary>
        public async Task<IEnumerable<TrakHoundLogItem>> QueryLogsByMinimumLevel(string serviceId, string logName, TrakHoundLogLevel minimumLevel, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            return null;
        }

        /// <summary>
        /// Not supported by the HTTP Client (always returns null)
        /// </summary>
        public async Task<IEnumerable<TrakHoundLogItem>> QueryLogsByLevel(string serviceId, string logName, TrakHoundLogLevel level, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending)
        {
            return null;
        }


        /// <inheritdoc/>
        public async Task<ITrakHoundStream<TrakHoundWorkerStatusResponse>> SubscribeStatus()
        {
            var url = Url.Combine(TrakHoundHttpConstants.WorkersPrefix, "status/subscribe");

            var websocketStream = new TrakHoundWebSocketsStream<TrakHoundWorkerStatusResponse>(BaseClient.ClientConfiguration, url);
            websocketStream.OnInputReceived = (o) =>
            {
                var json = StringFunctions.GetUtf8String(o);
                var jsonResponse = Json.Convert<TrakHoundWorkerStatusJsonResponse>(json);
                if (jsonResponse != null)
                {
                    var response = new TrakHoundWorkerStatusResponse();
                    response.WorkerId = jsonResponse.WorkerId;
                    response.Status = jsonResponse.Status;
                    response.Timestamp = jsonResponse.Timestamp;
                    return response;
                }

                return default;
            };
            return websocketStream;
        }

        /// <inheritdoc/>
        public async Task<ITrakHoundStream<TrakHoundWorkerStatusResponse>> SubscribeStatus(string workerId)
        {
            if (!string.IsNullOrEmpty(workerId))
            {
                var url = Url.Combine(TrakHoundHttpConstants.WorkersPrefix, workerId, "status/subscribe");

                var websocketStream = new TrakHoundWebSocketsStream<TrakHoundWorkerStatusResponse>(BaseClient.ClientConfiguration, url);
                websocketStream.OnInputReceived = (o) =>
                {
                    var json = StringFunctions.GetUtf8String(o);
                    var jsonResponse = Json.Convert<TrakHoundWorkerStatusJsonResponse>(json);
                    if (jsonResponse != null)
                    {
                        var response = new TrakHoundWorkerStatusResponse();
                        response.WorkerId = jsonResponse.WorkerId;
                        response.Status = jsonResponse.Status;
                        response.Timestamp = jsonResponse.Timestamp;
                        return response;
                    }

                    return default;
                };
                return websocketStream;
            }

            return default;
        }

        /// <inheritdoc/>
        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeLog(TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace)
        {
            var url = Url.Combine(TrakHoundHttpConstants.WorkersPrefix, "log/subscribe");
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

        /// <inheritdoc/>
        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeLog(string workerId, TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace)
        {
            if (!string.IsNullOrEmpty(workerId))
            {
                var url = Url.Combine(TrakHoundHttpConstants.WorkersPrefix, workerId, "log/subscribe");
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

            return default;
        }
    }
}
