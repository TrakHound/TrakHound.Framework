// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace TrakHound.Apps
{
    /// <summary>
    /// Information about a Page (a routable Component) in an App
    /// </summary>
    public class TrakHoundAppPageInformation
    {
        /// <summary>
        /// The App that the Page belongs to (set when the Page is looked up by Route; not serialized)
        /// </summary>
        [JsonIgnore]
        public TrakHoundAppInformation App { get; set; }

        /// <summary>
        /// A unique Identifier for the Page (a Hash of the App ID and the Page's Type)
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// The Route Template of the Page relative to the App's Route (ex. "/orders/{id}")
        /// </summary>
        [JsonPropertyName("route")]
        public string Route { get; set; }

        /// <summary>
        /// A description of the Page
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The Parameters that the Page accepts
        /// </summary>
        [JsonPropertyName("parameters")]
        public IEnumerable<TrakHoundAppPageParameterInformation> Parameters { get; set; }
    }
}
