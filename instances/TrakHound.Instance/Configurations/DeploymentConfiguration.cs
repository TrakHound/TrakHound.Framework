// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.Instance.Configurations
{
    /// <summary>
    /// Deployment creation settings and history ("deployment.config.yaml" in the Configuration directory)
    /// </summary>
    public class DeploymentConfiguration
    {
        public const string Filename = "deployment.config.yaml";
        private const int _maxHistory = 20;


        public string ProfileId { get; set; }

        /// <summary>
        /// The version of the last Deployment created (ex. "1.4.0"; earlier versions stored a number)
        /// </summary>
        public string Version { get; set; }

        public string Description { get; set; }

        /// <summary>
        /// When the last Deployment was created (Unix nanoseconds). Incremental Deployments include files changed since.
        /// </summary>
        public long LastUpdated { get; set; }

        public List<DeploymentHistoryItem> History { get; set; } = new List<DeploymentHistoryItem>();


        /// <summary>
        /// The next version (the last part of the previous version incremented, ex. "1.4.0" => "1.4.1", "7" => "8")
        /// </summary>
        public string GetNextVersion()
        {
            if (string.IsNullOrWhiteSpace(Version)) return "1.0.0";

            var parts = Version.Trim().Split('.');
            if (int.TryParse(parts[^1], out var last))
            {
                parts[^1] = (last + 1).ToString();
                return string.Join('.', parts);
            }

            return Version.Trim() + ".1";
        }

        public void AddHistory(DeploymentHistoryItem item)
        {
            History ??= new List<DeploymentHistoryItem>();
            History.Insert(0, item);
            if (History.Count > _maxHistory) History.RemoveRange(_maxHistory, History.Count - _maxHistory);
        }


        public static DeploymentConfiguration Read(string path)
        {
            if (!string.IsNullOrEmpty(path) && File.Exists(path))
            {
                try
                {
                    var text = File.ReadAllText(path);
                    if (!string.IsNullOrEmpty(text))
                    {
                        var deserializer = new DeserializerBuilder()
                            .WithNamingConvention(CamelCaseNamingConvention.Instance)
                            .IgnoreUnmatchedProperties()
                            .Build();

                        var configuration = deserializer.Deserialize<DeploymentConfiguration>(text);
                        if (configuration != null) configuration.History ??= new List<DeploymentHistoryItem>();
                        return configuration;
                    }
                }
                catch { }
            }

            return null;
        }

        /// <summary>
        /// Write the file (throws if it can't be written)
        /// </summary>
        public void Save(string path)
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory)) Directory.CreateDirectory(directory);

            var serializer = new SerializerBuilder()
                .WithNamingConvention(CamelCaseNamingConvention.Instance)
                .Build();

            File.WriteAllText(path, serializer.Serialize(this));
        }
    }

    public class DeploymentHistoryItem
    {
        public string ProfileId { get; set; }
        public string Version { get; set; }
        public string Description { get; set; }
        public DateTime Created { get; set; }
        public bool Incremental { get; set; }
        public bool SecretsIncluded { get; set; }
        public List<string> Profiles { get; set; } = new List<string>();

        /// <summary>
        /// The Configuration Categories included (null for all)
        /// </summary>
        public List<string> Categories { get; set; }

        public int ConfigurationFiles { get; set; }
        public int Packages { get; set; }
        public int VolumeFiles { get; set; }
        public int Variables { get; set; }
        public long Size { get; set; }
        public string Filename { get; set; }

        /// <summary>
        /// Where the created file was written (it is deleted an hour after it is created)
        /// </summary>
        public string FilePath { get; set; }
    }
}
