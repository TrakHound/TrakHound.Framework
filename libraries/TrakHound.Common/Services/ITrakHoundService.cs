// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Logging;
using TrakHound.Volumes;

namespace TrakHound.Services
{
    /// <summary>
    /// Used to access external Infrastructure
    /// </summary>
    public interface ITrakHoundService : IDisposable
    {
        /// <summary>
        /// A unique Identifier for the Service instance
        /// </summary>
        string Id { get; }

        /// <summary>
        /// The name of the Service
        /// </summary>
        string Name { get; }

        //bool IsAvailable { get; }

        //string AvailabilityMessage { get; }

        /// <summary>
        /// The Configuration the Service was created from
        /// </summary>
        ITrakHoundServiceConfiguration Configuration { get; }

        /// <summary>
        /// The Volume the Service can use to store files (null if not set)
        /// </summary>
        ITrakHoundVolume Volume { get; }


        /// <summary>
        /// Raised when the Service writes a Log entry
        /// </summary>
        event EventHandler<TrakHoundLogItem> LogReceived;

        /// <summary>
        /// Checks whether the infrastructure the Service connects to is currently available
        /// </summary>
        Task<TrakHoundServiceResponse<TrakHoundServiceAvailabilityResponse>> CheckAvailability(CancellationToken cancellationToken = default);
    }

    internal static class TrakHoundServiceDriverExtensions
    {
        public static bool IsRouteMatch(this ITrakHoundService service, string route)
        {
            if (service != null && !string.IsNullOrEmpty(route))
            {
                return TrakHoundServiceRoutes.IsMatch(service.GetType(), route);
            }

            return false;
        }
    }
}
