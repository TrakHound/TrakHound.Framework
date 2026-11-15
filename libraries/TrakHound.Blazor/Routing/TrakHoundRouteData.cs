// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using TrakHound.Apps;
using TrakHound.Clients;
using TrakHound.Logging;
using TrakHound.Packages;
using TrakHound.Security;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Blazor.Routing
{
    public class TrakHoundRouteData
    {
        public ITrakHoundPageRouteManager RouteManager { get; set; }

        public string AppId { get; set; }

        public string AppName { get; set; }

        public TrakHoundAppConnection AppConnection { get; set; }

        public ITrakHoundClient Client { get; set; }

        public TrakHoundPackage Package { get; set; }

        public ITrakHoundVolume Volume { get; set; }

        public ITrakHoundVariableCollection Variables { get; set; }

        public ITrakHoundLogger Logger { get; set; }

        public ITrakHoundSession Session { get; set; }

        /// <summary>
        /// The App Configuration's Parameters (cascaded as "Parameters", see TrakHoundComponentBase.GetParameter)
        /// </summary>
        public Dictionary<string, object> Parameters { get; set; }

        public string BaseUrl { get; set; }

        public string BasePath { get; set; }

        public string BaseLocation { get; set; }

        public RouteData Data { get; set; }

        /// <summary>
        /// The Layout for Pages that don't set one with @layout (the App's "Layout" component)
        /// </summary>
        public Type DefaultLayout { get; set; }
    }
}
