// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Services;
using TrakHound.Services.MessageQueues;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// HTTP implementation of ITrakHoundMessageQueueServiceClient that uses the Message Queue Service Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpMessageQueueServiceClient : TrakHoundHttpClientBase, ITrakHoundMessageQueueServiceClient
    {
        /// <summary>
        /// Creates a new Message Queue Service Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpMessageQueueServiceClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.MessageQueueServicePrefix, "availability");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);
            return CreateResponse(httpResponse, httpResponse.GetJsonContentObject<TrakHoundServiceAvailabilityResponse>());
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueMessage>>> Pull(string queue, int count = 1, bool autoAcknowledge = false, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.MessageQueueServicePrefix, "pull");
            url = Url.AddQueryParameter(url, "queue", queue);
            url = Url.AddQueryParameter(url, "count", count);
            url = Url.AddQueryParameter(url, "autoAcknowledge", autoAcknowledge);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);
            return CreateResponse<IEnumerable<TrakHoundQueueMessage>>(httpResponse, httpResponse.GetJsonContentObject<List<TrakHoundQueueMessage>>());
        }

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundQueueMessage>>> Subscribe(string queue, bool autoAcknowledge = false, int prefetch = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var url = Url.Combine(TrakHoundHttpConstants.MessageQueueServicePrefix, "subscribe");
            url = Url.AddQueryParameter(url, "queue", queue);
            url = Url.AddQueryParameter(url, "autoAcknowledge", autoAcknowledge);
            if (prefetch > 0) url = Url.AddQueryParameter(url, "prefetch", prefetch);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            // Each WebSocket message is one TrakHoundQueueMessage as JSON (parsed by the Stream's default JSON parsing)
            var stream = new TrakHoundWebSocketsStream<TrakHoundQueueMessage>(BaseClient.ClientConfiguration, url);
            return Task.FromResult(TrakHoundServiceResponse<ITrakHoundStream<TrakHoundQueueMessage>>.Ok(stream));
        }


        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, Stream content, TrakHoundQueuePublishOptions options = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(queue) || content == null) return TrakHoundServiceResponse<TrakHoundQueuePublishResult>.BadRequest();

            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.MessageQueueServicePrefix, "publish");
            url = Url.AddQueryParameter(url, "queue", queue);
            url = Url.AddQueryParameter(url, "messageId", options?.MessageId);
            url = Url.AddQueryParameter(url, "contentType", options?.ContentType);
            foreach (var header in TrakHoundQueueContent.FormatHeaders(options?.Headers)) url = Url.AddQueryParameter(url, "header", header);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.PostResponse(url, content, cancellationToken: cancellationToken);
            return CreateResponse(httpResponse, httpResponse.GetJsonContentObject<TrakHoundQueuePublishResult>());
        }

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, byte[] content, TrakHoundQueuePublishOptions options = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Publish(queue, TrakHoundQueueContent.FromBytes(content), options, routerId, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, string content, TrakHoundQueuePublishOptions options = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Publish(queue, TrakHoundQueueContent.FromString(content), TrakHoundQueueContent.WithDefaultContentType(options, TrakHoundQueueContent.TextContentType), routerId, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse<TrakHoundQueuePublishResult>> Publish(string queue, object content, TrakHoundQueuePublishOptions options = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Publish(queue, TrakHoundQueueContent.FromJson(content), TrakHoundQueueContent.WithDefaultContentType(options, TrakHoundQueueContent.JsonContentType), routerId, cancellationToken);
        }


        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Acknowledge(string queue, IEnumerable<string> deliveryIds, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.MessageQueueServicePrefix, "acknowledge");
            url = Url.AddQueryParameter(url, "queue", queue);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.PutResponse(url, (object)deliveryIds, cancellationToken: cancellationToken);
            return CreateResponse<IEnumerable<TrakHoundQueueDeliveryResult>>(httpResponse, httpResponse.GetJsonContentObject<List<TrakHoundQueueDeliveryResult>>());
        }

        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundQueueDeliveryResult>>> Reject(string queue, IEnumerable<string> deliveryIds, bool requeue = true, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.MessageQueueServicePrefix, "reject");
            url = Url.AddQueryParameter(url, "queue", queue);
            url = Url.AddQueryParameter(url, "requeue", requeue);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.PutResponse(url, (object)deliveryIds, cancellationToken: cancellationToken);
            return CreateResponse<IEnumerable<TrakHoundQueueDeliveryResult>>(httpResponse, httpResponse.GetJsonContentObject<List<TrakHoundQueueDeliveryResult>>());
        }


        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<IEnumerable<TrakHoundMessageQueueMetrics>>> GetMetrics(IEnumerable<string> queues = null, string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.MessageQueueServicePrefix, "metrics");
            if (!queues.IsNullOrEmpty())
            {
                foreach (var queue in queues) url = Url.AddQueryParameter(url, "queues", queue);
            }
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);
            return CreateResponse<IEnumerable<TrakHoundMessageQueueMetrics>>(httpResponse, httpResponse.GetJsonContentObject<List<TrakHoundMessageQueueMetrics>>());
        }


        private TrakHoundServiceResponse<TResult> CreateResponse<TResult>(TrakHoundHttpResponse httpResponse, TResult result)
        {
            var serviceResponse = new TrakHoundServiceResponse<TResult>();
            serviceResponse.RouterId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.RouterIdHeader);
            serviceResponse.ServiceId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.ServiceIdHeader);
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = serviceResponse.Success ? result : default;
            return serviceResponse;
        }
    }
}
