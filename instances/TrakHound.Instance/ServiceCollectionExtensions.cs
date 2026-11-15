// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Identity;
using TrakHound.Apps;
using TrakHound.Blazor.Apps;
using TrakHound.Blazor.Routing;
using TrakHound.Blazor.Services;
using TrakHound.Instances;
using TrakHound.Modules;

namespace TrakHound.Blazor
{
    public static class ServiceCollectionExtensions
    {
        public static void AddTrakHoundHosting(this IServiceCollection services, ITrakHoundInstance instance)
        {
            instance.ConfigurationProfile.Load<TrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory);

            services.AddSingleton(instance.PackageManager);

            services.AddScoped<TrakHoundThemeService>();

            services.AddSingleton<ITrakHoundModuleProvider>(instance.ModuleProvider);

            // Forwards to the current App Provider : the App Provider is replaced each time the Instance is Started (a registered instance would go stale after a restart).
            // Registered as an existing (non-IDisposable) instance so the container never Disposes the Instance's App Provider.
            services.AddSingleton<ITrakHoundAppProvider>(new TrakHoundCurrentAppProvider(instance));

            services.AddTransient<TrakHoundTransientAppInjectionServices>();
            services.AddScoped<TrakHoundScopedAppInjectionServices>();
            services.AddSingleton<TrakHoundSingletonAppInjectionServices>();
            services.AddTransient<ITrakHoundAppInjectionServices, TrakHoundAppInjectionServices>();

            services.AddScoped<TrakHoundAppService>();
        }

        //public static void AddTrakHoundHosting(this IServiceCollection services, ITrakHoundInstance instance)
        //{
        //    instance.ConfigurationProfile.Load<TrakHoundAppConfiguration>(TrakHoundAppConfiguration.ConfigurationCategory);

        //    services.AddSingleton(instance.PackageManager);


        //    services.AddScoped<TrakHoundThemeService>();


        //    var contextId = Guid.NewGuid().ToString();
        //    var context = new TrakHoundModuleContext(contextId);
        //    services.AddSingleton<ITrakHoundModuleProvider>(instance.ModuleProvider);

        //    var injectionServiceManager = new TrakHoundAppInjectionServiceManager(instance.ConfigurationProfile, instance.PackageManager, instance.ModuleProvider, context, instance.ClientProvider, instance.VolumeProvider, instance.LogProvider);
        //    services.AddSingleton<ITrakHoundAppInjectionServiceManager>(injectionServiceManager);

        //    services.AddTransient<TrakHoundTransientAppInjectionServices>();
        //    services.AddScoped<TrakHoundScopedAppInjectionServices>();
        //    services.AddSingleton<TrakHoundSingletonAppInjectionServices>();
        //    services.AddTransient<ITrakHoundAppInjectionServices, TrakHoundAppInjectionServices>();


        //    var routeManager = new TrakHoundPageRouteManager<ComponentBase>(instance.Configuration.BasePath, instance, instance.ConfigurationProfile, instance.ModuleProvider, "app", context, instance.ClientProvider, instance.VolumeProvider, instance.LogProvider);
        //    services.AddSingleton<ITrakHoundPageRouteManager>(routeManager);


        //    var appProvider = new TrakHoundAppProvider(instance, instance.ConfigurationProfile, routeManager, injectionServiceManager, instance.PackageManager, instance.ModuleProvider, instance.LogProvider);
        //    services.AddSingleton<ITrakHoundAppProvider>(appProvider);

        //    services.AddScoped<TrakHoundAppService>();
        //}
    }
}
