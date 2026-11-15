// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Mqtt.Service
{
    public class MqttConnectionConfiguration
    {
        /// <summary>
        /// The display Name of the connection
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// A description of the connection
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The MQTT broker hostname
        /// </summary>
        [JsonPropertyName("hostname")]
        public string Hostname { get; set; }

        /// <summary>
        /// The MQTT broker port number
        /// </summary>
        [JsonPropertyName("port")]
        public int Port { get; set; }

        /// <summary>
        /// The timeout (in milliseconds) to use for connection and read/write (default 10000)
        /// </summary>
        [JsonPropertyName("timeout")]
        public int Timeout { get; set; }

        /// <summary>
        /// The interval (in milliseconds) to wait before reconnecting a Subscription after a disconnection (default 5000)
        /// </summary>
        [JsonPropertyName("reconnectInterval")]
        public int ReconnectInterval { get; set; }

        /// <summary>
        /// Sets whether to use TLS or not (true or false)
        /// </summary>
        [JsonPropertyName("useTls")]
        public bool UseTls { get; set; }

        /// <summary>
        /// The Username to authenticate with (optional)
        /// </summary>
        [JsonPropertyName("username")]
        public string Username { get; set; }

        /// <summary>
        /// The Password to authenticate with (optional, used with <see cref="Username"/>)
        /// </summary>
        [JsonPropertyName("password")]
        public string Password { get; set; }

        /// <summary>
        /// Sets the Client ID to use for the connection
        /// </summary>
        [JsonPropertyName("clientId")]
        public string ClientId { get; set; }

        /// <summary>
        /// Sets the CleanSession flag (true or false)
        /// </summary>
        [JsonPropertyName("cleanSession")]
        public bool CleanSession { get; set; }

        /// <summary>
        /// Sets the Quality Of Service (Qos) to use. 0 = At Most Once, 1 = At least Once, 2 = Exactly Once.
        /// Not currently used: the QoS is specified on each Publish and Subscribe request.
        /// </summary>
        [JsonPropertyName("qos")]
        public int Qos { get; set; }
    }
}
