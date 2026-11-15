// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components.Server.Circuits;
using TrakHound.Instances;

namespace TrakHound.Instance.Apps
{
    /// <summary>
    /// Measures the CPU used by each App (and the Admin Interface) in its Blazor Circuits
    /// </summary>
    /// <remarks>
    /// Each event, render and JS interop call reaches a Circuit as its own inbound activity (not from the context the Circuit started in),
    /// so every activity is run in the CPU Monitor of the App the Circuit is showing at the time. Circuits that aren't showing an App are the Admin Interface.
    /// </remarks>
    public class TrakHoundAppCpuCircuitHandler : CircuitHandler
    {
        private const string AdminId = "admin";

        private readonly TrakHoundAppConnectionService _connectionService;
        private readonly ITrakHoundInstance _instance;


        public TrakHoundAppCpuCircuitHandler(TrakHoundAppConnectionService connectionService, ITrakHoundInstance instance)
        {
            _connectionService = connectionService;
            _instance = instance;
        }


        public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next)
        {
            return context =>
            {
                var monitor = GetMonitor();
                return monitor != null ? monitor.Run(() => next(context)) : next(context);
            };
        }


        private TrakHoundCpuMonitor GetMonitor()
        {
            var monitors = TrakHoundModuleCpuMonitors.Default;
            if (!monitors.Enabled) return null;

            // Read for each activity, since the Circuit can navigate to another App (or to the Admin Interface)
            var appId = _connectionService.Connection?.AppId;
            if (string.IsNullOrEmpty(appId))
            {
                return monitors.FindMonitor(TrakHoundCpuModuleTypes.Admin, AdminId) ?? monitors.GetMonitor(TrakHoundCpuModuleTypes.Admin, AdminId, "Admin Interface");
            }

            // The App's Name is only looked up when its Monitor is created
            return monitors.FindMonitor(TrakHoundCpuModuleTypes.App, appId) ?? monitors.GetMonitor(TrakHoundCpuModuleTypes.App, appId, _instance?.AppProvider?.GetInformation(appId)?.Name);
        }
    }
}
