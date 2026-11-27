// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.Instance.Configurations
{
    public class RemotesConfiguration
    {
        public const string Filename = "remotes.config.yaml";
        private string _path;

        public IEnumerable<RemoteConfiguration> Remotes { get; set; }


        public static RemotesConfiguration Read(string path = null)
        {
            var configurationPath = path;
            if (string.IsNullOrEmpty(configurationPath)) configurationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Filename);

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

                        var configuration = deserializer.Deserialize<RemotesConfiguration>(text);
                        configuration._path = configurationPath;
                        return configuration;
                    }
                }
                catch { }
            }

            return default;
        }

        public void Save(string path = null)
        {
            Save(this, path);
        }

        public static void Save(RemotesConfiguration configuration, string path = null)
        {
            try
            {
                Write(configuration, path);
            }
            catch { }
        }

        /// <summary>
        /// Write the Configuration File (throws if it can't be written)
        /// </summary>
        public static void Write(RemotesConfiguration configuration, string path = null)
        {
            if (configuration == null) return;

            var configurationPath = configuration._path;
            if (string.IsNullOrEmpty(configurationPath)) configurationPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config", Filename);
            if (path != null) configurationPath = path;

            if (!string.IsNullOrEmpty(configurationPath))
            {
                var serializer = new SerializerBuilder()
                    .WithNamingConvention(CamelCaseNamingConvention.Instance)
                    .Build();
                var yaml = serializer.Serialize(configuration);
                File.WriteAllText(configurationPath, yaml);
            }
        }

        public static RemotesConfiguration GetDefault()
        {
            var configuration = new RemotesConfiguration();

            var remoteConfiguration = new RemoteConfiguration();
            remoteConfiguration.Id = "public";
            remoteConfiguration.Name = "TrakHound Public";
            remoteConfiguration.BaseUrl = "https://packages.trakhound.com";
            remoteConfiguration.Organization = "TrakHound";
            configuration.Remotes = new RemoteConfiguration[] { remoteConfiguration };

            return configuration;
        }
    }
}
