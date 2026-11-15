// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using TrakHound.Configurations;

namespace TrakHound.Security
{
    /// <summary>
    /// The Configuration of an Identity Provider within an Instance
    /// </summary>
    public interface ITrakHoundIdentityProviderConfiguration : ITrakHoundConfiguration
    {
        /// <summary>
        /// A description of the Identity Provider
        /// </summary>
        string Description { get; }

        /// <summary>
        /// The Id of the Router the Identity Provider's Client sends Requests through
        /// </summary>
        string RouterId { get; }

        /// <summary>
        /// The Id of the Volume used to store Files for the Identity Provider
        /// </summary>
        string VolumeId { get; }

        /// <summary>
        /// The Id of the Package that contains the Identity Provider
        /// </summary>
        string PackageId { get; }

        /// <summary>
        /// The Version of the Package to load
        /// </summary>
        string PackageVersion { get; }

        /// <summary>
        /// The Parameters passed to the Identity Provider (values may reference Secrets)
        /// </summary>
        Dictionary<string, object> Parameters { get; }


        /// <summary>
        /// Returns true if a Parameter with the specified name exists
        /// </summary>
        bool ParameterExists(string name);

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
