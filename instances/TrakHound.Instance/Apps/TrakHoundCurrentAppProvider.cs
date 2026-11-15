// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;

namespace TrakHound.Apps
{
    /// <summary>
    /// Forwards to the Instance's current App Provider (which is replaced each time the Instance is Started).
    /// Registered for Dependency Injection in place of the App Provider itself, since a registered App Provider would go stale
    /// after a restart. Intentionally not IDisposable, so the container can never Dispose the Instance's App Provider.
    /// </summary>
    internal sealed class TrakHoundCurrentAppProvider : ITrakHoundAppProvider
    {
        private readonly ITrakHoundInstance _instance;


        private ITrakHoundAppProvider Current => _instance.AppProvider;

        public ITrakHoundAppInjectionServiceManager InjectionServiceManager => Current?.InjectionServiceManager;

        public IEnumerable<string> PackageStylesheets => Current?.PackageStylesheets ?? Enumerable.Empty<string>();

        public IEnumerable<string> PackageScripts => Current?.PackageScripts ?? Enumerable.Empty<string>();


        // Events subscribe to the App Provider that is current at the time of subscription
        public event TrakHoundAppRefreshHandler AppRefreshRequested
        {
            add { if (Current != null) Current.AppRefreshRequested += value; }
            remove { if (Current != null) Current.AppRefreshRequested -= value; }
        }

        public event TrakHoundAppRefreshHandler ConnectionRefreshRequested
        {
            add { if (Current != null) Current.ConnectionRefreshRequested += value; }
            remove { if (Current != null) Current.ConnectionRefreshRequested -= value; }
        }


        public TrakHoundCurrentAppProvider(ITrakHoundInstance instance)
        {
            _instance = instance;
        }


        public bool ReloadConfiguration(string appId) => Current?.ReloadConfiguration(appId) ?? false;

        public bool RefreshApp(string appId, int delay) => Current?.RefreshApp(appId, delay) ?? false;

        public bool RefreshConnection(string connectionId, int delay) => Current?.RefreshConnection(connectionId, delay) ?? false;

        public bool IsRouteValid(string route) => Current?.IsRouteValid(route) ?? false;

        public string RewriteUrl(string url) => Current?.RewriteUrl(url);

        public IEnumerable<TrakHoundAppInformation> GetInformation() => Current?.GetInformation();

        public TrakHoundAppInformation GetInformation(string appId) => Current?.GetInformation(appId);

        public TrakHoundAppPageInformation GetPageInformation(string route) => Current?.GetPageInformation(route);

        public IEnumerable<string> GetAppStylesheets(string appId) => Current?.GetAppStylesheets(appId);

        public IEnumerable<string> GetAppScripts(string appId) => Current?.GetAppScripts(appId);

        public IEnumerable<TrakHoundAppMetrics> GetMetrics() => Current?.GetMetrics();

        public TrakHoundAppMetrics GetMetrics(string appId) => Current?.GetMetrics(appId);

        public IEnumerable<TrakHoundAppConnectionMetrics> GetConnectionMetrics(string appId = null) => Current?.GetConnectionMetrics(appId);
    }
}
