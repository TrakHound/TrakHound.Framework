// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using TrakHound.Apps;
using TrakHound.Packages;

namespace TrakHound.Blazor
{
    /// <summary>
    /// Records the App Package Version the Browser connection is running, so a Retired Version (and its Injection Services)
    /// is kept until the connection is no longer using it (ex. after the App is updated while the page is open)
    /// </summary>
    public class TrakHoundAppSessionTracker : ComponentBase
    {
        private string _trackedKey;


        [Parameter]
        public string AppId { get; set; }

        [Parameter]
        public TrakHoundPackage Package { get; set; }

        [Inject]
        public TrakHoundScopedAppInjectionServices ScopedServices { get; set; }


        protected override void OnParametersSet()
        {
            if (!string.IsNullOrEmpty(AppId) && Package != null)
            {
                // Only record each App Version once per connection
                var key = $"{AppId}:{Package.Id}:{Package.Version}";
                if (key != _trackedKey)
                {
                    _trackedKey = key;
                    ScopedServices?.AddSession(AppId, Package.Id, Package.Version);
                }
            }
        }

        protected override bool ShouldRender() => false;
    }
}
