// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Text.Json.Serialization;

namespace TrakHound.Instances
{
    /// <summary>
    /// A network Interface (ex. HTTP) that an Instance can be reached on
    /// </summary>
    public class TrakHoundInstanceInterfaceInformation
    {
        /// <summary>
        /// The unique Identifier of the Interface (defaults to a new Guid)
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The type of Interface (ex. "HTTP")
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// The address (hostname or IP) of the Interface
        /// </summary>
        [JsonPropertyName("address")]
        public string Address { get; set; }

        /// <summary>
        /// The port of the Interface (defaults to 8472)
        /// </summary>
        [JsonPropertyName("port")]
        public int Port { get; set; }


        /// <summary>
        /// Creates a new Interface with a new Guid Id and the default port (8472)
        /// </summary>
        public TrakHoundInstanceInterfaceInformation()
        {
            Id = Guid.NewGuid().ToString();
            Port = 8472;
        }


        /// <summary>
        /// Gets the base URL of the Interface (ex. "http://localhost:8472"). Only HTTP Interfaces are supported; returns null otherwise
        /// </summary>
        public string GetBaseUrl()
        {
            if (!string.IsNullOrEmpty(Type) && !string.IsNullOrEmpty(Address))
            {
                switch (Type.ToLower())
                {
                    case "http": return $"http://{Address}:{Port}";
                }
            }

            return null;
        }
    }
}
