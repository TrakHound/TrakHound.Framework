// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;

namespace TrakHound.Services
{
    /// <summary>
    /// Provides access to the Services (loaded from Service Packages) and the Service Routers used to access them
    /// </summary>
    public class TrakHoundServicesProvider : IDisposable
    {
        private readonly TrakHoundServiceProvider _serviceProvider;
        private readonly TrakHoundServiceRouterProvider _routerProvider;


        /// <summary>
        /// The Services (ex. Table, Key Value, Blob, Message drivers). Named "Drivers" for compatibility.
        /// </summary>
        public TrakHoundServiceProvider Drivers => _serviceProvider;

        /// <summary>
        /// The Service Routers
        /// </summary>
        public TrakHoundServiceRouterProvider Routers => _routerProvider;


        public TrakHoundServicesProvider(TrakHoundServiceProvider serviceProvider, TrakHoundServiceRouterProvider routerProvider)
        {
            _serviceProvider = serviceProvider;
            _routerProvider = routerProvider;
        }

        public void Dispose()
        {
            // Dispose the Routers first so nothing is routed to a Service after it is Disposed
            if (_routerProvider != null) _routerProvider.Dispose();
            if (_serviceProvider != null) _serviceProvider.Dispose();
        }
    }
}
