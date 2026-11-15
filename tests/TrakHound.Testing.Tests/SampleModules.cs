// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Functions;
using TrakHound.Security;
using TrakHound.Services;
using TrakHound.Services.KeyValues;
using TrakHound.Workers;

namespace TrakHound.Testing.Tests
{
    // Modules written the way a package would write them, to test the test context against

    /// <summary>
    /// Adds "by" to a count kept in Client.Services.KeyValues
    /// </summary>
    [FunctionParameter("key", Description = "The key of the count", Required = true)]
    [FunctionParameter("by", Description = "How much to add", Type = TrakHoundFunctionParameterType.Integer, DefaultValue = "1")]
    public class CounterFunction : TrakHoundFunction
    {
        public CounterFunction(ITrakHoundFunctionConfiguration configuration) : base(configuration) { }

        protected override async Task<TrakHoundFunctionResponse> OnRun(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
        {
            var collection = Configuration.GetParameter("collection") ?? "counts";
            var key = parameters["key"];

            var read = await Client.Services.KeyValues.Read(collection, key);
            var count = read.Success ? int.Parse(read.Result) : 0;
            count += int.Parse(parameters["by"]);

            var publish = await Client.Services.KeyValues.Publish(collection, key, count.ToString());
            if (!publish.Success) return InternalError($"Publish Failed : {publish.StatusCode}");

            Log(TrakHoundLogLevel.Information, $"{key} = {count}");
            return Ok(count.ToString());
        }
    }

    /// <summary>
    /// Runs CounterFunction through Client.Functions twice
    /// </summary>
    public class TwiceFunction : TrakHoundFunction
    {
        public TwiceFunction(ITrakHoundFunctionConfiguration configuration) : base(configuration) { }

        protected override async Task<TrakHoundFunctionResponse> OnRun(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
        {
            var input = new Dictionary<string, string> { ["key"] = "twice" };

            var first = await Client.Functions.Run("counter", input);
            if (first.StatusCode != 200) return InternalError($"First Run Failed : {first.StatusCode}");

            var second = await Client.Functions.Run("counter", input);
            return second.StatusCode == 200 ? Ok(second.GetParameter("message")) : InternalError($"Second Run Failed : {second.StatusCode}");
        }
    }

    /// <summary>
    /// Writes a heartbeat to the Volume when it starts
    /// </summary>
    public class HeartbeatWorker : TrakHoundWorker
    {
        public HeartbeatWorker(ITrakHoundWorkerConfiguration configuration) : base(configuration) { }

        protected override async Task OnStartAsync()
        {
            await Volume.WriteString("heartbeat.txt", Configuration.GetParameter("message") ?? "alive");
            Log(TrakHoundLogLevel.Warning, "Heartbeat Written");
        }
    }

    public class GreetingController : TrakHoundApiController
    {
        private string _greeting;

        protected override Task OnInitializeAsync()
        {
            _greeting = Configuration.GetParameter("greeting") ?? "Hello";
            return Task.CompletedTask;
        }

        [ApiGet("greet", Description = "Greets the name.")]
        public async Task<TrakHoundApiResponse> Greet([FromQuery(Description = "Who to greet")] string name)
        {
            Log(TrakHoundLogLevel.Information, $"Greeting {name}");
            await Client.Services.Blobs.Upload("greetings", name, new MemoryStream(System.Text.Encoding.UTF8.GetBytes(_greeting)), "text/plain");
            return Ok($"{_greeting}, {name}");
        }
    }

    /// <summary>
    /// A Key Value Service that always returns "fixed", to test a module against a Service of your own
    /// </summary>
    public class FixedKeyValueService : TrakHoundService, ITrakHoundKeyValueService
    {
        public FixedKeyValueService(ITrakHoundServiceConfiguration configuration) : base(configuration) { }

        public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListCollections(CancellationToken cancellationToken = default) => Task.FromResult(Ok<IEnumerable<string>>(new[] { "counts" }));

        public Task<TrakHoundServiceResponse<IEnumerable<string>>> ListKeys(string collection, string pattern, int skip, int take, CancellationToken cancellationToken = default) => Task.FromResult(Ok<IEnumerable<string>>(Array.Empty<string>()));

        public Task<TrakHoundServiceResponse<TrakHoundKeyValueResponse>> Read(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(Ok(new TrakHoundKeyValueResponse { Collection = collection, Values = keys.ToDictionary(o => o, o => "41") }));
        }

        public Task<TrakHoundServiceResponse<int>> Count(string collection, CancellationToken cancellationToken = default) => Task.FromResult(Ok(1));

        public Task<TrakHoundServiceResponse> Publish(string collection, IReadOnlyDictionary<string, string> entries, TimeSpan? ttl = null, CancellationToken cancellationToken = default) => Task.FromResult(Ok());

        public Task<TrakHoundServiceResponse> Delete(string collection, IEnumerable<string> keys, CancellationToken cancellationToken = default) => Task.FromResult(Ok());
    }

    public class HeaderIdentityProvider : TrakHoundIdentityProviderBase
    {
        public HeaderIdentityProvider(ITrakHoundIdentityProviderConfiguration configuration, TrakHound.Clients.ITrakHoundClient client, TrakHound.Volumes.ITrakHoundVolume volume, TrakHound.Variables.ITrakHoundVariableCollection variables) : base(configuration, client, volume, variables) { }

        public override TrakHoundIdentityProviderFeatures Features => TrakHoundIdentityProviderFeatures.Token;

        protected override Task<TrakHoundIdentityResult> OnAuthenticate(TrakHoundIdentityContext context)
        {
            var token = context.Request.GetHeader("X-Test-Token");
            if (string.IsNullOrEmpty(token)) return Task.FromResult(TrakHoundIdentityResult.NoResult());
            if (token != Configuration.Parameters?.GetValueOrDefault("token")?.ToString()) return Task.FromResult(TrakHoundIdentityResult.Fail("Invalid Token"));

            return Task.FromResult(TrakHoundIdentityResult.Success(new TrakHoundIdentity { UserId = "tester", Roles = new[] { "admin" } }));
        }
    }
}
