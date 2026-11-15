// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using TrakHound.Apps;

namespace TrakHound.Blazor.Apps
{
    public static class WebApplicationExtensions
    {
        public static void MapTrakHoundDebugServices(this WebApplication application)
        {
            var injectionServiceManager = application.Services.GetService<ITrakHoundAppInjectionServiceManager>();
            if (injectionServiceManager != null)
            {
                // Set ServiceProvider for TrakHound Injection Services
                ((TrakHoundAppInjectionServiceManagerDebug)injectionServiceManager).ServiceProvider = application.Services;
            }
        }
    }
}
