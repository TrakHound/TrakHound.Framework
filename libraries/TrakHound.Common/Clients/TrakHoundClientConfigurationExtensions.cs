// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Configurations;

namespace TrakHound.Clients
{
    /// <summary>
    /// Extension methods for creating a TrakHoundHttpClientConfiguration
    /// </summary>
    public static class TrakHoundClientConfigurationExtensions
    {
        /// <summary>
        /// Creates a Client Configuration from the Instance settings of the Environment Configuration (defaults if the Instance is not set)
        /// </summary>
        public static TrakHoundHttpClientConfiguration GetClientConfiguration(this TrakHoundEnvironmentConfiguration environmentConfiguration)
        {
            if (environmentConfiguration != null && environmentConfiguration.Instance != null)
            {
                return new TrakHoundHttpClientConfiguration(
                    environmentConfiguration.Instance.Hostname,
                    environmentConfiguration.Instance.Port,
                    environmentConfiguration.Instance.Path,
                    environmentConfiguration.Instance.UseSSL
                    );
            }
            else
            {
                return new TrakHoundHttpClientConfiguration();
            }
        }
    }
}
