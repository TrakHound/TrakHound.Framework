// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;

namespace TrakHound.Instance
{
    /// <summary>
    /// The Instance's default command : reads where the Configuration, Packages and Volumes are, then runs the Instance (see TrakHoundInstanceHost)
    /// </summary>
    [CommandGroup]
    public class InstanceApplication
    {
        private const string _configurationPathEnvironmentVariable = "TRAKHOUND_CONFIGURATION_PATH";
        private const string _configurationProfileEnvironmentVariable = "TRAKHOUND_CONFIGURATION_PROFILE";
        private const string _packagePathEnvironmentVariable = "TRAKHOUND_PACKAGE_PATH";
        private const string _volumePathEnvironmentVariable = "TRAKHOUND_VOLUME_PATH";


        [Command]
        public static async Task Run(
            [CommandParameter] string configurationProfileId = TrakHoundConfigurationProfile.Default,
            [CommandOption] string configurationPath = null,
            [CommandOption] string packagePath = null,
            [CommandOption] string volumePath = null,
            [CommandOption] string deploymentPath = null
            )
        {
            TrakHoundTemp.Clear();

            // Read Parameters (Environment Variables take priority over Command Line Options)
            var configurationProfileSource = ReadEnvironmentVariable(_configurationProfileEnvironmentVariable, ref configurationProfileId);
            var configurationPathSource = ReadEnvironmentVariable(_configurationPathEnvironmentVariable, ref configurationPath);
            var packagePathSource = ReadEnvironmentVariable(_packagePathEnvironmentVariable, ref packagePath);
            var volumePathSource = ReadEnvironmentVariable(_volumePathEnvironmentVariable, ref volumePath);
            var deploymentPathSource = ReadEnvironmentVariable(TrakHoundStartupDeployments.EnvironmentVariable, ref deploymentPath);

            // Set default Paths
            if (string.IsNullOrEmpty(configurationPath)) configurationPath = Path.Combine(AppContext.BaseDirectory, "config");
            if (string.IsNullOrEmpty(packagePath)) packagePath = Path.Combine(AppContext.BaseDirectory, "packages");
            if (string.IsNullOrEmpty(volumePath)) volumePath = Path.Combine(AppContext.BaseDirectory, "volumes");
            var startupDeployments = new TrakHoundStartupDeployments(deploymentPath, configurationPath);

            // Print Parameters
            ConsoleOutput.WriteProperty("Configuration Profile", configurationProfileId, configurationProfileSource);
            ConsoleOutput.WriteProperty("Configuration Path", Path.Combine(configurationPath, configurationProfileId), configurationPathSource);
            ConsoleOutput.WriteProperty("Package Path", packagePath, packagePathSource);
            ConsoleOutput.WriteProperty("Volume Path", volumePath, volumePathSource);
            ConsoleOutput.WriteProperty("Deployment Path", startupDeployments.SourcePath, deploymentPathSource);
            var deviceId = Licenses.TrakHoundDeviceId.GetInformation();
            ConsoleOutput.WriteProperty("Device ID", deviceId.DeviceId, deviceId.Source == Licenses.TrakHoundDeviceIdSource.Variable ? $"from {Licenses.TrakHoundDeviceId.Variable}" : "from the machine name");
            ConsoleOutput.WriteDivider();
            Console.WriteLine();

            await new TrakHoundInstanceHost(configurationProfileId, configurationPath, packagePath, volumePath, startupDeployments).Run();
        }

        /// <summary>
        /// Read an Environment Variable into the specified value (if set)
        /// </summary>
        /// <returns>A description of the source (for display) if the Environment Variable was set, otherwise null</returns>
        private static string ReadEnvironmentVariable(string name, ref string value)
        {
            var environmentVariable = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(environmentVariable))
            {
                value = environmentVariable;
                return $"from {name}";
            }

            return null;
        }
    }
}
