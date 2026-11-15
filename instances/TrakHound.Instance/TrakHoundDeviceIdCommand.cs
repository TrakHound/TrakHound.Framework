// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json;
using TrakHound.Licenses;

namespace TrakHound.Instance
{
    /// <summary>
    /// The device-id command, which shows the Device ID that Licenses are issued for, so a License can be requested for an Instance without an Admin interface:
    /// <code>TrakHound.Instance device-id [--json]</code>
    /// The Device ID alone is written to standard output (so a script can read it), and where it came from to standard error.
    /// With --json, one JSON object is written to standard output instead
    /// </summary>
    public static class TrakHoundDeviceIdCommand
    {
        public const string Name = "device-id";

        public const string Usage = "Usage : TrakHound.Instance device-id [--json]";

        private const string _jsonOption = "--json";

        private static readonly JsonSerializerOptions _jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };


        public static bool IsCommand(string[] args) => args != null && args.Length > 0 && args[0] == Name;

        /// <summary>
        /// Run the command. Returns the process Exit Code (0 when the Device ID was written)
        /// </summary>
        /// <param name="args">Every argument (starting with "device-id")</param>
        /// <param name="output">Where the Device ID is written (standard output)</param>
        /// <param name="messages">Where where it came from, and any warning, are written (standard error)</param>
        /// <param name="information">The Device ID (this device's when null)</param>
        /// <param name="machineName">The machine name (this machine's when null)</param>
        public static int Run(string[] args, TextWriter output, TextWriter messages, TrakHoundDeviceIdInformation information = null, string machineName = null)
        {
            var json = false;
            foreach (var arg in args.Skip(1))
            {
                if (arg == _jsonOption)
                {
                    json = true;
                }
                else
                {
                    messages.WriteLine(arg.StartsWith("--") ? $"Unknown option : {arg}" : $"Unexpected argument : {arg}");
                    messages.WriteLine(Usage);
                    return 2;
                }
            }

            information ??= TrakHoundDeviceId.GetInformation();
            machineName ??= Environment.MachineName;

            if (json)
            {
                output.WriteLine(JsonSerializer.Serialize(new
                {
                    deviceId = information.DeviceId,
                    source = information.Source == TrakHoundDeviceIdSource.Variable ? "variable" : "machineName",
                    machineName = information.Source == TrakHoundDeviceIdSource.MachineName ? machineName : null,
                    container = information.Container,
                    warning = information.Warning
                }, _jsonOptions));
            }
            else
            {
                output.WriteLine(information.DeviceId);
                messages.WriteLine(DescribeSource(information, machineName));
                if (information.Warning != null) messages.WriteLine($"Warning : {information.Warning}");
            }

            return 0;
        }

        /// <summary>
        /// Where the Device ID came from (ex. "From the machine name 'PLANT-PC-04'")
        /// </summary>
        public static string DescribeSource(TrakHoundDeviceIdInformation information, string machineName)
        {
            if (information.Source == TrakHoundDeviceIdSource.Variable) return $"From {TrakHoundDeviceId.Variable} (container detected : {information.Container})";

            // Renaming the machine changes the Device ID (and the Licenses issued for it no longer apply)
            return $"From the machine name '{machineName}'. Renaming the machine changes the Device ID";
        }
    }
}
