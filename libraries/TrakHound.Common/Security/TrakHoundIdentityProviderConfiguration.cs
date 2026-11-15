// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Text.Json;
using TrakHound.Configurations;
using YamlDotNet.Serialization;

namespace TrakHound.Security
{
    /// <summary>
    /// The Configuration of an Identity Provider within an Instance
    /// </summary>
    public class TrakHoundIdentityProviderConfiguration : ITrakHoundIdentityProviderConfiguration
    {
        /// <summary>
        /// The Category used for Identity Provider Configurations ("identity")
        /// </summary>
        public const string ConfigurationCategory = "identity";


        /// <summary>
        /// A unique Identifier for the Identity Provider
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// A description of the Identity Provider
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// The Id of the Router the Identity Provider's Client sends Requests through
        /// </summary>
        public string RouterId { get; set; }

        /// <summary>
        /// The Id of the Volume used to store Files for the Identity Provider
        /// </summary>
        public string VolumeId { get; set; }

        /// <summary>
        /// The Id of the Package that contains the Identity Provider
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the Package to load
        /// </summary>
        public string PackageVersion { get; set; }

        /// <summary>
        /// The Parameters passed to the Identity Provider (values may reference Secrets)
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }

        /// <summary>
        /// The Configuration Category (always "identity")
        /// </summary>
        [YamlIgnore]
        public string Category => ConfigurationCategory;

        /// <summary>
        /// The path of the file the Configuration was read from
        /// </summary>
        [YamlIgnore]
        public string Path { get; set; }

        /// <summary>
        /// A SHA256 Hash of the Configuration properties and Parameters, used to detect changes
        /// </summary>
        [YamlIgnore]
        public string Hash => GenerateHash();


        private string GenerateHash()
        {
            return $"{Id}:{Description}:{VolumeId}:{RouterId}:{PackageId}:{PackageVersion}:{Parameters?.ToJson()}".ToSHA256Hash();
        }


        /// <summary>
        /// Returns true if a Parameter with the specified name exists
        /// </summary>
        public bool ParameterExists(string name)
        {
            return !Parameters.IsNullOrEmpty() && Parameters.ContainsKey(name);
        }

        /// <summary>
        /// Gets the value of the Parameter as a string (Secrets are resolved). Returns null if not found
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
        /// Gets the value of the Parameter converted to T (Secrets are resolved). Returns default if not found or not convertible
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
        /// Sets the value of the Parameter (stored as a string), replacing any existing value (ignored if the name is empty or the value is null)
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
