// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Functions;

namespace TrakHound.Instance.Runtime.Tests
{
    public class TestFunction : TrakHoundFunction
    {
        public TestFunction(ITrakHoundFunctionConfiguration configuration) : base(configuration) { }
    }


    /// <summary>
    /// Functions follow their Configurations: removing a Configuration stops serving its Function, and a disposed Manager (the Instance
    /// creates a new one each time it starts) no longer reacts to Configuration changes
    /// </summary>
    public class FunctionManagerTests : IDisposable
    {
        private readonly RuntimeManagerFixture _fixture = new RuntimeManagerFixture(typeof(TestFunction), "function");
        private readonly TrakHoundFunctionManager _manager;


        public FunctionManagerTests()
        {
            _manager = new TrakHoundFunctionManager(_fixture.Instance, _fixture.ConfigurationProfile, _fixture.ModuleProvider, _fixture.ClientProvider, _fixture.VolumeProvider, _fixture.VariableProvider, _fixture.LogProvider, _fixture.PackageManager);
        }

        public void Dispose()
        {
            _manager.Dispose();
            _fixture.Dispose();
        }


        private static TrakHoundFunctionConfiguration CreateConfiguration(string id, string functionId)
        {
            return new TrakHoundFunctionConfiguration { Id = id, FunctionId = functionId, PackageId = "Test.Package", PackageVersion = "1.0.0" };
        }


        [Fact]
        public void AddedConfiguration_IsServed()
        {
            _fixture.ConfigurationProfile.Add(CreateConfiguration("config-1", "fn-1"));

            Assert.NotNull(_manager.GetConfiguration("fn-1"));
        }

        [Fact]
        public void RemovedConfiguration_IsNoLongerServed()
        {
            _fixture.ConfigurationProfile.Add(CreateConfiguration("config-1", "fn-1"));
            _fixture.ConfigurationProfile.Remove(TrakHoundFunctionConfiguration.ConfigurationCategory, "config-1");

            Assert.Null(_manager.GetConfiguration("fn-1"));
            Assert.Null(_manager.GetInformation("fn-1"));
        }

        [Fact]
        public async Task RemovedConfiguration_CannotBeRun()
        {
            _fixture.ConfigurationProfile.Add(CreateConfiguration("config-1", "fn-1"));
            _fixture.ConfigurationProfile.Remove(TrakHoundFunctionConfiguration.ConfigurationCategory, "config-1");

            var response = await _manager.Run("fn-1");

            Assert.Null(response.Id);
        }

        [Fact]
        public void RemovedConfiguration_CanBeAddedAgain()
        {
            var configuration = CreateConfiguration("config-1", "fn-1");
            _fixture.ConfigurationProfile.Add(configuration);
            _fixture.ConfigurationProfile.Remove(TrakHoundFunctionConfiguration.ConfigurationCategory, "config-1");

            // The same (unchanged) Configuration is loaded again, since it is no longer recorded as installed
            _fixture.ConfigurationProfile.Add(CreateConfiguration("config-1", "fn-1"));

            Assert.NotNull(_manager.GetConfiguration("fn-1"));
        }

        [Fact]
        public void RemovedConfiguration_KeepsFunctionLoadedByAnotherConfiguration()
        {
            _fixture.ConfigurationProfile.Add(CreateConfiguration("config-1", "fn-1"));
            _fixture.ConfigurationProfile.Add(CreateConfiguration("config-2", "fn-1")); // Replaces config-1's Function (same Function ID)
            _fixture.ConfigurationProfile.Remove(TrakHoundFunctionConfiguration.ConfigurationCategory, "config-1");

            Assert.Equal("config-2", _manager.GetConfiguration("fn-1")?.Id);
        }

        [Fact]
        public void DisposedManager_IgnoresConfigurationChanges()
        {
            _manager.Dispose();

            _fixture.ConfigurationProfile.Add(CreateConfiguration("config-1", "fn-1"));

            Assert.Null(_manager.GetConfiguration("fn-1"));
        }
    }
}
