// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Workers;

namespace TrakHound.Instance.Runtime.Tests
{
    public class TestWorker : TrakHoundWorker
    {
        public TestWorker(ITrakHoundWorkerConfiguration configuration) : base(configuration) { }
    }


    /// <summary>
    /// A disposed Worker Manager (the Instance creates a new one each time it starts) must not load or start Workers when Configurations change
    /// </summary>
    public class WorkerManagerTests : IAsyncLifetime
    {
        private readonly RuntimeManagerFixture _fixture = new RuntimeManagerFixture(typeof(TestWorker), "worker");
        private TrakHoundWorkerManager _manager;


        public Task InitializeAsync()
        {
            _manager = new TrakHoundWorkerManager(_fixture.Instance, _fixture.ConfigurationProfile, _fixture.ModuleProvider, _fixture.ClientProvider, _fixture.VolumeProvider, _fixture.VariableProvider, _fixture.LogProvider, _fixture.PackageManager);
            _manager.CpuMonitoringEnabled = false;
            return Task.CompletedTask;
        }

        public async Task DisposeAsync()
        {
            await _manager.DisposeAsync();
            _fixture.Dispose();
        }


        private static TrakHoundWorkerConfiguration CreateConfiguration(string id)
        {
            return new TrakHoundWorkerConfiguration { Id = id, PackageId = "Test.Package", PackageVersion = "1.0.0" };
        }

        private static async Task<bool> WaitFor(Func<bool> condition)
        {
            var stop = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < stop)
            {
                if (condition()) return true;
                await Task.Delay(25);
            }
            return condition();
        }


        [Fact]
        public async Task AddedConfiguration_IsLoaded()
        {
            _fixture.ConfigurationProfile.Add(CreateConfiguration("worker-1"));

            Assert.True(await WaitFor(() => _manager.Engines.Count() == 1));
        }

        [Fact]
        public async Task RemovedConfiguration_IsUnloaded()
        {
            _fixture.ConfigurationProfile.Add(CreateConfiguration("worker-1"));
            Assert.True(await WaitFor(() => _manager.Engines.Count() == 1));

            _fixture.ConfigurationProfile.Remove(TrakHoundWorkerConfiguration.ConfigurationCategory, "worker-1");

            Assert.True(await WaitFor(() => !_manager.Engines.Any()));
        }

        [Fact]
        public async Task DisposedManager_IgnoresConfigurationChanges()
        {
            await _manager.DisposeAsync();

            _fixture.ConfigurationProfile.Add(CreateConfiguration("worker-1"));

            // Configuration events are handled asynchronously, so give a handler that is still subscribed time to load the Worker
            await Task.Delay(500);
            Assert.Empty(_manager.Engines);
        }
    }
}
