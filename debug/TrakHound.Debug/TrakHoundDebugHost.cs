// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients;
using TrakHound.Functions;
using TrakHound.Hosting;
using TrakHound.Logging;
using TrakHound.Security;
using TrakHound.Services;
using TrakHound.Variables;
using TrakHound.Volumes;
using TrakHound.Workers;

namespace TrakHound.Debug
{
    /// <summary>
    /// Runs a package locally, without an Instance. Creates the Client, Volume and Variables from trakhound.debug.json
    /// and creates modules the same way the Instance does.
    /// </summary>
    public class TrakHoundDebugHost
    {
        private readonly TrakHoundDebugConfiguration _configuration;
        private readonly TrakHoundHttpClientConfiguration _clientConfiguration;
        private readonly ITrakHoundClient _client;
        private readonly ITrakHoundVolume _volume;
        private readonly ITrakHoundVariableCollection _variables;
        private string _instanceId;


        /// <summary>
        /// The settings read from trakhound.debug.json
        /// </summary>
        public TrakHoundDebugConfiguration Configuration => _configuration;

        /// <summary>
        /// The configuration of the Client (the Instance URL and credentials)
        /// </summary>
        public TrakHoundHttpClientConfiguration ClientConfiguration => _clientConfiguration;

        /// <summary>
        /// A Client for the Instance set by "instanceUrl"
        /// </summary>
        public ITrakHoundClient Client => _client;

        /// <summary>
        /// The module's Volume (the "volume" folder in the build output by default)
        /// </summary>
        public ITrakHoundVolume Volume => _volume;

        /// <summary>
        /// The module's Variables (kept in the build output)
        /// </summary>
        public ITrakHoundVariableCollection Variables => _variables;

        /// <summary>
        /// The ID of the Instance (null when the Instance can't be reached)
        /// </summary>
        public string InstanceId => _instanceId;


        private TrakHoundDebugHost(TrakHoundDebugConfiguration configuration)
        {
            _configuration = configuration;

            var instanceUrl = !string.IsNullOrEmpty(configuration.InstanceUrl) ? configuration.InstanceUrl : TrakHoundDebugConfiguration.DefaultInstanceUrl;
            _clientConfiguration = TrakHoundHttpClientConfiguration.FromUrl(instanceUrl);
            if (_clientConfiguration == null) throw new InvalidOperationException($"Invalid Instance URL : {instanceUrl} (set \"instanceUrl\" in {TrakHoundDebugConfiguration.Filename})");

            _clientConfiguration.ApiKey = configuration.ApiKey;
            _clientConfiguration.BearerToken = configuration.BearerToken;
            _client = new TrakHoundHttpClient(_clientConfiguration, null);

            var volumePath = !string.IsNullOrEmpty(configuration.VolumePath) ? configuration.VolumePath : "volume";
            _volume = new TrakHoundVolume("volume", Path.Combine(AppContext.BaseDirectory, volumePath));

            _variables = new TrakHoundVariableCollection("variables", AppContext.BaseDirectory);
        }

        /// <summary>
        /// Reads trakhound.debug.json (or the file at configurationPath) and checks that the Instance can be reached
        /// </summary>
        public static async Task<TrakHoundDebugHost> Create(string configurationPath = null)
        {
            var host = new TrakHoundDebugHost(TrakHoundDebugConfiguration.Read(configurationPath));

            // The module runs without an Instance, so a failed connection is only a warning (Client calls will fail)
            try
            {
                var information = await host._client.Instances.GetHostInformation();
                host._instanceId = information?.Id;
            }
            catch { }

            if (host._instanceId != null) Console.WriteLine($"Connected to Instance : {host._configuration.InstanceUrl} : {host._instanceId}");
            else Console.WriteLine($"Instance Not Reachable : {host._configuration.InstanceUrl} : Client calls will fail (start the Instance or set \"instanceUrl\" in {TrakHoundDebugConfiguration.Filename})");

            return host;
        }


        /// <summary>
        /// Writes a Log entry to the Console
        /// </summary>
        public static void WriteLog(TrakHoundLogItem item)
        {
            Console.WriteLine($"Log : {item.Timestamp.ToLocalDateTime()} : {item.LogLevel} : {item.Code} : {item.Message}");
        }

        /// <summary>
        /// Returns a copy of the Parameters in trakhound.debug.json (each module gets its own copy, as on an Instance)
        /// </summary>
        public Dictionary<string, object> GetParameters() => new Dictionary<string, object>(_configuration.Parameters ?? new Dictionary<string, object>());


        #region "Functions"

        /// <summary>
        /// Creates the Function the same way the Instance does
        /// </summary>
        public TFunction CreateFunction<TFunction>() where TFunction : TrakHoundFunction
        {
            var configuration = new TrakHoundFunctionConfiguration();
            configuration.Id = _configuration.ConfigurationId;
            configuration.FunctionId = _configuration.ConfigurationId;
            configuration.Parameters = GetParameters();

            var function = TrakHoundModuleFactory.CreateFunction<TFunction>(configuration, _client, _volume, _variables, _instanceId);
            function.LogReceived += (sender, item) => WriteLog(item);
            return function;
        }

        /// <summary>
        /// Runs the Function once and prints the Response. The input Parameters are the JSON in the first argument,
        /// or "input" in trakhound.debug.json. Returns the exit code (0 when the Run succeeds)
        /// </summary>
        public async Task<int> RunFunction<TFunction>(string[] args) where TFunction : TrakHoundFunction
        {
            Dictionary<string, string> input;
            if (args != null && args.Length > 0)
            {
                input = Json.Convert<Dictionary<string, string>>(args[0]);
                if (input == null)
                {
                    Console.WriteLine($"Invalid Input : the first argument must be a JSON object of Parameters, ex. {{\"name\":\"value\"}}");
                    return 1;
                }
            }
            else
            {
                input = _configuration.GetInput();
            }

            // Apply the FunctionParameter defaults and refuse invalid input, the same as the Instance
            var parametersError = TrakHoundModuleFactory.PrepareFunctionParameters(typeof(TFunction), input, out var parameters);

            TrakHoundFunctionResponse response;
            if (parametersError != null)
            {
                response = new TrakHoundFunctionResponse(400, $"Invalid Parameters : {parametersError}");
            }
            else
            {
                var function = CreateFunction<TFunction>();

                // Cancel the Run on Ctrl+C (a Function should honor its CancellationToken)
                using var cancellation = new CancellationTokenSource();
                ConsoleCancelEventHandler cancelHandler = (sender, e) =>
                {
                    e.Cancel = true;
                    cancellation.Cancel();
                };
                Console.CancelKeyPress += cancelHandler;

                try
                {
                    response = await function.Run(parameters, cancellation.Token);
                }
                finally
                {
                    Console.CancelKeyPress -= cancelHandler;
                }
            }

            // Print the Response even when the Run fails (its StatusCode and content say why)
            Console.WriteLine();
            Console.WriteLine("RESPONSE:");
            Console.WriteLine("--------------");
            Console.WriteLine($"ID = {response.Id}");
            Console.WriteLine($"StatusCode = {response.StatusCode}");
            Console.WriteLine();
            Console.WriteLine(response.ToJson(true));

            return response.Success ? 0 : 1;
        }

        #endregion

        #region "Workers"

        /// <summary>
        /// Creates the Worker the same way the Instance does
        /// </summary>
        public TWorker CreateWorker<TWorker>() where TWorker : TrakHoundWorker
        {
            var configuration = new TrakHoundWorkerConfiguration();
            configuration.Id = _configuration.ConfigurationId;
            configuration.Parameters = GetParameters();

            var worker = TrakHoundModuleFactory.CreateWorker<TWorker>(configuration, _client, _volume, _variables, _instanceId);
            worker.LogReceived += (sender, item) => WriteLog(item);
            return worker;
        }

        /// <summary>
        /// Starts the Worker and stops it on Ctrl+C
        /// </summary>
        public async Task RunWorker<TWorker>() where TWorker : TrakHoundWorker
        {
            var worker = CreateWorker<TWorker>();

            var stopped = new TaskCompletionSource();
            ConsoleCancelEventHandler cancelHandler = (sender, e) =>
            {
                e.Cancel = true;
                stopped.TrySetResult();
            };
            Console.CancelKeyPress += cancelHandler;

            await worker.Start();

            Console.WriteLine("Press Ctrl+C to Stop Worker..");
            await stopped.Task;
            Console.CancelKeyPress -= cancelHandler;

            Console.WriteLine("Stopping Worker..");
            await worker.Stop();
        }

        #endregion

        #region "Services"

        /// <summary>
        /// Creates the Service the same way the Instance does. Dispose it when done
        /// </summary>
        public TService CreateService<TService>() where TService : TrakHoundService
        {
            var configuration = new TrakHoundServiceConfiguration();
            configuration.Id = _configuration.ConfigurationId;
            configuration.Parameters = GetParameters();

            var service = TrakHoundModuleFactory.CreateService<TService>(configuration, _volume, _variables, _instanceId);
            service.LogReceived += (sender, item) => WriteLog(item);
            return service;
        }

        #endregion

        #region "Identity Providers"

        /// <summary>
        /// Creates the Identity Provider the same way the Instance does
        /// </summary>
        public TProvider CreateIdentityProvider<TProvider>() where TProvider : ITrakHoundIdentityProvider
        {
            var configuration = new TrakHoundIdentityProviderConfiguration();
            configuration.Id = _configuration.ConfigurationId;
            configuration.Parameters = GetParameters();

            return TrakHoundModuleFactory.CreateIdentityProvider<TProvider>(configuration, _client, _volume, _variables);
        }

        /// <summary>
        /// Creates a Context for calling an Identity Provider, with a Request that has no Headers or Query Parameters yet
        /// </summary>
        public TrakHoundIdentityContext CreateIdentityContext(TrakHoundDebugIdentityRequest request = null)
        {
            return new TrakHoundIdentityContext(request ?? new TrakHoundDebugIdentityRequest(), new TrakHoundDebugIdentityStateStore(), _configuration.InstanceUrl, null);
        }

        #endregion
    }
}
