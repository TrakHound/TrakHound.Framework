// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Http;
using TrakHound.Services;
using TrakHound.Services.Messages;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// HTTP implementation of ITrakHoundMessageServiceClient that uses the Message Service Api of a remote Instance
    /// </summary>
    public class TrakHoundHttpMessageServiceClient : TrakHoundHttpClientBase, ITrakHoundMessageServiceClient
    {
        /// <summary>
        /// Creates a new Message Service Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpMessageServiceClient(TrakHoundHttpClient baseClient) : base(baseClient) { }


        /// <inheritdoc/>
        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default)
        {
            var url = Url.Combine(BaseUrl, TrakHoundHttpConstants.MessageServicePrefix, "availability");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var httpResponse = await Requests.GetResponse(url, cancellationToken: cancellationToken);

            var serviceResponse = new TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>();
            serviceResponse.RouterId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.RouterIdHeader);
            serviceResponse.ServiceId = GetHeaderValue(httpResponse, TrakHoundHttpConstants.ServiceIdHeader);
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            serviceResponse.Result = httpResponse.GetJsonContentObject<TrakHoundServiceAvailabilityResponse>();
            return serviceResponse;
        }


        #region "Subscribe"

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>> Subscribe(string topic, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Subscribe(new[] { topic }, clientId, qos, routerId, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>> Subscribe(IEnumerable<string> topics, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var validTopics = topics?.Where(o => !string.IsNullOrEmpty(o)).ToList();
            if (validTopics.IsNullOrEmpty())
            {
                return Task.FromResult(TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.BadRequest("No Topics Specified"));
            }

            var stream = CreateMessageStream(validTopics, clientId, qos, routerId);
            return Task.FromResult(TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.Ok(stream));
        }

        /// <inheritdoc/>
        public Task<ITrakHoundStream<string>> SubscribeString(string topic, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var responseStream = new TrakHoundStream<TrakHoundMessageResponse, string>(CreateMessageStream(new[] { topic }, clientId, qos, routerId));
            responseStream.OnInputReceived = (o) => o.GetContentUtf8String();
            return Task.FromResult<ITrakHoundStream<string>>(responseStream);
        }

        /// <inheritdoc/>
        public Task<ITrakHoundStream<TOutput>> SubscribeJson<TOutput>(string topic, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var responseStream = new TrakHoundStream<TrakHoundMessageResponse, TOutput>(CreateMessageStream(new[] { topic }, clientId, qos, routerId));
            responseStream.OnInputReceived = (o) => o.GetJsonContentObject<TOutput>();
            return Task.FromResult<ITrakHoundStream<TOutput>>(responseStream);
        }

        private TrakHoundWebSocketsStream<TrakHoundMessageResponse> CreateMessageStream(IEnumerable<string> topics, string clientId, int qos, string routerId)
        {
            var url = Url.Combine(TrakHoundHttpConstants.MessageServicePrefix, "subscribe");
            foreach (var topic in topics) url = Url.AddQueryParameter(url, "topic", topic);
            url = Url.AddQueryParameter(url, "clientId", clientId);
            url = Url.AddQueryParameter(url, "qos", qos);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            var stream = new TrakHoundWebSocketsStream<TrakHoundMessageResponse>(BaseClient.ClientConfiguration, url);
            stream.OnInputReceived = (o) => TrakHoundHttpMessageResponse.Parse(o);
            return stream;
        }

        #endregion

        #region "Publish"

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse> Publish(string topic, Stream content, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return content != null ? Publish(topic, retain, qos, routerId, url => Requests.PostResponse(url, content, cancellationToken: cancellationToken)) : Task.FromResult(TrakHoundServiceResponse.BadRequest());
        }

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse> Publish(string topic, byte[] content, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return content != null ? Publish(topic, retain, qos, routerId, url => Requests.PostResponse(url, content, cancellationToken: cancellationToken)) : Task.FromResult(TrakHoundServiceResponse.BadRequest());
        }

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse> Publish(string topic, string content, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return content != null ? Publish(topic, retain, qos, routerId, url => Requests.PostResponse(url, content, cancellationToken: cancellationToken)) : Task.FromResult(TrakHoundServiceResponse.BadRequest());
        }

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse> Publish(string topic, object content, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return content != null ? Publish(topic, retain, qos, routerId, url => Requests.PostResponse(url, content, cancellationToken: cancellationToken)) : Task.FromResult(TrakHoundServiceResponse.BadRequest());
        }

        private async Task<TrakHoundServiceResponse> Publish(string topic, bool retain, int qos, string routerId, System.Func<string, Task<TrakHoundHttpResponse>> post)
        {
            var url = Url.Combine(TrakHoundHttpConstants.MessageServicePrefix, "publish");
            url = Url.AddQueryParameter(url, "topic", topic);
            url = Url.AddQueryParameter(url, "retain", retain);
            url = Url.AddQueryParameter(url, "qos", qos);
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));
            url = Url.Combine(BaseClient.ClientConfiguration?.GetHttpBaseUrl(), url);

            var httpResponse = await post(url);

            var serviceResponse = new TrakHoundServiceResponse();
            serviceResponse.StatusCode = httpResponse.StatusCode;
            serviceResponse.Message = httpResponse.Message;
            return serviceResponse;
        }

        #endregion

        #region "Publish Stream"

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<TrakHoundMessageResponse>>> PublishStream(string routerId = null, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var stream = CreatePublishStream<TrakHoundMessageResponse>(
                message => TrakHoundHttpMessageResponse.CreatePublish(message.Topic, message.Retain, message.Qos, message.GetContentBytes()),
                message => message.Topic,
                routerId,
                cancellationToken
                );

            return Task.FromResult(TrakHoundServiceResponse<ITrakHoundMessagePublishStream<TrakHoundMessageResponse>>.Ok(stream));
        }

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<byte[]>>> PublishStreamBytes(string topic, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return CreateTopicPublishStream<byte[]>(topic, retain, qos, routerId, content => content, cancellationToken);
        }

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<string>>> PublishStreamString(string topic, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return CreateTopicPublishStream<string>(topic, retain, qos, routerId, content => content?.ToUtf8Bytes(), cancellationToken);
        }

        /// <inheritdoc/>
        public Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<TInput>>> PublishStreamJson<TInput>(string topic, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return CreateTopicPublishStream<TInput>(topic, retain, qos, routerId, content => TrakHoundMessageResponse.GetContentBytes(TrakHoundMessageResponse.GetContent(content)), cancellationToken);
        }

        private Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<T>>> CreateTopicPublishStream<T>(string topic, bool retain, int qos, string routerId, System.Func<T, byte[]> getContent, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var error = TrakHoundMessageTopics.GetPublishError(topic, qos);
            if (error != null) return Task.FromResult(TrakHoundServiceResponse<ITrakHoundMessagePublishStream<T>>.BadRequest(error));

            var stream = CreatePublishStream<T>(
                item => TrakHoundHttpMessageResponse.CreatePublish(topic, retain, qos, getContent(item)),
                item => topic,
                routerId,
                cancellationToken
                );

            return Task.FromResult(TrakHoundServiceResponse<ITrakHoundMessagePublishStream<T>>.Ok(stream));
        }

        private ITrakHoundMessagePublishStream<T> CreatePublishStream<T>(System.Func<T, byte[]> createFrame, System.Func<T, string> getTopic, string routerId, CancellationToken cancellationToken)
        {
            var url = Url.Combine(TrakHoundHttpConstants.MessageServicePrefix, "publish", "stream");
            url = Url.AddQueryParameter(url, "routerId", BaseClient.GetRouterId(routerId));

            return new TrakHoundHttpMessagePublishStream<T>(BaseClient.ClientConfiguration, url, createFrame, getTopic, cancellationToken);
        }

        #endregion
    }
}
