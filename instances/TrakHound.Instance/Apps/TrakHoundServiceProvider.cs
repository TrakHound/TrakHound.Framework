// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;

namespace TrakHound.Apps
{
    internal class TrakHoundServiceProvider : IServiceProvider
    {
        private readonly string _scopeId;
        private readonly ITrakHoundAppInjectionServiceManager _serviceManager;
        private readonly Dictionary<Type, object> _services = new Dictionary<Type, object>();
        private readonly object _lock = new object();


        public TrakHoundServiceProvider(string scopeId, ITrakHoundAppInjectionServiceManager serviceManager)
        {
            _scopeId = scopeId;
            _serviceManager = serviceManager;
        }


        public object GetService(Type serviceType)
        {
            if (_serviceManager != null && _serviceManager.ServiceProvider != null)
            {
                var service = _serviceManager.ServiceProvider.GetService(serviceType);

                if (service == null)
                {
                    service = _serviceManager.GetService(_scopeId, serviceType);
                }

                if (service == null)
                {
                    lock (_lock)
                    {
                        service = _services.GetValueOrDefault(serviceType);
                    }
                }

                return service;
            }

            return null;
        }


        public void AddService(object service)
        {
            if (service != null)
            {
                var type = service.GetType();

                lock (_lock)
                {
                    _services.Remove(type);
                    _services.Add(type, service);
                }
            }
        }

        public void AddService<TService>(object service)
        {
            if (service != null)
            {
                var type = typeof(TService);

                lock (_lock)
                {
                    _services.Remove(type);
                    _services.Add(type, service);
                }
            }
        }

        public void AddService(Type type, object service)
        {
            if (type != null && service != null)
            {
                lock (_lock)
                {
                    _services.Remove(type);
                    _services.Add(type, service);
                }
            }
        }
    }
}
