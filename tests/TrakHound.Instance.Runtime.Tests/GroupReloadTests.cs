// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using TrakHound.Clients;
using TrakHound.Functions;
using TrakHound.Groups;
using TrakHound.Workers;

namespace TrakHound.Instance.Runtime.Tests
{
    /// <summary>
    /// A Module whose Router comes from its Group is reloaded (with a new Client) when the Group's Router changes. Other Modules aren't
    /// </summary>
    public class GroupReloadTests : IAsyncLifetime
    {
        private readonly RuntimeManagerFixture _workerFixture = new RuntimeManagerFixture(typeof(TestWorker), "worker");
        private readonly RuntimeManagerFixture _functionFixture = new RuntimeManagerFixture(typeof(TestFunction), "function");

        // The Router of each Client created, in order (null for the Default Router)
        private readonly ConcurrentQueue<string> _workerRouters = new ConcurrentQueue<string>();
        private readonly ConcurrentQueue<string> _functionRouters = new ConcurrentQueue<string>();

        private TrakHoundWorkerManager _workerManager;
        private TrakHoundFunctionManager _functionManager;


        private static ITrakHoundClientProvider CreateClientProvider(ConcurrentQueue<string> routers)
        {
            return Fake.Create<ITrakHoundClientProvider>(o => o.On("GetClient", args =>
            {
                routers.Enqueue((string)args[0]);
                return Fake.Create<ITrakHoundClient>();
            }));
        }

        public Task InitializeAsync()
        {
            var w = _workerFixture;
            _workerManager = new TrakHoundWorkerManager(w.Instance, w.ConfigurationProfile, w.ModuleProvider, CreateClientProvider(_workerRouters), w.VolumeProvider, w.VariableProvider, w.LogProvider, w.PackageManager);
            _workerManager.CpuMonitoringEnabled = false;

            var f = _functionFixture;
            _functionManager = new TrakHoundFunctionManager(f.Instance, f.ConfigurationProfile, f.ModuleProvider, CreateClientProvider(_functionRouters), f.VolumeProvider, f.VariableProvider, f.LogProvider, f.PackageManager);

            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _workerManager.DisposeAsync();
            _functionManager.Dispose();
            _workerFixture.Dispose();
            _functionFixture.Dispose();
        }


        private static TrakHoundGroupConfiguration CreateGroup(string routerId)
        {
            return new TrakHoundGroupConfiguration { Id = "line-1", RouterId = routerId };
        }

        private static TrakHoundWorkerConfiguration CreateWorker(string id, string routerId = null)
        {
            return new TrakHoundWorkerConfiguration { Id = id, PackageId = "Test.Package", PackageVersion = "1.0.0", GroupId = "line-1", RouterId = routerId };
        }

        private static async Task<bool> WaitFor(Func<bool> condition)
        {
            // Group changes are applied by the Managers' delayed Load (2 seconds)
            var stop = DateTime.UtcNow.AddSeconds(8);
            while (DateTime.UtcNow < stop)
            {
                if (condition()) return true;
                await Task.Delay(25);
            }
            return condition();
        }


        [Fact]
        public async Task Worker_IsReloaded_WhenItsGroupRouterChanges()
        {
            _workerFixture.ConfigurationProfile.Add(CreateGroup("first"));
            _workerFixture.ConfigurationProfile.Add(CreateWorker("worker-1"));
            Assert.True(await WaitFor(() => _workerRouters.LastOrDefault() == "first"));

            _workerFixture.ConfigurationProfile.Add(CreateGroup("second"));

            Assert.True(await WaitFor(() => _workerRouters.LastOrDefault() == "second"));
            Assert.Single(_workerManager.Engines);
        }

        [Fact]
        public async Task Worker_UsesTheDefaultRouter_WhenItsGroupIsRemoved()
        {
            _workerFixture.ConfigurationProfile.Add(CreateGroup("first"));
            _workerFixture.ConfigurationProfile.Add(CreateWorker("worker-1"));
            Assert.True(await WaitFor(() => _workerRouters.LastOrDefault() == "first"));

            _workerFixture.ConfigurationProfile.Remove(TrakHoundGroupConfiguration.ConfigurationCategory, "line-1");

            Assert.True(await WaitFor(() => _workerRouters.Count == 2 && _workerRouters.Last() == null));
        }

        [Fact]
        public async Task Worker_WithItsOwnRouter_IsNotReloaded()
        {
            _workerFixture.ConfigurationProfile.Add(CreateGroup("first"));
            _workerFixture.ConfigurationProfile.Add(CreateWorker("worker-1", "own"));
            Assert.True(await WaitFor(() => _workerRouters.LastOrDefault() == "own"));

            _workerFixture.ConfigurationProfile.Add(CreateGroup("second"));

            // Long enough for the delayed Load to run
            await Task.Delay(3000);
            Assert.Equal(new[] { "own" }, _workerRouters);
        }

        [Fact]
        public async Task Function_IsReloaded_WhenItsGroupRouterChanges()
        {
            _functionFixture.ConfigurationProfile.Add(CreateGroup("first"));
            _functionFixture.ConfigurationProfile.Add(new TrakHoundFunctionConfiguration { Id = "config-1", FunctionId = "fn-1", PackageId = "Test.Package", PackageVersion = "1.0.0", GroupId = "line-1" });
            Assert.True(await WaitFor(() => _functionRouters.LastOrDefault() == "first"));

            _functionFixture.ConfigurationProfile.Add(CreateGroup("second"));

            Assert.True(await WaitFor(() => _functionRouters.LastOrDefault() == "second"));
        }
    }
}
