// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Threading.Tasks;
using TrakHound.Apps;
using TrakHound.Logging;
using TrakHound.Zones;

namespace TrakHound.Clients
{
    public partial class TrakHoundInstanceAppsClient : ITrakHoundAppsClient
    {
        private readonly TrakHoundInstanceClient _baseClient;
        private readonly ITrakHoundAppProvider _appProvider;
        private readonly ITrakHoundLogProvider _logProvider;


        public TrakHoundInstanceAppsClient(TrakHoundInstanceClient baseClient, ITrakHoundAppProvider appProvider, ITrakHoundLogProvider logProvider)
        {
            _baseClient = baseClient;
            _appProvider = appProvider;
            _logProvider = logProvider;
        }


        public async Task<IEnumerable<TrakHoundAppInformation>> GetInformation()
        {
            return _appProvider.GetInformation();
        }

        public async Task<TrakHoundAppInformation> GetInformation(string apiId)
        {
            return _appProvider.GetInformation(apiId);
        }

        public async Task<bool> ReloadConfiguration(string appId)
        {
            if (!IsManageAllowed(appId)) return false;
            return _appProvider.ReloadConfiguration(appId);
        }

        public async Task<bool> RefreshApp(string appId)
        {
            if (!IsManageAllowed(appId)) return false;
            return _appProvider.RefreshApp(appId, 0);
        }

        public async Task<bool> RefreshConnection(string connectionId)
        {
            var zoneDecision = _baseClient.AuthorizeZone(null, TrakHoundZoneResources.GetManageResourceId("app-connection", connectionId), TrakHoundZoneResources.WritePermission);
            if (!zoneDecision.IsAllowed) return false;

            return _appProvider.RefreshConnection(connectionId, 0);
        }

        private bool IsManageAllowed(string appId)
        {
            var targetMemberId = TrakHoundZoneResources.GetMemberId(TrakHoundZoneResources.AppPrefix, appId);
            return _baseClient.AuthorizeZone(targetMemberId, TrakHoundZoneResources.GetManageResourceId(TrakHoundZoneResources.AppPrefix, appId), TrakHoundZoneResources.WritePermission).IsAllowed;
        }


        public Task<IEnumerable<TrakHoundAppMetrics>> GetMetrics()
        {
            return Task.FromResult(_appProvider.GetMetrics());
        }

        public Task<TrakHoundAppMetrics> GetMetrics(string appId)
        {
            return Task.FromResult(_appProvider.GetMetrics(appId));
        }

        public Task<IEnumerable<TrakHoundAppConnectionMetrics>> GetConnectionMetrics(string appId = null)
        {
            return Task.FromResult(_appProvider.GetConnectionMetrics(appId));
        }


        public async Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeToLog(string appId, TrakHoundLogLevel minimumLevel = TrakHoundLogLevel.Information)
        {
            var packageId = !string.IsNullOrEmpty(appId) ? _appProvider.GetInformation(appId)?.PackageId : null;
            if (!string.IsNullOrEmpty(packageId))
            {
                // App Pages write to their Package's Logger (shared by every App that uses the Package)
                var logFilter = new[] { $"app/{packageId}" };
                return TrakHoundInstanceLogSubscription.Subscribe(_logProvider, logFilter, minimumLevel);
            }

            return null;
        }
    }
}
