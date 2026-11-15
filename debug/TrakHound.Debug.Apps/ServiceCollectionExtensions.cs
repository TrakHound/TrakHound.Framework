// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Blazored.LocalStorage;
using Microsoft.Extensions.DependencyInjection;
using TrakHound.Apps;
using TrakHound.Clients;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Blazor.Apps
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the services the Instance provides to Apps, with a Client for the Instance set in trakhound.debug.json.
        /// Call MapTrakHoundDebugServices() after the WebApplication is built.
        /// </summary>
        public static void AddTrakHoundDebugHosting(this IServiceCollection services, TrakHound.Debug.TrakHoundDebugHost host)
        {
            AddTrakHoundDebugHosting(services, new TrakHoundHttpClientProvider(host.ClientConfiguration));

            // Read by TrakHoundDebugApp (the values the Instance cascades to App pages)
            services.AddSingleton(host);

            // The Theme shown until one is picked (TrakHoundThemeService keeps the picked Theme in local storage)
            var theme = string.Equals(host.Configuration.Theme, "dark", StringComparison.OrdinalIgnoreCase) ? TrakHoundThemes.Dark : TrakHoundThemes.Light;
            TrakHound.Blazor.Services.TrakHoundThemeService.SetDefaultTheme(TrakHoundDebugApp.ThemeKey, theme);
        }

        /// <summary>
        /// Adds the services the Instance provides to Apps. Call MapTrakHoundDebugServices() after the WebApplication is built.
        /// </summary>
        public static void AddTrakHoundDebugHosting(this IServiceCollection services, ITrakHoundClientProvider clientProvider)
        {
            // Add Global Services
            services.AddScoped<TrakHound.Blazor.Services.JavascriptService>();
            services.AddScoped<TrakHound.Blazor.Services.TrakHoundAppService>();
            services.AddScoped<TrakHound.Blazor.Services.TrakHoundThemeService>();

            // Add ITrakHoundClient as Service
            services.AddSingleton<ITrakHoundClientProvider>(clientProvider);
            services.AddSingleton<ITrakHoundClient>(clientProvider.GetClient());

            var volumeProvider = new TrakHoundVolumeProvider();

            var variableProvider = new TrakHoundVariableProvider();

            // Add DI Services
            var injectionServiceManager = new TrakHoundAppInjectionServiceManagerDebug(clientProvider, volumeProvider, variableProvider);
            services.AddSingleton<ITrakHoundAppInjectionServiceManager>(injectionServiceManager);
            services.AddTransient<TrakHoundTransientAppInjectionServices>();
            services.AddScoped<TrakHoundScopedAppInjectionServices>();
            services.AddSingleton<TrakHoundSingletonAppInjectionServices>();
            services.AddTransient<ITrakHoundAppInjectionServices, TrakHoundAppInjectionServices>();

            // Add Local Storage (using Blazored)
            services.AddBlazoredLocalStorage();
            services.AddTransient<ITrakHoundAppLocalStorageService, TrakHoundLocalStorageService>();
        }
    }
}
