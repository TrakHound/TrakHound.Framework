// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using ModelContextProtocol;
using ModelContextProtocol.Server;
using System.ComponentModel;
using TrakHound.Functions;
using TrakHound.Instance.Security;
using TrakHound.Instances;

namespace TrakHound.Instance.Mcp
{
    [McpServerToolType]
    public class TrakHoundMcpFunctionTools : TrakHoundMcpTools
    {
        /// <summary>
        /// The most recent Log lines returned with a Function Run
        /// </summary>
        private const int MaxRunLogs = 50;


        public TrakHoundMcpFunctionTools(ITrakHoundInstance instance) : base(instance) { }


        [McpServerTool(Name = "functions_list", Title = "List Functions", ReadOnly = true)]
        [Description("Lists the Functions on the Instance (ID, name, description, package, run count and average duration).")]
        [TrakHoundMcpTool(Resource = TrakHoundSystemSecurity.Functions)]
        public async Task<string> List()
        {
            var functions = await GetClient().Functions.GetInformation();
            return ToJson(functions?.OrderBy(o => o.FunctionId).Select(o => new
            {
                functionId = o.FunctionId,
                name = o.Name,
                description = o.Description,
                packageId = o.PackageId,
                packageVersion = o.PackageVersion,
                runCount = o.RunCount,
                averageDurationMs = Math.Round(o.AverageDuration.TotalMilliseconds, 1)
            }));
        }

        [McpServerTool(Name = "functions_get", Title = "Get Function", ReadOnly = true)]
        [Description("Gets a Function's details: its input parameters (name, description, type, required, default value, options) when the Function describes them, and its package ReadMe.")]
        [TrakHoundMcpTool(Resource = TrakHoundSystemSecurity.Functions)]
        public async Task<string> Get([Description("The Function ID (from functions_list)")] string functionId)
        {
            var function = await GetClient().Functions.GetInformation(functionId);
            if (function == null) throw new McpException($"Function '{functionId}' was not found");

            return ToJson(new
            {
                functionId = function.FunctionId,
                name = function.Name,
                description = function.Description,
                packageId = function.PackageId,
                packageVersion = function.PackageVersion,
                runCount = function.RunCount,
                averageDurationMs = Math.Round(function.AverageDuration.TotalMilliseconds, 1),
                parameters = function.Parameters?.Select(o => new
                {
                    name = o.Name,
                    description = o.Description,
                    type = o.Type.ToString(),
                    required = o.Required,
                    defaultValue = o.DefaultValue,
                    options = o.Options,
                    secret = o.Secret
                }),
                readMe = Truncate(function.PackageReadMe, 20_000)
            });
        }

        [McpServerTool(Name = "functions_run", Title = "Run Function", Destructive = true, OpenWorld = true)]
        [Description("Runs a Function and waits for it to finish. Returns the status code, the output parameters and the most recent log lines. Status changes are reported as progress. Functions can change data or control equipment, so check what a Function does (functions_get) before running it.")]
        [TrakHoundMcpTool(Write = true, Resource = TrakHoundSystemSecurity.Functions)]
        public async Task<string> Run(
            [Description("The Function ID (from functions_list)")] string functionId,
            [Description("Input parameters (names and string values). functions_get lists the parameters a Function describes; a Run missing a required one is refused")] Dictionary<string, string> inputParameters,
            IProgress<ProgressNotificationValue> progress,
            CancellationToken cancellationToken)
        {
            var client = GetClient();
            var runId = Guid.NewGuid().ToString();

            // Report each Status change as progress (subscribed before the Run starts so the first Status isn't missed)
            ITrakHoundStream<TrakHoundFunctionStatusResponse> statusStream = null;
            if (progress != null)
            {
                try
                {
                    statusStream = await client.Functions.SubscribeRunStatus(runId);
                    if (statusStream != null)
                    {
                        var step = 0;
                        statusStream.OnReceived = status => progress.Report(new ProgressNotificationValue { Progress = ++step, Message = status.Status.ToString() });
                        statusStream.Start();
                    }
                }
                catch { } // Progress is optional
            }

            // Cancel the Run when the client cancels the call
            using var cancellation = cancellationToken.Register(() => _ = client.Functions.CancelRun(functionId, runId));

            try
            {
                var response = await client.Functions.Run(functionId, inputParameters, runId);
                if (response.StatusCode == 0) throw new McpException($"Function '{functionId}' was not found or could not be run");

                return ToJson(new
                {
                    runId,
                    success = response.Success,
                    statusCode = response.StatusCode,
                    durationMs = response.Completed > response.Started ? Math.Round((response.Completed - response.Started) / 1_000_000d, 1) : (double?)null,
                    output = response.Parameters,
                    logs = response.Logs?.TakeLast(MaxRunLogs).Select(FormatLogItem)
                });
            }
            finally
            {
                statusStream?.Dispose();
            }
        }

        [McpServerTool(Name = "functions_cancel", Title = "Cancel Function Run", Destructive = true)]
        [Description("Cancels a running Function Run.")]
        [TrakHoundMcpTool(Write = true, Resource = TrakHoundSystemSecurity.Functions)]
        public async Task<string> Cancel(
            [Description("The Function ID")] string functionId,
            [Description("The Run ID (returned by functions_run)")] string runId)
        {
            var cancelled = await GetClient().Functions.CancelRun(functionId, runId);
            return ToJson(new { cancelled });
        }
    }
}
