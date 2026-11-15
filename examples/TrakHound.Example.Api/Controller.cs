// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using TrakHound.Api;

namespace TrakHound.Example.Api
{
    /// <summary>
    /// Serves the simulated sensor Readings that the Example Worker writes to the shared Volume, runs the Example Function, and reads and writes Variables
    /// </summary>
    public class Controller : TrakHoundApiController
    {
        private const string DefaultFunctionId = "TrakHound.Example.Function";


        [ApiGet("hello", Description = "Returns a greeting with the Instance time. Use it to check that the Api is running.")]
        [ApiMcpTool]
        public TrakHoundApiResponse Hello(
            [FromQuery(Description = "Who to greet")] string name = "World"
            )
        {
            return Ok($"Hello {name} from {Package?.Id} at {DateTime.Now:o}");
        }


        #region "Readings"

        [ApiGet("readings/latest", Description = "Gets the most recent simulated sensor Reading (written by TrakHound.Example.Worker).")]
        [ApiMcpTool]
        public async Task<TrakHoundApiResponse> GetLatestReading()
        {
            var reading = (await SensorReading.ReadAll(Volume)).LastOrDefault();
            return reading != null ? Ok(reading) : NotFound("No Readings found. Is TrakHound.Example.Worker running with the same Volume?");
        }

        [ApiGet("readings", Description = "Lists the most recent simulated sensor Readings, newest first.")]
        [ApiMcpTool]
        public async Task<TrakHoundApiResponse> GetReadings(
            [FromQuery(Description = "The most Readings to return (1 to 10,000)")] int take = 100
            )
        {
            var readings = await SensorReading.ReadAll(Volume);
            return Ok(Enumerable.Reverse(readings).Take(Math.Clamp(take, 1, 10000)));
        }

        [ApiStream("readings", Description = "Streams each new simulated sensor Reading as the Worker writes it.")]
        public Task<ITrakHoundStream<TrakHoundApiResponse>> StreamReadings()
        {
            var stream = new TrakHoundStream<TrakHoundApiResponse>();
            DateTime? lastTimestamp = null;

            // Check the Volume every second and send a Reading when it changes
            var timer = new System.Timers.Timer(1000);
            timer.Elapsed += async (s, args) =>
            {
                var reading = (await SensorReading.ReadAll(Volume)).LastOrDefault();
                if (reading != null && reading.Timestamp != lastTimestamp)
                {
                    lastTimestamp = reading.Timestamp;
                    await stream.Push(Ok(reading));
                }
            };
            timer.Start();

            stream.OnDisposed = () => timer.Dispose();

            return Task.FromResult<ITrakHoundStream<TrakHoundApiResponse>>(stream);
        }

        [ApiGet("summary", Description = "Gets the count, minimum, maximum and average of the recent Readings by running TrakHound.Example.Function.")]
        [ApiMcpTool]
        public async Task<TrakHoundApiResponse> GetSummary(
            [FromQuery(Description = "How many minutes of Readings to summarize")] int minutes = 10
            )
        {
            // The "functionId" Parameter in the Api's Configuration, for when the Function was added with a different ID
            var functionId = Configuration?.GetParameter("functionId");
            if (string.IsNullOrEmpty(functionId)) functionId = DefaultFunctionId;

            var inputParameters = new Dictionary<string, string> { ["minutes"] = minutes.ToString() };
            var response = await Client.Functions.Run(functionId, inputParameters);

            if (!response.Success)
            {
                var message = response.GetParameter("message") ?? $"{functionId} returned {response.StatusCode}";
                return new TrakHoundApiResponse(response.StatusCode, message.ToUtf8Bytes(), "text/plain");
            }

            return Ok(response.Parameters);
        }

        #endregion

        #region "Variables"

        [ApiGet("variables/{key}", Description = "Reads a Variable of the Instance.")]
        [ApiMcpTool]
        public async Task<TrakHoundApiResponse> GetVariable(
            [FromRoute(Description = "The Variable key")] string key
            )
        {
            var value = await Variables.ReadString(key);
            return value != null ? Ok(value) : NotFound();
        }

        [ApiPut("variables/{key}", Description = "Sets a Variable of the Instance.")]
        public async Task<TrakHoundApiResponse> SetVariable(
            [FromRoute(Description = "The Variable key")] string key,
            [FromQuery(Description = "The value to set")] string value
            )
        {
            return await Variables.Set(key, value) ? Ok() : BadRequest();
        }

        #endregion
    }
}
