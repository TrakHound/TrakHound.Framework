// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace TrakHound.Api
{
    /// <summary>
    /// Loads the configured Apis and routes Requests to their Controllers
    /// </summary>
    public interface ITrakHoundApiProvider : IDisposable
    {
        /// <summary>
        /// Raised when an Api Controller writes a Log entry
        /// </summary>
        public event TrakHoundApiLogHandler ApiLogUpdated;


        /// <summary>
        /// Gets Information for all Apis that are currently loaded at a Route
        /// </summary>
        IEnumerable<TrakHoundApiInformation> GetRouteInformation();

        /// <summary>
        /// Gets Information for the Api Packages that are available
        /// </summary>
        IEnumerable<TrakHoundApiInformation> GetPackageInformation();

        /// <summary>
        /// Gets Information for the specified Api (null if it is not loaded)
        /// </summary>
        TrakHoundApiInformation GetInformation(string apiId);

        /// <summary>
        /// Gets the Endpoint that a Request (or Stream, matched regardless of the Method) for the Route is dispatched to (null if no Endpoint matches)
        /// </summary>
        TrakHoundApiEndpointInformation GetEndpointInformation(TrakHoundApiEndpointType endpointType, TrakHoundApiMethod method, string route);


        /// <summary>
        /// Gets the Request Metrics for all APIs that have received Requests
        /// </summary>
        IEnumerable<TrakHoundApiMetrics> GetMetrics();

        /// <summary>
        /// Gets the Request Metrics for the specified API (null if it has not received any Requests)
        /// </summary>
        TrakHoundApiMetrics GetMetrics(string apiId);

        /// <summary>
        /// Gets the most recent Requests to every API (newest first)
        /// </summary>
        TrakHoundApiRecentRequests GetRecentRequests();

        /// <summary>
        /// Gets the open and most recently closed Streams of every API
        /// </summary>
        TrakHoundApiStreams GetStreams();

        /// <summary>
        /// Closes an open Stream. Returns false if no Stream with the ID is open
        /// </summary>
        bool CloseStream(string streamId);


        /// <summary>
        /// Reloads the Configuration for the specified Api. Returns true if the Api was reloaded
        /// </summary>
        Task<bool> ReloadConfiguration(string apiId);

        /// <summary>
        /// Returns true if the specified Route matches a loaded Api
        /// </summary>
        bool IsRouteValid(string route);

        /// <summary>
        /// Returns true if the Api that matches the specified Route is served over HTTP
        /// </summary>
        bool IsHttpEnabled(string route);


        /// <summary>
        /// Routes a Request to the matching Api Endpoint and returns its Response. The Request Headers bind [FromHeader] Parameters
        /// (Withheld Headers are removed, see TrakHoundApiHeaders)
        /// </summary>
        Task<TrakHoundApiResponse> HandleRequest(TrakHoundApiMethod requestMethod, string requestRoute, Stream requestBody, string requestContentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, TrakHoundApiRequestSource source = null, IDictionary<string, string> requestHeaders = null, CancellationToken cancellationToken = default);
        
        /// <summary>
        /// Routes a Stream Request to the matching Api Stream Endpoint and returns the Stream of Responses. The Request Headers bind [FromHeader] Parameters
        /// (Withheld Headers are removed, see TrakHoundApiHeaders)
        /// </summary>
        Task<ITrakHoundStream<TrakHoundApiResponse>> HandleStream(string requestRoute, Stream requestBody, string requestContentType = "application/octet-stream", Dictionary<string, string> queryParameters = null, TrakHoundApiRequestSource source = null, IDictionary<string, string> requestHeaders = null, CancellationToken cancellationToken = default);
    }
}
