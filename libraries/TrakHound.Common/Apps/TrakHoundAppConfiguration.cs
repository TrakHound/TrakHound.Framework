// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json;
using TrakHound.Configurations;
using YamlDotNet.Serialization;

namespace TrakHound.Apps
{
    /// <summary>
    /// The Configuration for an App (an App Package hosted at a Route on the Instance)
    /// </summary>
    public class TrakHoundAppConfiguration : ITrakHoundAppConfiguration
    {
        /// <summary>
        /// The Configuration Category used for App Configurations ("app")
        /// </summary>
        public const string ConfigurationCategory = "app";


        /// <summary>
        /// A unique Identifier for the App
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The display Name of the App
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// A description of the App
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The base Route (URL path) that the App's Pages are hosted at
        /// </summary>
        public string Route { get; set; }

        /// <summary>
        /// The ID of the Volume used by the App (if null, the Configuration Id is used as the Volume ID)
        /// </summary>
        public string VolumeId { get; set; }

        /// <summary>
        /// The ID of the Router used by the App's Client to send Requests
        /// </summary>
        public string RouterId { get; set; }

        /// <summary>
        /// The ID of the Group the App belongs to (the App uses the Group's Router when RouterId is empty)
        /// </summary>
        public string GroupId { get; set; }

        /// <summary>
        /// The ID of the App Package to run
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the App Package to run (supports '*' wildcards to use the latest matching installed Version)
        /// </summary>
        public string PackageVersion { get; set; }

        /// <summary>
        /// Custom Parameters passed to the App (keyed by Parameter Name)
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }

        /// <summary>
        /// The Configuration Category (always "app")
        /// </summary>
        [YamlIgnore]
        public string Category => ConfigurationCategory;

        /// <summary>
        /// The file Path that the Configuration was loaded from or is saved to
        /// </summary>
        [YamlIgnore]
        public string Path { get; set; }

        /// <summary>
        /// A SHA256 Hash of the Configuration's properties and Parameters used to detect changes
        /// </summary>
        [YamlIgnore]
        public string Hash => GenerateHash();


        private string GenerateHash()
        {
            return $"{Id}:{Name}:{Description}:{Route}:{VolumeId}:{RouterId}:{GroupId}:{PackageId}:{PackageVersion}:{Parameters?.ToJson()}".ToSHA256Hash();
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
        /// Gets the value of the specified Parameter converted to <typeparamref name="T"/> (Secret references are resolved). Returns default if not found or not convertible
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
        /// Sets (adds or replaces) the value of the specified Parameter. Null values are ignored
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
