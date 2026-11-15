// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using TrakHound.Functions;

namespace TrakHound.Hosting
{
    public static class TrakHoundFunctionHostExtensions
    {
        /// <summary>
        /// Runs the Function once the way the Instance runs it: a new Function for the Run, with the FunctionParameter defaults applied
        /// and invalid input refused (a 400 Response). Its Log entries go to ILogger&lt;TFunction&gt;
        /// </summary>
        public static async Task<TrakHoundFunctionResponse> RunFunction<TFunction>(this TrakHoundHostContext context, ILogger logger, IReadOnlyDictionary<string, string> input, CancellationToken cancellationToken = default) where TFunction : TrakHoundFunction
        {
            var parametersError = TrakHoundModuleFactory.PrepareFunctionParameters(typeof(TFunction), input, out var parameters);
            if (parametersError != null) return new TrakHoundFunctionResponse(400, $"Invalid Parameters : {parametersError}");

            var configuration = new TrakHoundFunctionConfiguration();
            configuration.Id = context.Options.ConfigurationId;
            configuration.FunctionId = context.Options.ConfigurationId;
            configuration.Parameters = context.GetParameters();

            var function = TrakHoundModuleFactory.CreateFunction<TFunction>(configuration, context.Client, context.Volume, context.Variables, await context.ConnectAsync());
            function.LogReceived += (sender, item) => TrakHoundHostLogging.Write(logger, item);

            return await function.Run(parameters, cancellationToken);
        }

        /// <summary>
        /// Starts the host, runs the Function once with the input (or "TrakHound:Input" from the settings), writes the Response (JSON) to the console,
        /// and stops the host. Returns the exit code: 0 when the Run succeeds. Stopping the host (ex. Ctrl+C) cancels the Run.
        /// For scheduled and one-off jobs (ex. Task Scheduler, cron, a container job)
        /// </summary>
        public static async Task<int> RunTrakHoundFunction<TFunction>(this IHost host, IReadOnlyDictionary<string, string> input = null) where TFunction : TrakHoundFunction
        {
            var context = host.Services.GetService<TrakHoundHostContext>() ?? throw new InvalidOperationException("Call services.AddTrakHound(...) before RunTrakHoundFunction");
            var logger = host.Services.GetRequiredService<ILogger<TFunction>>();
            var lifetime = host.Services.GetRequiredService<IHostApplicationLifetime>();

            await host.StartAsync();

            TrakHoundFunctionResponse response;
            try
            {
                response = await context.RunFunction<TFunction>(logger, input ?? context.Options.Input, lifetime.ApplicationStopping);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Function Run Error : {Message}", ex.Message);
                response = new TrakHoundFunctionResponse(500, ex.Message);
            }
            finally
            {
                await host.StopAsync();
            }

            // Write the Response even when the Run fails (its StatusCode and content say why)
            Console.WriteLine(Json.Convert(new TrakHoundFunctionJsonResponse(response), indented: true));
            return response.Success ? 0 : 1;
        }
    }
}
