// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace TrakHound.Instances
{
    /// <summary>
    /// Information that describes an Instance (identity, status, environment, Deployment, and network Interfaces)
    /// </summary>
    public class TrakHoundInstanceInformation
    {
        /// <summary>
        /// The unique Identifier of the Instance
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The Version of the Instance software
        /// </summary>
        [JsonPropertyName("version")]
        public string Version { get; set; }

        /// <summary>
        /// The path the Instance is identified by
        /// </summary>
        [JsonPropertyName("path")]
        public string Path { get; set; }

        /// <summary>
        /// The Organization the Instance belongs to
        /// </summary>
        [JsonPropertyName("organization")]
        public string Organization { get; set; }

        /// <summary>
        /// The display Name of the Instance
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// A description of the Instance
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The current Status of the Instance
        /// </summary>
        [JsonPropertyName("status")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundInstanceStatus Status { get; set; }

        /// <summary>
        /// When the Information was last updated
        /// </summary>
        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        /// <summary>
        /// Information about the machine the Instance is running on
        /// </summary>
        [JsonPropertyName("environment")]
        public TrakHoundInstanceEnvironmentInformation Environment { get; set; }

        /// <summary>
        /// The Deployment installed on the Instance (null if none)
        /// </summary>
        [JsonPropertyName("deployment")]
        public TrakHoundInstanceDeploymentInformation Deployment { get; set; }

        /// <summary>
        /// The network Interfaces (ex. HTTP) the Instance can be reached on
        /// </summary>
        [JsonPropertyName("interfaces")]
        public IEnumerable<TrakHoundInstanceInterfaceInformation> Interfaces { get; set; }


        /// <summary>
        /// Creates a new Instance Information with empty Environment Information
        /// </summary>
        public TrakHoundInstanceInformation()
        {
            Environment = new TrakHoundInstanceEnvironmentInformation();
        }


        /// <summary>
        /// Gets the first Interface of the specified Type (case-sensitive, ex. "HTTP"). Returns null if not found
        /// </summary>
        public TrakHoundInstanceInterfaceInformation GetInterface(string interfaceType)
        {
            if (!string.IsNullOrEmpty(interfaceType) && !Interfaces.IsNullOrEmpty())
            {
                return Interfaces.FirstOrDefault(o => o.Type == interfaceType);
            }

            return null;
        }
    }
}
