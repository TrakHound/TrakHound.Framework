// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Apps
{
    /// <summary>
    /// Creates and tracks the Injection Services loaded from App Packages, keyed by Scope (ex. a Browser connection)
    /// </summary>
    public interface ITrakHoundAppInjectionServiceManager
    {
        /// <summary>
        /// The host's Service Provider (used to resolve dependencies that are not App Injection Services)
        /// </summary>
        IServiceProvider ServiceProvider { get; }


        /// <summary>
        /// Gets the Service of the specified Type for the Scope, creating and registering it if it does not exist yet (null if the Type is not loaded)
        /// </summary>
        object GetService(string scopeId, Type serviceType);

        /// <summary>
        /// Gets the Service of the specified Type for the Scope, creating and registering it if it does not exist yet (null if the Type is not loaded)
        /// </summary>
        object GetService<TServiceType>(string scopeId, Type serviceType) where TServiceType : ITrakHoundAppInjectionService;

        /// <summary>
        /// Gets the Service Configurations for the loaded Service Types that implement <typeparamref name="TServiceType"/>
        /// </summary>
        IEnumerable<TrakHoundAppInjectionServiceConfiguration> GetServiceConfigurations<TServiceType>() where TServiceType : ITrakHoundAppInjectionService;

        /// <summary>
        /// Gets the Service Configurations that a Service of <typeparamref name="TServiceType"/> can depend on
        /// (Transient can use Transient, Scoped, and Singleton; Scoped can use Scoped and Singleton; Singleton can use Singleton)
        /// </summary>
        IEnumerable<TrakHoundAppInjectionServiceConfiguration> GetRequiredServiceConfigurations<TServiceType>() where TServiceType : ITrakHoundAppInjectionService;


        /// <summary>
        /// Registers an existing Service object for the Scope (ignored if a Service of the same Type is already registered)
        /// </summary>
        void RegisterService(string scopeId, object service);

        /// <summary>
        /// Registers an existing Service object for the Scope (ignored if a Service of the same Type is already registered)
        /// </summary>
        void RegisterService<TServiceType>(string scopeId, object service) where TServiceType : ITrakHoundAppInjectionService;


        /// <summary>
        /// Disposes the Services registered for the Scope (Singleton Services are not Disposed)
        /// </summary>
        void DisposeScope(string scopeId);


        /// <summary>
        /// Disposes the current Services for the specified App Configuration and reloads its Service Types from its Package
        /// </summary>
        void ReloadServices(string configurationId);
    }
}
