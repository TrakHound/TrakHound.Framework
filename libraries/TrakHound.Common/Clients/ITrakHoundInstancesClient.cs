// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Instances;

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to query and control a TrakHound Instance
    /// </summary>
    public interface ITrakHoundInstancesClient
    {
        /// <summary>
        /// Gets the Information (ID, Version, Status, etc.) for the Instance the Client is connected to
        /// </summary>
        Task<TrakHoundInstanceInformation> GetHostInformation();

        /// <summary>
        /// Gets the overall Health of the Instance and the result of each Health Check
        /// </summary>
        Task<TrakHoundInstanceHealth> GetHealth() => Task.FromResult<TrakHoundInstanceHealth>(null);

        /// <summary>
        /// Gets the CPU used by the Instance process, by Module Type and by Module, with the CPU not measured for any Module (null if not supported by the Client)
        /// </summary>
        Task<TrakHoundCpuUsage> GetCpuUsage() => Task.FromResult<TrakHoundCpuUsage>(null);

        /// <summary>
        /// Gets the recent Boots of the Instance process, newest first (null if not supported by the Client)
        /// </summary>
        Task<IEnumerable<TrakHoundInstanceBoot>> GetBoots(int take = 100) => Task.FromResult<IEnumerable<TrakHoundInstanceBoot>>(null);

        /// <summary>
        /// Gets a Boot of the Instance process by its ID (null if not found or not supported by the Client)
        /// </summary>
        Task<TrakHoundInstanceBoot> GetBoot(string bootId) => Task.FromResult<TrakHoundInstanceBoot>(null);

        /// <summary>
        /// Subscribes to Information updates for the specified Instance (null if not supported by the Client)
        /// </summary>
        Task<ITrakHoundStream<TrakHoundInstanceInformation>> SubscribeInformation(string instanceId);

        /// <summary>
        /// Starts the specified Instance (no effect if not supported by the Client)
        /// </summary>
        Task Start(string instanceId);

        /// <summary>
        /// Stops the specified Instance (no effect if not supported by the Client)
        /// </summary>
        Task Stop(string instanceId);
    }
}
