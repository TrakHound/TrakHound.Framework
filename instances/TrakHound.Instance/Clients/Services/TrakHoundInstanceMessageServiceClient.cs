// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Clients.Services;
using TrakHound.Services;
using TrakHound.Services.Messages;
using TrakHound.Zones;

namespace TrakHound.Clients
{
    public class TrakHoundInstanceMessageServiceClient : ITrakHoundMessageServiceClient
    {
        private readonly TrakHoundInstanceClient _baseClient;


        public TrakHoundInstanceMessageServiceClient(TrakHoundInstanceClient baseClient)
        {
            _baseClient = baseClient;
        }


        public async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(string routerId = null, CancellationToken cancellationToken = default)
        {
            var router = _baseClient.GetRouter(routerId);
            if (router != null)
            {
                return await router.Messages.CheckAvailability(cancellationToken: cancellationToken);
            }
            else
            {
                return TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>.BadRequest($"Invalid Router ID : {routerId}");
            }
        }


        #region "Subscribe"

        public Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>> Subscribe(string topic, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Subscribe(!string.IsNullOrEmpty(topic) ? new string[] { topic } : null, clientId, qos, routerId, cancellationToken);
        }

        public async Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>> Subscribe(IEnumerable<string> topics, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (topics.IsNullOrEmpty()) return TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.BadRequest("No Topics Specified");

            var router = _baseClient.GetRouter(routerId);
            if (router == null) return TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.BadRequest($"Invalid Router ID : {routerId}");

            return await router.Messages.Subscribe(clientId, topics, qos, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Subscribes and converts each Message's Content to a UTF-8 string. Returns null if the Subscription fails.
        /// </summary>
        public Task<ITrakHoundStream<string>> SubscribeString(string topic, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return SubscribeString(!string.IsNullOrEmpty(topic) ? new string[] { topic } : null, clientId, qos, routerId, cancellationToken);
        }

        /// <summary>
        /// Subscribes and converts each Message's Content to a UTF-8 string. Returns null if the Subscription fails.
        /// </summary>
        public async Task<ITrakHoundStream<string>> SubscribeString(IEnumerable<string> topics, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            var messageResponse = await Subscribe(topics, clientId, qos, routerId, cancellationToken);
            if (messageResponse.Success && messageResponse.Result != null)
            {
                var responseStream = new TrakHoundStream<TrakHoundMessageResponse, string>(messageResponse.Result);
                responseStream.OnInputReceived = (o) => o.GetContentUtf8String();
                return responseStream;
            }

            return null;
        }

        /// <summary>
        /// Subscribes and deserializes each Message's Content from JSON. Returns null if the Subscription fails.
        /// </summary>
        public Task<ITrakHoundStream<TOutput>> SubscribeJson<TOutput>(string topic, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return SubscribeJson<TOutput>(!string.IsNullOrEmpty(topic) ? new string[] { topic } : null, clientId, qos, routerId, cancellationToken);
        }

        /// <summary>
        /// Subscribes and deserializes each Message's Content from JSON. Returns null if the Subscription fails.
        /// </summary>
        public async Task<ITrakHoundStream<TOutput>> SubscribeJson<TOutput>(IEnumerable<string> topics, string clientId = null, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            var messageResponse = await Subscribe(topics, clientId, qos, routerId, cancellationToken);
            if (messageResponse.Success && messageResponse.Result != null)
            {
                var responseStream = new TrakHoundStream<TrakHoundMessageResponse, TOutput>(messageResponse.Result);
                responseStream.OnInputReceived = (o) => o.GetJsonContentObject<TOutput>();
                return responseStream;
            }

            return null;
        }

        #endregion

        #region "Publish"

        public async Task<TrakHoundServiceResponse> Publish(string topic, Stream content, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(topic)) return TrakHoundServiceResponse.BadRequest("No Topic Specified");
            if (content == null) return TrakHoundServiceResponse.BadRequest("No Content Specified");

            var router = _baseClient.GetRouter(routerId);
            if (router == null) return TrakHoundServiceResponse.BadRequest($"Invalid Router ID : {routerId}");

            return await router.Messages.Publish(topic, content, retain, qos, cancellationToken: cancellationToken);
        }

        public Task<TrakHoundServiceResponse> Publish(string topic, byte[] content, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Publish(topic, TrakHoundMessageResponse.GetContent(content), retain, qos, routerId, cancellationToken);
        }

        public Task<TrakHoundServiceResponse> Publish(string topic, string content, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Publish(topic, TrakHoundMessageResponse.GetContent(content), retain, qos, routerId, cancellationToken);
        }

        public Task<TrakHoundServiceResponse> Publish(string topic, object content, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            return Publish(topic, TrakHoundMessageResponse.GetContent(content), retain, qos, routerId, cancellationToken);
        }

        #endregion

        #region "Publish Stream"

        /// <summary>
        /// Opens a Publish Stream that publishes each pushed Message through the Router. Each Message is authorized as a Publish
        /// </summary>
        public Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<TrakHoundMessageResponse>>> PublishStream(string routerId = null, CancellationToken cancellationToken = default)
        {
            var publisher = CreatePublisher(routerId);
            return CreatePublishStream<TrakHoundMessageResponse>(publisher, routerId, cancellationToken, async (message, token) =>
            {
                var response = await publisher.Publish(message.Topic, message.Content, message.Retain, message.Qos, token);
                return TrakHoundMessagePublishFailure.FromResponse(message.Topic, response);
            });
        }

        public Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<byte[]>>> PublishStreamBytes(string topic, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            var publisher = CreatePublisher(routerId);
            return CreateTopicPublishStream<byte[]>(topic, qos, publisher, routerId, cancellationToken, (content, token) => publisher.Publish(topic, TrakHoundMessageResponse.GetContent(content), retain, qos, token));
        }

        public Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<string>>> PublishStreamString(string topic, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            var publisher = CreatePublisher(routerId);
            return CreateTopicPublishStream<string>(topic, qos, publisher, routerId, cancellationToken, (content, token) => publisher.Publish(topic, TrakHoundMessageResponse.GetContent(content), retain, qos, token));
        }

        public Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<TInput>>> PublishStreamJson<TInput>(string topic, bool retain = false, int qos = 0, string routerId = null, CancellationToken cancellationToken = default)
        {
            var publisher = CreatePublisher(routerId);
            return CreateTopicPublishStream<TInput>(topic, qos, publisher, routerId, cancellationToken, (content, token) => publisher.Publish(topic, TrakHoundMessageResponse.GetContent((object)content), retain, qos, token));
        }

        private Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<T>>> CreateTopicPublishStream<T>(string topic, int qos, StreamPublisher publisher, string routerId, CancellationToken cancellationToken, System.Func<T, CancellationToken, Task<TrakHoundServiceResponse>> publish)
        {
            var error = TrakHoundMessageTopics.GetPublishError(topic, qos);
            if (error != null) return Task.FromResult(TrakHoundServiceResponse<ITrakHoundMessagePublishStream<T>>.BadRequest(error));

            return CreatePublishStream<T>(publisher, routerId, cancellationToken, async (item, token) =>
            {
                var response = await publish(item, token);
                return TrakHoundMessagePublishFailure.FromResponse(topic, response);
            });
        }

        private Task<TrakHoundServiceResponse<ITrakHoundMessagePublishStream<T>>> CreatePublishStream<T>(StreamPublisher publisher, string routerId, CancellationToken cancellationToken, System.Func<T, CancellationToken, Task<TrakHoundMessagePublishFailure>> publish)
        {
            if (!publisher.Resolve())
            {
                return Task.FromResult(TrakHoundServiceResponse<ITrakHoundMessagePublishStream<T>>.BadRequest($"Invalid Router ID : {routerId}"));
            }

            ITrakHoundMessagePublishStream<T> stream = new TrakHoundMessagePublishStream<T>(publish, cancellationToken);
            return Task.FromResult(TrakHoundServiceResponse<ITrakHoundMessagePublishStream<T>>.Ok(stream));
        }

        /// <summary>
        /// Creates a Publisher that keeps the Router for the Messages of one stream (ex. a Publish Stream or a publish WebSocket)
        /// </summary>
        internal StreamPublisher CreatePublisher(string routerId = null) => new StreamPublisher(_baseClient, routerId);


        /// <summary>
        /// Publishes the Messages of one stream. The Router is resolved once (and again only when the Routers are reloaded)
        /// instead of for every Message. Each Message is still authorized as a Publish and checked against the caller's Zone.
        /// </summary>
        internal sealed class StreamPublisher
        {
            private readonly TrakHoundInstanceClient _baseClient;
            private readonly string _routerId;
            private volatile TrakHoundServiceRouter _router;


            public StreamPublisher(TrakHoundInstanceClient baseClient, string routerId)
            {
                _baseClient = baseClient;
                _routerId = routerId;
            }

            /// <summary>
            /// Resolves the Router. Returns false if it doesn't exist
            /// </summary>
            public bool Resolve() => GetRouter() != null;

            public async Task<TrakHoundServiceResponse> Publish(string topic, Stream content, bool retain, int qos, CancellationToken cancellationToken)
            {
                if (string.IsNullOrEmpty(topic)) return TrakHoundServiceResponse.BadRequest("No Topic Specified");
                if (content == null) return TrakHoundServiceResponse.BadRequest("No Content Specified");

                var router = GetRouter();
                if (router == null) return TrakHoundServiceResponse.BadRequest($"Invalid Router ID : {_routerId}");

                return await router.Messages.Publish(topic, content, retain, qos, cancellationToken: cancellationToken);
            }

            private TrakHoundServiceRouter GetRouter()
            {
                var router = _router;
                if (router != null && _baseClient.Instance.ServicesProvider.Routers.IsCurrent(router))
                {
                    // The Zone context only applies to the async method that enters it, so it is entered for each Message
                    // (the same as TrakHoundInstanceClient.GetRouter, without resolving the Router again)
                    var requestSource = _baseClient.RequestSource;
                    var caller = TrakHoundZoneResources.GetMemberId(requestSource) != null ? requestSource : null;
                    TrakHoundZoneContext.Enter(_baseClient.Instance.ZoneManager, caller);
                    _baseClient.EnterAdminContext();
                    return router;
                }

                // Also enters the caller's Zone
                router = _baseClient.GetRouter(_routerId);
                _router = router;
                return router;
            }
        }

        #endregion
    }
}
