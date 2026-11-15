// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Api
{
    /// <summary>
    /// Information describing a Parameter accepted by an Api Endpoint
    /// </summary>
    public class TrakHoundApiParameterInformation
    {
        /// <summary>
        /// The name of the Parameter (the FromQuery Name for Query Parameters, otherwise the Method Parameter Name)
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// Where the Parameter is read from (Route, Query, or Body)
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// A description of the Parameter (from the Description of its FromQuery, FromRoute, or FromBody attribute)
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The name of the Parameter's data Type (ex. "String", "Int32", "String[]" for collections, or "Dictionary&lt;String,String&gt;" for dictionaries)
        /// </summary>
        [JsonPropertyName("dataType")]
        public string DataType { get; set; }

        /// <summary>
        /// True if the Parameter has no default value
        /// </summary>
        [JsonPropertyName("required")]
        public bool Required { get; set; }

        /// <summary>
        /// The default value of the Parameter as a string (null if Required or the default is null)
        /// </summary>
        [JsonPropertyName("defaultValue")]
        public string DefaultValue { get; set; }

        /// <summary>
        /// The Content Type the Request Body is sent as (Body Parameters only, ex. "application/json")
        /// </summary>
        [JsonPropertyName("contentType")]
        public string ContentType { get; set; }
    }
}
