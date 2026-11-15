// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using TrakHound.Instance.Http;

namespace TrakHound.Blazor.Routing
{
    public class TrakHoundInstanceRouter : TrakHoundInstanceRouter<TrakHoundRouteData> { }

    /// <summary>
    /// Routes to the Instance's own (Admin) Pages
    /// </summary>
    public abstract class TrakHoundInstanceRouter<TRouteData> : TrakHoundRouterBase<TRouteData> where TRouteData : TrakHoundRouteData, new()
    {
        [Inject]
        TrakHoundInstancePageRouteManager RouteManager { get; set; }


        protected override ITrakHoundPageRouteManager GetRouteManager()
        {
            // The Admin Pages render Not Found when the Admin Interface is disabled (ex. navigating from the Error Page), and for
            // connections on the HTTP Port when there is a separate Admin Port, and for clients that aren't an Allowed Address (checked here
            // too since navigating inside the Circuit doesn't make a Request)
            if (Instance?.Configuration != null && !Instance.Configuration.AdminInterfaceEnabled) return null;
            if (!TrakHoundAdminPort.IsAdminConnection(LocalPort)) return null;
            if (!TrakHoundAdminAddresses.IsAllowed(RemoteAddress, Instance?.Configuration?.AdminAllowedAddresses)) return null;

            return RouteManager;
        }

        protected override string GetConnectionPathName(TRouteData routeData)
        {
            return "Admin";
        }
    }
}
