// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Instance.Pages.Explorer.Functions
{
    /// <summary>
    /// A named set of Parameters for a Function, from the TrakHound.Functions.Api "functions/{functionId}/jobs" routes
    /// </summary>
    public class FunctionJob
    {
        [JsonPropertyName("id")]
        public string Id { get; set; }

        [JsonPropertyName("functionId")]
        public string FunctionId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; }

        [JsonPropertyName("description")]
        public string Description { get; set; }

        [JsonPropertyName("parameters")]
        public Dictionary<string, string> Parameters { get; set; }

        [JsonPropertyName("updated")]
        public DateTime Updated { get; set; }


        /// <summary>
        /// The Parameters as "name=value" lines (for tooltips)
        /// </summary>
        [JsonIgnore]
        public string ParametersText => Parameters != null ? string.Join("\n", Parameters.Select(o => $"{o.Key}={o.Value}")) : null;
    }
}
