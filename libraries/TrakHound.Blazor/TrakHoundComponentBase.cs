// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using TrakHound.Apps;
using TrakHound.Blazor.Services;
using TrakHound.Clients;
using TrakHound.Logging;
using TrakHound.Packages;
using TrakHound.Security;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Blazor
{
    // The state and logic are in TrakHoundComponentCore (shared with TrakHoundLayoutBase). Blazor needs the parameters declared here
    public abstract class TrakHoundComponentBase : ComponentBase, ITrakHoundAppComponent, ITrakHoundComponentHost, IDisposable
    {
        private readonly TrakHoundComponentCore _core;


        [CascadingParameter(Name = "AppId")]
        public string AppId
        {
            get => _core.AppId;
            set => _core.AppId = value;
        }

        [CascadingParameter(Name = "AppName")]
        public string AppName { get; set; }

        [CascadingParameter(Name = "AppConnection")]
        public TrakHoundAppConnection AppConnection
        {
            get => _core.AppConnection;
            set => _core.AppConnection = value;
        }

        [CascadingParameter(Name = "Package")]
        public TrakHoundPackage Package { get; set; }

        [CascadingParameter(Name = "Client")]
        public ITrakHoundClient Client { get; set; }

        [CascadingParameter(Name = "Volume")]
        public ITrakHoundVolume Volume { get; set; }

        [CascadingParameter(Name = "Variables")]
        public ITrakHoundVariableCollection Variables { get; set; }

        [CascadingParameter(Name = "Logger")]
        public ITrakHoundLogger Logger { get; set; }

        [CascadingParameter(Name = "Session")]
        public ITrakHoundSession Session { get; set; }

        public ITrakHoundAuthenticatedSession AuthenticatedSession => Session as ITrakHoundAuthenticatedSession;

        public ITrakHoundAnnonymousSession AnnonymousSession => Session as ITrakHoundAnnonymousSession;

        [CascadingParameter(Name = "BaseUrl")]
        public string BaseUrl { get; set; }

        [CascadingParameter(Name = "BasePath")]
        public string BasePath { get; set; }

        [CascadingParameter(Name = "BaseLocation")]
        public string BaseLocation { get; set; }

        /// <summary>
        /// Cascaded from the parent, so changes made with SetParameter() are shared with the parent and its other children
        /// </summary>
        [CascadingParameter(Name = "Parameters")]
        public Dictionary<string, object> Parameters
        {
            get => _core.Parameters;
            set => _core.Parameters = value;
        }

        [Inject]
        public ITrakHoundAppInjectionServices Services { get; set; }

        [Inject]
        public NavigationManager Navigation { get; set; }

        [Inject]
        public JavascriptService Javascript { get; set; }

        [Inject]
        public TrakHoundAppService AppService { get; set; }

        protected bool IsRendered => _core.IsRendered;

        /// <summary>
        /// Set to true to render again after the first render (for components whose markup depends on IsRendered)
        /// </summary>
        protected virtual bool RenderAfterFirstRender => false;


        protected TrakHoundComponentBase()
        {
            _core = new TrakHoundComponentCore(this, typeof(TrakHoundComponentBase));
        }


        public override Task SetParametersAsync(ParameterView parameters)
        {
            // Subscribe here (instead of OnInitialized) so it still happens when a derived class doesn't call base.OnInitialized()
            _core.Subscribe();

            return base.SetParametersAsync(parameters);
        }

        public virtual void Dispose() => _core.Dispose();

        protected override void OnAfterRender(bool firstRender) => _core.OnAfterRender(firstRender);

        protected virtual void OnLocationChanged(LocationChangedEventArgs args) { }
        protected virtual Task OnLocationChangedAsync(LocationChangedEventArgs args) => Task.CompletedTask;

        protected Task Refresh() => _core.Refresh();


        protected string GetUrl(string destination) => _core.GetUrl(destination);

        protected string GetPath(string destination) => _core.GetPath(destination);

        protected string GetLocation(string destination) => _core.GetLocation(destination);

        protected TService GetService<TService>() where TService : class, ITrakHoundAppInjectionService => _core.GetService<TService>();

        public bool ParameterExists(string name) => _core.ParameterExists(name);

        public string GetParameter(string name) => _core.GetParameter(name);

        public T GetParameter<T>(string name) => _core.GetParameter<T>(name);

        /// <summary>
        /// Gets the value of a Configuration Variable shared by every configuration in the Profile (decrypted, with ${env:NAME} references replaced).
        /// Returns null if it isn't defined. Parameters that reference a Variable as ${name} are already replaced by GetParameter().
        /// </summary>
        public string GetConfigurationVariable(string name) => TrakHound.Configurations.TrakHoundConfigurationVariables.GetValue(name);

        public void SetParameter(string name, object value) => _core.SetParameter(name, value);


        // Explicit, so the core can call these without making them public
        bool ITrakHoundComponentHost.RenderAfterFirstRender => RenderAfterFirstRender;
        void ITrakHoundComponentHost.StateHasChanged() => StateHasChanged();
        Task ITrakHoundComponentHost.InvokeAsync(Func<Task> workItem) => InvokeAsync(workItem);
        void ITrakHoundComponentHost.OnLocationChanged(LocationChangedEventArgs args) => OnLocationChanged(args);
        Task ITrakHoundComponentHost.OnLocationChangedAsync(LocationChangedEventArgs args) => OnLocationChangedAsync(args);
    }
}
