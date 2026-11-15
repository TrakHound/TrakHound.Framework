// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json;
using TrakHound.Configurations;
using YamlDotNet.Serialization;

namespace TrakHound.Functions
{
    /// <summary>
    /// The Configuration of a Function within an Instance
    /// </summary>
    public class TrakHoundFunctionConfiguration : ITrakHoundFunctionConfiguration
    {
        /// <summary>
        /// The Category used for Function Configurations ("function")
        /// </summary>
        public const string ConfigurationCategory = "function";


        /// <summary>
        /// The unique Identifier of the Configuration
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The Name of the Function (defaults to the Package name if empty)
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A description of the Function (defaults to the Package description if empty)
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The Id of the Function used when Running it (ex. through the Functions Api)
        /// </summary>
        public string FunctionId { get; set; }

        /// <summary>
        /// The Id of the Volume used to store Files for the Function
        /// </summary>
        public string VolumeId { get; set; }

        /// <summary>
        /// The Id of the Router the Function's Client sends Requests through
        /// </summary>
        public string RouterId { get; set; }

        /// <summary>
        /// The ID of the Group the Function belongs to (the Function uses the Group's Router when RouterId is empty)
        /// </summary>
        public string GroupId { get; set; }

        /// <summary>
        /// The Id of the Package that contains the Function
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the Package to load (latest if empty)
        /// </summary>
        public string PackageVersion { get; set; }

        /// <summary>
        /// The Parameters passed to the Function (values may reference Secrets)
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }

        /// <summary>
        /// The Configuration Category (always "function")
        /// </summary>
        [YamlIgnore]
        public string Category => ConfigurationCategory;

        /// <summary>
        /// The path of the file the Configuration was read from
        /// </summary>
        [YamlIgnore]
        public string Path { get; set; }

        /// <summary>
        /// A SHA256 Hash of the Configuration's values, used to detect changes
        /// </summary>
        [YamlIgnore]
        public string Hash => GenerateHash();


        /// <summary>
        /// Create a new empty Configuration
        /// </summary>
        public TrakHoundFunctionConfiguration()
        {
            Parameters = new Dictionary<string, object>();
        }

        /// <summary>
        /// Create a new Configuration copied from the specified Configuration (the Parameters dictionary is shared, not copied)
        /// </summary>
        public TrakHoundFunctionConfiguration(ITrakHoundFunctionConfiguration configuration)
        {
            Parameters = new Dictionary<string, object>();

            if (configuration != null)
            {
                Id = configuration.Id;
                Path = configuration.Path;
                Name = configuration.Name;
                Description = configuration.Description;
                FunctionId = configuration.FunctionId;
                VolumeId = configuration.VolumeId;
                RouterId = configuration.RouterId;
                GroupId = configuration.GroupId;
                PackageId = configuration.PackageId;
                PackageVersion = configuration.PackageVersion;
                Parameters = configuration.Parameters;
            }
        }


        private string GenerateHash()
        {
            return $"{Id}:{Name}:{Description}:{FunctionId}:{VolumeId}:{RouterId}:{GroupId}:{PackageId}:{PackageVersion}:{Parameters?.ToJson()}".ToSHA256Hash();
        }


        /// <summary>
        /// Gets the value of the Parameter as a string (Secrets are resolved). Returns null if not found
        /// </summary>
        public string GetParameter(string name)
        {
            Parameters.TryGetValue(name, out var obj);
            obj = TrakHoundSecrets.Resolve(obj, ConfigurationCategory, Id, name);
            if (obj != null) return obj.ToString();
            return null;
        }

        /// <summary>
        /// Gets the value of the Parameter converted to T (Secrets are resolved). Returns default if not found or not convertible
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
        /// Sets the value of the Parameter, replacing any existing value (ignored if the name is empty or the value is null)
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
