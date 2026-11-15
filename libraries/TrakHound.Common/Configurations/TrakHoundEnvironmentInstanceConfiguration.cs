// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Configurations
{
    /// <summary>
    /// The connection settings for the Instance in an Environment Configuration
    /// </summary>
    public class TrakHoundEnvironmentInstanceConfiguration
    {
        /// <summary>
        /// The default Hostname ("localhost")
        /// </summary>
        public const string DefaultHostname = "localhost";

        /// <summary>
        /// The default Port (8472)
        /// </summary>
        public const int DefaultPort = 8472;

        /// <summary>
        /// The default base Path ("/")
        /// </summary>
        public const string DefaultPath = "/";

        /// <summary>
        /// The default UseSSL setting (false)
        /// </summary>
        public const bool DefaultUseSSL = false;


        /// <summary>
        /// The Hostname of the Instance
        /// </summary>
        public string Hostname { get; set; }

        /// <summary>
        /// The Port of the Instance's HTTP interface
        /// </summary>
        public int Port { get; set; }

        /// <summary>
        /// The base Path of the Instance's HTTP interface
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Whether to connect to the Instance using HTTPS
        /// </summary>
        public bool UseSSL { get; set; }


        /// <summary>
        /// Creates a Configuration using the default values (http://localhost:8472/)
        /// </summary>
        public static TrakHoundEnvironmentInstanceConfiguration GetDefault()
        {
            var configuration = new TrakHoundEnvironmentInstanceConfiguration();
            configuration.Hostname = DefaultHostname;
            configuration.Port = DefaultPort;
            configuration.Path = DefaultPath;
            configuration.UseSSL = DefaultUseSSL;
            return configuration;
        }
    }
}
