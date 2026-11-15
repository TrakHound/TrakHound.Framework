// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrakHound.Configurations;
using YamlDotNet.Serialization;

namespace TrakHound.Api
{
    /// <summary>
    /// The Configuration for an Api, which loads an Api Package at a Route
    /// </summary>
    public class TrakHoundApiConfiguration : ITrakHoundApiConfiguration
    {
        /// <summary>
        /// The Configuration Category used for Api Configurations ("api")
        /// </summary>
        public const string ConfigurationCategory = "api";


        /// <summary>
        /// A unique Identifier for the Api
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The display Name of the Api
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A description of the Api
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The Route to configure the base URL for the Api
        /// </summary>
        public string Route { get; set; }

        /// <summary>
        /// The ID of the Volume the Api uses for file storage (the Api ID is used when empty)
        /// </summary>
        public string VolumeId { get; set; }

        /// <summary>
        /// The ID of the Router the Api's Client uses to send Requests
        /// </summary>
        public string RouterId { get; set; }

        /// <summary>
        /// The ID of the Group the Api belongs to (the Api uses the Group's Router when RouterId is empty)
        /// </summary>
        public string GroupId { get; set; }

        /// <summary>
        /// The ID of the Package that contains the Api Controllers
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the Package to load (can contain '*' wildcards to match the latest installed Version)
        /// </summary>
        public string PackageVersion { get; set; }

        /// <summary>
        /// Whether the Api is served over HTTP (it is still available within the Instance when disabled). Defaults to true
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)] // Defaults to true, so false must be written
        public bool HttpEnabled { get; set; }

        /// <summary>
        /// Which Endpoints are served as Tools by the Instance's MCP Server (Off, Marked with ApiMcpToolAttribute, or All). Defaults to Marked
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)] // Defaults to Marked, so Off (the Type's default) must be written
        public TrakHoundApiMcpToolsMode McpTools { get; set; }

        /// <summary>
        /// Patterns (an optional Method list and a space, ex. "GET ", "GET,HEAD " or "STREAM " for Streams, then a Route pattern relative to the Api Route where '*' matches anything) of the Endpoints to serve as MCP Tools. All when empty
        /// </summary>
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitEmptyCollections | DefaultValuesHandling.OmitNull)]
        public List<string> McpInclude { get; set; }

        /// <summary>
        /// Patterns (an optional Method list and a space, ex. "GET ", "GET,HEAD " or "STREAM " for Streams, then a Route pattern relative to the Api Route where '*' matches anything) of the Endpoints not to serve as MCP Tools
        /// </summary>
        [YamlMember(DefaultValuesHandling = DefaultValuesHandling.OmitEmptyCollections | DefaultValuesHandling.OmitNull)]
        public List<string> McpExclude { get; set; }

        /// <summary>
        /// Only serve the MCP Tools that are read only (GET and Stream Endpoints, and Endpoints marked ApiMcpTool(ReadOnly = true)).
        /// Defaults to true, so Tools that change something are only served when it is set to false
        /// </summary>
        [JsonIgnore(Condition = JsonIgnoreCondition.Never)] // Defaults to true, so false must be written
        public bool McpReadOnly { get; set; }

        /// <summary>
        /// Custom Parameters that are passed to the Api Controllers
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }

        /// <summary>
        /// The Configuration Category (always "api")
        /// </summary>
        [YamlIgnore]
        public string Category => ConfigurationCategory;

        /// <summary>
        /// The path of the file the Configuration was loaded from
        /// </summary>
        [YamlIgnore]
        public string Path { get; set; }

        /// <summary>
        /// A SHA256 hash of the Configuration properties, used to detect changes
        /// </summary>
        [YamlIgnore]
        public string Hash => GenerateHash();


        /// <summary>
        /// Creates a new Configuration with an empty set of Parameters
        /// </summary>
        public TrakHoundApiConfiguration()
        {
            HttpEnabled = true;
            McpReadOnly = true;
            McpTools = TrakHoundApiMcpToolsMode.Marked;
            Parameters = new Dictionary<string, object>();
        }

        /// <summary>
        /// Creates a new Configuration by copying the properties of an existing Configuration
        /// </summary>
        public TrakHoundApiConfiguration(ITrakHoundApiConfiguration configuration)
        {
            Parameters = new Dictionary<string, object>();
            HttpEnabled = true;
            McpReadOnly = true;
            McpTools = TrakHoundApiMcpToolsMode.Marked;

            if (configuration != null)
            {
                Id = configuration.Id;
                Path = configuration.Path;
                Name = configuration.Name;
                Description = configuration.Description;
                Route = configuration.Route;
                VolumeId = configuration.VolumeId;
                RouterId = configuration.RouterId;
                GroupId = configuration.GroupId;
                PackageId = configuration.PackageId;
                PackageVersion = configuration.PackageVersion;
                HttpEnabled = configuration.HttpEnabled;
                McpTools = configuration.McpTools;
                McpInclude = configuration.McpInclude?.ToList();
                McpExclude = configuration.McpExclude?.ToList();
                McpReadOnly = configuration.McpReadOnly;
                Parameters = configuration.Parameters;
            }
        }


        private string GenerateHash()
        {
            return $"{Id}:{Name}:{Description}:{Route}:{VolumeId}:{RouterId}:{GroupId}:{PackageId}:{PackageVersion}:{HttpEnabled}:{McpTools}:{McpInclude?.ToJson()}:{McpExclude?.ToJson()}:{McpReadOnly}:{Parameters?.ToJson()}".ToSHA256Hash();
        }


        /// <summary>
        /// Gets the value of the specified Parameter as a string (Secret references are resolved). Returns null if not found
        /// </summary>
        public string GetParameter(string name)
        {
            Parameters.TryGetValue(name, out var obj);
            obj = TrakHoundSecrets.Resolve(obj, ConfigurationCategory, Id, name);
            if (obj != null) return obj.ToString();
            return null;
        }

        /// <summary>
        /// Gets the value of the specified Parameter converted to T (Secret references are resolved). Returns default if not found or not convertible
        /// </summary>
        public T GetParameter<T>(string name)
        {
            if (!string.IsNullOrEmpty(name))
            {
                Parameters.TryGetValue(name, out var obj);
                obj = TrakHoundSecrets.Resolve(obj, ConfigurationCategory, Id, name);
                if (obj != null)
                {
                    var x = obj;
                    if (obj.GetType() == typeof(JsonElement) && ((JsonElement)x).ValueKind == JsonValueKind.Number) x = x.ToInt();


                    try
                    {
                        if (typeof(T).IsAssignableFrom(obj.GetType()))
                        {
                            return (T)obj;
                        }
                        else
                        {
                            return (T)Convert.ChangeType(obj, typeof(T));
                        }
                    }
                    catch { }
                }
            }

            return default(T);
        }

        /// <summary>
        /// Sets (or replaces) the value of the specified Parameter. Null values are ignored
        /// </summary>
        public void SetParameter(string name, object value)
        {
            if (!string.IsNullOrEmpty(name) && value != null)
            {
                Parameters.Remove(name);
                Parameters.Add(name, value);
            }
        }
    }
}
