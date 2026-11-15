// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using TrakHound.Clients;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Hosting
{
    /// <summary>
    /// What a standalone host gives its modules: the Client, Volume, Variables and Parameters from TrakHoundHostOptions.
    /// Registered as a singleton by AddTrakHound
    /// </summary>
    public class TrakHoundHostContext
    {
        // How long to wait for the Instance before running without it (a module shouldn't wait on an unreachable Instance to start)
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(10);

        private readonly ILogger _logger;
        private readonly Lazy<Task<string>> _connect;


        public TrakHoundHostOptions Options { get; }

        /// <summary>
        /// A Client for the Instance set by InstanceUrl. Without one, every call fails with a message that says why
        /// </summary>
        public ITrakHoundClient Client { get; }

        public ITrakHoundVolume Volume { get; }

        public ITrakHoundVariableCollection Variables { get; }

        /// <summary>
        /// Whether InstanceUrl is set
        /// </summary>
        public bool IsInstanceConfigured { get; }

        /// <summary>
        /// The ID of the Instance (null until ConnectAsync reaches it)
        /// </summary>
        public string InstanceId { get; private set; }


        public TrakHoundHostContext(IOptions<TrakHoundHostOptions> options, ILogger<TrakHoundHostContext> logger)
        {
            Options = options.Value;
            _logger = logger;

            if (!string.IsNullOrEmpty(Options.InstanceUrl))
            {
                var clientConfiguration = TrakHoundHttpClientConfiguration.FromUrl(Options.InstanceUrl);
                if (clientConfiguration == null) throw new InvalidOperationException($"Invalid Instance URL : {Options.InstanceUrl} (set \"{TrakHoundHostOptions.SectionName}:InstanceUrl\")");

                clientConfiguration.ApiKey = Options.ApiKey;
                clientConfiguration.BearerToken = Options.BearerToken;
                Client = new TrakHoundHttpClient(clientConfiguration, null);
                IsInstanceConfigured = true;
            }
            else
            {
                Client = new TrakHoundUnconfiguredClient();
            }

            var volumePath = !string.IsNullOrEmpty(Options.VolumePath) ? Options.VolumePath : TrakHoundHostOptions.DefaultVolumePath;
            Volume = new TrakHoundVolume("volume", Path.Combine(AppContext.BaseDirectory, volumePath));

            var variablesPath = !string.IsNullOrEmpty(Options.VariablesPath) ? Path.Combine(AppContext.BaseDirectory, Options.VariablesPath) : AppContext.BaseDirectory;
            Variables = new TrakHoundVariableCollection("variables", variablesPath);

            _connect = new Lazy<Task<string>>(Connect);
        }


        /// <summary>
        /// Returns a copy of the Parameters (each module gets its own copy, as on an Instance)
        /// </summary>
        public Dictionary<string, object> GetParameters()
        {
            var parameters = new Dictionary<string, object>();
            if (Options.Parameters != null)
            {
                foreach (var parameter in Options.Parameters) parameters[parameter.Key] = parameter.Value;
            }
            return parameters;
        }

        /// <summary>
        /// Connects to the Instance once (later calls return the same result) and returns its ID, or null when there is no Instance or it can't be reached.
        /// The module still runs without it
        /// </summary>
        public Task<string> ConnectAsync() => _connect.Value;

        /// <summary>
        /// Asks the Instance for its ID now (used by the health check). Returns null when it can't be reached
        /// </summary>
        public async Task<string> CheckInstanceAsync(CancellationToken cancellationToken = default)
        {
            if (!IsInstanceConfigured) return null;

            try
            {
                var information = await Client.Instances.GetHostInformation().WaitAsync(ConnectTimeout, cancellationToken);
                return information?.Id;
            }
            catch
            {
                return null;
            }
        }


        private async Task<string> Connect()
        {
            if (!IsInstanceConfigured)
            {
                _logger.LogInformation("No Instance Configured : Client calls will fail (set \"{Section}:InstanceUrl\")", TrakHoundHostOptions.SectionName);
                return null;
            }

            InstanceId = await CheckInstanceAsync();
            if (InstanceId != null) _logger.LogInformation("Connected to Instance : {InstanceUrl} : {InstanceId}", Options.InstanceUrl, InstanceId);
            else _logger.LogWarning("Instance Not Reachable : {InstanceUrl} : Client calls will fail until it is", Options.InstanceUrl);

            return InstanceId;
        }
    }
}
