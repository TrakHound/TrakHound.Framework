// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json;
using TrakHound.Configurations;
using YamlDotNet.Serialization;

namespace TrakHound.Services
{
    /// <summary>
    /// A Configuration used to create a Service from a Package
    /// </summary>
    public class TrakHoundServiceConfiguration : ITrakHoundServiceConfiguration
    {
        /// <summary>
        /// The Category of Service Configurations ("service")
        /// </summary>
        public const string ConfigurationCategory = "service";


        /// <summary>
        /// A unique Identifier for the Configuration
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// A friendly name for the Service
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A description of the Service
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The ID of the Volume used by the Service (the Configuration ID is used when null)
        /// </summary>
        public string VolumeId { get; set; }

        /// <summary>
        /// The ID of the Package that contains the Service
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// The version of the Package that contains the Service
        /// </summary>
        public string PackageVersion { get; set; }

        /// <summary>
        /// Service specific Parameters (ex. connection settings)
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }


        /// <summary>
        /// The Category of the Configuration (always "service")
        /// </summary>
        [YamlIgnore]
        public string Category => ConfigurationCategory;

        /// <summary>
        /// The path of the file the Configuration was read from
        /// </summary>
        [YamlIgnore]
        public string Path { get; set; }

        /// <summary>
        /// A hash of the Configuration's properties, used to detect changes
        /// </summary>
        [YamlIgnore]
        public string Hash => GenerateHash();



        private string GenerateHash()
        {
            return $"{Id}:{Name}:{Description}:{PackageId}:{PackageVersion}:{Parameters?.ToJson()}".ToSHA256Hash();
        }


        /// <summary>
        /// Returns true if a Parameter with the specified name exists
        /// </summary>
        public bool ParameterExists(string name)
        {
            return !Parameters.IsNullOrEmpty() && Parameters.ContainsKey(name);
        }

        /// <summary>
        /// Gets the value of the specified Parameter as a string, resolving Secrets (null if not found)
        /// </summary>
        public string GetParameter(string name)
        {
            if (!Parameters.IsNullOrEmpty())
            {
                Parameters.TryGetValue(name, out var obj);
                obj = TrakHoundSecrets.Resolve(obj, ConfigurationCategory, Id, name);
                if (obj != null) return obj.ToString();
            }

            return null;
        }

        /// <summary>
        /// Gets the value of the specified Parameter converted to T, resolving Secrets (default if not found or can't be converted)
        /// </summary>
        public T GetParameter<T>(string name)
        {
            if (!Parameters.IsNullOrEmpty() && !string.IsNullOrEmpty(name))
            {
                Parameters.TryGetValue(name, out var obj);
                obj = TrakHoundSecrets.Resolve(obj, ConfigurationCategory, Id, name);
                if (obj != null)
                {
                    var x = obj;
                    if (obj.GetType() == typeof(JsonElement) && ((JsonElement)x).ValueKind == JsonValueKind.Number) x = x.ToInt();


                    try
                    {
                        return (T)Convert.ChangeType(obj, typeof(T));
                    }
                    catch { }
                }
            }

            return default(T);
        }

        /// <summary>
        /// Sets (or replaces) the specified Parameter. Values are stored as strings and null values are ignored
        /// </summary>
        public void SetParameter(string name, object value)
        {
            if (!string.IsNullOrEmpty(name) && value != null)
            {
                if (Parameters == null) Parameters = new Dictionary<string, object>();
                Parameters.Remove(name);
                Parameters.Add(name, value.ToString());
            }
        }
    }
}
