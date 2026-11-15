// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;

namespace TrakHound.Apps
{
    public abstract class TrakHoundAppInjectionServicesBase<TServiceBase> : IDisposable where TServiceBase : class, ITrakHoundAppInjectionService
    {
        private readonly string _scopeId;
        private readonly ITrakHoundInstance _instance;


        public string ScopeId => _scopeId;


        public TrakHoundAppInjectionServicesBase(ITrakHoundInstance instance)
        {
            _scopeId = Guid.NewGuid().ToString();
            _instance = instance;
        }

        public void Dispose()
        {
            var injectionServiceManager = _instance.AppProvider?.InjectionServiceManager;
            if (injectionServiceManager != null)
            {
                injectionServiceManager.DisposeScope(_scopeId);
            }
        }


        /// <summary>
        /// Record that this Scope (used as the Session for a Browser connection) is using the App Version the Service Type belongs to
        /// </summary>
        public void AddServiceSession(Type serviceType)
        {
            var injectionServiceManager = _instance.AppProvider?.InjectionServiceManager as TrakHoundAppInjectionServiceManager;
            injectionServiceManager?.AddServiceSession(_scopeId, serviceType);
        }

        /// <summary>
        /// Record that this Scope (used as the Session for a Browser connection) is using the App Configuration's Package Version
        /// </summary>
        public void AddSession(string configurationId, string packageId, string packageVersion)
        {
            var injectionServiceManager = _instance.AppProvider?.InjectionServiceManager as TrakHoundAppInjectionServiceManager;
            injectionServiceManager?.AddSession(_scopeId, configurationId, packageId, packageVersion);
        }


        public TService Get<TService>(string appId) where TService : class, ITrakHoundAppInjectionService
        {
            return (TService)Get(appId, typeof(TService));
        }

        public object Get(string appId, Type requestedType)
        {
            if (!string.IsNullOrEmpty(appId))
            {
                var injectionServiceManager = _instance.AppProvider.InjectionServiceManager;
                if (injectionServiceManager != null)
                {
                    return injectionServiceManager.GetService<TServiceBase>(_scopeId, requestedType);
                }
            }

            return null;
        }
    }
}
