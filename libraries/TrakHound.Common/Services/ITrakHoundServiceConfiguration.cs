// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using TrakHound.Configurations;

namespace TrakHound.Services
{
    /// <summary>
    /// A Configuration used to create a Service from a Package
    /// </summary>
    public interface ITrakHoundServiceConfiguration : ITrakHoundConfiguration
    {
        /// <summary>
        /// A friendly name for the Service
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A description of the Service
        /// </summary>
        string Description { get; }

        /// <summary>
        /// The ID of the Package that contains the Service
        /// </summary>
        string PackageId { get; }

        /// <summary>
        /// The version of the Package that contains the Service
        /// </summary>
        string PackageVersion { get; }

        /// <summary>
        /// The ID of the Volume used by the Service (the Configuration ID is used when null)
        /// </summary>
        string VolumeId { get; }

        /// <summary>
        /// Service specific Parameters (ex. connection settings)
        /// </summary>
        Dictionary<string, object> Parameters { get; }


        /// <summary>
        /// Returns true if a Parameter with the specified name exists
        /// </summary>
        bool ParameterExists(string name);

        /// <summary>
        /// Gets the value of the specified Parameter as a string, resolving Secrets (null if not found)
        /// </summary>
        string GetParameter(string name);

        /// <summary>
        /// Gets the value of the specified Parameter converted to T, resolving Secrets (default if not found or can't be converted)
        /// </summary>
        T GetParameter<T>(string name);

        /// <summary>
        /// Sets (or replaces) the specified Parameter. Values are stored as strings and null values are ignored
        /// </summary>
        void SetParameter(string name, object value);
    }
}
