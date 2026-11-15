// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;
using System.Linq;

namespace TrakHound.Functions
{
    /// <summary>
    /// A JSON serializable version of a TrakHoundFunctionResponse
    /// </summary>
    public class TrakHoundFunctionJsonResponse
    {
        /// <summary>
        /// The Id of the Function Run
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// The Id of the Package that contains the Function
        /// </summary>
        public string PackageId { get; set; }

        /// <summary>
        /// The Version of the Package that contains the Function
        /// </summary>
        public string PackageVersion { get; set; }

        /// <summary>
        /// The Id of the Engine that Ran the Function
        /// </summary>
        public string EngineId { get; set; }

        /// <summary>
        /// The Status Code returned by the Function (2xx = Success)
        /// </summary>
        public int StatusCode { get; set; }

        /// <summary>
        /// The time the Run started (Unix Nanoseconds)
        /// </summary>
        public long Started { get; set; }

        /// <summary>
        /// The time the Run completed (Unix Nanoseconds)
        /// </summary>
        public long Completed { get; set; }

        /// <summary>
        /// CPU time consumed by the Run in milliseconds (0 when CPU Monitoring is disabled)
        /// </summary>
        public double CpuTime { get; set; }

        /// <summary>
        /// The Parameters returned by the Function
        /// </summary>
        public Dictionary<string, string> Parameters { get; set; }


        /// <summary>
        /// Create a new empty Response
        /// </summary>
        public TrakHoundFunctionJsonResponse() { }

        /// <summary>
        /// Create a new JSON Response from the specified Function Response (Logs are not included)
        /// </summary>
        public TrakHoundFunctionJsonResponse(TrakHoundFunctionResponse response)
        {
            Id = response.Id;
            PackageId = response.PackageId;
            PackageVersion = response.PackageVersion;
            EngineId = response.EngineId;
            StatusCode = response.StatusCode;
            Started = response.Started;
            Completed = response.Completed;
            CpuTime = response.CpuTime;
            Parameters = response.Parameters?.ToDictionary(o => o.Key, o=> o.Value);
        }


        /// <summary>
        /// Convert to a TrakHoundFunctionResponse
        /// </summary>
        public TrakHoundFunctionResponse ToResponse()
        {
            var response = new TrakHoundFunctionResponse();
            response.Id = Id;
            response.PackageId = PackageId;
            response.PackageVersion = PackageVersion;
            response.EngineId = EngineId;
            response.StatusCode = StatusCode;
            response.Started = Started;
            response.Completed = Completed;
            response.CpuTime = CpuTime;

            if (!Parameters.IsNullOrEmpty())
            {
                foreach (var parameter in Parameters)
                {
                    response.AddParameter(parameter.Key, parameter.Value);
                }
            }

            return response;
        }
    }
}
