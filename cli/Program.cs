// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Reflection;

namespace TrakHound.CLI
{
    internal class Program
    {
        public static async Task<int> Main(string[] args)
        {
            // Use a per-process Temp directory so that multiple 'th' processes don't clear each other's files
            TrakHoundTemp.BaseDirectory = Path.Combine(Path.GetTempPath(), "TrakHound", "cli", Environment.ProcessId.ToString());
            TrakHoundTemp.Clear();

            try
            {
                TrakHoundCliConfiguration.Load();

#if DEBUG
                // Run an interactive console when no args are passed (Debug only)
                if (args == null || args.Length == 0)
                {
                    await Debug();
                    return CommandParser.SuccessExitCode;
                }
#endif

                return await CommandParser.Run(args);
            }
            finally
            {
                TrakHoundTemp.Clear();
                try { Directory.Delete(TrakHoundTemp.BaseDirectory, false); } catch { }
            }
        }

        private static async Task Debug()
        {
            PrintConsoleHeader();

            while (true)
            {
                Console.Write('>');
                var cmd = Console.ReadLine();
                if (cmd == null) break;

                if (!string.IsNullOrEmpty(cmd))
                {
                    var exitCode = await CommandParser.Run(CommandParser.Tokenize(cmd));
                    if (exitCode != CommandParser.SuccessExitCode) Console.WriteLine($"Exit Code : {exitCode}");
                }
            }
        }

        private static void PrintConsoleHeader()
        {
            var assembly = Assembly.GetEntryAssembly();
            var buildDate = File.GetCreationTime(Assembly.GetExecutingAssembly().Location);

            // Use the Informational Version (ex. 0.9.6.1-prerelease) without the Commit suffix (ex. +abc1234)
            var version = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion?.Split('+')[0];
            if (string.IsNullOrEmpty(version)) version = assembly.GetName().Version?.ToString();

            Console.WriteLine("--------------------");
            Console.WriteLine($"Copyright {buildDate.Year} TrakHound Inc., All Rights Reserved");
            Console.WriteLine($"TrakHound CLI : Version {version}");
            Console.WriteLine("--------------------");
            Console.WriteLine("This application is licensed under the Apache License, Version 2.0 (https://www.apache.org/licenses/LICENSE-2.0)");
            Console.WriteLine("Source code available at Github.com (https://github.com/TrakHound/TrakHound.Framework)");
            Console.WriteLine("--------------------");
        }
    }
}
