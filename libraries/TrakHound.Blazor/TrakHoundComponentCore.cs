// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using System.Reflection;
using System.Runtime.CompilerServices;
using TrakHound.Apps;
using TrakHound.Blazor.Services;
using TrakHound.Logging;

namespace TrakHound.Blazor
{
    /// <summary>
    /// What TrakHoundComponentCore needs from its component (the protected members are implemented explicitly so they stay protected)
    /// </summary>
    internal interface ITrakHoundComponentHost
    {
        NavigationManager Navigation { get; }

        JavascriptService Javascript { get; }

        TrakHoundAppService AppService { get; }

        ITrakHoundAppInjectionServices Services { get; }

        ITrakHoundLogger Logger { get; }

        string BaseUrl { get; }

        string BasePath { get; }

        string BaseLocation { get; }

        bool RenderAfterFirstRender { get; }

        void StateHasChanged();

        Task InvokeAsync(Func<Task> workItem);

        void OnLocationChanged(LocationChangedEventArgs args);

        Task OnLocationChangedAsync(LocationChangedEventArgs args);
    }

    /// <summary>
    /// The state and logic shared by TrakHoundComponentBase and TrakHoundLayoutBase (which can't share a base class,
    /// since App layouts must derive from LayoutComponentBase)
    /// </summary>
    internal sealed class TrakHoundComponentCore
    {
        // Type => whether it overrides OnLocationChanged(Async). Weak so hot loaded App assemblies can still unload
        private static readonly ConditionalWeakTable<Type, StrongBox<bool>> _handlesLocationChanged = new ConditionalWeakTable<Type, StrongBox<bool>>();

        private readonly ITrakHoundComponentHost _host;
        private readonly Type _baseType;
        private string _appId;
        private TrakHoundAppConnection _appConnection;
        private Dictionary<string, object> _parameters;
        private bool _isRendered;
        private bool _subscribed;
        private bool _locationSubscribed;
        private bool _disposed;


        public string AppId
        {
            get => _appId;
            set
            {
                _appId = value;

                // The AppService is shared by every component in the circuit, so don't clear it from a component outside of the App cascade
                if (_host.AppService != null && _appId != null) _host.AppService.AppId = _appId;
            }
        }

        public TrakHoundAppConnection AppConnection
        {
            get => _appConnection;
            set
            {
                _appConnection = value;
                if (_host.AppService != null && _appConnection?.ConnectionId != null) _host.AppService.ConnectionId = _appConnection.ConnectionId;
            }
        }

        public Dictionary<string, object> Parameters
        {
            get => _parameters ??= new Dictionary<string, object>();
            set => _parameters = value;
        }

        public bool IsRendered => _isRendered;


        /// <param name="baseType">The base class that declares OnLocationChanged(Async), so overrides in the derived type can be detected</param>
        public TrakHoundComponentCore(ITrakHoundComponentHost host, Type baseType)
        {
            _host = host;
            _baseType = baseType;
        }


        public void Subscribe()
        {
            if (!_subscribed)
            {
                _subscribed = true;

                // Only components that handle Location changes subscribe (otherwise every component costs an InvokeAsync per Navigation)
                if (_host.Navigation != null && HandlesLocationChanged(_host.GetType()))
                {
                    _locationSubscribed = true;
                    _host.Navigation.LocationChanged += NavigationLocationChanged;
                }
                if (_host.AppService != null) _host.AppService.RefreshRequested += AppServiceRefreshRequested;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            if (_subscribed)
            {
                if (_locationSubscribed && _host.Navigation != null) _host.Navigation.LocationChanged -= NavigationLocationChanged;
                if (_host.AppService != null) _host.AppService.RefreshRequested -= AppServiceRefreshRequested;
            }
        }

        private bool HandlesLocationChanged(Type type)
        {
            return _handlesLocationChanged.GetValue(type, t => new StrongBox<bool>(
                IsOverridden(t, nameof(ITrakHoundComponentHost.OnLocationChanged)) || IsOverridden(t, nameof(ITrakHoundComponentHost.OnLocationChangedAsync))
                )).Value;
        }

        private bool IsOverridden(Type type, string methodName)
        {
            try
            {
                var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, new[] { typeof(LocationChangedEventArgs) }, null);
                return method == null || method.DeclaringType != _baseType;
            }
            catch
            {
                return true; // Subscribe when it can't be determined
            }
        }

        public void OnAfterRender(bool firstRender)
        {
            if (firstRender)
            {
                _isRendered = true;
                if (_host.RenderAfterFirstRender) _host.StateHasChanged();
            }
        }

        public async Task Refresh()
        {
            if (_host.Javascript != null)
            {
                await _host.Javascript.Reload();
            }
        }

        private async void NavigationLocationChanged(object sender, LocationChangedEventArgs args)
        {
            if (_disposed) return;

            try
            {
                await _host.InvokeAsync(async () =>
                {
                    _host.OnLocationChanged(args);
                    await _host.OnLocationChangedAsync(args);
                });
            }
            catch (Exception ex)
            {
                _host.Logger?.LogError($"OnLocationChanged : {_host.GetType().Name} : {ex.Message}");
            }
        }

        private async void AppServiceRefreshRequested(object sender, EventArgs e)
        {
            // Only the first component to claim the request reloads the page
            if (_disposed || _host.AppService == null || !_host.AppService.TryClaimRefresh()) return;

            try
            {
                await Refresh();
            }
            catch (Exception ex)
            {
                _host.Logger?.LogError($"Refresh : {_host.GetType().Name} : {ex.Message}");
            }
        }


        public string GetUrl(string destination)
        {
            return TrakHoundComponentHelper.GetUrl(_host.BaseUrl, destination, _host.AppService);
        }

        public string GetPath(string destination)
        {
            return Url.Combine(_host.BasePath, destination);
        }

        public string GetLocation(string destination)
        {
            return Url.Combine(_host.BaseLocation, destination);
        }

        public TService GetService<TService>() where TService : class, ITrakHoundAppInjectionService
        {
            var service = _host.Services?.Get<TService>(AppId);
            if (service == null)
            {
                _host.Logger?.LogError($"GetService : {_host.GetType().Name} : APP ID = {AppId} : Service Not Found : {typeof(TService).Name}");
            }

            return service;
        }

        public bool ParameterExists(string name)
        {
            return !string.IsNullOrEmpty(name) && Parameters.ContainsKey(name);
        }

        public string GetParameter(string name)
        {
            return TrakHoundComponentHelper.GetParameter(Parameters, name);
        }

        public T GetParameter<T>(string name)
        {
            return TrakHoundComponentHelper.GetParameter<T>(Parameters, name, _host.Logger, _host.GetType().Name);
        }

        public void SetParameter(string name, object value)
        {
            TrakHoundComponentHelper.SetParameter(Parameters, name, value);
        }
    }
}
