// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients.Services;

namespace TrakHound.Clients
{
    /// <summary>
    /// HTTP implementation of ITrakHoundServicesClient that groups the HTTP Service, Driver, and Router Clients
    /// </summary>
    public class TrakHoundHttpServicesClient : ITrakHoundServicesClient
    {
        private readonly TrakHoundHttpServiceDriversClient _drivers;
        private readonly TrakHoundHttpServiceRoutersClient _routers;
        private readonly TrakHoundHttpServiceConfigurationsClient _configurations;
        private readonly TrakHoundHttpBlobServiceClient _blobs;
        private readonly TrakHoundHttpKeyValueServiceClient _keyValues;
        private readonly TrakHoundHttpMessageServiceClient _messages;
        private readonly TrakHoundHttpMessageQueueServiceClient _messageQueues;
        private readonly TrakHoundHttpTableServiceClient _tables;
        private readonly TrakHoundHttpTimeSeriesServiceClient _timeSeries;


        /// <inheritdoc/>
        public ITrakHoundServiceDriversClient Drivers => _drivers;

        /// <inheritdoc/>
        public ITrakHoundServiceRoutersClient Routers => _routers;

        /// <inheritdoc/>
        public ITrakHoundServiceConfigurationsClient Configurations => _configurations;

        /// <inheritdoc/>
        public ITrakHoundBlobServiceClient Blobs => _blobs;

        /// <inheritdoc/>

        /// <inheritdoc/>
        public ITrakHoundKeyValueServiceClient KeyValues => _keyValues;

        /// <inheritdoc/>
        public ITrakHoundMessageServiceClient Messages => _messages;

        /// <inheritdoc/>
        public ITrakHoundMessageQueueServiceClient MessageQueues => _messageQueues;

        /// <inheritdoc/>
        public ITrakHoundTableServiceClient Tables => _tables;

        /// <inheritdoc/>
        public ITrakHoundTimeSeriesServiceClient TimeSeries => _timeSeries;


        /// <summary>
        /// Creates the Service Clients using the connection of the specified base Client
        /// </summary>
        public TrakHoundHttpServicesClient(TrakHoundHttpClient baseClient)
        {
            _drivers = new TrakHoundHttpServiceDriversClient(baseClient);
            _routers = new TrakHoundHttpServiceRoutersClient(baseClient);
            _configurations = new TrakHoundHttpServiceConfigurationsClient(baseClient);
            _blobs = new TrakHoundHttpBlobServiceClient(baseClient);
            _keyValues = new TrakHoundHttpKeyValueServiceClient(baseClient);
            _messages = new TrakHoundHttpMessageServiceClient(baseClient);
            _messageQueues = new TrakHoundHttpMessageQueueServiceClient(baseClient);
            _tables = new TrakHoundHttpTableServiceClient(baseClient);
            _timeSeries = new TrakHoundHttpTimeSeriesServiceClient(baseClient);
        }
    }
}
