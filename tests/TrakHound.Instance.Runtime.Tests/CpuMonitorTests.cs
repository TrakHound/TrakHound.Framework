// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Diagnostics;
using TrakHound.Instances;

namespace TrakHound.Instance.Runtime.Tests
{
    [CollectionDefinition(Name, DisableParallelization = true)]
    public class CpuCollection
    {
        // TrakHoundModuleCpuMonitors.Default is shared, and one test disables it
        public const string Name = "Cpu";
    }


    /// <summary>
    /// CPU time is recorded to the Monitor the code runs in and added to its Parents (Type > Module > Endpoint), and a Module called by
    /// another Module is counted to the called (innermost) Module
    /// </summary>
    [Collection(CpuCollection.Name)]
    public class CpuMonitorTests
    {
        // Wall time spent busy. The CPU time recorded is less when other tests are running, so the tests only check that time was recorded
        private static readonly TimeSpan _busy = TimeSpan.FromMilliseconds(60);


        [Fact]
        public async Task Time_Is_Added_To_Parents()
        {
            var parent = new TrakHoundCpuMonitor();
            var child = new TrakHoundCpuMonitor(parent);

            await child.Run(() => Task.Run(() => Spin(_busy)));

            Assert.True(child.TotalProcessorTime > TimeSpan.Zero, "Child recorded no time");
            Assert.Equal(child.TotalProcessorTime, parent.TotalProcessorTime);
        }

        [Fact]
        public async Task Called_Module_Is_Counted_To_Itself()
        {
            var worker = new TrakHoundCpuMonitor();
            var api = new TrakHoundCpuMonitor();

            // The Worker does no work itself, so its time is only what it took to call the API
            await worker.Run(() => api.Run(() => Task.Run(() => Spin(_busy))));

            Assert.True(api.TotalProcessorTime > TimeSpan.Zero, "API recorded no time");
            Assert.True(worker.TotalProcessorTime < api.TotalProcessorTime / 2, $"Worker {worker.TotalProcessorTime.TotalMilliseconds} ms, API {api.TotalProcessorTime.TotalMilliseconds} ms");
        }

        [Fact]
        public async Task Endpoint_Time_Is_Included_In_Its_Api_And_Type()
        {
            var monitors = TrakHoundModuleCpuMonitors.Default;
            var apiId = Guid.NewGuid().ToString();
            var before = GetTypeCpuTime(monitors, TrakHoundCpuModuleTypes.Api);

            monitors.GetMonitor(TrakHoundCpuModuleTypes.Api, apiId, "Test API");
            var endpoint = monitors.GetChildMonitor(TrakHoundCpuModuleTypes.Api, apiId, "Request:GET:/runs");
            await endpoint.Run(() => Task.Run(() => Spin(_busy)));

            var api = monitors.FindMonitor(TrakHoundCpuModuleTypes.Api, apiId);
            Assert.Same(endpoint, monitors.FindMonitor(TrakHoundCpuModuleTypes.Api, apiId, "Request:GET:/runs"));
            Assert.Equal(endpoint.TotalProcessorTime, api.TotalProcessorTime);

            var usage = monitors.GetUsage(includeHistory: false);
            var module = Assert.Single(usage.Modules, o => o.Id == apiId);
            Assert.Equal("Test API", module.Name);
            Assert.True(GetTypeCpuTime(monitors, TrakHoundCpuModuleTypes.Api) > before, "The API Type recorded no time");
            Assert.True(usage.UnattributedUsage >= 0);
        }

        [Fact]
        public void No_Monitors_When_Disabled()
        {
            var monitors = TrakHoundModuleCpuMonitors.Default;
            monitors.Enabled = false;

            try
            {
                Assert.Null(monitors.GetMonitor(TrakHoundCpuModuleTypes.App, Guid.NewGuid().ToString()));
                Assert.False(monitors.GetUsage().Enabled);
                Assert.Null(monitors.GetUsage().Modules);
            }
            finally
            {
                monitors.Enabled = true;
            }
        }


        private static double GetTypeCpuTime(TrakHoundModuleCpuMonitors monitors, string type)
        {
            return monitors.GetUsage(includeHistory: false).Types.First(o => o.Type == type).CpuTime;
        }

        private static void Spin(TimeSpan duration)
        {
            var stopwatch = Stopwatch.StartNew();
            double value = 0;
            while (stopwatch.Elapsed < duration) value += Math.Sqrt(value + 1);
        }
    }
}
