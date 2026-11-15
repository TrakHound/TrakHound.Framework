// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using TrakHound.Configurations;

namespace TrakHound.Apps
{
    /// <summary>
    /// The Configuration for an App (an App Package hosted at a Route on the Instance)
    /// </summary>
    public interface ITrakHoundAppConfiguration : ITrakHoundConfiguration
    {
        /// <summary>
        /// The display Name of the App
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A description of the App
        /// </summary>
        string Description { get; }

        /// <summary>
        /// The base Route (URL path) that the App's Pages are hosted at
        /// </summary>
        string Route { get; }

        /// <summary>
        /// The ID of the Volume used by the App (if null, the Configuration Id is used as the Volume ID)
        /// </summary>
        string VolumeId { get; }

        /// <summary>
        /// The ID of the Router used by the App's Client to send Requests
        /// </summary>
        string RouterId { get; }

        /// <summary>
        /// The ID of the Group the App belongs to (the App uses the Group's Router when RouterId is empty)
        /// </summary>
        string GroupId { get; }

        /// <summary>
        /// The ID of the App Package to run
        /// </summary>
        string PackageId { get; }

        /// <summary>
        /// The Version of the App Package to run (supports '*' wildcards to use the latest matching installed Version)
        /// </summary>
        string PackageVersion { get; }

        /// <summary>
        /// Custom Parameters passed to the App (keyed by Parameter Name)
        /// </summary>
        Dictionary<string, object> Parameters { get; }


        /// <summary>
        /// Gets the value of the specified Parameter as a string (Secret references are resolved). Returns null if not found
        /// </summary>
        string GetParameter(string name);

        /// <summary>
        /// Gets the value of the specified Parameter converted to <typeparamref name="T"/> (Secret references are resolved). Returns default if not found or not convertible
        /// </summary>
        T GetParameter<T>(string name);

        /// <summary>
        /// Sets (adds or replaces) the value of the specified Parameter. Null values are ignored
        /// </summary>
        void SetParameter(string name, object value);
    }
}
