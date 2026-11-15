// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance
{
    /// <summary>
    /// Stops the TrakHound Instance as part of the Host's shutdown, so that the Host waits for it to finish
    /// (within HostOptions.ShutdownTimeout) before the process exits.
    /// </summary>
    internal class TrakHoundInstanceShutdownService : IHostedService
    {
        private readonly Func<Task> _stop;


        public TrakHoundInstanceShutdownService(Func<Task> stop)
        {
            _stop = stop;
        }


        public Task StartAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_stop != null)
            {
                // Stop the Instance, but don't wait past the Host's shutdown timeout
                await _stop().WaitAsync(cancellationToken);
            }
        }
    }
}
