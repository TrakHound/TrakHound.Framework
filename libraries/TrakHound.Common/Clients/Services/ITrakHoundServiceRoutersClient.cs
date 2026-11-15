// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Services;

namespace TrakHound.Clients.Services
{
    /// <summary>
    /// Client used to query the Service Routers configured on the Instance
    /// </summary>
    public interface ITrakHoundServiceRoutersClient
    {
        /// <summary>
        /// Gets the Information for each Service Router configured on the Instance
        /// </summary>
        Task<IEnumerable<ITrakHoundServiceRouterInformation>> GetRouters();

        /// <summary>
        /// Gets the Information for the Service Router with the specified ID (null if not found)
        /// </summary>
        Task<ITrakHoundServiceRouterInformation> GetRouter(string routerId);
    }
}
