// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;
using YamlDotNet.Serialization;

namespace TrakHound
{
    public class TrakHoundInstanceDirectoryConfiguration
    {
        [JsonPropertyName("config")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string Config { get; set; }

        [JsonPropertyName("packages")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string Packages { get; set; }

        [JsonPropertyName("logs")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string Logs { get; set; }

        [JsonPropertyName("volumes")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string Volumes { get; set; }

        [JsonPropertyName("modules")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string Modules { get; set; }

        [JsonPropertyName("temp")]
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
        public string Temp { get; set; }
    }
}
