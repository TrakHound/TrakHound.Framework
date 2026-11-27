// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Packages
{
    /// <summary>
    /// A Package found by a Package Server search across Organizations (GET packages/search), with the Organization it belongs to
    /// </summary>
    public class TrakHoundPackageSearchResultJson
    {
        [JsonPropertyName("organization")]
        public string Organization { get; set; }

        [JsonPropertyName("package")]
        public TrakHoundPackageJson Package { get; set; }
    }
}
