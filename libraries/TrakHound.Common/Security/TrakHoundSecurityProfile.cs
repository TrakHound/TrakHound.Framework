// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.Security
{
    /// <summary>
    /// A Security Profile links Resources (through its Assignments) to the Identity Provider used to Authenticate them
    /// </summary>
    public class TrakHoundSecurityProfile
    {
        /// <summary>
        /// The unique Identifier of the Profile (also used as the Configuration filename)
        /// </summary>
        [JsonPropertyName("id")]
        public string Id { get; set; }

        /// <summary>
        /// A description of the Profile
        /// </summary>
        [JsonPropertyName("description")]
        public string Description { get; set; }

        /// <summary>
        /// The Priority of the Profile. When multiple Profiles match a Resource, higher Priority Profiles are tried first
        /// </summary>
        [JsonPropertyName("priority")]
        public int Priority { get; set; }

        /// <summary>
        /// The Id of the Identity Provider used to Authenticate Requests (if empty, a Success Response is returned without a Session)
        /// </summary>
        [JsonPropertyName("providerId")]
        public string ProviderId { get; set; }

        /// <summary>
        /// The Assignments that map Roles to Permissions on Resources
        /// </summary>
        [JsonPropertyName("assignments")]
        public IEnumerable<TrakHoundIdentityAssignment> Assignments { get; set; }


        /// <summary>
        /// Read a Profile from the specified YAML file. Returns null if the file can't be read
        /// </summary>
        public static TrakHoundSecurityProfile Read(string configurationPath)
        {
            if (!string.IsNullOrEmpty(configurationPath))
            {
                try
                {
                    var text = System.IO.File.ReadAllText(configurationPath);
                    if (!string.IsNullOrEmpty(text))
                    {
                        var deserializer = new DeserializerBuilder()
                            .WithNamingConvention(CamelCaseNamingConvention.Instance)
                            .IgnoreUnmatchedProperties()
                            .Build();

                        return deserializer.Deserialize<TrakHoundSecurityProfile>(text);
                    }
                }
                catch { }
            }

            return default;
        }

        /// <summary>
        /// Save the Profile as YAML to "security/{Id}.config.yaml" under the specified path (defaults to the "config" directory in the application's base directory)
        /// </summary>
        public async Task Save(string path = null)
        {
            var basePath = path;
            if (basePath.IsNullOrEmpty()) basePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config");

            var configurationPath = Path.Combine(basePath, "security", $"{Id}.config.yaml");
            var dirPath = Path.GetDirectoryName(configurationPath);        

            try
            {
                if (!Directory.Exists(dirPath)) Directory.CreateDirectory(dirPath);

                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                var yaml = serializer.Serialize(this);
                await System.IO.File.WriteAllTextAsync(configurationPath, yaml);
            }
            catch { }
        }
    }
}
