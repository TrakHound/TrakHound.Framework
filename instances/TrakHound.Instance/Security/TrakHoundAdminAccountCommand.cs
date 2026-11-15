// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;

namespace TrakHound.Instance.Security
{
    /// <summary>
    /// The set-admin command, which creates an Owner Admin Account from the machine the Instance runs on (or, when the Username is already
    /// used, sets that Account's Password, enables it and makes it an Owner, so it is also how to get back in when no Owner can sign in):
    /// <code>TrakHound.Instance set-admin &lt;username&gt; [--profile &lt;id&gt;] [--configurationPath &lt;path&gt;]</code>
    /// The Password is read from standard input (prompted for twice in a console), so it isn't kept in the command line or shell history.
    /// Options take priority over the TRAKHOUND_CONFIGURATION_PROFILE and TRAKHOUND_CONFIGURATION_PATH Environment Variables
    /// </summary>
    public static class TrakHoundAdminAccountCommand
    {
        public const string Name = "set-admin";

        public const string Usage = "Usage : TrakHound.Instance set-admin <username> [--profile <id>] [--configurationPath <path>]   (the Password is read from standard input)";

        private const string _profileOption = "--profile";
        private const string _configurationPathOption = "--configurationPath";
        private const string _configurationProfileEnvironmentVariable = "TRAKHOUND_CONFIGURATION_PROFILE";
        private const string _configurationPathEnvironmentVariable = "TRAKHOUND_CONFIGURATION_PATH";


        public class Arguments
        {
            public string Username { get; set; }

            public string ConfigurationProfileId { get; set; }

            public string ConfigurationPath { get; set; }

            /// <summary>
            /// The Configuration Profile directory the Admin Account is written to
            /// </summary>
            public string ConfigurationProfilePath => Path.Combine(ConfigurationPath, ConfigurationProfileId);
        }


        public static bool IsCommand(string[] args) => args != null && args.Length > 0 && args[0] == Name;

        /// <summary>
        /// Read the Arguments that follow "set-admin". Returns null (with the reason) when they can't be used
        /// </summary>
        public static Arguments Parse(string[] args, Func<string, string> getEnvironmentVariable, out string error)
        {
            error = null;
            var arguments = new Arguments();

            for (var i = 1; i < args.Length; i++)
            {
                var arg = args[i];
                if (arg == _profileOption || arg == _configurationPathOption)
                {
                    if (i + 1 >= args.Length || string.IsNullOrEmpty(args[i + 1]))
                    {
                        error = $"{arg} requires a value";
                        return null;
                    }

                    if (arg == _profileOption) arguments.ConfigurationProfileId = args[++i];
                    else arguments.ConfigurationPath = args[++i];
                }
                else if (arg.StartsWith("--"))
                {
                    error = $"Unknown option : {arg}";
                    return null;
                }
                else if (arguments.Username == null)
                {
                    arguments.Username = arg;
                }
                else
                {
                    error = $"Unexpected argument : {arg}";
                    return null;
                }
            }

            if (string.IsNullOrEmpty(arguments.Username))
            {
                error = "A username is required";
                return null;
            }

            // The same Profile and Path the Instance uses (when the Options aren't given)
            if (string.IsNullOrEmpty(arguments.ConfigurationProfileId)) arguments.ConfigurationProfileId = getEnvironmentVariable(_configurationProfileEnvironmentVariable);
            if (string.IsNullOrEmpty(arguments.ConfigurationProfileId)) arguments.ConfigurationProfileId = TrakHoundConfigurationProfile.Default;
            if (string.IsNullOrEmpty(arguments.ConfigurationPath)) arguments.ConfigurationPath = getEnvironmentVariable(_configurationPathEnvironmentVariable);
            if (string.IsNullOrEmpty(arguments.ConfigurationPath)) arguments.ConfigurationPath = Path.Combine(AppContext.BaseDirectory, "config");

            return arguments;
        }

        /// <summary>
        /// Run the command. Returns the process Exit Code (0 when the Admin Account was written)
        /// </summary>
        /// <param name="args">Every argument (starting with "set-admin")</param>
        /// <param name="input">Where the Password is read from</param>
        /// <param name="output">Where messages are written</param>
        /// <param name="interactive">Prompt for the Password (twice, without showing it). Otherwise the first line of the input is the Password</param>
        public static int Run(string[] args, TextReader input, TextWriter output, bool interactive, Func<string, string> getEnvironmentVariable = null)
        {
            var arguments = Parse(args, getEnvironmentVariable ?? Environment.GetEnvironmentVariable, out var error);
            if (arguments == null)
            {
                output.WriteLine(error);
                output.WriteLine(Usage);
                return 2;
            }

            string password;
            if (interactive)
            {
                password = ReadHidden(output, "Password : ");
                if (ReadHidden(output, "Confirm Password : ") != password)
                {
                    output.WriteLine("The Passwords don't match. The Admin Account was not changed");
                    return 1;
                }
            }
            else
            {
                password = input.ReadLine();
            }

            var directory = TrakHoundAdminAccountStore.GetDirectory(arguments.ConfigurationProfilePath);
            var message = new TrakHoundAdminAccountStore(directory).SetOwner(arguments.Username, password, out var created);
            if (message != null)
            {
                output.WriteLine($"{message}. The Admin Account was not changed");
                return 1;
            }

            output.WriteLine(created
                ? $"Admin Account '{arguments.Username}' created as an Owner in {directory}"
                : $"Admin Account '{arguments.Username}' updated in {directory} : its Password was set, it is enabled and it is an Owner. Its sessions that were signed in are signed out");
            return 0;
        }

        private static string ReadHidden(TextWriter output, string prompt)
        {
            output.Write(prompt);

            var value = new System.Text.StringBuilder();
            while (true)
            {
                var key = Console.ReadKey(true);
                if (key.Key == ConsoleKey.Enter) break;
                if (key.Key == ConsoleKey.Backspace)
                {
                    if (value.Length > 0) value.Length--;
                }
                else if (!char.IsControl(key.KeyChar))
                {
                    value.Append(key.KeyChar);
                }
            }

            output.WriteLine();
            return value.ToString();
        }
    }
}
