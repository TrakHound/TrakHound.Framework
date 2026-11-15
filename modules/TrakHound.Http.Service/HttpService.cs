// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.

// This file is subject to the terms and conditions defined in
// file 'LICENSE', which is part of this source code package.

using TrakHound.Clients;
using TrakHound.Clients.Services;
using TrakHound.Services;

namespace TrakHound.Http.Service
{
    /// <summary>
    /// Base for Services that route requests to another TrakHound Instance using its HTTP Services API (_services)
    /// </summary>
    public abstract class HttpService : TrakHoundService, IDisposable
    {
        protected const string NotConfiguredMessage = "Remote Instance Not Configured (hostname)";

        private readonly TrakHoundHttpClient _client;


        /// <summary>
        /// Client for the remote Instance (null if the Hostname is not configured)
        /// </summary>
        protected TrakHoundHttpClient Client => _client;

        /// <summary>
        /// The Service Client (on the remote Instance) that this Service routes to
        /// </summary>
        protected abstract ITrakHoundServiceClient ServiceClient { get; }


        public HttpService(ITrakHoundServiceConfiguration configuration) : base(configuration)
        {
            if (configuration != null)
            {
                var hostname = configuration.GetParameter("hostname");
                if (!string.IsNullOrEmpty(hostname))
                {
                    var port = configuration.GetParameter<int>("port");
                    if (port <= 0) port = TrakHoundHttpClientConfiguration.DefaultPort;

                    var path = configuration.GetParameter("path");
                    if (string.IsNullOrEmpty(path)) path = TrakHoundHttpClientConfiguration.DefaultPath;

                    var useSsl = configuration.GetParameter<bool>("useSsl");

                    // Router on the remote Instance to route requests to (null = remote Instance's default Router)
                    var routerId = configuration.GetParameter("routerId");

                    // An API Key of the remote Instance (needed once its Service operations are protected, or to call it from another machine)
                    var clientConfiguration = new TrakHoundHttpClientConfiguration(hostname, port, path, useSsl);
                    clientConfiguration.ApiKey = configuration.GetParameter("apiKey");

                    _client = new TrakHoundHttpClient(clientConfiguration, routerId);
                }
            }
        }


        /// <summary>
        /// Returns the remote response, or Not Available if the remote Instance could not be reached (no Status Code)
        /// </summary>
        protected TrakHoundServiceResponse Forward(TrakHoundServiceResponse response, CancellationToken cancellationToken = default)
        {
            if (response.StatusCode > 0) return response;

            // The client returns no Status Code (instead of throwing) when the request is cancelled
            cancellationToken.ThrowIfCancellationRequested();

            return NotAvailable(GetUnreachableMessage(response.Message));
        }

        /// <summary>
        /// Returns the remote response, or Not Available if the remote Instance could not be reached (no Status Code)
        /// </summary>
        protected TrakHoundServiceResponse<TResult> Forward<TResult>(TrakHoundServiceResponse<TResult> response, CancellationToken cancellationToken = default)
        {
            if (response.StatusCode > 0) return response;

            cancellationToken.ThrowIfCancellationRequested();

            return NotAvailable<TResult>(GetUnreachableMessage(response.Message));
        }

        private string GetUnreachableMessage(string message)
        {
            return $"Remote Instance ({_client?.HttpBaseUrl}) Unreachable : {message}";
        }


        public override async Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default)
        {
            if (ServiceClient != null)
            {
                var serviceResponse = await ServiceClient.CheckAvailability(cancellationToken: cancellationToken);
                if (serviceResponse.Success && serviceResponse.Result != null)
                {
                    return Ok(serviceResponse.Result);
                }
                else
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    // The Service is Unavailable (not the Availability request) when the remote Instance can't be reached or has no matching Service
                    var response = new TrakHoundServiceAvailabilityResponse();
                    response.Message = $"Remote Instance ({_client.HttpBaseUrl}) : {serviceResponse.StatusCode} : {serviceResponse.Message}";
                    return Ok(response);
                }
            }
            else
            {
                var response = new TrakHoundServiceAvailabilityResponse();
                response.Message = NotConfiguredMessage;
                return Ok(response);
            }
        }
    }
}
