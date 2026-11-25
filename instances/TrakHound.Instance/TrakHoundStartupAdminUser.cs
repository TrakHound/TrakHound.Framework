// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using NLog;
using System.Text.Json;
using System.Text.Json.Serialization;
using TrakHound.Instance.Security;

namespace TrakHound.Instance
{
    /// <summary>
    /// Creates the first Admin Account (an Owner) when the Instance starts, so it never has to be created from a browser. From (in order):
    /// <list type="number">
    /// <item>A Seed file left for the Instance (ex. by the Installer). It has the Password in plain text, so it is always deleted</item>
    /// <item>The TRAKHOUND_ADMIN_USERNAME and TRAKHOUND_ADMIN_PASSWORD_FILE Environment Variables (ex. a Docker secret). The Password file is kept</item>
    /// <item>In a container with neither of those, a generated Password for 'admin', written once to the container's output (unless
    /// TRAKHOUND_ADMIN_GENERATE is false). Trying the Docker Image then needs no setup step, and there are still no default credentials</item>
    /// </list>
    /// Each is ignored when an Admin Account already exists (use the set-admin command, or the Admin Accounts page, to change them).
    /// </summary>
    public class TrakHoundStartupAdminUser
    {
        /// <summary>
        /// Kept in the Configuration Profile directory
        /// </summary>
        public const string SeedFilename = ".user.setup";

        public const string UsernameEnvironmentVariable = "TRAKHOUND_ADMIN_USERNAME";
        public const string PasswordFileEnvironmentVariable = "TRAKHOUND_ADMIN_PASSWORD_FILE";
        public const string GenerateEnvironmentVariable = "TRAKHOUND_ADMIN_GENERATE";
        public const string ContainerEnvironmentVariable = "DOTNET_RUNNING_IN_CONTAINER";

        public const string GeneratedUsername = "admin";

        // Lowercase, and without characters that are easy to mistake for each other (0/o, 1/l/i), since the Password may be typed from
        // the log. 8 characters (about 40 bits) is far more than Sign In throttling lets anyone guess (at most a few thousand guesses a day
        // for one Username), and it's replaced after signing in
        private const string GeneratedPasswordCharacters = "abcdefghjkmnpqrstuvwxyz23456789";
        private const int GeneratedPasswordGroups = 2;
        private const int GeneratedPasswordGroupLength = 4;


        public enum ResultType
        {
            None,
            Created,
            Ignored,
            Invalid
        }

        public enum SourceType
        {
            None,

            /// <summary>
            /// The Seed file (ex. from the Installer)
            /// </summary>
            Seed,

            /// <summary>
            /// The TRAKHOUND_ADMIN_USERNAME and TRAKHOUND_ADMIN_PASSWORD_FILE Environment Variables
            /// </summary>
            PasswordFile,

            /// <summary>
            /// A generated Password, in a container with no other source (written to the container's output)
            /// </summary>
            Generated
        }

        class Seed
        {
            [JsonPropertyName("username")]
            public string Username { get; set; }

            [JsonPropertyName("password")]
            public string Password { get; set; }
        }


        public ResultType Result { get; private set; }

        public SourceType Source { get; private set; }

        public string Username { get; private set; }

        public string Message { get; private set; }


        /// <summary>
        /// Create the Admin User from the Seed file or the Password file (if there is one). Must be run before the Admin Interface is served
        /// </summary>
        /// <param name="configurationProfilePath">The Configuration Profile directory</param>
        /// <param name="getEnvironmentVariable">Reads an Environment Variable (Environment.GetEnvironmentVariable when null)</param>
        /// <param name="output">Where a generated Password is written (the Console when null). Never the Logger, whose Log History other Admins can read</param>
        public static TrakHoundStartupAdminUser Apply(string configurationProfilePath, Logger logger = null, Func<string, string> getEnvironmentVariable = null, TextWriter output = null)
        {
            var seedPath = Path.Combine(configurationProfilePath, SeedFilename);
            if (File.Exists(seedPath)) return ApplySeed(configurationProfilePath, seedPath, logger);

            getEnvironmentVariable ??= Environment.GetEnvironmentVariable;

            var result = ApplyPasswordFile(configurationProfilePath, getEnvironmentVariable, logger);
            if (result.Source != SourceType.None) return result;

            return ApplyGenerated(configurationProfilePath, getEnvironmentVariable, logger, output ?? Console.Out);
        }

        private static TrakHoundStartupAdminUser ApplyGenerated(string configurationProfilePath, Func<string, string> getEnvironmentVariable, Logger logger, TextWriter output)
        {
            var result = new TrakHoundStartupAdminUser();

            // Only in a container, whose output is only seen by whoever runs it. Installed Instances create the Account during Setup
            if (!string.Equals(getEnvironmentVariable(ContainerEnvironmentVariable), "true", StringComparison.OrdinalIgnoreCase)) return result;
            if (IsFalse(getEnvironmentVariable(GenerateEnvironmentVariable))) return result;

            try
            {
                if (GetStore(configurationProfilePath).Any()) return result;

                result.Source = SourceType.Generated;
                result.Username = GeneratedUsername;

                var password = GeneratePassword();
                result.Message = GetStore(configurationProfilePath).SetOwner(GeneratedUsername, password, out _);
                if (result.Message != null)
                {
                    result.Result = ResultType.Invalid;
                    logger?.Error($"Startup Admin User : {result.Message}");
                    return result;
                }

                result.Result = ResultType.Created;
                WriteGeneratedPassword(output, password);
                logger?.Info($"Startup Admin User : Admin User '{GeneratedUsername}' Created with a generated Password (written once to the container's output, not to the Log)");
            }
            catch (Exception ex)
            {
                result.Result = ResultType.Invalid;
                result.Message = ex.Message;
                logger?.Error($"Startup Admin User : The Admin User could not be created : {ex.Message}");
            }

            return result;
        }

        private static void WriteGeneratedPassword(TextWriter output, string password)
        {
            output.WriteLine();
            output.WriteLine("==================================================================");
            output.WriteLine(" TrakHound Admin Account created");
            output.WriteLine();
            output.WriteLine($"   Username : {GeneratedUsername}");
            output.WriteLine($"   Password : {password}");
            output.WriteLine();
            output.WriteLine(" Sign in at /_admin and change the Password in Security > Admin Accounts.");
            output.WriteLine(" It's only shown here, once. If it's lost, run set-admin in the container.");
            output.WriteLine($" Set {UsernameEnvironmentVariable} and {PasswordFileEnvironmentVariable} to choose it instead.");
            output.WriteLine("==================================================================");
            output.WriteLine();
            output.Flush();
        }

        /// <summary>
        /// A random Password of groups of lowercase letters and digits (ex. k7mq-2xpz), about 40 bits
        /// </summary>
        public static string GeneratePassword()
        {
            var groups = new string[GeneratedPasswordGroups];
            for (var i = 0; i < groups.Length; i++)
            {
                groups[i] = System.Security.Cryptography.RandomNumberGenerator.GetString(GeneratedPasswordCharacters, GeneratedPasswordGroupLength);
            }
            return string.Join('-', groups);
        }

        private static bool IsFalse(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            var trimmed = value.Trim();
            return string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase) || string.Equals(trimmed, "no", StringComparison.OrdinalIgnoreCase) || trimmed == "0";
        }

        private static TrakHoundStartupAdminUser ApplySeed(string configurationProfilePath, string seedPath, Logger logger)
        {
            var result = new TrakHoundStartupAdminUser { Source = SourceType.Seed };

            try
            {
                if (GetStore(configurationProfilePath).Any())
                {
                    result.Result = ResultType.Ignored;
                    result.Message = "An Admin User already exists";
                    logger?.Warn($"Startup Admin User : {SeedFilename} was ignored. An Admin User already exists");
                    return result;
                }

                Seed seed = null;
                try
                {
                    seed = JsonSerializer.Deserialize<Seed>(File.ReadAllText(seedPath));
                }
                catch (Exception ex)
                {
                    // Not the Exception Message, which can include part of the file
                    logger?.Debug($"Startup Admin User : {SeedFilename} could not be read : {ex.GetType().Name}");
                }

                result.Username = seed?.Username;
                result.Message = seed != null ? GetStore(configurationProfilePath).SetOwner(seed.Username, seed.Password, out _) : $"{SeedFilename} could not be read";
                if (result.Message != null)
                {
                    result.Result = ResultType.Invalid;
                    logger?.Error($"Startup Admin User : {result.Message}");
                    return result;
                }

                result.Result = ResultType.Created;
                logger?.Info($"Startup Admin User : Admin User '{seed.Username}' Created");
            }
            catch (Exception ex)
            {
                result.Result = ResultType.Invalid;
                result.Message = ex.Message;
                logger?.Error($"Startup Admin User : The Admin User could not be created : {ex.Message}");
            }
            finally
            {
                try
                {
                    File.Delete(seedPath);
                }
                catch (Exception ex)
                {
                    logger?.Error($"Startup Admin User : Could not delete {seedPath} : {ex.Message}. Delete it, since it contains the Admin Password");
                }
            }

            return result;
        }

        private static TrakHoundStartupAdminUser ApplyPasswordFile(string configurationProfilePath, Func<string, string> getEnvironmentVariable, Logger logger)
        {
            var result = new TrakHoundStartupAdminUser();

            var username = getEnvironmentVariable(UsernameEnvironmentVariable);
            var passwordFile = getEnvironmentVariable(PasswordFileEnvironmentVariable);
            if (string.IsNullOrEmpty(username) && string.IsNullOrEmpty(passwordFile)) return result;

            result.Source = SourceType.PasswordFile;
            result.Username = username;

            // Set on every start (ex. in a container), so an existing Admin User is expected and isn't a warning
            if (GetStore(configurationProfilePath).Any())
            {
                result.Result = ResultType.Ignored;
                result.Message = "An Admin User already exists";
                logger?.Debug($"Startup Admin User : {PasswordFileEnvironmentVariable} was ignored. An Admin User already exists");
                return result;
            }

            if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(passwordFile))
            {
                result.Result = ResultType.Invalid;
                result.Message = $"Both {UsernameEnvironmentVariable} and {PasswordFileEnvironmentVariable} must be set";
                logger?.Error($"Startup Admin User : {result.Message}");
                return result;
            }

            string password;
            try
            {
                password = ReadPasswordFile(passwordFile);
            }
            catch (Exception ex)
            {
                result.Result = ResultType.Invalid;
                result.Message = $"{passwordFile} could not be read : {ex.GetType().Name}";
                logger?.Error($"Startup Admin User : {result.Message}");
                return result;
            }

            result.Message = GetStore(configurationProfilePath).SetOwner(username, password, out _);
            if (result.Message != null)
            {
                result.Result = ResultType.Invalid;
                logger?.Error($"Startup Admin User : {result.Message}");
                return result;
            }

            result.Result = ResultType.Created;
            logger?.Info($"Startup Admin User : Admin User '{username}' Created from {PasswordFileEnvironmentVariable}");
            return result;
        }

        private static TrakHoundAdminAccountStore GetStore(string configurationProfilePath)
        {
            return new TrakHoundAdminAccountStore(TrakHoundAdminAccountStore.GetDirectory(configurationProfilePath));
        }

        /// <summary>
        /// The Password is the first line of the file (secret files usually end with a line break, which isn't part of the Password)
        /// </summary>
        public static string ReadPasswordFile(string path)
        {
            using var reader = new StreamReader(path);
            return reader.ReadLine() ?? string.Empty;
        }
    }
}
