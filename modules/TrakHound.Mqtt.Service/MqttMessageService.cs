// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using MQTTnet;
using MQTTnet.Client;
using MQTTnet.Protocol;
using System.Collections.Concurrent;
using TrakHound.Logging;
using TrakHound.Services;
using TrakHound.Services.Messages;

namespace TrakHound.Mqtt.Service
{
    public class MqttMessageService :
        TrakHoundService,
        ITrakHoundMessageService
    {
        private readonly MqttConnectionConfiguration _connectionConfiguration;
        private readonly MqttServiceClient _publishClient;
        private readonly ConcurrentDictionary<string, string> _activeClientIds = new ConcurrentDictionary<string, string>(); // ClientId => StreamId
        private readonly string _notAvailableMessage = "MQTT Connection Not Configured";


        public MqttMessageService(ITrakHoundServiceConfiguration configuration) : base(configuration)
        {
            // Without a Hostname there is nothing to connect to, so requests return 503 (Not Configured) instead of failing to connect
            if (configuration != null && !string.IsNullOrEmpty(configuration.GetParameter("hostname")))
            {
                var connectionConfiguration = new MqttConnectionConfiguration();
                connectionConfiguration.Hostname = configuration.GetParameter("hostname");
                connectionConfiguration.Port = configuration.GetParameter<int>("port");
                connectionConfiguration.ClientId = configuration.GetParameter("clientId");
                connectionConfiguration.Username = configuration.GetParameter("username");
                connectionConfiguration.Password = configuration.GetParameter("password");
                connectionConfiguration.Timeout = configuration.GetParameter<int>("timeout");
                connectionConfiguration.ReconnectInterval = configuration.GetParameter<int>("reconnectInterval");
                connectionConfiguration.CleanSession = !bool.TryParse(configuration.GetParameter("cleanSession"), out var cleanSession) || cleanSession; // True unless set to false
                connectionConfiguration.UseTls = configuration.GetParameter<bool>("useTls");
                connectionConfiguration.ClientCertificate = configuration.GetParameter("clientCertificate");
                connectionConfiguration.ClientKey = configuration.GetParameter("clientKey");
                connectionConfiguration.ClientCertificatePassword = configuration.GetParameter("clientCertificatePassword");
                connectionConfiguration.CaCertificate = configuration.GetParameter("caCertificate");

                try
                {
                    // Loaded once, so a file that can't be read is reported here instead of on every connection
                    connectionConfiguration.Certificates = MqttCertificates.Load(connectionConfiguration);
                }
                catch (Exception ex)
                {
                    // Connecting without the certificates would fail, or skip the check of the Broker, so requests return 503 instead
                    _notAvailableMessage = $"MQTT Certificate Error : {ex.Message}";
                    Log(TrakHoundLogLevel.Error, _notAvailableMessage);
                    return;
                }

                _connectionConfiguration = connectionConfiguration;

                // One Publish connection per Service instance (replaced along with the Service when the Configuration changes)
                _publishClient = new MqttServiceClient(_connectionConfiguration, _connectionConfiguration.ClientId, (level, message) => Log(level, message));
            }
        }

        protected override void OnDisposed()
        {
            _publishClient?.Dispose();
        }


        public override async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            var response = new TrakHoundServiceAvailabilityResponse();

            if (_publishClient != null)
            {
                try
                {
                    // Uses (and keeps) the Publish connection so the check uses exactly the same settings as real requests
                    await _publishClient.EnsureConnected(cancellationToken);
                    response.IsAvailable = true;
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    response.Message = $"Connect : Connection Error : {ex.Message}";
                }
            }
            else
            {
                response.Message = _notAvailableMessage;
            }

            return TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>.Ok(response);
        }

        public Task<TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>> Subscribe(string clientId, IEnumerable<string> topics, int qos, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (_connectionConfiguration == null) return Task.FromResult(TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.NotAvailable(_notAvailableMessage));
            if (topics.IsNullOrEmpty()) return Task.FromResult(TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.BadRequest("No Topics Specified"));
            if (!MqttClientFactory.IsValidQos(qos)) return Task.FromResult(TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.BadRequest($"Invalid QoS ({qos}) : Must be 0, 1, or 2"));

            foreach (var topic in topics)
            {
                try
                {
                    MqttTopicValidator.ThrowIfInvalidSubscribe(topic);
                }
                catch (Exception ex)
                {
                    return Task.FromResult(TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.BadRequest($"Invalid Topic ({topic}) : {ex.Message}"));
                }
            }

            // A Broker only allows one connection per Client ID, so two Streams with the same ID would disconnect each other indefinitely
            var streamId = Guid.NewGuid().ToString();
            if (!string.IsNullOrEmpty(clientId))
            {
                if (clientId == _publishClient?.ClientId || !_activeClientIds.TryAdd(clientId, streamId))
                {
                    return Task.FromResult(TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.BadRequest($"Client ID ({clientId}) is already in use"));
                }
            }

            var stream = new MqttStream<TrakHoundMessageResponse>(Configuration, _connectionConfiguration, clientId, ProcessLog);
            stream.OnProcessMessage = ProcessMqttMessage;
            stream.AddSubscriptions(topics, qos);

            if (!string.IsNullOrEmpty(clientId))
            {
                stream.Disposed += (s, id) => _activeClientIds.TryRemove(new KeyValuePair<string, string>(clientId, streamId));
            }

            return Task.FromResult(TrakHoundServiceResponse<ITrakHoundStream<TrakHoundMessageResponse>>.Ok(stream));
        }

        public async Task<TrakHoundServiceResponse> Publish(string topic, Stream content, bool retain, int qos, CancellationToken cancellationToken = default)
        {
            if (_publishClient == null) return TrakHoundServiceResponse.NotAvailable(_notAvailableMessage);
            if (string.IsNullOrEmpty(topic) || content == null) return TrakHoundServiceResponse.BadRequest();
            if (!MqttClientFactory.IsValidQos(qos)) return TrakHoundServiceResponse.BadRequest($"Invalid QoS ({qos}) : Must be 0, 1, or 2");

            try
            {
                MqttTopicValidator.ThrowIfInvalid(topic);
            }
            catch (Exception ex)
            {
                return TrakHoundServiceResponse.BadRequest($"Invalid Topic ({topic}) : {ex.Message}");
            }

            try
            {
                await _publishClient.EnsureConnected(cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                // 503 so that a Service Router can try its next Target
                return TrakHoundServiceResponse.NotAvailable($"Connection Error : {ex.Message}");
            }

            try
            {
                // An empty payload is valid (ex. to clear a Retained Message)
                using var memoryStream = new MemoryStream();
                if (content.CanSeek) content.Seek(0, SeekOrigin.Begin);
                await content.CopyToAsync(memoryStream, cancellationToken);

                var message = new MqttApplicationMessage();
                message.Topic = topic;
                message.PayloadSegment = new ArraySegment<byte>(memoryStream.GetBuffer(), 0, (int)memoryStream.Length);
                message.Retain = retain;
                message.QualityOfServiceLevel = (MqttQualityOfServiceLevel)qos;

                var result = await _publishClient.Publish(message, cancellationToken);
                if (result != null && result.IsSuccess)
                {
                    Log(TrakHoundLogLevel.Debug, $"MQTT : {_connectionConfiguration.Hostname}:{_connectionConfiguration.Port} : Message Published Successfully : Topic = {topic}");
                    return TrakHoundServiceResponse.Ok();
                }
                else
                {
                    var reason = result?.ReasonString ?? result?.ReasonCode.ToString();
                    Log(TrakHoundLogLevel.Debug, $"MQTT : {_connectionConfiguration.Hostname}:{_connectionConfiguration.Port} : Error Publishing Message : {reason}");
                    return TrakHoundServiceResponse.InternalError(reason);
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return TrakHoundServiceResponse.InternalError(ex.Message);
            }
        }

        private void ProcessLog(TrakHoundLogItem item)
        {
            Log(item);
        }

        private static TrakHoundMessageResponse ProcessMqttMessage(MqttApplicationMessage mqttMessage)
        {
            var response = new TrakHoundMessageResponse();
            response.Topic = mqttMessage.Topic;
            response.Retain = mqttMessage.Retain;
            response.Qos = (byte)mqttMessage.QualityOfServiceLevel;
            response.Timestamp = UnixDateTime.Now;
            response.Content = new MemoryStream(mqttMessage.PayloadSegment.ToArray(), writable: false);
            return response;
        }
    }
}
