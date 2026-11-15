// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Logging;
using TrakHound.Workers;

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to query and control the Workers hosted by a TrakHound Instance
    /// </summary>
    public interface ITrakHoundWorkersClient
    {
        /// <summary>
        /// Gets the Information for each Worker loaded on the Instance
        /// </summary>
        Task<IEnumerable<TrakHoundWorkerInformation>> GetInformation();

        /// <summary>
        /// Gets the Information for the specified Worker (null if not found)
        /// </summary>
        Task<TrakHoundWorkerInformation> GetInformation(string serviceId);

        /// <summary>
        /// Gets the Information for the specified Workers
        /// </summary>
        Task<IEnumerable<TrakHoundWorkerInformation>> GetInformation(IEnumerable<string> serviceIds);


        //Task<IEnumerable<string>> Retrieve(int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending);

        /// <summary>
        /// Gets the current Status of each of the specified Workers
        /// </summary>
        Task<IEnumerable<TrakHoundWorkerStatus>> GetStatus(IEnumerable<string> serviceIds);

        /// <summary>
        /// Starts the specified Worker and returns its resulting Status
        /// </summary>
        Task<TrakHoundWorkerStatus> StartWorker(string serviceId);

        /// <summary>
        /// Stops the specified Worker and returns its resulting Status
        /// </summary>
        Task<TrakHoundWorkerStatus> StopWorker(string serviceId);


        /// <summary>
        /// Get the recent CPU Usage history for a Worker. If 'from' is specified, only samples after that time (UTC) are returned.
        /// </summary>
        Task<IEnumerable<TrakHoundWorkerCpuSample>> GetCpuHistory(string workerId, DateTime? from = null);


        /// <summary>
        /// Gets the Lifecycle, Error and CPU Metrics for each Worker
        /// </summary>
        Task<IEnumerable<TrakHoundWorkerMetrics>> GetMetrics() => Task.FromResult<IEnumerable<TrakHoundWorkerMetrics>>(null);

        /// <summary>
        /// Gets the Metrics, including the Recent Status changes, for the specified Worker (null if it is unknown)
        /// </summary>
        Task<TrakHoundWorkerMetrics> GetMetrics(string workerId) => Task.FromResult<TrakHoundWorkerMetrics>(null);


        /// <summary>
        /// Subscribes to Information updates for all Workers (null if not supported by the Client)
        /// </summary>
        Task<ITrakHoundStream<TrakHoundWorkerInformation>> Subscribe();

        /// <summary>
        /// Subscribes to Information updates for the specified Worker (null if not supported by the Client)
        /// </summary>
        Task<ITrakHoundStream<TrakHoundWorkerInformation>> Subscribe(string serviceId);

        /// <summary>
        /// Subscribes to Log entries from all Logs of the specified Worker at or above the minimum Log Level
        /// </summary>
        Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeToLog(string serviceId, TrakHoundLogLevel minimumLevel = TrakHoundLogLevel.Trace);

        /// <summary>
        /// Subscribes to Log entries from the named Log of the specified Worker at or above the minimum Log Level
        /// </summary>
        Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeToLog(string serviceId, string logName, TrakHoundLogLevel minimumLevel = TrakHoundLogLevel.Trace);


        /// <summary>
        /// Gets the names of the Logs written by the specified Worker
        /// </summary>
        Task<IEnumerable<string>> QueryLogNames(string serviceId);


        /// <summary>
        /// Queries the Log entries from all Logs of the specified Worker between the from and to timestamps
        /// </summary>
        Task<IEnumerable<TrakHoundLogItem>> QueryLogs(string serviceId, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending);

        /// <summary>
        /// Queries the Log entries from all Logs of the specified Worker at or above the minimum Log Level between the from and to timestamps
        /// </summary>
        Task<IEnumerable<TrakHoundLogItem>> QueryLogsByMinimumLevel(string serviceId, TrakHoundLogLevel minimumLevel, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending);

        /// <summary>
        /// Queries the Log entries from all Logs of the specified Worker with exactly the specified Log Level between the from and to timestamps
        /// </summary>
        Task<IEnumerable<TrakHoundLogItem>> QueryLogsByLevel(string serviceId, TrakHoundLogLevel level, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending);


        /// <summary>
        /// Queries the Log entries from the named Log of the specified Worker between the from and to timestamps
        /// </summary>
        Task<IEnumerable<TrakHoundLogItem>> QueryLogs(string serviceId, string logName, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending);

        /// <summary>
        /// Queries the Log entries from the named Log of the specified Worker at or above the minimum Log Level between the from and to timestamps
        /// </summary>
        Task<IEnumerable<TrakHoundLogItem>> QueryLogsByMinimumLevel(string serviceId, string logName, TrakHoundLogLevel minimumLevel, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending);

        /// <summary>
        /// Queries the Log entries from the named Log of the specified Worker with exactly the specified Log Level between the from and to timestamps
        /// </summary>
        Task<IEnumerable<TrakHoundLogItem>> QueryLogsByLevel(string serviceId, string logName, TrakHoundLogLevel level, DateTime from, DateTime to, int skip = 0, int take = 1000, TrakHoundSortOrder sortOrder = TrakHoundSortOrder.Ascending);


        /// <summary>
        /// Subscribes to Status changes for all Workers
        /// </summary>
        Task<ITrakHoundStream<TrakHoundWorkerStatusResponse>> SubscribeStatus();

        /// <summary>
        /// Subscribes to Status changes for the specified Worker (null if workerId is empty)
        /// </summary>
        Task<ITrakHoundStream<TrakHoundWorkerStatusResponse>> SubscribeStatus(string workerId);

        /// <summary>
        /// Subscribes to Log entries from all Workers at or above the minimum Log Level
        /// </summary>
        Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeLog(TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace);
        
        /// <summary>
        /// Subscribes to Log entries from the specified Worker at or above the minimum Log Level (null if workerId is empty)
        /// </summary>
        Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeLog(string workerId, TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace);
    }
}
