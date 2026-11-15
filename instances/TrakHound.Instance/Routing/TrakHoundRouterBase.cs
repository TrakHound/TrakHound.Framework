// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using TrakHound.Apps;
using TrakHound.Instance.Apps;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Logging;

namespace TrakHound.Blazor.Routing
{
    /// <summary>
    /// Shared Routing logic for the App (TrakHoundAppRouter) and Instance (TrakHoundInstanceRouter) Routers
    /// </summary>
    public abstract class TrakHoundRouterBase<TRouteData> : IComponent, IHandleAfterRender, IDisposable where TRouteData : TrakHoundRouteData, new()
    {
        private ITrakHoundLogger _logger;
        private RenderHandle _renderHandle;
        private bool _initialized;
        private bool _navigationInterceptionEnabled;
        private string _location;
        private bool _navigationIntercepted;
        private string _lastRewriteLocation;
        private int _navigationVersion;
        private bool _rendered;
        private string _renderedAppId;


        protected RenderHandle RenderHandle => _renderHandle;

        /// <summary>
        /// Unique ID of the Client Connection (browser tab) this Router is rendering for
        /// </summary>
        protected string ConnectionId => ConnectionService?.Id;

        protected TrakHoundAppConnection Connection => ConnectionService?.Connection;

        /// <summary>
        /// Whether the Router is Prerendering (the Page is rendered again once the Blazor Circuit connects)
        /// </summary>
        protected bool IsPrerendering => ConnectionService?.IsPrerendering == true;

        /// <summary>
        /// The local Port the Client connected to (null when not known)
        /// </summary>
        protected int? LocalPort => ConnectionService?.LocalPort;

        /// <summary>
        /// The Client's address (null when not known)
        /// </summary>
        protected System.Net.IPAddress RemoteAddress => ConnectionService?.RemoteAddress;


        [Inject]
        private NavigationManager NavigationManager { get; set; }

        [Inject]
        private INavigationInterception NavigationInterception { get; set; }

        [Inject]
        protected ITrakHoundInstance Instance { get; set; }

        [Inject]
        TrakHoundAppConnectionService ConnectionService { get; set; }

        [Inject]
        TrakHoundAppConnectionManager ConnectionManager { get; set; }

        [Inject]
        TrakHoundAuthenticationService AuthenticationService { get; set; }

        [Parameter]
        public RenderFragment NotFound { get; set; }

        /// <summary>
        /// Rendered when the signed in Identity does not have access to the Page
        /// </summary>
        [Parameter]
        public RenderFragment NotAuthorized { get; set; }

        [Parameter]
        public RenderFragment<TRouteData> Found { get; set; }


        protected abstract ITrakHoundPageRouteManager GetRouteManager();

        /// <summary>
        /// The Name the current Path is recorded under for the App Connection
        /// </summary>
        protected abstract string GetConnectionPathName(TRouteData routeData);

        public void Attach(RenderHandle renderHandle)
        {
            _renderHandle = renderHandle;
            _location = NavigationManager.Uri;
            NavigationManager.LocationChanged += HandleLocationChanged;
        }

        /// <summary>
        /// Called when the Router navigates to a Page (or with a null Route Data when no Page matches the Location)
        /// </summary>
        protected virtual void OnNavigated(TRouteData routeData, string relativeUri) { }

        /// <summary>
        /// Called when the Router is disposed (ex. the Connection navigated to a Location this Router doesn't render)
        /// </summary>
        protected virtual void OnDisposed() { }

        protected virtual void OnRefresh(TRouteData routeData)
        {
            routeData.Logger?.Log(TrakHoundLogLevel.Debug, $"Page Requested : {routeData.Data?.Template}");

            if (Found != null) _renderHandle.Render(Found(routeData));
        }

        public async Task SetParametersAsync(ParameterView parameters)
        {
            parameters.SetParameterProperties(this);

            // Routes only need to be built once (SetParametersAsync is called each time the parent renders)
            if (!_initialized)
            {
                _initialized = true;
                GetRouteManager()?.EnsureInitialised();
            }

            await RefreshSafe();
        }

        public Task OnAfterRenderAsync()
        {
            if (!_navigationInterceptionEnabled)
            {
                _navigationInterceptionEnabled = true;
                return NavigationInterception.EnableNavigationInterceptionAsync();
            }

            return Task.CompletedTask;
        }

        public void Dispose()
        {
            NavigationManager.LocationChanged -= HandleLocationChanged;
            OnDisposed();
        }

        private async void HandleLocationChanged(object sender, LocationChangedEventArgs args)
        {
            _location = args.Location;
            _navigationIntercepted = args.IsNavigationIntercepted;

            await RefreshSafe();
        }

        private async Task RefreshSafe()
        {
            try
            {
                await Refresh();
            }
            catch (Exception ex)
            {
                _logger ??= new TrakHoundLogger(GetType().Name);
                _logger.LogError($"App Router Error : {_location} : {ex.Message} : {TrakHoundLogger.GetExceptionStackTrace(ex)}");
            }
        }

        private string RewriteUrl(string url)
        {
            var basePath = Url.Combine(Url.GetLeftPart(url), Instance.Configuration.BasePath);
            var path = Url.GetRelativeTo(basePath, url);
            if (path == ".") path = "/";
            if (!path.StartsWith('/')) path = '/' + path;

            path = RewritePath(path);

            return Url.Combine(basePath, path);
        }

        private string RewritePath(string path)
        {
            foreach (var routeRewrite in Instance.Configuration.RouteRewrites)
            {
                var output = Url.Rewrite(path, routeRewrite.Key, routeRewrite.Value);
                if (!output.StartsWith('/')) output = '/' + output;

                path = output;
            }

            return path;
        }


        internal virtual async Task Refresh()
        {
            // Used to detect when a newer Navigation has started while this one was awaiting
            var version = ++_navigationVersion;

            var routeManager = GetRouteManager();
            if (routeManager == null)
            {
                if (!LoadFromServer()) RenderNotFound();
                return;
            }

            var location = _location.TrimEnd('/');

            // Rewrite Location (based on Instance Configuration RouteRewrites parameter)
            // Skip when arriving at a Location that was just Rewritten to, so that a non-idempotent Rewrite can't cause a Navigation loop
            if (!Instance.Configuration.RouteRewrites.IsNullOrEmpty() && location != _lastRewriteLocation)
            {
                var rewriteLocation = RewriteUrl(location)?.TrimEnd('/');
                if (rewriteLocation != null && rewriteLocation != location)
                {
                    _lastRewriteLocation = rewriteLocation;

                    // LocationChanged is raised for the new Location, which then renders the Page
                    NavigationManager.NavigateTo(rewriteLocation, false, true);
                    return;
                }
            }

            _lastRewriteLocation = null;

            // Get Relative Path
            var relativeUri = NavigationManager.ToBaseRelativePath(_location);
            if (!relativeUri.StartsWith('/')) relativeUri = "/" + relativeUri;

            // Remove Fragment
            var fragmentIndex = relativeUri.IndexOf('#');
            if (fragmentIndex > -1) relativeUri = relativeUri.Substring(0, fragmentIndex);

            // Remove Query for matching Route to App/Page
            var matchUri = relativeUri;
            var queryIndex = matchUri.IndexOf('?');
            if (queryIndex > -1) matchUri = matchUri.Substring(0, queryIndex);

            var matchResult = routeManager.Match(matchUri);
            if (!matchResult.IsMatch)
            {
                if (!LoadFromServer()) RenderNotFound();
                return;
            }

            var matchedRoute = matchResult.MatchedRoute;

            // The App's Stylesheets and Scripts are part of the page (rendered statically), so a different App is loaded from the server
            if (!IsPrerendering && _rendered && !string.Equals(matchedRoute.AppId, _renderedAppId, StringComparison.Ordinal))
            {
                NavigationManager.NavigateTo(_location, forceLoad: true);
                return;
            }

            // Route Parameters only : Blazor supplies [SupplyParameterFromQuery] Properties from the location itself, the same as with its own Router
            // (a value set here would override it, and a Property without [Parameter] can't be set here at all)
            var parameters = TrakHoundRouteParameters.GetRouteParameters(matchedRoute.Handler, matchResult);

            var routeData = new TRouteData();
            routeData.RouteManager = routeManager;
            routeData.AppId = matchedRoute.AppId;
            routeData.AppName = matchedRoute.AppName;
            routeData.Package = matchedRoute.Package;
            routeData.Client = matchedRoute.Client;
            routeData.Volume = matchedRoute.Volume;
            routeData.Logger = matchedRoute.Logger;

            // A copy for each Page (Components can set Parameters, which must not change the App Configuration)
            routeData.Parameters = matchedRoute.Parameters != null ? new Dictionary<string, object>(matchedRoute.Parameters) : null;

            if (ConnectionService != null)
            {
                routeData.AppConnection = ConnectionService.Connection;

                if (ConnectionService.Connection != null && ConnectionManager != null)
                {
                    ConnectionManager.SetPath(ConnectionService.Id, GetConnectionPathName(routeData), relativeUri, routeData.AppId, routeData.Package?.Version, routeData.Package?.Hash);
                }
            }

            // Authorize App Pages (Instance Pages, such as the Admin interface, have their own sign in)
            if (AuthenticationService != null && routeData.AppId != null)
            {
                var resourceId = TrakHound.Blazor.Apps.TrakHoundAppProvider.GetSecurityResourceId(routeData.AppId, matchedRoute.Handler);
                var (session, authorization) = await AuthenticationService.Authorize(resourceId);

                // A newer Navigation has started, so don't render a stale Page
                if (version != _navigationVersion) return;

                if (authorization.Status == Security.TrakHoundAuthorizationStatus.AuthenticationRequired)
                {
                    // Sign in (with a full page load so that the Session Cookie is sent to the new Circuit)
                    var signInUrl = AuthenticationService.GetSignInUrl(authorization.ProviderIds, relativeUri);
                    if (signInUrl != null)
                    {
                        NavigationManager.NavigateTo(signInUrl, true);
                        return;
                    }
                }

                if (!authorization.IsAllowed)
                {
                    _logger?.LogDebug(authorization.Message);
                    RenderNotAuthorized();
                    return;
                }

                routeData.Session = session;
            }

            routeData.BaseUrl = matchedRoute.BaseUrl;
            routeData.BasePath = matchedRoute.BasePath;
            routeData.BaseLocation = matchedRoute.BaseLocation;
            routeData.Data = new Microsoft.AspNetCore.Components.RouteData(matchedRoute.Handler, parameters);
            routeData.Data.Template = matchedRoute.Template;
            routeData.DefaultLayout = matchedRoute.DefaultLayout;

            _rendered = true;
            _renderedAppId = routeData.AppId;

            OnNavigated(routeData, relativeUri);
            OnRefresh(routeData);
        }

        /// <summary>
        /// When a link that this Router doesn't have a Page for is clicked, load it from the server (ex. an Admin page from an App,
        /// or an App from the Admin Interface), the same as Blazor's Router. Not Found is rendered for other Navigations
        /// </summary>
        private bool LoadFromServer()
        {
            if (!_navigationIntercepted) return false;

            _navigationIntercepted = false;
            NavigationManager.NavigateTo(_location, forceLoad: true);
            return true;
        }

        private void RenderNotFound()
        {
            OnNavigated(null, null);
            if (NotFound != null) _renderHandle.Render(NotFound);
        }

        private void RenderNotAuthorized()
        {
            OnNavigated(null, null);
            _renderHandle.Render(NotAuthorized ?? (builder =>
            {
                builder.OpenElement(0, "div");
                builder.AddAttribute(1, "class", "trakhound-not-authorized");
                builder.AddContent(2, "You do not have access to this page.");
                builder.CloseElement();
            }));
        }
    }
}
