// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using TrakHound.Configurations;

namespace TrakHound.Functions
{
    /// <summary>
    /// The Configuration of a Function within an Instance
    /// </summary>
    public interface ITrakHoundFunctionConfiguration : ITrakHoundConfiguration
    {
        /// <summary>
        /// The Name of the Function (defaults to the Package name if empty)
        /// </summary>
        string Name { get; }

        /// <summary>
        /// A description of the Function (defaults to the Package description if empty)
        /// </summary>
        string Description { get; }

        /// <summary>
        /// The Id of the Function used when Running it (ex. through the Functions Api)
        /// </summary>
        string FunctionId { get; }

        /// <summary>
        /// The Id of the Volume used to store Files for the Function
        /// </summary>
        string VolumeId { get; }

        /// <summary>
        /// The Id of the Router the Function's Client sends Requests through
        /// </summary>
        string RouterId { get; }

        /// <summary>
        /// The ID of the Group the Function belongs to (the Function uses the Group's Router when RouterId is empty)
        /// </summary>
        string GroupId { get; }

        /// <summary>
        /// The Id of the Package that contains the Function
        /// </summary>
        string PackageId { get; }

        /// <summary>
        /// The Version of the Package to load (latest if empty)
        /// </summary>
        string PackageVersion { get; }

        /// <summary>
        /// The Parameters passed to the Function (values may reference Secrets)
        /// </summary>
        Dictionary<string, object> Parameters { get; }


        /// <summary>
        /// Gets the value of the Parameter as a string (Secrets are resolved). Returns null if not found
        /// </summary>
        string GetParameter(string name);

        /// <summary>
        /// Gets the value of the Parameter converted to T (Secrets are resolved). Returns default if not found or not convertible
        /// </summary>
        T GetParameter<T>(string name);

        /// <summary>
        /// Sets the value of the Parameter, replacing any existing value (ignored if the value is null)
        /// </summary>
        void SetParameter(string name, object value);
    }
}
