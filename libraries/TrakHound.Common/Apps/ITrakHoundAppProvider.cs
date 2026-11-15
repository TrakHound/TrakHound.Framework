// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Apps
{
    /// <summary>
    /// Loads the Apps configured on the Instance and provides their Routes, Information, Static Files, and Metrics
    /// </summary>
    public interface ITrakHoundAppProvider
    {
        /// <summary>
        /// The Manager that creates the Injection Services loaded from App Packages
        /// </summary>
        ITrakHoundAppInjectionServiceManager InjectionServiceManager { get; }

        /// <summary>
        /// The paths of the Stylesheets (.css) from every loaded App Package
        /// </summary>
        IEnumerable<string> PackageStylesheets { get; }

        /// <summary>
        /// The paths of the Scripts (.js) from every loaded App Package
        /// </summary>
        IEnumerable<string> PackageScripts { get; }


        /// <summary>
        /// Raised when the open pages of an App are requested to Refresh (the ID is the App ID)
        /// </summary>
        event TrakHoundAppRefreshHandler AppRefreshRequested;

        /// <summary>
        /// Raised when the page of a single Client Connection is requested to Refresh (the ID is the Connection ID)
        /// </summary>
        event TrakHoundAppRefreshHandler ConnectionRefreshRequested;


        /// <summary>
        /// Reloads the Injection Services and Route for the specified App Configuration. Returns true if the App was found and reloaded
        /// </summary>
        bool ReloadConfiguration(string appId);

        /// <summary>
        /// Requests every open page of the specified App to Refresh. Returns false if the App ID is null or empty
        /// </summary>
        bool RefreshApp(string appId, int delay);

        /// <summary>
        /// Requests the page of the specified Client Connection to Refresh. Returns false if the Connection ID is null or empty
        /// </summary>
        bool RefreshConnection(string connectionId, int delay);

        /// <summary>
        /// Returns true if the Route matches a Page of a loaded App (a null or empty Route is treated as "/")
        /// </summary>
        bool IsRouteValid(string route);

        /// <summary>
        /// Converts a requested URL to a path relative to the Instance's Base Path and applies the configured Route Rewrites
        /// </summary>
        string RewriteUrl(string url);


        /// <summary>
        /// Gets the Information for each loaded App
        /// </summary>
        IEnumerable<TrakHoundAppInformation> GetInformation();

        /// <summary>
        /// Gets the Information for the specified App (null if not found)
        /// </summary>
        TrakHoundAppInformation GetInformation(string appId);

        /// <summary>
        /// Gets the Information for the App Page that matches the specified Route (null if no Page matches)
        /// </summary>
        TrakHoundAppPageInformation GetPageInformation(string route);


        /// <summary>
        /// Gets the paths of the Stylesheets (.css) from the specified App's Package
        /// </summary>
        IEnumerable<string> GetAppStylesheets(string appId);

        /// <summary>
        /// Gets the paths of the Scripts (.js) from the specified App's Package
        /// </summary>
        IEnumerable<string> GetAppScripts(string appId);


        /// <summary>
        /// Gets the Page View Metrics for each App that has been viewed
        /// </summary>
        IEnumerable<TrakHoundAppMetrics> GetMetrics() => null;

        /// <summary>
        /// Gets the Page View Metrics for the specified App (null if it has not been viewed)
        /// </summary>
        TrakHoundAppMetrics GetMetrics(string appId) => null;

        /// <summary>
        /// Gets the Page View Metrics for each Client Connection (optionally only those that viewed the specified App)
        /// </summary>
        IEnumerable<TrakHoundAppConnectionMetrics> GetConnectionMetrics(string appId = null) => null;
    }
}
