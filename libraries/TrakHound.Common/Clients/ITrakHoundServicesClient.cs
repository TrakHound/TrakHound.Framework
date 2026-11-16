// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients.Services;

namespace TrakHound.Clients
{
    /// <summary>
    /// Client used to access the Services, Drivers, and Routers of a TrakHound Instance
    /// </summary>
    public interface ITrakHoundServicesClient
    {
        /// <summary>
        /// Client used to query the Service Drivers loaded on the Instance
        /// </summary>
        ITrakHoundServiceDriversClient Drivers { get; }

        /// <summary>
        /// Client used to query the Service Routers configured on the Instance
        /// </summary>
        ITrakHoundServiceRoutersClient Routers { get; }

        /// <summary>
        /// The Service and Router Configurations (null if the Client doesn't support them)
        /// </summary>
        ITrakHoundServiceConfigurationsClient Configurations { get; }

        /// <summary>
        /// Client used to access the Blob Service
        /// </summary>
        ITrakHoundBlobServiceClient Blobs { get; }

        /// <summary>
        /// Client used to access the KeyValue Service
        /// </summary>
        ITrakHoundKeyValueServiceClient KeyValues { get; }

        /// <summary>
        /// Client used to access the Message Service
        /// </summary>
        ITrakHoundMessageServiceClient Messages { get; }

        /// <summary>
        /// Client used to access the Message Queue Service
        /// </summary>
        ITrakHoundMessageQueueServiceClient MessageQueues { get; }

        /// <summary>
        /// Client used to access the Table Service
        /// </summary>
        ITrakHoundTableServiceClient Tables { get; }

        /// <summary>
        /// Client used to access the Time Series Service
        /// </summary>
        ITrakHoundTimeSeriesServiceClient TimeSeries { get; }
    }
}
