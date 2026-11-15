// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Clients;
using TrakHound.Functions;

namespace TrakHound.ScheduledTasks
{
    internal class FunctionRunResult
    {
        public bool Success { get; set; }
        public int StatusCode { get; set; }
        public string RunId { get; set; }
        public string Message { get; set; }
    }

    /// <summary>
    /// Runs the Function of a Scheduled Task (replaced in tests)
    /// </summary>
    internal interface IFunctionRunner
    {
        Task<FunctionRunResult> Run(ScheduledTaskConfiguration configuration, CancellationToken cancellationToken);
    }

    /// <summary>
    /// Runs Functions with the TrakHound.Functions.Api package (which records the Run, and the Task that started it)
    /// </summary>
    internal class ApiFunctionRunner : IFunctionRunner
    {
        private readonly ITrakHoundClient _client;


        public ApiFunctionRunner(ITrakHoundClient client)
        {
            _client = client;
        }

        public async Task<FunctionRunResult> Run(ScheduledTaskConfiguration configuration, CancellationToken cancellationToken)
        {
            var route = Url.Combine("functions", configuration.FunctionId, "run");

            // The Task ID attributes the Run to this Scheduled Task (for the per Schedule metrics)
            var queryParameters = new Dictionary<string, string>();
            queryParameters.Add("taskId", configuration.Id);

            // The Job's Parameters are read by the Run route when the Run starts (a Task without a Job runs with no Parameters)
            if (!string.IsNullOrEmpty(configuration.JobId)) queryParameters.Add("jobId", configuration.JobId);

            var apiResponse = await _client.Api.Post(route, (object)new Dictionary<string, string>(), "application/json", queryParameters, cancellationToken);

            var result = new FunctionRunResult();
            result.Success = apiResponse.Success;
            result.StatusCode = apiResponse.StatusCode;

            // The Function's Response (the body may not be JSON, ex. when the Functions Api isn't installed)
            TrakHoundFunctionJsonResponse jsonResponse = null;
            try { jsonResponse = apiResponse.GetJsonContentObject<TrakHoundFunctionJsonResponse>(); }
            catch { }

            if (jsonResponse != null)
            {
                var functionResponse = jsonResponse.ToResponse();
                result.RunId = functionResponse.Id;
                result.StatusCode = functionResponse.StatusCode;
                result.Message = functionResponse.GetParameter("message");
            }
            else if (!apiResponse.Success)
            {
                result.Message = apiResponse.GetContentUtf8String();
            }

            return result;
        }
    }
}
