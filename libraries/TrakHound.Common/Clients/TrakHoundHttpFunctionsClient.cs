// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Functions;
using TrakHound.Http;
using TrakHound.Logging;

namespace TrakHound.Clients
{
    /// <summary>
    /// HTTP implementation of ITrakHoundFunctionsClient that uses the Functions Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpFunctionsClient : TrakHoundHttpClientBase, ITrakHoundFunctionsClient
    {
		private const string _apiRoute = "functions";


		/// <summary>
		/// Creates a new Functions Client that uses the connection of the specified base Client
		/// </summary>
		public TrakHoundHttpFunctionsClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundFunctionInformation>> GetInformation()
        {
			var route = Url.Combine(_apiRoute, "list");
            return await BaseClient.Api.GetJson<IEnumerable<TrakHoundFunctionInformation>>(route);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundFunctionInformation> GetInformation(string functionId)
		{
			var route = Url.Combine(_apiRoute, functionId);
			return await BaseClient.Api.GetJson<TrakHoundFunctionInformation>(route);
		}

		/// <summary>
		/// Not supported by the HTTP Client (always returns null)
		/// </summary>
		public async Task<IEnumerable<TrakHoundFunctionInformation>> GetInformation(IEnumerable<string> functionIds)
		{
			return null;
		}


        /// <inheritdoc/>
        public async Task<IEnumerable<TrakHoundFunctionMetrics>> GetMetrics()
        {
            var url = Url.Combine(BaseUrl, "_functions/metrics");
            return await Requests.Get<IEnumerable<TrakHoundFunctionMetrics>>(url);
        }

        /// <inheritdoc/>
        public async Task<TrakHoundFunctionMetrics> GetMetrics(string functionId)
        {
            if (string.IsNullOrEmpty(functionId)) return null;

            var url = Url.Combine(BaseUrl, "_functions/metrics", functionId);
            return await Requests.Get<TrakHoundFunctionMetrics>(url);
        }


		/// <inheritdoc/>
		public async Task<TrakHoundFunctionResponse> Run(string functionId, IReadOnlyDictionary<string, string> inputParameters = null, string runId = null, long timestamp = 0)
		{
            var route = Url.Combine(_apiRoute, functionId, "run");

            var queryParameters = new Dictionary<string, string>();
            queryParameters.Add("runId", runId);
            queryParameters.Add("timestamp", timestamp.ToString());

            var jsonResponse = await BaseClient.Api.PostJson<TrakHoundFunctionJsonResponse>(route, inputParameters, queryParameters: queryParameters);
			return jsonResponse != null ? jsonResponse.ToResponse() : new TrakHoundFunctionResponse();
        }

        /// <inheritdoc/>
        public async Task<bool> CancelRun(string functionId, string runId)
        {
            if (!string.IsNullOrEmpty(functionId) && !string.IsNullOrEmpty(runId))
            {
                var route = Url.Combine(_apiRoute, functionId, "runs", runId, "cancel");
                var response = await BaseClient.Api.Post(route);
                return response.Success;
            }

            return false;
        }

        /// <inheritdoc/>
        public async Task<ITrakHoundStream<TrakHoundFunctionStatusResponse>> SubscribeRunStatus(string runId)
        {
            if (!string.IsNullOrEmpty(runId))
            {
                var url = Url.Combine(_apiRoute, "run", runId, "status/subscribe");

                var websocketConsumer = new TrakHoundWebSocketsStream<TrakHoundFunctionStatusResponse>(BaseClient.ClientConfiguration, url);
                websocketConsumer.OnInputReceived = (o) =>
                {
                    var json = StringFunctions.GetUtf8String(o);
                    var jsonResponse = Json.Convert<TrakHoundFunctionStatusJsonResponse>(json);
                    if (jsonResponse != null)
                    {
                        var response = new TrakHoundFunctionStatusResponse();
                        response.Status = jsonResponse.Status;
                        response.Timestamp = jsonResponse.Timestamp;
                        return response;
                    }

                    return default;
                };
                return websocketConsumer;
            }

            return default;
        }

        /// <inheritdoc/>
        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeRunLog(string runId, TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace)
        {
            if (!string.IsNullOrEmpty(runId))
            {
                var url = Url.Combine(_apiRoute, "run", runId, "log/subscribe");
                url = Url.AddQueryParameter(url, "minimumLogLevel", minimumLogLevel.ToString());

                var websocketConsumer = new TrakHoundWebSocketsStream<IEnumerable<TrakHoundLogItem>>(BaseClient.ClientConfiguration, url);
                websocketConsumer.OnInputReceived = (o) =>
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
                return websocketConsumer;
            }

            return default;
        }
    }
}
