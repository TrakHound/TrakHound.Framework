// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Apps;

namespace TrakHound.Blazor.Routing
{
    public interface ITrakHoundPageRouteManager
    {
        IEnumerable<Route> Routes { get; }

        //event EventHandler<string> AppUpdated; // AppId as argument


        //void AddApp(ITrakHoundAppConfiguration configuration);

        //void RemoveApp(string configurationId);


        void ClearPages();

        /// <summary>
        /// Builds (or rebuilds) the Routes
        /// </summary>
        void Initialise();

        /// <summary>
        /// Builds the Routes if they haven't been built yet. Routers call this for every page, so it does nothing once the Routes are built
        /// </summary>
        void EnsureInitialised() => Initialise();

        MatchResult Match(string url);
    }
}
