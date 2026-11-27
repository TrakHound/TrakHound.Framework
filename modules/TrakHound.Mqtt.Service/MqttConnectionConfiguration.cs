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
        /// The Client Certificate to authenticate with (optional): a PEM file, a PKCS#12 file (.pfx or .p12) or the PEM text. Setting it turns on TLS
        /// </summary>
        [JsonPropertyName("clientCertificate")]
        public string ClientCertificate { get; set; }

        /// <summary>
        /// The private key of a PEM <see cref="ClientCertificate"/>: a PEM file or the PEM text (optional when the certificate's PEM includes the key)
        /// </summary>
        [JsonPropertyName("clientKey")]
        public string ClientKey { get; set; }

        /// <summary>
        /// The password of a PKCS#12 <see cref="ClientCertificate"/> or of an encrypted <see cref="ClientKey"/> (optional)
        /// </summary>
        [JsonPropertyName("clientCertificatePassword")]
        public string ClientCertificatePassword { get; set; }

        /// <summary>
        /// The CA Certificate(s) the Broker's certificate must chain to, instead of the ones the system trusts (optional): a PEM or DER file or the PEM text. Setting it turns on TLS
        /// </summary>
        [JsonPropertyName("caCertificate")]
        public string CaCertificate { get; set; }

        /// <summary>
        /// True when the connection uses TLS: <see cref="UseTls"/> is set, or a Client or CA Certificate is
        /// </summary>
        [JsonIgnore]
        public bool IsTls => UseTls || !string.IsNullOrEmpty(ClientCertificate) || !string.IsNullOrEmpty(CaCertificate);

        /// <summary>
        /// The loaded <see cref="ClientCertificate"/> and <see cref="CaCertificate"/> (set by the Service)
        /// </summary>
        [JsonIgnore]
        internal MqttCertificates Certificates { get; set; }

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
        /// Sets the CleanSession flag (true or false). False keeps a Persistent Session on the Broker, which only applies with a set Client ID
        /// </summary>
        [JsonPropertyName("cleanSession")]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public bool CleanSession { get; set; } = true;
    }
}
