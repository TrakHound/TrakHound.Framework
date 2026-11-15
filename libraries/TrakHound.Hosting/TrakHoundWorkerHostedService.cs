// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TrakHound.Workers;

namespace TrakHound.Hosting
{
    /// <summary>
    /// Runs a Worker for the lifetime of the host: created and started when the host starts, stopped when it stops
    /// </summary>
    internal class TrakHoundWorkerHostedService<TWorker> : IHostedService, IAsyncDisposable where TWorker : TrakHoundWorker
    {
        private readonly TrakHoundHostContext _context;
        private readonly ILogger _logger;
        private TWorker _worker;


        public TrakHoundWorkerHostedService(TrakHoundHostContext context, ILogger<TWorker> logger)
        {
            _context = context;
            _logger = logger;
        }


        public async Task StartAsync(CancellationToken cancellationToken)
        {
            var instanceId = await _context.ConnectAsync();

            var configuration = new TrakHoundWorkerConfiguration();
            configuration.Id = _context.Options.ConfigurationId;
            configuration.Parameters = _context.GetParameters();

            _worker = TrakHoundModuleFactory.CreateWorker<TWorker>(configuration, _context.Client, _context.Volume, _context.Variables, instanceId);
            _worker.LogReceived += (sender, item) => TrakHoundHostLogging.Write(_logger, item);

            // An exception stops the host from starting (the same as any hosted service), so a broken Worker isn't left half running
            await _worker.Start();
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_worker != null) await _worker.Stop();
        }

        public async ValueTask DisposeAsync()
        {
            if (_worker != null) await _worker.DisposeAsync();
        }
    }
}
