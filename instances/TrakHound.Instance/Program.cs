// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Reflection;
using TrakHound.Instance.Security;

namespace TrakHound.Instance
{
    public class Program
    {
        public static async Task Main(string[] args)
        {
            // Handled before the Command Parser, whose default command (Run) would take "set-admin" as the Configuration Profile ID
            if (TrakHoundAdminAccountCommand.IsCommand(args))
            {
                Environment.ExitCode = TrakHoundAdminAccountCommand.Run(args, Console.In, Console.Out, !Console.IsInputRedirected);
                return;
            }

            // Also before the Command Parser (and without the Console Header, so a script can read the Device ID alone)
            if (TrakHoundDeviceIdCommand.IsCommand(args))
            {
                Environment.ExitCode = TrakHoundDeviceIdCommand.Run(args, Console.Out, Console.Error);
                return;
            }

//#if DEBUG
//            await Debug();
//#else
            await Run(args);
//#endif
        }

        private static async Task Run(string[] args)
        {
            PrintConsoleHeader();

            var cmdArgs = new List<string>();
            if (args != null && args.Length > 0)
            {
                foreach (var arg in args)
                {
                    if (arg.Contains(' ')) cmdArgs.Add($"\"{arg}\"");
                    else cmdArgs.Add(arg);
                }
            }

            var cmd = string.Join(' ', cmdArgs);
            await CommandParser.Run(cmd);
        }

        private static async Task Debug()
        {
            PrintConsoleHeader();

            while (true)
            {
                var cmd = Console.ReadLine();
                if (!string.IsNullOrEmpty(cmd))
                {
                    await CommandParser.Run(cmd);
                }
            }
        }

        private static void PrintConsoleHeader()
        {
            var assembly = Assembly.GetEntryAssembly();
            var buildDate = File.GetCreationTime(Assembly.GetExecutingAssembly().Location);

            // Use the Informational Version (ex. 0.4.5-prerelease) without any Source Revision suffix (ex. +abc123)
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0];
            if (string.IsNullOrEmpty(version)) version = assembly.GetName().Version?.ToString();

            Console.WriteLine();
            ConsoleOutput.WriteDivider();
            ConsoleOutput.WriteTitle("TrakHound Instance", $"v{version}");
            ConsoleOutput.WriteText($"Copyright {buildDate.Year} TrakHound Inc., All Rights Reserved");
            ConsoleOutput.WriteDivider();
            ConsoleOutput.WriteProperty("License", "Apache-2.0 (https://www.apache.org/licenses/LICENSE-2.0)");
            ConsoleOutput.WriteProperty("Source", "https://github.com/TrakHound/TrakHound.Framework");
            ConsoleOutput.WriteDivider();
        }
    }
}
