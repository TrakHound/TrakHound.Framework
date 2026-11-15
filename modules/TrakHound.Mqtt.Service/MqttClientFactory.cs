// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using MQTTnet;
using MQTTnet.Client;

namespace TrakHound.Mqtt.Service
{
    /// <summary>
    /// Creates MQTT clients and connection options from a <see cref="MqttConnectionConfiguration"/>
    /// so that every connection (publish, subscribe and availability) uses the same settings.
    /// </summary>
    internal static class MqttClientFactory
    {
        public const int DefaultTimeout = 10000;
        public const int DefaultReconnectInterval = 5000;

        private static readonly MqttFactory _factory = new MqttFactory();


        public static IMqttClient CreateClient() => _factory.CreateMqttClient();

        public static MqttClientOptions CreateOptions(MqttConnectionConfiguration configuration, string clientId)
        {
            var builder = new MqttClientOptionsBuilder();

            // TCP (the default port is chosen by MQTTnet based on TLS when not set)
            builder.WithTcpServer(configuration.Hostname, configuration.Port > 0 ? configuration.Port : null);
            builder.WithTimeout(TimeSpan.FromMilliseconds(GetTimeout(configuration)));

            // TLS
            if (configuration.UseTls)
            {
                builder.WithTlsOptions(o => o.UseTls());
            }

            // Credentials
            if (!string.IsNullOrEmpty(configuration.Username))
            {
                builder.WithCredentials(configuration.Username, configuration.Password);
            }

            if (configuration.CleanSession) builder.WithCleanSession();

            // Client ID
            if (!string.IsNullOrEmpty(clientId)) builder.WithClientId(clientId);

            return builder.Build();
        }

        public static MqttClientDisconnectOptions CreateDisconnectOptions()
        {
            return new MqttClientDisconnectOptionsBuilder()
                .WithReason(MqttClientDisconnectOptionsReason.NormalDisconnection)
                .Build();
        }

        public static MqttClientSubscribeOptions CreateSubscribeOptions(IEnumerable<MqttSubscription> subscriptions)
        {
            var builder = _factory.CreateSubscribeOptionsBuilder();
            foreach (var subscription in subscriptions)
            {
                builder.WithTopicFilter(subscription.Topic, (MQTTnet.Protocol.MqttQualityOfServiceLevel)subscription.Qos);
            }
            return builder.Build();
        }

        public static int GetTimeout(MqttConnectionConfiguration configuration)
        {
            return configuration?.Timeout > 0 ? configuration.Timeout : DefaultTimeout;
        }

        public static int GetReconnectInterval(MqttConnectionConfiguration configuration)
        {
            return configuration?.ReconnectInterval > 0 ? configuration.ReconnectInterval : DefaultReconnectInterval;
        }

        /// <summary>
        /// Returns true if <paramref name="qos"/> is a valid MQTT Quality of Service level (0, 1 or 2)
        /// </summary>
        public static bool IsValidQos(int qos) => qos >= 0 && qos <= 2;
    }
}
