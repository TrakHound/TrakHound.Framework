// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json;
using TrakHound.Configurations;
using YamlDotNet.Serialization;

namespace TrakHound.Workers
{
    /// <summary>
    /// The Configuration for a Worker (stored as YAML in the "worker" Configuration Category)
    /// </summary>
    public class TrakHoundWorkerConfiguration : ITrakHoundWorkerConfiguration
    {
        /// <summary>
        /// The Configuration Category for Worker Configurations
        /// </summary>
        public const string ConfigurationCategory = "worker";


        /// <inheritdoc/>
        public string Id { get; set; }

        /// <inheritdoc/>
        public string Name { get; set; }

        /// <inheritdoc/>
        public string Description { get; set; }

        /// <inheritdoc/>
        public string VolumeId { get; set; }

        /// <inheritdoc/>
        public string RouterId { get; set; }

        /// <inheritdoc/>
        public string GroupId { get; set; }

        /// <inheritdoc/>
        public string PackageId { get; set; }

        /// <inheritdoc/>
        public string PackageVersion { get; set; }

        /// <inheritdoc/>
        public Dictionary<string, object> Parameters { get; set; }

        /// <inheritdoc/>
        [YamlIgnore]
        public string Category => ConfigurationCategory;

        /// <inheritdoc/>
        [YamlIgnore]
        public string Path { get; set; }

        /// <summary>
        /// A SHA256 hash of the Configuration's properties and Parameters (used to detect when it has changed)
        /// </summary>
        [YamlIgnore]
        public string Hash => GenerateHash();


        /// <summary>
        /// Creates a new Worker Configuration with no Parameters
        /// </summary>
        public TrakHoundWorkerConfiguration()
        {
            Parameters = new Dictionary<string, object>();
        }

        /// <summary>
        /// Creates a copy of the Worker Configuration (the Parameters dictionary is shared, not copied)
        /// </summary>
        public TrakHoundWorkerConfiguration(ITrakHoundWorkerConfiguration configuration)
        {
            Parameters = new Dictionary<string, object>();

            if (configuration != null)
            {
                Id = configuration.Id;
                Path = configuration.Path;
                Name = configuration.Name;
                Description = configuration.Description;
                PackageId = configuration.PackageId;
                PackageVersion = configuration.PackageVersion;
                VolumeId = configuration.VolumeId;
                RouterId = configuration.RouterId;
                GroupId = configuration.GroupId;
                Parameters = configuration.Parameters;
            }
        }


        private string GenerateHash()
        {
            return $"{Id}:{Name}:{Description}:{VolumeId}:{RouterId}:{GroupId}:{PackageId}:{PackageVersion}:{Parameters?.ToJson()}".ToSHA256Hash();
        }


        /// <inheritdoc/>
        public string GetParameter(string name)
        {
            Parameters.TryGetValue(name, out var obj);
            obj = TrakHoundSecrets.Resolve(obj, ConfigurationCategory, Id, name);
            if (obj != null) return obj.ToString();
            return null;
        }

        /// <inheritdoc/>
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
        /// Sets (adds or replaces) a Parameter value. Ignored if the name is empty or the value is null
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
