// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using System.Reflection;
using TrakHound.Api;
using TrakHound.Clients;
using TrakHound.Hosting;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Debug.AspNetCore
{
    public static class WebApplicationExtensions
    {
        /// <summary>
        /// Where MapTrakHoundApiEndpoints serves the Api Debugger
        /// </summary>
        public const string DebuggerRoute = "/_debug";

        /// <summary>
        /// Where MapTrakHoundApiEndpoints serves the Api's OpenAPI Document
        /// </summary>
        public const string OpenApiRoute = "/_debug/openapi.json";

        /// <summary>
        /// Where MapTrakHoundApiEndpoints serves the Log entries (Server-Sent Events) shown by the Api Debugger
        /// </summary>
        public const string LogsRoute = "/_debug/logs";


        /// <summary>
        /// Maps the Api's endpoints using the Client, Volume, Variables and Parameters of the Debug Host (trakhound.debug.json)
        /// </summary>
        public static void MapTrakHoundApiEndpoints(this WebApplication app, TrakHoundDebugHost host)
        {
            var configuration = new TrakHoundApiConfiguration();
            configuration.Id = host.Configuration.ConfigurationId;
            configuration.Parameters = host.GetParameters();

            MapTrakHoundApiEndpoints(app, configuration, host.Client, host.Volume, host.Variables, host.InstanceId);
        }

        public static void MapTrakHoundApiEndpoints(
            this WebApplication app,
            ITrakHoundClient client,
            ITrakHoundVolume volume,
            string instanceId = null
            )
        {
            MapTrakHoundApiEndpoints(app, new TrakHoundApiConfiguration(), client, volume, null, instanceId);
        }

        /// <param name="assembly">The assembly with the Controllers (the entry assembly when null, which is the Api project when it runs its debug host)</param>
        public static void MapTrakHoundApiEndpoints(
            this WebApplication app,
            ITrakHoundClient client,
            ITrakHoundVolume volume,
            ITrakHoundVariableCollection variables,
            string instanceId = null,
            Assembly assembly = null
            )
        {
            MapTrakHoundApiEndpoints(app, new TrakHoundApiConfiguration(), client, volume, variables, instanceId, assembly);
        }

        private static void MapTrakHoundApiEndpoints(
            WebApplication app,
            TrakHoundApiConfiguration moduleConfiguration,
            ITrakHoundClient client,
            ITrakHoundVolume volume,
            ITrakHoundVariableCollection variables,
            string instanceId,
            Assembly assembly = null
            )
        {
            // Map the same way a standalone host does, and write what happens to the Console and the Api Debugger's Log
            var options = new TrakHoundApiHostOptions();
            options.Assembly = assembly;
            options.Configuration = moduleConfiguration;
            options.Client = client;
            options.Volume = volume;
            options.Variables = variables;
            options.InstanceId = instanceId;
            options.LogReceived = TrakHoundDebugLogs.Write;
            options.ErrorReceived = TrakHoundDebugLogs.WriteError;
            options.RequestCompleted = request => TrakHoundDebugLogs.WriteRequest(request.Method, request.Path, request.StatusCode, request.Duration);

            var endpoints = app.MapTrakHoundApi(options);

            app.MapTrakHoundOpenApi(endpoints, OpenApiRoute, ReadPackageInformation());
            MapDebugger(app);
        }


        /// <summary>
        /// The Api Debugger (a page that sends Requests to the Endpoints and connects to the Streams, described by the OpenAPI Document)
        /// at /_debug, and the Log entries it shows at /_debug/logs
        /// </summary>
        private static void MapDebugger(WebApplication app)
        {
            app.MapGet(DebuggerRoute, (HttpContext httpContext) => WriteDebuggerFile(httpContext, "index.html"));
            app.MapGet($"{DebuggerRoute}/{{file}}", (HttpContext httpContext, string file) => WriteDebuggerFile(httpContext, file));
            app.MapGet(LogsRoute, (HttpContext httpContext) => TrakHoundDebugLogs.Stream(httpContext, app.Lifetime.ApplicationStopping));

            app.Lifetime.ApplicationStarted.Register(() =>
            {
                var addresses = app.Urls.Count > 0 ? app.Urls : new List<string> { "http://localhost:5000" };
                foreach (var address in addresses) Console.WriteLine($"Api Debugger : {address.TrimEnd('/')}{DebuggerRoute}");
            });
        }

        private static async Task WriteDebuggerFile(HttpContext httpContext, string file)
        {
            var extension = Path.GetExtension(file);
            var contentType = extension switch
            {
                ".html" => "text/html; charset=utf-8",
                ".css" => "text/css; charset=utf-8",
                ".js" => "text/javascript; charset=utf-8",
                ".svg" => "image/svg+xml",
                _ => null
            };

            using var stream = contentType != null ? typeof(WebApplicationExtensions).Assembly.GetManifestResourceStream($"Debugger/{file}") : null;
            if (stream == null)
            {
                httpContext.Response.StatusCode = StatusCodes.Status404NotFound;
                return;
            }

            httpContext.Response.ContentType = contentType;
            httpContext.Response.Headers.CacheControl = "no-cache";
            await stream.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
        }


        /// <summary>
        /// The Package Information (trakhound.package.json) from the project directory: the current directory when run with 'dotnet run',
        /// or a parent of the build output when run from Visual Studio. Its version is the two part prefix in the file (ex. "0.3")
        /// </summary>
        private static TrakHound.Packages.TrakHoundPackage ReadPackageInformation()
        {
            var directories = new List<string> { Environment.CurrentDirectory };

            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            for (var i = 0; i < 5 && directory != null; i++)
            {
                directories.Add(directory.FullName);
                directory = directory.Parent;
            }

            foreach (var path in directories.Select(o => Path.Combine(o, TrakHound.Packages.TrakHoundPackage.PackageConfigurationFilename)))
            {
                if (File.Exists(path)) return TrakHoundPackageInformation.Read(path);
            }

            return null;
        }
    }
}
