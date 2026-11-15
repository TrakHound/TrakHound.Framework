// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Apps;
using TrakHound.Functions;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Services;
using TrakHound.Volumes;
using TrakHound.Workers;
using TrakHound.Zones;

namespace TrakHound.Clients
{
    public partial class TrakHoundInstanceClient : ITrakHoundClient, ITrakHoundModuleTokenIssuer, IDisposable
    {
        private readonly ITrakHoundInstance _instance;
        private readonly ITrakHoundAppProvider _appProvider;
        private readonly TrakHoundFunctionManager _functionManager;
        private readonly TrakHoundWorkerManager _workerManager;
        private readonly TrakHoundServicesProvider _servicesProvider;
        private readonly ITrakHoundVolumeProvider _volumeProvider;

        // A Client is created for most Requests and usually only uses one or two of these, so each is created when it is first used
        // (the Api Client is always created, since it holds the Request Source)
        private readonly TrakHoundInstanceApiClient _api;
        private TrakHoundInstanceAppsClient _apps;
        private TrakHoundInstanceFunctionsClient _functions;
        private TrakHoundInstanceWorkersClient _workers;
        private TrakHoundInstanceBuffersClient _buffers;
        private TrakHoundInstanceModulesClient _modules;
        private TrakHoundInstanceStreamsClient _streams;
        private TrakHoundInstanceMcpClient _mcp;
        private TrakHoundInstanceHttpRequestsClient _httpRequests;
        private TrakHoundInstanceServicesClient _services;
        private TrakHoundInstanceVolumesClient _volumes;
        private TrakHoundInstanceLogsClient _logs;
        private TrakHoundInstanceInstancesClient _instances;
        private bool _requestSourceLocked;


        public string RouterId { get; set; }

        public ITrakHoundInstance Instance => _instance;

        public ITrakHoundApiClient Api => _api;

        /// <summary>
        /// What Requests made through this Client are attributed to (for API Metrics and Zones).
        /// Once set to a Module (an Api, App, Function or Worker) it can't be changed, so a Module can't make calls as something else
        /// </summary>
        public TrakHoundApiRequestSource RequestSource
        {
            get => _api.Source;
            set
            {
                if (_requestSourceLocked) return;

                _api.Source = value ?? TrakHoundApiRequestSource.Instance;
                _requestSourceLocked = TrakHoundZoneResources.GetMemberId(_api.Source) != null;
            }
        }

        public ITrakHoundAppsClient Apps => LazyInitializer.EnsureInitialized(ref _apps, () => new TrakHoundInstanceAppsClient(this, _appProvider, _instance.LogProvider));

        public ITrakHoundFunctionsClient Functions => LazyInitializer.EnsureInitialized(ref _functions, () => new TrakHoundInstanceFunctionsClient(this, _functionManager));

        public ITrakHoundWorkersClient Workers => LazyInitializer.EnsureInitialized(ref _workers, () => new TrakHoundInstanceWorkersClient(this, _workerManager, _instance.LogProvider));

        public ITrakHoundBuffersClient Buffers => LazyInitializer.EnsureInitialized(ref _buffers, () => new TrakHoundInstanceBuffersClient());

        public ITrakHoundModulesClient Modules => LazyInitializer.EnsureInitialized(ref _modules, () => new TrakHoundInstanceModulesClient(_instance.ModuleProvider));

        public ITrakHoundStreamsClient Streams => LazyInitializer.EnsureInitialized(ref _streams, () => new TrakHoundInstanceStreamsClient(_instance));

        public ITrakHoundMcpClient Mcp => LazyInitializer.EnsureInitialized(ref _mcp, () => new TrakHoundInstanceMcpClient(_instance));

        public ITrakHoundHttpRequestsClient HttpRequests => LazyInitializer.EnsureInitialized(ref _httpRequests, () => new TrakHoundInstanceHttpRequestsClient(_instance));

        public ITrakHoundServicesClient Services => LazyInitializer.EnsureInitialized(ref _services, () => new TrakHoundInstanceServicesClient(this, _instance, _servicesProvider));

        public ITrakHoundVolumesClient Volumes => LazyInitializer.EnsureInitialized(ref _volumes, () => new TrakHoundInstanceVolumesClient(this, _volumeProvider));

        public ITrakHoundLogsClient Logs => LazyInitializer.EnsureInitialized(ref _logs, () => new TrakHoundInstanceLogsClient(_instance.LogProvider));

        public ITrakHoundInstancesClient Instances => LazyInitializer.EnsureInitialized(ref _instances, () => new TrakHoundInstanceInstancesClient(this, _instance));


        public TrakHoundInstanceClient(
            ITrakHoundInstance instance,
            ITrakHoundApiProvider apiProvider,
            ITrakHoundAppProvider appProvider,
            TrakHoundFunctionManager functionManager,
            TrakHoundWorkerManager workerManager,
            TrakHoundServicesProvider servicesProvider,
            ITrakHoundVolumeProvider volumeProvider
            )
        {
            _instance = instance;
            _appProvider = appProvider;
            _functionManager = functionManager;
            _workerManager = workerManager;
            _servicesProvider = servicesProvider;
            _volumeProvider = volumeProvider;

            _api = new TrakHoundInstanceApiClient(this, apiProvider);
        }

        public void Dispose()
        {
            // Only the Clients that were used were created
            _functions?.Dispose();
            _workers?.Dispose();
            _logs?.Dispose();
            _instances?.Dispose();
        }


        internal TrakHoundServiceRouter GetRouter(string routerId)
        {
            var zoneManager = _instance.ZoneManager;

            // Service calls made after this (in the calling async method) are checked against the Zone of the Module making them
            var caller = TrakHoundZoneResources.GetMemberId(RequestSource) != null ? RequestSource : null;
            TrakHoundZoneContext.Enter(zoneManager, caller);
            EnterAdminContext();

            // A Zone can send every Service call made by its Members through its own Router (ex. to test data)
            var zoneRouterId = caller != null ? zoneManager?.GetRouterId(caller) : null;
            if (zoneRouterId != null) return _instance.ServicesProvider.Routers.GetRouter(zoneRouterId);

            return _instance.ServicesProvider.Routers.GetRouter(!string.IsNullOrEmpty(routerId) ? routerId : RouterId);
        }

        /// <summary>
        /// Whether calls through this Client are made by the Admin Interface itself for a signed in Admin. A Module's Client (ex. an App
        /// the Admin has open) makes its calls as the Module, so they aren't checked against the Admin's Roles
        /// </summary>
        internal bool IsAdminInterfaceCall => TrakHoundAdminContext.Current != null && TrakHoundZoneResources.GetMemberId(RequestSource) == null;

        /// <summary>
        /// Service calls made after this (in the calling async method) by the Admin Interface itself are checked against the signed in Admin's Roles
        /// </summary>
        internal void EnterAdminContext()
        {
            if (IsAdminInterfaceCall) TrakHound.Security.TrakHoundSecurityContext.Begin(new TrakHoundAdminServiceAuthorizer(TrakHoundAdminContext.Current), null);
        }

        /// <summary>
        /// Checks a call made through this Client against the Zones of the caller and the target
        /// </summary>
        internal TrakHoundZoneDecision AuthorizeZone(string targetMemberId, string resourceId, string permission)
        {
            // The Admin Interface's own calls are checked against the signed in Admin's Roles first
            if (IsAdminInterfaceCall)
            {
                var adminResource = TrakHoundAdminSecurity.GetZoneResource(resourceId, permission, out var adminPermission);
                if (adminResource != null)
                {
                    // An Admin whose Role is limited to Zones can only act on what is within them
                    var denial = TrakHoundAdminContext.GetDenial(adminResource, adminPermission, TrakHoundAdminSecurity.GetZoneTarget(targetMemberId, resourceId, permission));
                    if (denial != null) return TrakHoundZoneDecision.Denied(null, denial);
                }
            }

            var zoneManager = _instance.ZoneManager;
            if (zoneManager == null) return TrakHoundZoneDecision.Allowed;

            return zoneManager.Authorize(RequestSource, targetMemberId, resourceId, permission);
        }

        public string GetLink(string route)
        {
            return route;
        }


        /// <summary>
        /// Issues a Module Token for the Module this Client belongs to (its Request Source), using this Client's Router. Null if the Client doesn't belong to a Module
        /// </summary>
        public Task<TrakHoundModuleToken> CreateModuleToken(TimeSpan? lifetime = null)
        {
            return Task.FromResult(_instance.ModuleTokens?.Issue(RequestSource, RouterId, lifetime));
        }

        /// <summary>
        /// Revokes a Module Token issued for the Module this Client belongs to
        /// </summary>
        public Task<bool> RevokeModuleToken(string token)
        {
            if (TrakHoundZoneResources.GetMemberId(RequestSource) == null) return Task.FromResult(false);
            return Task.FromResult(_instance.ModuleTokens?.Revoke(token, RequestSource) ?? false);
        }
    }
}
