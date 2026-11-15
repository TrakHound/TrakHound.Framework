// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Api
{
    /// <summary>
    /// Information describing an Api Endpoint (a Controller Method marked with an Endpoint Attribute)
    /// </summary>
    public class TrakHoundApiEndpointInformation
    {
        /// <summary>
        /// The Api the Endpoint belongs to (only set when returned from a Route match, not serialized)
        /// </summary>
        [JsonIgnore]
        public TrakHoundApiInformation Api { get; set; }

        /// <summary>
        /// The Controller the Endpoint belongs to (only set when returned from a Route match, not serialized)
        /// </summary>
        [JsonIgnore]
        public TrakHoundApiControllerInformation Controller { get; set; }


        /// <summary>
        /// A unique Identifier for the Endpoint (an MD5 hash of the Method and Route)
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The Group Name used to organize the Endpoint (from the TrakHoundApiGroupAttribute)
        /// </summary>
        [JsonPropertyName("group")]
        public string Group { get; set; }

        /// <summary>
        /// Whether the Endpoint handles single Requests or a Stream
        /// </summary>
        [JsonPropertyName("type")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundApiEndpointType Type { get; set; }

        /// <summary>
        /// The HTTP Method the Endpoint handles (null for Stream Endpoints)
        /// </summary>
        [JsonPropertyName("method")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
        public TrakHoundApiMethod? Method { get; set; }

        /// <summary>
        /// The Route of the Endpoint, relative to the Controller Route (always starts with "/")
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        /// <summary>
        /// A description of the Endpoint
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The Route, Query, Header, and Body Parameters the Endpoint accepts (Reserved Client Parameters are excluded)
        /// </summary>
        [JsonPropertyName("parameters")]
        public IEnumerable<TrakHoundApiParameterInformation> Parameters { get; set; }

        /// <summary>
        /// The Responses the Endpoint can return (from TrakHoundApiResponseAttributes)
        /// </summary>
        [JsonPropertyName("responses")]
        public IEnumerable<TrakHoundApiResponseInformation> Responses { get; set; }

        /// <summary>
        /// The Permissions required to call the Endpoint (from TrakHoundPermissionAttributes)
        /// </summary>
        [JsonPropertyName("permissions")]
        public IEnumerable<string> Permissions { get; set; }
    }
}
