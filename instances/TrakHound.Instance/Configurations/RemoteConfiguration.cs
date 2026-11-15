// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Instance.Configurations
{
    public class RemoteConfiguration
    {
        public string Id { get; set; }

        public string Name { get; set; }

        public string BaseUrl { get; set; }

        public string Organization { get; set; }

        public string AccessTokenName { get; set; }

        public string AccessTokenCode { get; set; }

        /// <summary>
        /// The Source is a TrakHound Package Manager: the Instance reports its installed Packages to it and trusts the publishers it verified
        /// </summary>
        [JsonPropertyName("packageManager")]
        public bool PackageManager { get; set; }
    }
}
