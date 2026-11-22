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
        private const string _variablePathEnvironmentVariable = "TRAKHOUND_VARIABLE_PATH";
        private const string _variableDirectoryName = "variables";


        [Command]
        public static async Task Run(
            [CommandParameter] string configurationProfileId = TrakHoundConfigurationProfile.Default,
            [CommandOption] string configurationPath = null,
            [CommandOption] string packagePath = null,
            [CommandOption] string volumePath = null,
            [CommandOption] string variablePath = null,
            [CommandOption] string deploymentPath = null
            )
        {
            TrakHoundTemp.Clear();

            // Read Parameters (Environment Variables take priority over Command Line Options)
            var configurationProfileSource = ReadEnvironmentVariable(_configurationProfileEnvironmentVariable, ref configurationProfileId);
            var configurationPathSource = ReadEnvironmentVariable(_configurationPathEnvironmentVariable, ref configurationPath);
            var packagePathSource = ReadEnvironmentVariable(_packagePathEnvironmentVariable, ref packagePath);
            var volumePathSource = ReadEnvironmentVariable(_volumePathEnvironmentVariable, ref volumePath);
            var variablePathSource = ReadEnvironmentVariable(_variablePathEnvironmentVariable, ref variablePath);
            var deploymentPathSource = ReadEnvironmentVariable(TrakHoundStartupDeployments.EnvironmentVariable, ref deploymentPath);

            // Set default Paths
            if (string.IsNullOrEmpty(configurationPath)) configurationPath = Path.Combine(AppContext.BaseDirectory, "config");
            if (string.IsNullOrEmpty(packagePath)) packagePath = Path.Combine(AppContext.BaseDirectory, "packages");
            if (string.IsNullOrEmpty(volumePath)) volumePath = Path.Combine(AppContext.BaseDirectory, "volumes");
            if (string.IsNullOrEmpty(variablePath)) variablePath = GetDefaultVariablePath(configurationPath);
            var startupDeployments = new TrakHoundStartupDeployments(deploymentPath, configurationPath);

            // Print Parameters
            ConsoleOutput.WriteProperty("Configuration Profile", configurationProfileId, configurationProfileSource);
            ConsoleOutput.WriteProperty("Configuration Path", Path.Combine(configurationPath, configurationProfileId), configurationPathSource);
            ConsoleOutput.WriteProperty("Package Path", packagePath, packagePathSource);
            ConsoleOutput.WriteProperty("Volume Path", volumePath, volumePathSource);
            ConsoleOutput.WriteProperty("Variable Path", variablePath, variablePathSource);
            ConsoleOutput.WriteProperty("Deployment Path", startupDeployments.SourcePath, deploymentPathSource);
            var deviceId = Licenses.TrakHoundDeviceId.GetInformation();
            ConsoleOutput.WriteProperty("Device ID", deviceId.DeviceId, deviceId.Source == Licenses.TrakHoundDeviceIdSource.Variable ? $"from {Licenses.TrakHoundDeviceId.Variable}" : "from the machine name");
            ConsoleOutput.WriteDivider();
            Console.WriteLine();

            await new TrakHoundInstanceHost(configurationProfileId, configurationPath, packagePath, volumePath, variablePath, startupDeployments).Run();
        }

        /// <summary>
        /// The Variables are kept beside the Configuration directory (ex. [app]\variables, ProgramData\TrakHound\Instance\variables
        /// or /var/lib/trakhound/instance/variables), so they stay with the rest of the Instance's data, not in the program directory
        /// </summary>
        public static string GetDefaultVariablePath(string configurationPath)
        {
            var parent = Path.GetDirectoryName(Path.GetFullPath(configurationPath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            return Path.Combine(!string.IsNullOrEmpty(parent) ? parent : AppContext.BaseDirectory, _variableDirectoryName);
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
