// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using TrakHound.Apps;
using TrakHound.Instance.Apps;

namespace TrakHound.Blazor
{
    /// <summary>
    /// Records how the user is using an App page (reported by the Browser : last interaction, editing, input typed, Background tab)
    /// so the App Connections page can show whether it is safe to reload it. Cascades itself to the App Pages as "AppActivity"
    /// so an App can declare whether the Page has unsaved changes.
    /// </summary>
    public class TrakHoundAppActivityTracker : ComponentBase, ITrakHoundAppActivity, IDisposable
    {
        public const string CascadingName = "AppActivity";

        // How often the Browser reports while the user is interacting with the page
        private const int _reportIntervalSeconds = 10;

        private DotNetObjectReference<TrakHoundAppActivityTracker> _reference;
        private bool _started;
        private bool _disposed;
        private bool? _unsavedChanges;
        private string _unsavedChangesPath;
        private string _path;


        [Parameter]
        public RenderFragment ChildContent { get; set; }

        [Inject]
        private IJSRuntime JSRuntime { get; set; }

        [Inject]
        private NavigationManager NavigationManager { get; set; }

        [Inject]
        private TrakHoundAppConnectionService ConnectionService { get; set; }

        [Inject]
        private TrakHoundAppConnectionManager ConnectionManager { get; set; }


        /// <summary>
        /// The Path of the current Page (without the Query)
        /// </summary>
        private string CurrentPath
        {
            get
            {
                var path = NavigationManager.ToBaseRelativePath(NavigationManager.Uri);
                var i = path.IndexOfAny(new[] { '?', '#' });
                return i >= 0 ? path.Substring(0, i) : path;
            }
        }

        /// <summary>
        /// The Unsaved Changes declared by the current Page (a declaration only applies to the Page that made it)
        /// </summary>
        private bool? CurrentUnsavedChanges => _unsavedChangesPath == CurrentPath ? _unsavedChanges : null;


        protected override void OnInitialized()
        {
            _path = CurrentPath;
            NavigationManager.LocationChanged += LocationChanged;
        }

        public void Dispose()
        {
            _disposed = true;
            NavigationManager.LocationChanged -= LocationChanged;

            if (_started)
            {
                _started = false;
                _ = StopReporting();
            }
            else
            {
                _reference?.Dispose();
            }
        }

        protected override void BuildRenderTree(RenderTreeBuilder builder)
        {
            builder.OpenComponent<CascadingValue<ITrakHoundAppActivity>>(0);
            builder.AddComponentParameter(1, nameof(CascadingValue<ITrakHoundAppActivity>.Name), CascadingName);
            builder.AddComponentParameter(2, nameof(CascadingValue<ITrakHoundAppActivity>.Value), this);
            builder.AddComponentParameter(3, nameof(CascadingValue<ITrakHoundAppActivity>.IsFixed), true);
            builder.AddComponentParameter(4, nameof(CascadingValue<ITrakHoundAppActivity>.ChildContent), ChildContent);
            builder.CloseComponent();
        }

        protected override async Task OnAfterRenderAsync(bool firstRender)
        {
            if (firstRender && !_disposed)
            {
                try
                {
                    _reference = DotNetObjectReference.Create(this);
                    await JSRuntime.InvokeVoidAsync("TrakHoundActivity.start", _reference, _reportIntervalSeconds);
                    _started = true;

                    await SendUnsavedChanges();
                }
                catch
                {
                    // The connection may have closed (or the script isn't loaded)
                }
            }
        }


        [JSInvokable]
        public void ReportActivity(double idleSeconds, bool isEditing, bool hasInput, bool isHidden)
        {
            // The Browser reports how long ago the last interaction was (so the Browser's clock doesn't matter)
            var lastActivity = DateTime.UtcNow.AddSeconds(-Math.Clamp(idleSeconds, 0, TimeSpan.FromDays(365).TotalSeconds));
            ConnectionManager.SetActivity(ConnectionService.Id, lastActivity, isEditing, hasInput, isHidden);
        }

        public void SetUnsavedChanges(bool hasUnsavedChanges)
        {
            _unsavedChanges = hasUnsavedChanges;
            _unsavedChangesPath = CurrentPath;

            ConnectionManager.SetUnsavedChanges(ConnectionService.Id, hasUnsavedChanges);
            _ = SendUnsavedChanges();
        }


        private async void LocationChanged(object sender, LocationChangedEventArgs args)
        {
            // Only a change of Page (not of the Query, ex. a filter) starts over
            var path = CurrentPath;
            if (path == _path) return;
            _path = path;

            // Input typed and Unsaved Changes declared on the previous Page no longer apply
            // (the new Page may already have declared its own, so only the previous Page's declaration is cleared)
            ConnectionManager.SetUnsavedChanges(ConnectionService.Id, CurrentUnsavedChanges);

            if (!_started) return;

            try
            {
                await JSRuntime.InvokeVoidAsync("TrakHoundActivity.navigated");
                await SendUnsavedChanges();
            }
            catch { }
        }

        /// <summary>
        /// The Browser uses the declaration when it decides whether it can reload itself (ex. after an App update)
        /// </summary>
        private async Task SendUnsavedChanges()
        {
            if (!_started || _disposed) return;

            try
            {
                await JSRuntime.InvokeVoidAsync("TrakHoundActivity.setUnsaved", CurrentUnsavedChanges);
            }
            catch { }
        }

        private async Task StopReporting()
        {
            try
            {
                await JSRuntime.InvokeVoidAsync("TrakHoundActivity.stop");
            }
            catch { }
            finally
            {
                _reference?.Dispose();
            }
        }
    }
}
