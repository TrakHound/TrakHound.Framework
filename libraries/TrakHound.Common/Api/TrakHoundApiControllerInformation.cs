// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Api
{
    /// <summary>
    /// Information describing an Api Controller and its Endpoints
    /// </summary>
    public class TrakHoundApiControllerInformation
    {
        /// <summary>
        /// A unique Identifier for the Controller (an MD5 hash of the Api ID and Controller Class Name)
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The display Name of the Controller (the Class Name if not set in the TrakHoundApiControllerAttribute)
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The Route of the Controller, relative to the Api Route (always starts with "/")
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        /// <summary>
        /// A description of the Controller
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The Endpoints defined in the Controller
        /// </summary>
        [JsonPropertyName("endPoints")]
        public IEnumerable<TrakHoundApiEndpointInformation> EndPoints { get; set; }
    }
}
