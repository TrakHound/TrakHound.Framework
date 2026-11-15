// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using TrakHound.Api;
using TrakHound.Debug;
using TrakHound.Functions;
using TrakHound.Hosting;
using TrakHound.Logging;
using TrakHound.Security;
using TrakHound.Services;
using TrakHound.Variables;
using TrakHound.Volumes;
using TrakHound.Workers;

namespace TrakHound.Testing
{
    /// <summary>
    /// Everything a module gets from the Instance, for a test: a test Client, a Volume and Variables in a temporary directory (deleted on Dispose),
    /// and the configuration Parameters. Modules are created the same way the Instance creates them, and their Log entries are kept in Logs
    /// </summary>
    public class TrakHoundTestContext : IDisposable
    {
        private readonly ConcurrentQueue<TrakHoundLogItem> _logs = new();


        /// <summary>
        /// The temporary directory of the Volume and Variables
        /// </summary>
        public string Directory { get; }

        /// <summary>
        /// The Client modules get (Services in memory, Functions you register)
        /// </summary>
        public TrakHoundTestClient Client { get; }

        public ITrakHoundVolume Volume { get; }

        public ITrakHoundVariableCollection Variables { get; }

        /// <summary>
        /// The configuration Parameters modules are created with (as set in the module's Configuration on an Instance). Each module gets a copy
        /// </summary>
        public Dictionary<string, object> Parameters { get; } = new Dictionary<string, object>();

        /// <summary>
        /// The ID of the Configuration modules are created with (and the Function ID of Functions)
        /// </summary>
        public string ConfigurationId { get; set; } = "test";

        public string InstanceId { get; set; } = "test-instance";

        /// <summary>
        /// The Log entries written by the modules created by this context, in order
        /// </summary>
        public IReadOnlyList<TrakHoundLogItem> Logs => _logs.ToList();


        public TrakHoundTestContext()
        {
            Directory = Path.Combine(Path.GetTempPath(), "trakhound-tests", Guid.NewGuid().ToString("N"));
            System.IO.Directory.CreateDirectory(Directory);

            Client = new TrakHoundTestClient();
            Volume = new TrakHoundVolume("volume", Path.Combine(Directory, "volume"));
            Variables = new TrakHoundVariableCollection("variables", Directory);
        }

        public void Dispose()
        {
            try { System.IO.Directory.Delete(Directory, true); } catch { }
        }


        /// <summary>
        /// The Log entries at or above the Log Level (ex. Warning returns Warnings, Errors and Critical entries)
        /// </summary>
        public IEnumerable<TrakHoundLogItem> GetLogs(TrakHoundLogLevel minimumLevel) => Logs.Where(o => o.LogLevel <= minimumLevel);


        #region "Functions"

        public TFunction CreateFunction<TFunction>() where TFunction : TrakHoundFunction
        {
            var configuration = new TrakHoundFunctionConfiguration();
            configuration.Id = ConfigurationId;
            configuration.FunctionId = ConfigurationId;
            configuration.Parameters = CopyParameters();

            var function = TrakHoundModuleFactory.CreateFunction<TFunction>(configuration, Client, Volume, Variables, InstanceId);
            function.LogReceived += (sender, item) => _logs.Enqueue(item);
            return function;
        }

        /// <summary>
        /// Runs the Function the way the Instance does: the FunctionParameter defaults are applied, and invalid input returns 400 without running it
        /// </summary>
        public Task<TrakHoundFunctionResponse> RunFunction<TFunction>(IReadOnlyDictionary<string, string> input = null, CancellationToken cancellationToken = default) where TFunction : TrakHoundFunction
        {
            var error = TrakHoundModuleFactory.PrepareFunctionParameters(typeof(TFunction), input, out var parameters);
            if (error != null) return Task.FromResult(new TrakHoundFunctionResponse(400, $"Invalid Parameters : {error}"));

            return CreateFunction<TFunction>().Run(parameters, cancellationToken);
        }

        /// <summary>
        /// Runs the Function type when the module under test calls Client.Functions.Run with the Function ID
        /// </summary>
        public void RegisterFunction<TFunction>(string functionId) where TFunction : TrakHoundFunction
        {
            Client.Functions.Register(functionId, parameters => RunFunction<TFunction>(parameters));
        }

        #endregion

        #region "Workers"

        public TWorker CreateWorker<TWorker>() where TWorker : TrakHoundWorker
        {
            var configuration = new TrakHoundWorkerConfiguration();
            configuration.Id = ConfigurationId;
            configuration.Parameters = CopyParameters();

            var worker = TrakHoundModuleFactory.CreateWorker<TWorker>(configuration, Client, Volume, Variables, InstanceId);
            worker.LogReceived += (sender, item) => _logs.Enqueue(item);
            return worker;
        }

        #endregion

        #region "Services"

        /// <summary>
        /// Creates the Service. Dispose it when done (or pass it to Client.Services.Use to test a module against it)
        /// </summary>
        public TService CreateService<TService>() where TService : TrakHoundService
        {
            var configuration = new TrakHoundServiceConfiguration();
            configuration.Id = ConfigurationId;
            configuration.Parameters = CopyParameters();

            var service = TrakHoundModuleFactory.CreateService<TService>(configuration, Volume, Variables, InstanceId);
            service.LogReceived += (sender, item) => _logs.Enqueue(item);
            return service;
        }

        #endregion

        #region "Apis"

        /// <summary>
        /// Creates the Controller and initializes it (OnInitialize and OnInitializeAsync). Call its endpoint methods directly
        /// </summary>
        public Task<TController> CreateApiController<TController>() where TController : TrakHoundApiController
        {
            var configuration = new TrakHoundApiConfiguration();
            configuration.Id = ConfigurationId;
            configuration.Route = ConfigurationId;
            configuration.Parameters = CopyParameters();

            var logger = new TrakHoundLogger(ConfigurationId);
            logger.LogEntryReceived += (sender, item) => _logs.Enqueue(item);

            return TrakHoundModuleFactory.CreateApiController<TController>(configuration, Client, Volume, Variables, logger, InstanceId);
        }

        #endregion

        #region "Identity Providers"

        public TProvider CreateIdentityProvider<TProvider>() where TProvider : ITrakHoundIdentityProvider
        {
            var configuration = new TrakHoundIdentityProviderConfiguration();
            configuration.Id = ConfigurationId;
            configuration.Parameters = CopyParameters();

            return TrakHoundModuleFactory.CreateIdentityProvider<TProvider>(configuration, Client, Volume, Variables);
        }

        /// <summary>
        /// Creates a Context for calling an Identity Provider. Set the Request's Headers, Query Parameters or Form Values first
        /// </summary>
        public TrakHoundIdentityContext CreateIdentityContext(TrakHoundDebugIdentityRequest request = null, string baseUrl = "http://localhost:8472")
        {
            return new TrakHoundIdentityContext(request ?? new TrakHoundDebugIdentityRequest(), new TrakHoundDebugIdentityStateStore(), baseUrl, null);
        }

        #endregion


        private Dictionary<string, object> CopyParameters() => new Dictionary<string, object>(Parameters);
    }
}
