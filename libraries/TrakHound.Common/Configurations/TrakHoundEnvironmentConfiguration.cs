// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.IO;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.Configurations
{
    /// <summary>
    /// The local Environment Configuration (read from a YAML file in the application's base directory) that specifies which Instance to connect to
    /// </summary>
    public class TrakHoundEnvironmentConfiguration
    {
        /// <summary>
        /// The filename of the Environment Configuration file
        /// </summary>
        public const string Filename = "trakhound-environment.config.yaml";
        private static TrakHoundEnvironmentConfiguration _configuration;


        /// <summary>
        /// The connection settings for the Instance
        /// </summary>
        public TrakHoundEnvironmentInstanceConfiguration Instance { get; set; }


        /// <summary>
        /// Gets the Environment Configuration (cached after the first call). If the file can't be read, a default Configuration is created and saved
        /// </summary>
        public static TrakHoundEnvironmentConfiguration Get()
        {
            if (_configuration == null) _configuration = Read();
            if (_configuration == null)
            {
                var configuration = new TrakHoundEnvironmentConfiguration();
                configuration.Instance = TrakHoundEnvironmentInstanceConfiguration.GetDefault();
                Save(configuration);
                _configuration = configuration;
            }
            return _configuration;
        }

        private static TrakHoundEnvironmentConfiguration Read()
        {
            var configurationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Filename);
            if (!string.IsNullOrEmpty(configurationPath))
            {
                try
                {
                    var text = File.ReadAllText(configurationPath);
                    if (!string.IsNullOrEmpty(text))
                    {
                        var deserializer = new DeserializerBuilder()
                            .WithNamingConvention(CamelCaseNamingConvention.Instance)
                            .IgnoreUnmatchedProperties()
                            .Build();

                        return deserializer.Deserialize<TrakHoundEnvironmentConfiguration>(text);
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Saves the Configuration as YAML to the specified path (or the default file in the application's base directory). Errors are ignored
        /// </summary>
        public static void Save(TrakHoundEnvironmentConfiguration configuration, string configurationPath = null)
        {
            var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Filename);
            if (!string.IsNullOrEmpty(configurationPath)) path = configurationPath;

            if (configuration != null && !string.IsNullOrEmpty(path))
            {
                try
                {
                    var serializer = new SerializerBuilder()
                        .WithNamingConvention(CamelCaseNamingConvention.Instance)
                        .Build();
                    var yaml = serializer.Serialize(configuration);
                    File.WriteAllText(path, yaml);
                }
                catch { }
            }
        }
    }
}
