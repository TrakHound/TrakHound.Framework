// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using TrakHound.Configurations;

namespace TrakHound.Workers
{
    /// <summary>
    /// The Configuration for a Worker (the Package to run and its Parameters)
    /// </summary>
    public interface ITrakHoundWorkerConfiguration : ITrakHoundConfiguration
    {
        /// <summary>
        /// The display Name of the Worker
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A description of the Worker
        /// </summary>
        string Description { get; }

        /// <summary>
        /// The Id of the Package that contains the Worker
        /// </summary>
        string PackageId { get; }

        /// <summary>
        /// The Version of the Package to use
        /// </summary>
        string PackageVersion { get; }


        /// <summary>
        /// The Id of the Volume the Worker uses to store files
        /// </summary>
        string VolumeId { get; }

        /// <summary>
        /// The Id of the Router the Worker's Client sends Requests through
        /// </summary>
        string RouterId { get; }

        /// <summary>
        /// The ID of the Group the Worker belongs to (the Worker uses the Group's Router when RouterId is empty)
        /// </summary>
        string GroupId { get; }

        /// <summary>
        /// The Parameters passed to the Worker (secret values may be stored encrypted)
        /// </summary>
        Dictionary<string, object> Parameters { get; }


        /// <summary>
        /// Gets a Parameter value as a string (decrypted if it is encrypted). Returns null if not found
        /// </summary>
        string GetParameter(string name);

        /// <summary>
        /// Gets a Parameter value converted to type T (decrypted if it is encrypted). Returns default if not found or not convertible
        /// </summary>
        T GetParameter<T>(string name);

        /// <summary>
        /// Sets (adds or replaces) a Parameter value
        /// </summary>
        void SetParameter(string name, object value);
    }
}
