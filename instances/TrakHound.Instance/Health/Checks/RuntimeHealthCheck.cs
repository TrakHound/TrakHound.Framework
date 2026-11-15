// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Instances;

namespace TrakHound.Instance.Health.Checks
{
    /// <summary>
    /// The Instance Runtime is Started (Workers, Functions, Apis, Apps and Services are loaded)
    /// </summary>
    public class RuntimeHealthCheck : ITrakHoundHealthCheck
    {
        public string Id => "runtime";

        public string Title => "Runtime";


        public Task<TrakHoundHealthCheckResult> Evaluate(TrakHoundHealthContext context)
        {
            var instance = context.Instance;
            var since = "";
            if (instance.LastUpdated != default)
            {
                var local = instance.LastUpdated.ToLocalTime();
                since = local.Date == DateTime.Now.Date ? $" at {local:HH:mm:ss}" : $" at {local:yyyy-MM-dd HH:mm:ss}";
            }

            var status = instance.Status switch
            {
                TrakHoundInstanceStatus.Started => TrakHoundHealthStatus.Healthy,
                TrakHoundInstanceStatus.Error => TrakHoundHealthStatus.Unhealthy,
                TrakHoundInstanceStatus.Stopped => TrakHoundHealthStatus.Unhealthy,
                TrakHoundInstanceStatus.Initializing or TrakHoundInstanceStatus.Initialized or TrakHoundInstanceStatus.Starting =>TrakHoundHealthStatus.Unknown, // Not a problem yet (every Start passes through these)
                _ => TrakHoundHealthStatus.Degraded // Stopping
            };

            return Task.FromResult(context.Result(this, status, $"{instance.Status}{since}"));
        }
    }
}
