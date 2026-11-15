// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients;
using TrakHound.Clients.Services;
using TrakHound.Services;
using TrakHound.Services.Blobs;
using TrakHound.Services.KeyValues;
using TrakHound.Services.Tables;
using TrakHound.Services.TimeSeries;
using TrakHound.Testing.Services;

namespace TrakHound.Testing.Clients
{
    /// <summary>
    /// Client.Services for tests. KeyValues, Blobs and TimeSeries are in memory by default; Use() puts a Service (ex. your own) behind the families
    /// it implements. Set a property to your own implementation for anything else (Messages, MessageQueues, Routers, Drivers)
    /// </summary>
    public class TrakHoundTestServicesClient : ITrakHoundServicesClient
    {
        public ITrakHoundKeyValueServiceClient KeyValues { get; set; }

        public ITrakHoundBlobServiceClient Blobs { get; set; }

        public ITrakHoundTableServiceClient Tables { get; set; }

        public ITrakHoundTimeSeriesServiceClient TimeSeries { get; set; }

        public ITrakHoundMessageServiceClient Messages { get; set; }

        public ITrakHoundMessageQueueServiceClient MessageQueues { get; set; }

        public ITrakHoundServiceRoutersClient Routers { get; set; }

        public ITrakHoundServiceDriversClient Drivers { get; set; }


        public TrakHoundTestServicesClient()
        {
            KeyValues = new TrakHoundTestKeyValueServiceClient(new TrakHoundMemoryKeyValueService());
            Blobs = new TrakHoundTestBlobServiceClient(new TrakHoundMemoryBlobService());
            Tables = TrakHoundNotSupportedProxy.Create<ITrakHoundTableServiceClient>("Services.Tables");
            TimeSeries = new TrakHoundTestTimeSeriesServiceClient(new TrakHoundMemoryTimeSeriesService());
            Messages = TrakHoundNotSupportedProxy.Create<ITrakHoundMessageServiceClient>("Services.Messages");
            MessageQueues = TrakHoundNotSupportedProxy.Create<ITrakHoundMessageQueueServiceClient>("Services.MessageQueues");
            Routers = TrakHoundNotSupportedProxy.Create<ITrakHoundServiceRoutersClient>("Services.Routers");
            Drivers = TrakHoundNotSupportedProxy.Create<ITrakHoundServiceDriversClient>("Services.Drivers");
        }


        /// <summary>
        /// Calls the Service for each family it implements (Key Values, Blobs, Tables and Time Series). Returns this Client
        /// </summary>
        public TrakHoundTestServicesClient Use(TrakHoundService service)
        {
            if (service == null) throw new ArgumentNullException(nameof(service));

            var used = false;
            if (service is ITrakHoundKeyValueService keyValueService) { KeyValues = new TrakHoundTestKeyValueServiceClient(keyValueService); used = true; }
            if (service is ITrakHoundBlobService blobService) { Blobs = new TrakHoundTestBlobServiceClient(blobService); used = true; }
            if (service is ITrakHoundTableService tableService) { Tables = new TrakHoundTestTableServiceClient(tableService); used = true; }
            if (service is ITrakHoundTimeSeriesService timeSeriesService) { TimeSeries = new TrakHoundTestTimeSeriesServiceClient(timeSeriesService); used = true; }

            if (!used) throw new ArgumentException($"{service.GetType().Name} doesn't implement a Service family the test Client supports (Key Values, Blobs, Tables or Time Series). Set Messages or MessageQueues to your own Client instead", nameof(service));
            return this;
        }
    }
}
