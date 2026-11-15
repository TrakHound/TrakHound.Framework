// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using System.Collections.Concurrent;
using TrakHound.Logging;
using TrakHound.Services;
using TrakHound.Services.Messages;

namespace TrakHound.Memory.Service
{
    public class MemoryMessageService :
        MemoryService,
        ITrakHoundMessageService
    {
        private const int _defaultBufferSize = 1000;

        private readonly ConcurrentDictionary<string, MemoryMessageStream> _streams = new ConcurrentDictionary<string, MemoryMessageStream>(); // StreamId => Stream
        private readonly ConcurrentDictionary<string, RetainedMessage> _retained = new ConcurrentDictionary<string, RetainedMessage>(); // Topic => Message
        private readonly object _retainLock = new object();
        private readonly int _bufferSize;


        private readonly struct RetainedMessage
        {
            public byte[] Content { get; }

            public byte Qos { get; }

            public long Timestamp { get; }

            public RetainedMessage(byte[] content, byte qos, long timestamp)
            {
                Content = content;
                Qos = qos;
                Timestamp = timestamp;
            }
        }


        public MemoryMessageService(ITrakHoundServiceConfiguration configuration) : base(configuration)
        {
            var bufferSize = configuration?.GetParameter<int>("bufferSize") ?? 0;
            _bufferSize = bufferSize > 0 ? bufferSize : _defaultBufferSize;
        }

        protected override void OnDisposed()
        {
            // The Service is replaced when its Configuration changes, so Subscribers can't be left attached to this instance
            foreach (var stream in _streams.Values) stream.Dispose();
            _streams.Clear();
            _retained.Clear();
        }


        public override Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            var response = new TrakHoundServiceAvailabilityResponse();
            response.IsAvailable = true;

            return Task.FromResult(TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>.Ok(response));
        }

        public Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>> Subscribe(string clientId, IEnumerable<string> topics, int qos, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (topics.IsNullOrEmpty()) return Task.FromResult(TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.BadRequest("No Topics Specified"));
            if (!IsValidQos(qos)) return Task.FromResult(TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.BadRequest($"Invalid QoS ({qos}) : Must be 0, 1, or 2"));

            foreach (var topic in topics)
            {
                var error = ValidateFilter(topic);
                if (error != null) return Task.FromResult(TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.BadRequest($"Invalid Topic ({topic}) : {error}"));
            }

            var stream = new MemoryMessageStream(clientId, topics, qos, _bufferSize);
            stream.OnLog = Log;
            stream.Disposed += (s, id) => _streams.TryRemove(id, out _);

            // Locked with Publish so a Retained Message is never both missed and replaced while Subscribing
            lock (_retainLock)
            {
                // Queued now and delivered once the consumer calls Start()
                foreach (var retained in _retained)
                {
                    if (stream.IsMatch(retained.Key))
                    {
                        stream.Enqueue(CreateMessage(retained.Key, retained.Value.Content, true, Math.Min(retained.Value.Qos, (byte)qos), retained.Value.Timestamp));
                    }
                }

                _streams[stream.Id] = stream;
            }

            Log(TrakHoundLogLevel.Debug, $"Memory Messages : Subscribed : ClientId = {stream.ClientId} : Topics = {string.Join(", ", stream.Filters)}");

            return Task.FromResult(TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.Ok(stream));
        }

        public async Task<TrakHoundServiceResponse> Publish(string topic, Stream content, bool retain, int qos, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(topic) || content == null) return TrakHoundServiceResponse.BadRequest();
            if (!IsValidQos(qos)) return TrakHoundServiceResponse.BadRequest($"Invalid QoS ({qos}) : Must be 0, 1, or 2");

            var error = ValidateTopic(topic);
            if (error != null) return TrakHoundServiceResponse.BadRequest($"Invalid Topic ({topic}) : {error}");

            // An empty payload is valid (ex. to clear a Retained Message)
            byte[] bytes;
            using (var memoryStream = new MemoryStream())
            {
                if (content.CanSeek) content.Seek(0, SeekOrigin.Begin);
                await content.CopyToAsync(memoryStream, cancellationToken);
                bytes = memoryStream.ToArray();
            }

            var timestamp = UnixDateTime.Now;
            var count = 0;

            lock (_retainLock)
            {
                if (retain)
                {
                    if (bytes.Length > 0) _retained[topic] = new RetainedMessage(bytes, (byte)qos, timestamp);
                    else _retained.TryRemove(topic, out _);
                }

                foreach (var stream in _streams.Values)
                {
                    // Delivered once per Subscriber, even if more than one of its Filters match
                    if (stream.IsMatch(topic))
                    {
                        // Live Messages are delivered with Retain = false (same as an MQTT Broker)
                        if (stream.Enqueue(CreateMessage(topic, bytes, false, Math.Min((byte)qos, (byte)stream.Qos), timestamp))) count++;
                    }
                }
            }

            Log(TrakHoundLogLevel.Debug, $"Memory Messages : Message Published Successfully : Topic = {topic} : Subscribers = {count}");

            return TrakHoundServiceResponse.Ok();
        }


        private static TrakHoundMessageResponse CreateMessage(string topic, byte[] content, bool retain, byte qos, long timestamp)
        {
            var response = new TrakHoundMessageResponse();
            response.Topic = topic;
            response.Retain = retain;
            response.Qos = qos;
            response.Timestamp = timestamp;
            // Each Subscriber gets its own (read-only) Stream over the shared bytes
            response.Content = new MemoryStream(content, writable: false);
            return response;
        }

        private static bool IsValidQos(int qos) => qos >= 0 && qos <= 2;

        /// <summary>
        /// Validates a Topic Name used to Publish. Returns null if valid, otherwise the reason it is invalid.
        /// </summary>
        private static string ValidateTopic(string topic)
        {
            if (string.IsNullOrEmpty(topic)) return "Topic cannot be empty";
            if (topic.Contains('\0')) return "Topic cannot contain a null character";
            if (topic.Contains('+') || topic.Contains('#')) return "Wildcards (+, #) are not allowed when Publishing";
            return null;
        }

        /// <summary>
        /// Validates a Topic Filter used to Subscribe. Returns null if valid, otherwise the reason it is invalid.
        /// </summary>
        private static string ValidateFilter(string filter)
        {
            if (string.IsNullOrEmpty(filter)) return "Topic cannot be empty";
            if (filter.Contains('\0')) return "Topic cannot contain a null character";

            var levels = filter.Split('/');
            for (var i = 0; i < levels.Length; i++)
            {
                var level = levels[i];

                if (level.Contains('#') && (level != "#" || i != levels.Length - 1)) return "Multi-level wildcard (#) must be the entire last level";
                if (level.Contains('+') && level != "+") return "Single-level wildcard (+) must be an entire level";
            }

            return null;
        }
    }
}
