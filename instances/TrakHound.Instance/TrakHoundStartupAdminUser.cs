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
    /// </list>
    /// Either is ignored when an Admin Account already exists (use the set-admin command, or the Admin Accounts page, to change them).
    /// </summary>
    public class TrakHoundStartupAdminUser
    {
        /// <summary>
        /// Kept in the Configuration Profile directory
        /// </summary>
        public const string SeedFilename = ".user.setup";

        public const string UsernameEnvironmentVariable = "TRAKHOUND_ADMIN_USERNAME";
        public const string PasswordFileEnvironmentVariable = "TRAKHOUND_ADMIN_PASSWORD_FILE";


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
            PasswordFile
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
        public static TrakHoundStartupAdminUser Apply(string configurationProfilePath, Logger logger = null, Func<string, string> getEnvironmentVariable = null)
        {
            var seedPath = Path.Combine(configurationProfilePath, SeedFilename);
            if (File.Exists(seedPath)) return ApplySeed(configurationProfilePath, seedPath, logger);

            return ApplyPasswordFile(configurationProfilePath, getEnvironmentVariable ?? Environment.GetEnvironmentVariable, logger);
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
