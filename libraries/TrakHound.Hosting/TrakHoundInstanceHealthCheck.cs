// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TrakHound.Hosting
{
    /// <summary>
    /// Reports whether the Instance can be reached. An unreachable Instance is Degraded, not Unhealthy: the module still runs, only its Client calls fail
    /// </summary>
    public class TrakHoundInstanceHealthCheck : IHealthCheck
    {
        public const string Name = "trakhound-instance";

        private readonly TrakHoundHostContext _context;


        public TrakHoundInstanceHealthCheck(TrakHoundHostContext context)
        {
            _context = context;
        }


        public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
        {
            if (!_context.IsInstanceConfigured) return HealthCheckResult.Healthy("No Instance Configured");

            var instanceId = await _context.CheckInstanceAsync(cancellationToken);
            if (instanceId != null) return HealthCheckResult.Healthy($"Connected to Instance : {instanceId}");
            return HealthCheckResult.Degraded($"Instance Not Reachable : {_context.Options.InstanceUrl}");
        }
    }
}
