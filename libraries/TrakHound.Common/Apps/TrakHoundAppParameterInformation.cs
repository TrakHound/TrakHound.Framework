// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Apps
{
    /// <summary>
    /// Information about a Parameter accepted by an App Page
    /// </summary>
    public class TrakHoundAppPageParameterInformation
    {
        /// <summary>
        /// The Name of the Parameter (the Page property's Name)
        /// </summary>
        [JsonPropertyName("name")]
        public string Name { get; set; }

        /// <summary>
        /// The kind of Parameter (not currently set when Page Information is created)
        /// </summary>
        [JsonPropertyName("type")]
        public string Type { get; set; }

        /// <summary>
        /// The .NET Type name of the Parameter's value
        /// </summary>
        [JsonPropertyName("dataType")]
        public string DataType { get; set; }

        /// <summary>
        /// True if the Parameter must be specified
        /// </summary>
        [JsonPropertyName("required")]
        public bool Required { get; set; }

        /// <summary>
        /// A description of the Parameter
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }
    }
}
