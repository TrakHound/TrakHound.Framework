// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Functions;

namespace TrakHound.Example.Function
{
    /// <summary>
    /// Summarizes the simulated sensor Readings that the Example Worker wrote to the shared Volume
    /// </summary>
    [FunctionParameter("minutes", Description = "How many minutes of Readings to summarize", Type = TrakHoundFunctionParameterType.Integer, DefaultValue = "10")]
    public class Function : TrakHoundFunction
    {
        // The Instance creates the Function with this constructor, then sets Client, Volume and Variables
        public Function(ITrakHoundFunctionConfiguration configuration) : base(configuration) { }

        protected override async Task<TrakHoundFunctionResponse> OnRun(IReadOnlyDictionary<string, string> parameters, CancellationToken cancellationToken)
        {
            if (!int.TryParse(parameters.GetValueOrDefault("minutes"), out var minutes) || minutes <= 0)
            {
                return BadRequest("minutes must be a whole number greater than 0");
            }

            var from = DateTime.UtcNow.AddMinutes(-minutes);
            var readings = (await SensorReading.ReadAll(Volume)).Where(o => o.Timestamp >= from).ToList();

            Log(TrakHoundLogLevel.Information, $"Summarizing {readings.Count} Readings from the last {minutes} minutes");

            if (readings.Count == 0) return NotFound("No Readings found. Is TrakHound.Example.Worker running with the same Volume?");

            // Response Parameters are strings (an Api or App that runs the Function reads them by name)
            var response = Ok();
            response.AddParameter("count", readings.Count.ToString(CultureInfo.InvariantCulture));
            response.AddParameter("minimum", readings.Min(o => o.Value).ToString(CultureInfo.InvariantCulture));
            response.AddParameter("maximum", readings.Max(o => o.Value).ToString(CultureInfo.InvariantCulture));
            response.AddParameter("average", Math.Round(readings.Average(o => o.Value), 2).ToString(CultureInfo.InvariantCulture));
            response.AddParameter("from", readings.First().Timestamp.ToString("o"));
            response.AddParameter("to", readings.Last().Timestamp.ToString("o"));
            return response;
        }
    }
}
