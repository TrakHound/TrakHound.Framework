// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace TrakHound.CLI
{
    /// <summary>
    /// CLI Configuration. Values are read (lowest to highest priority) from:
    /// 1. cli.config.yaml in the application directory
    /// 2. cli.config.yaml in the user directory (~/.trakhound/cli.config.yaml)
    /// 3. Environment Variables (TRAKHOUND_MANAGEMENT_SERVER, TRAKHOUND_ORGANIZATION, TRAKHOUND_API_TOKEN, TRAKHOUND_DOTNET_SDK)
    /// </summary>
    public class TrakHoundCliConfiguration
    {
        public const string Filename = "cli.config.yaml";
        public const string DefaultManagementServer = "https://packages.trakhound.com";
        public const string DefaultOrganization = "Public";

        public static TrakHoundCliConfiguration Instance { get; private set; } = new TrakHoundCliConfiguration();


        public string DotnetSdkLocation { get; set; }

        public string ManagementServer { get; set; }

        public string Organization { get; set; }

        public string ApiToken { get; set; }


        public static string ApplicationConfigurationPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, Filename);

        public static string UserConfigurationPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".trakhound", Filename);


        public static void Load()
        {
            var configuration = new TrakHoundCliConfiguration();
            configuration.Merge(Read(ApplicationConfigurationPath));
            configuration.Merge(Read(UserConfigurationPath));
            configuration.Merge(new TrakHoundCliConfiguration
            {
                ManagementServer = Environment.GetEnvironmentVariable("TRAKHOUND_MANAGEMENT_SERVER"),
                Organization = Environment.GetEnvironmentVariable("TRAKHOUND_ORGANIZATION"),
                ApiToken = Environment.GetEnvironmentVariable("TRAKHOUND_API_TOKEN"),
                DotnetSdkLocation = Environment.GetEnvironmentVariable("TRAKHOUND_DOTNET_SDK")
            });

            Instance = configuration;
        }

        private void Merge(TrakHoundCliConfiguration configuration)
        {
            if (configuration != null)
            {
                if (!string.IsNullOrEmpty(configuration.DotnetSdkLocation)) DotnetSdkLocation = configuration.DotnetSdkLocation;
                if (!string.IsNullOrEmpty(configuration.ManagementServer)) ManagementServer = configuration.ManagementServer;
                if (!string.IsNullOrEmpty(configuration.Organization)) Organization = configuration.Organization;
                if (!string.IsNullOrEmpty(configuration.ApiToken)) ApiToken = configuration.ApiToken;
            }
        }

        private static TrakHoundCliConfiguration Read(string path)
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

                        return deserializer.Deserialize<TrakHoundCliConfiguration>(text);
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"Warning : Error reading Configuration File ({path}) : {ex.Message}");
                }
            }

            return null;
        }
    }
}
