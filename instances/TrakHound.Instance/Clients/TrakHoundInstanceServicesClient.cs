// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients.Services;
using TrakHound.Instances;
using TrakHound.Services;

namespace TrakHound.Clients
{
    internal class TrakHoundInstanceServicesClient : ITrakHoundServicesClient
    {
        private readonly TrakHoundInstanceClient _baseClient;
        private readonly TrakHoundInstanceServiceDriversClient _drivers;
        private readonly TrakHoundInstanceServiceRoutersClient _routers;
        private readonly TrakHoundInstanceServiceConfigurationsClient _configurations;
        private readonly TrakHoundInstanceBlobServiceClient _blobs;
        private readonly TrakHoundInstanceKeyValueServiceClient _keyValues;
        private readonly TrakHoundInstanceMessageServiceClient _messages;
        private readonly TrakHoundInstanceMessageQueueServiceClient _messageQueues;
        private readonly TrakHoundInstanceTableServiceClient _tables;
        private readonly TrakHoundInstanceTimeSeriesServiceClient _timeSeries;


        public ITrakHoundServiceDriversClient Drivers => _drivers;

        public ITrakHoundServiceRoutersClient Routers => _routers;

        public ITrakHoundServiceConfigurationsClient Configurations => _configurations;

        public ITrakHoundBlobServiceClient Blobs => _blobs;


        public ITrakHoundKeyValueServiceClient KeyValues => _keyValues;

        public ITrakHoundMessageServiceClient Messages => _messages;

        public ITrakHoundMessageQueueServiceClient MessageQueues => _messageQueues;

        public ITrakHoundTableServiceClient Tables => _tables;

        public ITrakHoundTimeSeriesServiceClient TimeSeries => _timeSeries;


        public TrakHoundInstanceServicesClient(
            TrakHoundInstanceClient baseClient,
            ITrakHoundInstance instance,
            TrakHoundServicesProvider servicesProvider
            )
        {
            _baseClient = baseClient;
            _drivers = new TrakHoundInstanceServiceDriversClient(servicesProvider?.Drivers, servicesProvider?.Routers?.Metrics);
            _routers = new TrakHoundInstanceServiceRoutersClient(servicesProvider?.Routers);
            _configurations = new TrakHoundInstanceServiceConfigurationsClient(instance, baseClient);
            _blobs = new TrakHoundInstanceBlobServiceClient(baseClient);
            _keyValues = new TrakHoundInstanceKeyValueServiceClient(baseClient);
            _messages = new TrakHoundInstanceMessageServiceClient(baseClient);
            _messageQueues = new TrakHoundInstanceMessageQueueServiceClient(baseClient);
            _tables = new TrakHoundInstanceTableServiceClient(baseClient);
            _timeSeries = new TrakHoundInstanceTimeSeriesServiceClient(baseClient);
        }
    }
}
