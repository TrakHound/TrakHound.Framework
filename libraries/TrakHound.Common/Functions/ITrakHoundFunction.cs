// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Clients;
using TrakHound.Logging;
using TrakHound.Volumes;

namespace TrakHound.Functions
{
    /// <summary>
    /// A Function that is Run by an Instance with a set of Parameters and returns a Response
    /// </summary>
    public interface ITrakHoundFunction : IDisposable
    {
        /// <summary>
        /// The unique Identifier of the Function
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The Id of the Instance the Function is running in
        /// </summary>
        string InstanceId { get; }

        /// <summary>
        /// The Configuration the Function was created from
        /// </summary>
        ITrakHoundFunctionConfiguration Configuration { get; }

        /// <summary>
        /// The Client used to access the Instance (Entities, Apis, etc.)
        /// </summary>
        ITrakHoundClient Client { get; }

        /// <summary>
        /// The Volume used to store Files for the Function
        /// </summary>
        ITrakHoundVolume Volume { get; }

        /// <summary>
        /// Callback invoked when the Function writes a Log Item
        /// </summary>
        Action<TrakHoundLogItem> OnLogReceived { get; set; }

        /// <summary>
        /// Raised when the Function writes a Log Item
        /// </summary>
        event EventHandler<TrakHoundLogItem> LogReceived;


        /// <summary>
        /// Run the Function with the specified Parameters. The cancellationToken is signaled when the Run is Cancelled
        /// </summary>
        Task<TrakHoundFunctionResponse> Run(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken = default);
    }
}
