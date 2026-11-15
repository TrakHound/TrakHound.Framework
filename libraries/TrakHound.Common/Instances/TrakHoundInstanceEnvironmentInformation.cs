// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Text.Json.Serialization;

namespace TrakHound.Instances
{
    /// <summary>
    /// Information about the machine an Instance is running on
    /// </summary>
    public class TrakHoundInstanceEnvironmentInformation
    {
        /// <summary>
        /// The name of the machine the Instance is running on
        /// </summary>
        [JsonPropertyName("sender")]
        public string Sender { get; set; }

        /// <summary>
        /// The name of the user the Instance process is running as
        /// </summary>
        [JsonPropertyName("user")]
        public string User { get; set; }

        /// <summary>
        /// Information about the machine's Operating System
        /// </summary>
        [JsonPropertyName("operatingSystem")]
        public TrakHoundInstanceOperatingSystemInformation OperatingSystem { get; set; }


        /// <summary>
        /// Creates a new Environment Information with empty Operating System Information
        /// </summary>
        public TrakHoundInstanceEnvironmentInformation()
        {
            OperatingSystem = new TrakHoundInstanceOperatingSystemInformation();
        }
    }
}
