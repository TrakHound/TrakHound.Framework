// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TrakHound.Services;
using TrakHound.Workers;

namespace TrakHound.Hosting
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds the TrakHoundHostContext (the Client, Volume, Variables and Parameters modules get) with the settings in the configuration section
        /// (usually builder.Configuration.GetSection("TrakHound")). Connects to the Instance when the host starts
        /// </summary>
        public static IServiceCollection AddTrakHound(this IServiceCollection services, IConfiguration configuration)
        {
            services.Configure<TrakHoundHostOptions>(configuration);
            return AddTrakHoundContext(services);
        }

        /// <summary>
        /// Adds the TrakHoundHostContext (the Client, Volume, Variables and Parameters modules get) with settings set in code
        /// </summary>
        public static IServiceCollection AddTrakHound(this IServiceCollection services, Action<TrakHoundHostOptions> configure)
        {
            services.Configure(configure);
            return AddTrakHoundContext(services);
        }

        /// <summary>
        /// Runs the Worker for the lifetime of the host (started when the host starts, stopped when it stops). Its Log entries go to ILogger&lt;TWorker&gt;
        /// </summary>
        public static IServiceCollection AddTrakHoundWorker<TWorker>(this IServiceCollection services) where TWorker : TrakHoundWorker
        {
            services.AddHostedService<TrakHoundWorkerHostedService<TWorker>>();
            return services;
        }

        /// <summary>
        /// Adds the Service as a singleton, created the same way the Instance creates it (Services have no Start or Stop: the host's code calls them).
        /// Its Log entries go to ILogger&lt;TService&gt;, and it's disposed with the host
        /// </summary>
        public static IServiceCollection AddTrakHoundService<TService>(this IServiceCollection services) where TService : TrakHoundService
        {
            services.AddSingleton(provider =>
            {
                var context = provider.GetRequiredService<TrakHoundHostContext>();
                var logger = provider.GetRequiredService<ILogger<TService>>();

                var configuration = new TrakHoundServiceConfiguration();
                configuration.Id = context.Options.ConfigurationId;
                configuration.Parameters = context.GetParameters();

                var service = TrakHoundModuleFactory.CreateService<TService>(configuration, context.Volume, context.Variables, context.InstanceId);
                service.LogReceived += (sender, item) => TrakHoundHostLogging.Write(logger, item);
                return service;
            });
            return services;
        }

        /// <summary>
        /// Adds a health check that reports whether the Instance can be reached (Degraded when it can't: the module still runs)
        /// </summary>
        public static IHealthChecksBuilder AddTrakHoundInstance(this IHealthChecksBuilder builder)
        {
            return builder.AddCheck<TrakHoundInstanceHealthCheck>(TrakHoundInstanceHealthCheck.Name);
        }


        private static IServiceCollection AddTrakHoundContext(IServiceCollection services)
        {
            services.TryAddSingleton<TrakHoundHostContext>();

            // Connect when the host starts (before the Workers, which are added after this), so the Instance's ID is known early
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, TrakHoundHostConnection>());
            return services;
        }


        private class TrakHoundHostConnection : IHostedService
        {
            private readonly TrakHoundHostContext _context;

            public TrakHoundHostConnection(TrakHoundHostContext context) => _context = context;

            public Task StartAsync(CancellationToken cancellationToken) => _context.ConnectAsync();

            public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
        }
    }
}
