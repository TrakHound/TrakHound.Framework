// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Blazor.Apps;
using TrakHound.Instance.Apps;
using TrakHound.Instance.Http;

namespace TrakHound.Blazor.Routing
{
    public class TrakHoundAppRouter : TrakHoundAppRouter<TrakHoundRouteData> { }

    /// <summary>
    /// Routes to the Pages of the Apps loaded by the Instance's App Provider
    /// </summary>
    public abstract class TrakHoundAppRouter<TRouteData> : TrakHoundRouterBase<TRouteData> where TRouteData : TrakHoundRouteData, new()
    {
        protected override ITrakHoundPageRouteManager GetRouteManager()
        {
            // Apps not served over HTTP render Not Found
            if (Instance?.Configuration != null && !Instance.Configuration.AppsHttpEnabled) return null;

            // With a separate Admin Port, Apps render Not Found for connections on the Admin Port (so they never run in the Admin Interface's origin)
            if (!TrakHoundAdminPort.IsHttpConnection(LocalPort)) return null;

            return (Instance?.AppProvider as TrakHoundAppProvider)?.RouteManager;
        }

        protected override string GetConnectionPathName(TRouteData routeData)
        {
            return routeData.Package?.Id;
        }

        private TrakHoundAppMetricsCollector Metrics => (Instance?.AppProvider as TrakHoundAppProvider)?.Metrics;

        protected override void OnNavigated(TRouteData routeData, string relativeUri)
        {
            // Page Views and Durations for the Client Connection (the Prerender isn't a View, the Circuit records it)
            if (IsPrerendering) return;

            if (routeData != null)
            {
                Metrics?.PageViewed(ConnectionId, Connection, routeData.AppId, routeData.AppName, routeData.Package?.Id, routeData.Data?.Template, relativeUri);
            }
            else
            {
                Metrics?.PageEnded(ConnectionId);
            }
        }

        protected override void OnDisposed()
        {
            // The Connection left the Apps (ex. navigated to the Admin pages) or disconnected
            if (!IsPrerendering) Metrics?.PageEnded(ConnectionId);
        }
    }
}
