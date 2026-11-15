// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Logging;
using TrakHound.Volumes;

namespace TrakHound.Workers
{
    /// <summary>
    /// A Worker that runs in the background on an Instance (started and stopped by the Worker Engine)
    /// </summary>
    public interface ITrakHoundWorker : IDisposable
    {
        /// <summary>
        /// The unique Identifier of the Worker
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The Identifier of the Instance the Worker is running on
        /// </summary>
        string InstanceId { get; }

        /// <summary>
        /// The Configuration the Worker was created from
        /// </summary>
        ITrakHoundWorkerConfiguration Configuration { get; }

        /// <summary>
        /// The Client the Worker uses to access the Instance
        /// </summary>
        ITrakHoundClient Client { get; }

        /// <summary>
        /// The Volume the Worker uses to store files
        /// </summary>
        ITrakHoundVolume Volume { get; }

        /// <summary>
        /// The most recently reported Status of the Worker
        /// </summary>
        TrakHoundWorkerStatus Status { get; }


        /// <summary>
        /// Raised when the Worker reports a new Status
        /// </summary>
        event EventHandler<TrakHoundWorkerStatusType> StatusReceived;

        /// <summary>
        /// Raised when the Worker writes a Log Item
        /// </summary>
        event EventHandler<TrakHoundLogItem> LogReceived;


        /// <summary>
        /// Starts the Worker
        /// </summary>
        Task Start();

        /// <summary>
        /// Stops the Worker
        /// </summary>
        Task Stop();
    }
}
