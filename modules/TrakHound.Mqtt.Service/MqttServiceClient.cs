// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using MQTTnet;
using MQTTnet.Client;
using TrakHound.Logging;

namespace TrakHound.Mqtt.Service
{
    /// <summary>
    /// A single MQTT connection used for Publishing. Connects on first use (and reconnects on the next
    /// use after a disconnection). Concurrent callers wait for the same connection attempt.
    /// </summary>
    class MqttServiceClient : IDisposable
    {
        private readonly MqttConnectionConfiguration _connectionConfiguration;
        private readonly string _clientId;
        private readonly IMqttClient _mqttClient;
        private readonly Action<TrakHoundLogLevel, string> _log;
        private readonly SemaphoreSlim _connectLock = new SemaphoreSlim(1, 1);
        private volatile bool _disposed;


        public string ClientId => _clientId;

        public bool IsConnected => _mqttClient.IsConnected;


        public MqttServiceClient(MqttConnectionConfiguration connectionConfiguration, string clientId, Action<TrakHoundLogLevel, string> log = null)
        {
            _connectionConfiguration = connectionConfiguration ?? throw new ArgumentNullException(nameof(connectionConfiguration));
            _clientId = !string.IsNullOrEmpty(clientId) ? clientId : Guid.NewGuid().ToString();
            _log = log;

            _mqttClient = MqttClientFactory.CreateClient();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            try
            {
                if (_mqttClient.IsConnected)
                {
                    _mqttClient.DisconnectAsync(MqttClientFactory.CreateDisconnectOptions()).Wait(1000);
                }
            }
            catch { }

            try { _mqttClient.Dispose(); } catch { }
        }


        /// <summary>
        /// Connects to the Broker if not already connected. Throws if the connection fails or times out.
        /// </summary>
        public async Task EnsureConnected(CancellationToken cancellationToken = default)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);

            if (_mqttClient.IsConnected) return;

            await _connectLock.WaitAsync(cancellationToken);
            try
            {
                // Another caller may have connected while waiting
                if (_mqttClient.IsConnected) return;

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(MqttClientFactory.GetTimeout(_connectionConfiguration));

                Log(TrakHoundLogLevel.Debug, "Connecting to Broker..");
                await _mqttClient.ConnectAsync(MqttClientFactory.CreateOptions(_connectionConfiguration, _clientId), timeout.Token);
                Log(TrakHoundLogLevel.Debug, "Connected to Broker Successfully");
            }
            finally
            {
                _connectLock.Release();
            }
        }

        /// <summary>
        /// Publishes the Message, connecting first if needed
        /// </summary>
        public async Task<MqttClientPublishResult> Publish(MqttApplicationMessage message, CancellationToken cancellationToken = default)
        {
            await EnsureConnected(cancellationToken);

            if (_mqttClient.Options?.ProtocolVersion != MQTTnet.Formatter.MqttProtocolVersion.V500)
            {
                message.ContentType = null;
            }

            return await _mqttClient.PublishAsync(message, cancellationToken);
        }

        private void Log(TrakHoundLogLevel level, string message)
        {
            _log?.Invoke(level, $"MQTT : {_connectionConfiguration.Hostname}:{_connectionConfiguration.Port} : {message}");
        }
    }
}
