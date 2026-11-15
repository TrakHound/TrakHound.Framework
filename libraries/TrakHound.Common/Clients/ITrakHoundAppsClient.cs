// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Apps;
using TrakHound.Logging;

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to query and manage the Apps hosted by a TrakHound Instance
    /// </summary>
    public interface ITrakHoundAppsClient
    {
        /// <summary>
        /// Gets the Information for each App loaded on the Instance
        /// </summary>
        Task<IEnumerable<TrakHoundAppInformation>> GetInformation();

        /// <summary>
        /// Gets the Information for the App with the specified ID (null if not found)
        /// </summary>
        Task<TrakHoundAppInformation> GetInformation(string appId);

        /// <summary>
        /// Reloads the Configuration for the specified App. Returns true if the Request was successful
        /// </summary>
        Task<bool> ReloadConfiguration(string appId);

        /// <summary>
        /// Refreshes (reloads) every open page of the specified App. Returns true if the Request was successful
        /// </summary>
        Task<bool> RefreshApp(string appId);

        /// <summary>
        /// Refreshes (reloads) the page open on the specified Client Connection. Returns true if the Request was successful
        /// </summary>
        Task<bool> RefreshConnection(string connectionId);


        /// <summary>
        /// Gets the Page View Metrics for each App that has been viewed
        /// </summary>
        Task<IEnumerable<TrakHoundAppMetrics>> GetMetrics() => Task.FromResult<IEnumerable<TrakHoundAppMetrics>>(null);

        /// <summary>
        /// Gets the Page View Metrics for the specified App (null if it has not been viewed)
        /// </summary>
        Task<TrakHoundAppMetrics> GetMetrics(string appId) => Task.FromResult<TrakHoundAppMetrics>(null);

        /// <summary>
        /// Gets the Page View Metrics for each Client Connection (optionally only those that viewed the specified App)
        /// </summary>
        Task<IEnumerable<TrakHoundAppConnectionMetrics>> GetConnectionMetrics(string appId = null) => Task.FromResult<IEnumerable<TrakHoundAppConnectionMetrics>>(null);


        /// <summary>
        /// Subscribes to Log entries for the specified App at or above the minimum Log Level (null if not supported by the Client)
        /// </summary>
        Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeToLog(string appId, TrakHoundLogLevel minimumLevel = TrakHoundLogLevel.Trace);
    }
}
