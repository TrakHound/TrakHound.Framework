// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Concurrent;
using TrakHound.Clients;
using TrakHound.Functions;
using TrakHound.Logging;

namespace TrakHound.Testing.Clients
{
    /// <summary>
    /// Client.Functions for tests: Run calls the handler registered for the Function ID (TrakHoundTestContext.RegisterFunction registers a
    /// Function type), and every Run is recorded in Runs. A Function ID without a handler returns Not Found
    /// </summary>
    public class TrakHoundTestFunctionsClient : ITrakHoundFunctionsClient
    {
        private readonly ConcurrentDictionary<string, Func<IReadOnlyDictionary<string, string>, Task<TrakHoundFunctionResponse>>> _handlers = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentQueue<TrakHoundTestFunctionRun> _runs = new();


        /// <summary>
        /// The Runs requested through Client.Functions.Run, in order
        /// </summary>
        public IReadOnlyList<TrakHoundTestFunctionRun> Runs => _runs.ToList();


        /// <summary>
        /// Runs the handler when Client.Functions.Run is called with the Function ID
        /// </summary>
        public void Register(string functionId, Func<IReadOnlyDictionary<string, string>, Task<TrakHoundFunctionResponse>> handler)
        {
            if (string.IsNullOrEmpty(functionId)) throw new ArgumentNullException(nameof(functionId));
            _handlers[functionId] = handler ?? throw new ArgumentNullException(nameof(handler));
        }

        /// <summary>
        /// Returns the response when Client.Functions.Run is called with the Function ID
        /// </summary>
        public void Register(string functionId, Func<IReadOnlyDictionary<string, string>, TrakHoundFunctionResponse> handler)
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            Register(functionId, parameters => Task.FromResult(handler(parameters)));
        }


        public async Task<TrakHoundFunctionResponse> Run(string functionId, IReadOnlyDictionary<string, string> inputParameters = null, string runId = null, long timestamp = 0)
        {
            _runs.Enqueue(new TrakHoundTestFunctionRun(functionId, inputParameters));

            if (functionId != null && _handlers.TryGetValue(functionId, out var handler))
            {
                return await handler(inputParameters ?? new Dictionary<string, string>());
            }

            return new TrakHoundFunctionResponse(404, $"Function Not Found : {functionId} (register it with TrakHoundTestContext.RegisterFunction or Client.Functions.Register)");
        }

        public Task<IEnumerable<TrakHoundFunctionInformation>> GetInformation()
        {
            return Task.FromResult<IEnumerable<TrakHoundFunctionInformation>>(_handlers.Keys.Select(o => new TrakHoundFunctionInformation { FunctionId = o }).ToList());
        }

        public Task<TrakHoundFunctionInformation> GetInformation(string functionId)
        {
            var information = functionId != null && _handlers.ContainsKey(functionId) ? new TrakHoundFunctionInformation { FunctionId = functionId } : null;
            return Task.FromResult(information);
        }

        public async Task<IEnumerable<TrakHoundFunctionInformation>> GetInformation(IEnumerable<string> functionIds)
        {
            var results = new List<TrakHoundFunctionInformation>();
            foreach (var functionId in functionIds ?? Enumerable.Empty<string>())
            {
                var information = await GetInformation(functionId);
                if (information != null) results.Add(information);
            }
            return results;
        }

        public Task<ITrakHoundStream<TrakHoundFunctionStatusResponse>> SubscribeRunStatus(string runId)
        {
            throw new NotSupportedException("TrakHoundTestClient doesn't fake Client.Functions.SubscribeRunStatus (Run returns when the Function completes)");
        }

        public Task<ITrakHoundStream<IEnumerable<TrakHoundLogItem>>> SubscribeRunLog(string runId, TrakHoundLogLevel minimumLogLevel = TrakHoundLogLevel.Trace)
        {
            throw new NotSupportedException("TrakHoundTestClient doesn't fake Client.Functions.SubscribeRunLog (the Function's Log entries are in TrakHoundTestContext.Logs)");
        }
    }


    /// <summary>
    /// A Run requested through Client.Functions.Run
    /// </summary>
    public class TrakHoundTestFunctionRun
    {
        public string FunctionId { get; }

        public IReadOnlyDictionary<string, string> Parameters { get; }


        public TrakHoundTestFunctionRun(string functionId, IReadOnlyDictionary<string, string> parameters)
        {
            FunctionId = functionId;
            Parameters = parameters;
        }
    }
}
