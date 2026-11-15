// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Services
{
    /// <summary>
    /// Loads and manages the Services created from Service Configurations
    /// </summary>
    public interface ITrakHoundServiceProvider : IDisposable
    {
        /// <summary>
        /// The Services that are currently loaded
        /// </summary>
        IEnumerable<ITrakHoundService> Services { get; }

        /// <summary>
        /// Called when a Service is loaded
        /// </summary>
        EventHandler<ITrakHoundService> ServiceAdded { get; set; }

        /// <summary>
        /// Called with the Configuration ID when the Services for a Configuration are removed
        /// </summary>
        EventHandler<string> ServiceRemoved { get; set; }

        /// <summary>
        /// Called when an error occurs while loading a Service
        /// </summary>
        EventHandler<Exception> ServiceLoadError { get; set; }


        /// <summary>
        /// Gets information about all of the loaded Service Configurations
        /// </summary>
        IEnumerable<TrakHoundServiceInformation> GetInformation();

        /// <summary>
        /// Gets information about the specified Service Configuration (null if not loaded)
        /// </summary>
        TrakHoundServiceInformation GetInformation(string configurationId);


        /// <summary>
        /// Gets the Service of type TService loaded from the specified Configuration (default if not found)
        /// </summary>
        TService GetService<TService>(string configurationId) where TService : ITrakHoundService;
    }
}
