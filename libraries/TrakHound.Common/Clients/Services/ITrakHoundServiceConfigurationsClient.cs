// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Services;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// Read, Save, and Remove the Service and Router Configurations of the Instance. Saved Configurations are written to the
    /// Instance's Configuration Profile and are (re)loaded by the Instance the same as Configurations changed in the Admin Interface.
    /// </summary>
    public interface ITrakHoundServiceConfigurationsClient
    {
        /// <summary>
        /// Gets all of the Service Configurations of the Instance
        /// </summary>
        Task<IEnumerable<TrakHoundServiceConfiguration>> GetServices();

        /// <summary>
        /// Gets the Service Configuration with the specified ID (null if not found)
        /// </summary>
        Task<TrakHoundServiceConfiguration> GetService(string configurationId);

        /// <summary>
        /// Add or Update a Service Configuration. Returns true if the Configuration was saved
        /// </summary>
        Task<bool> SaveService(TrakHoundServiceConfiguration configuration);

        /// <summary>
        /// Removes the Service Configuration with the specified ID. Returns true if the Configuration was removed
        /// </summary>
        Task<bool> RemoveService(string configurationId);


        /// <summary>
        /// Gets all of the Router Configurations of the Instance
        /// </summary>
        Task<IEnumerable<TrakHoundServiceRouterConfiguration>> GetRouters();

        /// <summary>
        /// Gets the Router Configuration with the specified ID (null if not found)
        /// </summary>
        Task<TrakHoundServiceRouterConfiguration> GetRouter(string configurationId);

        /// <summary>
        /// Add or Update a Router Configuration. Returns true if the Configuration was saved
        /// </summary>
        Task<bool> SaveRouter(TrakHoundServiceRouterConfiguration configuration);

        /// <summary>
        /// Removes the Router Configuration with the specified ID. Returns true if the Configuration was removed
        /// </summary>
        Task<bool> RemoveRouter(string configurationId);
    }
}
