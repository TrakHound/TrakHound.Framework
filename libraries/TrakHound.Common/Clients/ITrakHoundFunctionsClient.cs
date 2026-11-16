// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Functions;
using TrakHound.Logging;

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to query and Run the Functions hosted by a TrakHound Instance
    /// </summary>
    public interface ITrakHoundFunctionsClient
    {
        /// <summary>
        /// Gets the Information for each Function loaded on the Instance
        /// </summary>
        Task<IEnumerable<TrakHoundFunctionInformation>> GetInformation();

        /// <summary>
        /// Gets the Information for the Function with the specified ID (null if not found)
        /// </summary>
        Task<TrakHoundFunctionInformation> GetInformation(string functionId);

        /// <summary>
        /// Gets the Information for the Functions with the specified IDs
        /// </summary>
        Task<IEnumerable<TrakHoundFunctionInformation>> GetInformation(IEnumerable<string> functionIds);


        /// <summary>
        /// Gets the Run Metrics for each Function that has been Run
        /// </summary>
        Task<IEnumerable<TrakHoundFunctionMetrics>> GetMetrics();

        /// <summary>
        /// Gets the Run Metrics, including the Recent Runs, for the specified Function (null if it has not been Run)
        /// </summary>
        Task<TrakHoundFunctionMetrics> GetMetrics(string functionId);


        /// <summary>
        /// Runs the specified Function with the input parameters and waits for its Response
        /// </summary>
        Task<TrakHoundFunctionResponse> Run(string functionId, IReadOnlyDictionary<string, string> inputParameters = null, string runId = null, long timestamp = 0);

        /// <summary>
        /// Subscribes to Status updates for the specified Function Run (null if runId is empty)
        /// </summary>
        Task<ITrakHoundStream<TrakHoundFunctionStatusResponse>> SubscribeRunStatus(string runId);

        /// <summary>
        /// Subscribes to Log entries for the specified Function Run at or above the minimum Log Level (null if runId is empty)
        /// </summary>
        Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeRunLog(string runId, TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace);

        /// <summary>
        /// Request that a running Function stop. Returns false if the Run was not found or cancellation is not supported.
        /// </summary>
        Task<bool> CancelRun(string functionId, string runId);
    }
}
