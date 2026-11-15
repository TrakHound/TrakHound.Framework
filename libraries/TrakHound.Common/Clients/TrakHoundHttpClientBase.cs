// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Linq;
using TrakHound.Http;

namespace TrakHound.Clients
{
    /// <summary>
    /// Base class for the HTTP sub-Clients that share the connection of a TrakHoundHttpClient
    /// </summary>
    public abstract class TrakHoundHttpClientBase
    {
        private readonly TrakHoundHttpClient _baseClient;


        /// <summary>
        /// The parent Client that holds the connection Configuration
        /// </summary>
        protected TrakHoundHttpClient BaseClient => _baseClient;

        /// <summary>
        /// The base URL used for HTTP Requests to the Instance
        /// </summary>
        protected string BaseUrl => _baseClient.ClientConfiguration.GetHttpBaseUrl();

        /// <summary>
        /// The ID of the Service Router used for Service Requests (null uses the default Router)
        /// </summary>
        protected string RouterId => _baseClient.RouterId;

        /// <summary>
        /// Sends HTTP Requests with the Authentication Headers of the parent Client
        /// </summary>
        protected TrakHoundHttpRequestSender Requests => _baseClient.Requests;


        /// <summary>
        /// Creates a new sub-Client that uses the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpClientBase(TrakHoundHttpClient baseClient)
        {
            _baseClient = baseClient;
        }


        /// <summary>
        /// Gets the first value of the named header from the HTTP Response (null if not present)
        /// </summary>
        protected static string GetHeaderValue(TrakHoundHttpResponse httpResponse, string name)
        {
            if (httpResponse.Headers != null && !string.IsNullOrEmpty(name))
            {
                if (httpResponse.Headers.TryGetValues(name, out var values))
                {
                    return values?.FirstOrDefault();
                }
            }

            return null;
        }
    }
}
