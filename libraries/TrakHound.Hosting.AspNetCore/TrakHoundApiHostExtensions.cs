// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using TrakHound.Api;
using TrakHound.Http;
using TrakHound.Logging;

namespace TrakHound.Hosting
{
    /// <summary>
    /// The Endpoints MapTrakHoundApi mapped. Conventions added to the Group (ex. RequireAuthorization) apply to every Endpoint and Stream
    /// </summary>
    public class TrakHoundApiEndpoints
    {
        public RouteGroupBuilder Group { get; }

        /// <summary>
        /// The assembly the Controllers were read from
        /// </summary>
        public Assembly Assembly { get; }

        /// <summary>
        /// The Configuration the Controllers were given
        /// </summary>
        public TrakHoundApiConfiguration Configuration { get; }

        /// <summary>
        /// The Controller types that were mapped (used to create the Api's OpenAPI Document)
        /// </summary>
        public IReadOnlyList<Type> ControllerTypes { get; }


        internal TrakHoundApiEndpoints(RouteGroupBuilder group, Assembly assembly, TrakHoundApiConfiguration configuration, IReadOnlyList<Type> controllerTypes)
        {
            Group = group;
            Assembly = assembly;
            Configuration = configuration;
            ControllerTypes = controllerTypes;
        }
    }

    public static class TrakHoundApiHostExtensions
    {
        private const string HostSource = "Api Host";


        /// <summary>
        /// Creates the Api's Controllers with the Client, Volume, Variables and Parameters of the TrakHoundHostContext (added by AddTrakHound) and maps
        /// their Endpoints and Streams at the root of the application. Log entries go to ILogger (a category for each Controller)
        /// </summary>
        /// <param name="assembly">The assembly with the Controllers (the entry assembly when null, which is only the Api when the Api project is the host)</param>
        public static TrakHoundApiEndpoints MapTrakHoundApi(this WebApplication app, Assembly assembly = null)
        {
            var context = app.Services.GetService<TrakHoundHostContext>() ?? throw new InvalidOperationException("Call services.AddTrakHound(...) before MapTrakHoundApi");
            var loggerFactory = app.Services.GetRequiredService<ILoggerFactory>();
            var hostLogger = loggerFactory.CreateLogger(typeof(TrakHoundApiHostExtensions).FullName);

            var configuration = new TrakHoundApiConfiguration();
            configuration.Id = context.Options.ConfigurationId;
            configuration.Parameters = context.GetParameters();

            var options = new TrakHoundApiHostOptions();
            options.Assembly = assembly;
            options.Configuration = configuration;
            options.Client = context.Client;
            options.Volume = context.Volume;
            options.Variables = context.Variables;

            // The Controllers are created now (before the host starts), so connect now to give them the Instance's ID
            options.InstanceId = context.ConnectAsync().GetAwaiter().GetResult();

            options.LogReceived = (source, item) => TrakHoundHostLogging.Write(loggerFactory.CreateLogger(source), item);
            options.ErrorReceived = (source, message) => hostLogger.LogError("{Source} : {Message}", source, message);

            return app.MapTrakHoundApi(options);
        }

        /// <summary>
        /// Creates the Api's Controllers the same way the Instance does and maps their Endpoints and Streams at the root of the application
        /// </summary>
        public static TrakHoundApiEndpoints MapTrakHoundApi(this WebApplication app, TrakHoundApiHostOptions options)
        {
            ArgumentNullException.ThrowIfNull(options);

            var assembly = options.Assembly ?? Assembly.GetEntryAssembly();
            var moduleConfiguration = options.Configuration ?? new TrakHoundApiConfiguration();

            var assemblyTypes = assembly.GetTypes();
            var controllerTypes = assemblyTypes.Where(o => typeof(ITrakHoundApiController).IsAssignableFrom(o) && !o.IsAbstract);
            var mappedControllerTypes = new List<Type>();
            var endpoints = new List<ApiEndpoint>();
            if (!controllerTypes.IsNullOrEmpty())
            {
                foreach (var controllerType in controllerTypes)
                {
                    var controllerConstructor = controllerType.GetConstructor(new Type[] { });
                    if (controllerConstructor != null)
                    {
                        var controllerRoute = Url.PathSeparator.ToString();
                        var controllerName = controllerType.Name;

                        var controllerAttribute = controllerType.GetCustomAttribute<TrakHoundApiControllerAttribute>();
                        if (controllerAttribute != null)
                        {
                            if (!string.IsNullOrEmpty(controllerAttribute.Route)) controllerRoute = controllerAttribute.Route;
                            if (!string.IsNullOrEmpty(controllerAttribute.Name)) controllerName = controllerAttribute.Name;
                        }

                        // Pass the Controller's Log entries to the host (the Instance writes them to its Logs)
                        var logger = new TrakHoundLogger(controllerName);
                        var logSource = controllerName;
                        logger.LogEntryReceived += (sender, item) => options.LogReceived?.Invoke(logSource, item);

                        // Create Instance of the Controller
                        var controller = (TrakHoundApiController)controllerConstructor.Invoke(new object[] { });
                        controller.Id = $"{controllerType.Assembly.Location}:{controllerType.AssemblyQualifiedName}";
                        controller.InstanceId = options.InstanceId;
                        controller.BaseUrl = null;
                        controller.BasePath = null;
                        controller.BaseLocation = null;
                        controller.SetConfiguration(moduleConfiguration);
                        controller.SetClient(options.Client);
                        controller.SetVolume(options.Volume);
                        controller.SetVariables(options.Variables);
                        controller.SetLogger(logger);

                        // Wait for InitializeAsync before mapping, so no Request reaches an uninitialized Controller.
                        // Like the Instance, an Initialization error is logged and the Controller is still mapped.
                        try
                        {
                            controller.Initialize();
                            controller.InitializeAsync().GetAwaiter().GetResult();
                        }
                        catch (Exception ex)
                        {
                            options.ErrorReceived?.Invoke(controllerName, $"API Controller Initialization Error : {TrakHoundLogger.GetExceptionStackTrace(ex)}");
                        }

                        // Get List of Endpoint Methods
                        var endpointMethods = controllerType.GetMethods().Where(o => o.GetCustomAttributes<TrakHoundApiEndpointRouteAttribute>().Any());
                        foreach (var endpointMethod in endpointMethods)
                        {
                            foreach (var routeAttribute in endpointMethod.GetCustomAttributes<TrakHoundApiEndpointRouteAttribute>())
                            {
                                var route = $"/{Url.Combine(controllerRoute, routeAttribute.Route)}";
                                endpoints.Add(new ApiEndpoint(controller, controllerRoute, route, endpointMethod, routeAttribute));
                            }
                        }

                        mappedControllerTypes.Add(controllerType);
                    }
                }
            }

            var group = app.MapGroup("");
            MapEndpoints(app, group, endpoints, options);
            return new TrakHoundApiEndpoints(group, assembly, moduleConfiguration, mappedControllerTypes);
        }

        /// <summary>
        /// Serves the Api's OpenAPI Document (the same one the Instance serves at _api/openapi/{apiId}). It's named and versioned from the Package
        /// Information: package, or else trakhound.package.json in the application's folder, or else the Controllers' assembly
        /// </summary>
        public static IEndpointConventionBuilder MapTrakHoundOpenApi(this WebApplication app, TrakHoundApiEndpoints endpoints, string route = "/openapi.json", TrakHound.Packages.TrakHoundPackage package = null)
        {
            ArgumentNullException.ThrowIfNull(endpoints);

            package ??= TrakHoundPackageInformation.Read(Path.Combine(AppContext.BaseDirectory, TrakHound.Packages.TrakHoundPackage.PackageConfigurationFilename));

            var configuration = endpoints.Configuration;
            var assemblyName = endpoints.Assembly?.GetName();
            if (string.IsNullOrEmpty(configuration.Name)) configuration.Name = package?.Id ?? assemblyName?.Name;
            if (string.IsNullOrEmpty(configuration.PackageId)) configuration.PackageId = package?.Id;
            if (string.IsNullOrEmpty(configuration.PackageVersion) && string.IsNullOrEmpty(package?.Version)) configuration.PackageVersion = assemblyName?.Version?.ToString(3);

            // The Endpoints are at the root of the application, which is under its PathBase when hosted in a sub-folder (ex. an IIS application)
            var documents = new ConcurrentDictionary<string, string>();
            return app.MapGet(route, (HttpContext httpContext) =>
            {
                var document = documents.GetOrAdd(httpContext.Request.PathBase.Value ?? "", pathBase =>
                {
                    var options = new TrakHoundApiOpenApiOptions();
                    options.ServerUrl = $"{pathBase}/";
                    return TrakHoundApiOpenApi.CreateJson(configuration, package, endpoints.ControllerTypes, options) ?? "{}";
                });
                return Results.Content(document, "application/json");
            });
        }


        private record ApiEndpoint(TrakHoundApiController Controller, string ControllerRoute, string Route, MethodInfo Method, TrakHoundApiEndpointRouteAttribute Attribute)
        {
            public bool IsStream => Attribute.Type == TrakHoundApiEndpointType.Stream || Attribute.Method == null;
        }

        /// <summary>
        /// Maps the Request Endpoints and the Stream Endpoints. A Stream shares its Route with a GET Endpoint the way it does on the Instance:
        /// a WebSocket Request, or a Server-Sent Events Request (Accept: text/event-stream), goes to the Stream
        /// </summary>
        private static void MapEndpoints(WebApplication app, RouteGroupBuilder group, List<ApiEndpoint> endpoints, TrakHoundApiHostOptions options)
        {
            var streams = new Dictionary<string, ApiEndpoint>(StringComparer.OrdinalIgnoreCase);
            foreach (var stream in endpoints.Where(o => o.IsStream))
            {
                if (!streams.TryAdd(stream.Route, stream)) options.ErrorReceived?.Invoke(HostSource, $"Duplicate Stream Route Not Mapped : {stream.Route} : {stream.Method.DeclaringType?.Name}.{stream.Method.Name}");
            }

            if (streams.Count > 0) app.UseWebSockets();

            // Connects WebSocket and Server-Sent Events Requests to Stream Endpoints (the Instance's implementation).
            // Open connections are closed when the application stops, since the host would otherwise wait for them until its ShutdownTimeout
            var streamManager = new TrakHoundHttpWebSocketManager();
            app.Lifetime.ApplicationStopping.Register(() => _ = streamManager.DisposeAsync().AsTask());

            var getRoutes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var endpoint in endpoints.Where(o => !o.IsStream))
            {
                foreach (var httpMethod in GetEndpointHttpMethods(endpoint.Attribute))
                {
                    var stream = httpMethod == "GET" ? streams.GetValueOrDefault(endpoint.Route) : null;
                    if (stream != null) getRoutes.Add(endpoint.Route);

                    group.MapMethods(endpoint.Route, new string[] { httpMethod }, async (HttpContext httpContext) =>
                    {
                        if (stream != null && IsStreamRequest(httpContext))
                        {
                            await ProcessStream(streamManager, stream, httpContext, options);
                            return Results.Empty;
                        }

                        return await ProcessRequest(endpoint.Controller, endpoint.ControllerRoute, endpoint.Method, endpoint.Attribute, httpContext, options);
                    });
                }
            }

            foreach (var stream in streams.Values.Where(o => !getRoutes.Contains(o.Route)))
            {
                group.MapGet(stream.Route, async (HttpContext httpContext) =>
                {
                    await ProcessStream(streamManager, stream, httpContext, options);
                    return Results.Empty;
                });
            }
        }

        private static bool IsStreamRequest(HttpContext httpContext) => httpContext.WebSockets.IsWebSocketRequest || TrakHoundHttpEventStreamManager.IsEventStreamRequest(httpContext);

        /// <summary>
        /// Connects a WebSocket or Server-Sent Events Request to the Stream Endpoint, with the same message format as the Instance
        /// (each message is a TrakHoundApiJsonResponse, and WebSocket messages from the client are pushed to the Stream)
        /// </summary>
        private static Task ProcessStream(TrakHoundHttpWebSocketManager streamManager, ApiEndpoint endpoint, HttpContext httpContext, TrakHoundApiHostOptions options)
        {
            options.RequestCompleted?.Invoke(new TrakHoundApiHostRequest("STREAM", httpContext.Request.Path + httpContext.Request.QueryString, 101, 0));

            return streamManager.Create<TrakHoundApiResponse>(httpContext,
                async () =>
                {
                    try
                    {
                        var (parameters, bindError) = await GetEndpointParameters(endpoint.ControllerRoute, endpoint.Method, endpoint.Attribute, httpContext);
                        if (bindError != null)
                        {
                            options.ErrorReceived?.Invoke(HostSource, $"Stream Bad Request : {httpContext.Request.Path} : {bindError}");
                            return null;
                        }

                        var result = endpoint.Method.Invoke(endpoint.Controller, parameters);
                        if (result is Task<ITrakHoundStream<TrakHoundApiResponse>> task) return await task;
                        return result as ITrakHoundStream<TrakHoundApiResponse>;
                    }
                    catch (Exception ex)
                    {
                        var exception = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                        options.ErrorReceived?.Invoke(HostSource, $"Stream Exception : {endpoint.Method.DeclaringType?.Name}.{endpoint.Method.Name} : {TrakHoundLogger.GetExceptionStackTrace(exception)}");
                        return null;
                    }
                },
                input => (Json.Convert<TrakHoundApiJsonResponse>(StringFunctions.GetUtf8String(input)) ?? new TrakHoundApiJsonResponse()).ToResponse(),
                response => Json.Convert(new TrakHoundApiJsonResponse(response)).ToUtf8Bytes());
        }

        private static async Task<IResult> ProcessRequest(TrakHoundApiController controller, string controllerRoute, MethodInfo endpointMethod, TrakHoundApiEndpointRouteAttribute routeAttribute, HttpContext httpContext, TrakHoundApiHostOptions options)
        {
            TrakHoundApiResponse response;
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                // Get Parameters based on Http Request
                var (parameters, bindError) = await GetEndpointParameters(controllerRoute, endpointMethod, routeAttribute, httpContext);
                if (bindError != null)
                {
                    response = TrakHoundApiResponse.BadRequest(bindError);
                }
                else if (endpointMethod.ReturnType == typeof(Task<TrakHoundApiResponse>))
                {
                    // Invoke Method and Get Response
                    response = await (Task<TrakHoundApiResponse>)endpointMethod.Invoke(controller, parameters);
                }
                else if (endpointMethod.ReturnType == typeof(TrakHoundApiResponse))
                {
                    // Invoke Method and Get Response
                    response = (TrakHoundApiResponse)endpointMethod.Invoke(controller, parameters);
                }
                else
                {
                    response = TrakHoundApiResponse.InternalError($"Endpoint Method must return TrakHoundApiResponse or Task<TrakHoundApiResponse> : {endpointMethod.Name}");
                }
            }
            catch (Exception ex)
            {
                // Return the Exception's message as a 500, the same as the Instance
                var exception = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                options.ErrorReceived?.Invoke(HostSource, $"API Endpoint Exception : {endpointMethod.DeclaringType?.Name}.{endpointMethod.Name} : {TrakHoundLogger.GetExceptionStackTrace(exception)}");
                response = TrakHoundApiResponse.InternalError(exception.Message);
            }

            options.RequestCompleted?.Invoke(new TrakHoundApiHostRequest(httpContext.Request.Method, httpContext.Request.Path + httpContext.Request.QueryString, response.StatusCode, stopwatch.Elapsed.TotalMilliseconds));

            return new ApiResponseResult(response);
        }

        private static string[] GetEndpointHttpMethods(TrakHoundApiEndpointRouteAttribute routeAttribute)
        {
            var methods = new List<string>();

            if (routeAttribute != null && routeAttribute.Method.HasValue)
            {
                methods.Add(routeAttribute.Method.Value.ToHttpMethod().ToString());
            }

            return methods.ToArray();
        }

        /// <summary>
        /// Create the Invoke arguments the same way the Instance does: unset parameters keep their default value (or null),
        /// a Body is read for any Method, and a Body that can't be read returns an error (sent as a 400)
        /// </summary>
        private static async Task<(object[] Parameters, string Error)> GetEndpointParameters(string controllerRoute, MethodInfo endpointMethod, TrakHoundApiEndpointRouteAttribute routeAttribute, HttpContext httpContext)
        {
            var endpointParameters = endpointMethod.GetParameters();
            var parameters = endpointParameters.Select(o => o.HasDefaultValue ? o.DefaultValue : null).ToArray();

            var route = Url.Combine(controllerRoute, routeAttribute.Route);

            for (var i = 0; i < endpointParameters.Length; i++)
            {
                var endpointParameter = endpointParameters[i];

                if (endpointParameter.ParameterType == typeof(CancellationToken))
                {
                    parameters[i] = httpContext.RequestAborted;
                }
                else if (endpointParameter.GetCustomAttribute<FromRouteAttribute>() != null)
                {
                    var routeParameterValue = Url.GetRouteParameter(httpContext.Request.Path, route, endpointParameter.Name);
                    if (routeParameterValue != null) parameters[i] = ChangeType(routeParameterValue, endpointParameter.ParameterType);
                }
                else if (endpointParameter.GetCustomAttribute<FromQueryAttribute>() != null)
                {
                    var queryParameterValue = GetQueryParameterValue(httpContext, endpointParameter);
                    if (queryParameterValue != null) parameters[i] = ChangeType(queryParameterValue, endpointParameter.ParameterType);
                }
                else if (endpointParameter.GetCustomAttribute<FromHeaderAttribute>() is FromHeaderAttribute headerAttribute)
                {
                    // Withheld Headers (the caller's credentials) are never bound, the same as on an Instance
                    var headerName = FromHeaderAttribute.GetHeaderName(headerAttribute, endpointParameter.Name);
                    if (!TrakHoundApiHeaders.IsWithheld(headerName) && httpContext.Request.Headers.TryGetValue(headerName, out var headerValue))
                    {
                        parameters[i] = ChangeType(headerValue.ToString(), endpointParameter.ParameterType);
                    }
                }
                else
                {
                    var fromBodyAttribute = endpointParameter.GetCustomAttribute<FromBodyAttribute>();
                    if (fromBodyAttribute != null)
                    {
                        byte[] requestBody;
                        using (var memoryStream = new MemoryStream())
                        {
                            await httpContext.Request.Body.CopyToAsync(memoryStream);
                            requestBody = memoryStream.ToArray();
                        }

                        if (requestBody.Length > 0)
                        {
                            var contentType = fromBodyAttribute.ContentType;
                            if (string.IsNullOrEmpty(contentType)) contentType = FromBodyAttribute.GetDefaultContentType(endpointParameter.ParameterType);

                            switch (contentType)
                            {
                                case "application/json":

                                    try
                                    {
                                        parameters[i] = JsonSerializer.Deserialize(requestBody, endpointParameter.ParameterType, Json.SharedOptions);
                                    }
                                    catch (Exception ex)
                                    {
                                        return (null, $"Invalid JSON Request Body : {ex.Message}");
                                    }
                                    break;

                                case "text/plain":

                                    try
                                    {
                                        parameters[i] = Convert.ChangeType(System.Text.Encoding.UTF8.GetString(requestBody), endpointParameter.ParameterType);
                                    }
                                    catch (Exception ex)
                                    {
                                        return (null, $"Invalid Text Request Body : {ex.Message}");
                                    }
                                    break;

                                default:
                                    if (endpointParameter.ParameterType == typeof(Stream)) parameters[i] = new MemoryStream(requestBody);
                                    else if (endpointParameter.ParameterType == typeof(byte[])) parameters[i] = requestBody;
                                    break;
                            }
                        }
                    }
                }
            }

            return (parameters, null);
        }

        private static string GetQueryParameterName(ParameterInfo endpointParameter)
        {
            var queryAttribute = endpointParameter.GetCustomAttribute<FromQueryAttribute>();
            return !string.IsNullOrEmpty(queryAttribute?.ParameterName) ? queryAttribute.ParameterName : endpointParameter.Name;
        }

        private static string GetQueryParameterValue(HttpContext httpContext, ParameterInfo endpointParameter)
        {
            var name = GetQueryParameterName(endpointParameter);

            // Reserved Client Parameters are set from the connection (same as the Instance) and can't be supplied in the query string
            if (string.Equals(name, TrakHoundApi.ClientAddressParameter, StringComparison.OrdinalIgnoreCase))
            {
                var remoteAddress = httpContext.Connection.RemoteIpAddress;
                if (remoteAddress != null && remoteAddress.IsIPv4MappedToIPv6) remoteAddress = remoteAddress.MapToIPv4();
                return remoteAddress?.ToString();
            }
            if (string.Equals(name, TrakHoundApi.ClientAgentParameter, StringComparison.OrdinalIgnoreCase))
            {
                return httpContext.Request.Headers.UserAgent.FirstOrDefault();
            }

            return httpContext.Request.Query[name].FirstOrDefault();
        }

        private static object ChangeType(object obj, Type type)
        {
            if (obj != null)
            {
                var convertType = type;

                if (type.IsValueType && Nullable.GetUnderlyingType(type) != null)
                {
                    convertType = Nullable.GetUnderlyingType(type);
                }

                try
                {
                    if (convertType == typeof(DateTime))
                    {
                        return obj.ToString().ToDateTime();
                    }
                    else if (typeof(Enum).IsAssignableFrom(convertType) && obj.GetType() == typeof(string))
                    {
                        if (Enum.TryParse(convertType, (string)obj, true, out var result))
                        {
                            return result;
                        }
                    }
                    else
                    {
                        return Convert.ChangeType(obj, convertType);
                    }
                }
                catch { }
            }

            return default;
        }


        /// <summary>
        /// Writes a TrakHoundApiResponse as is (the Content is copied, not read as text, so binary Content isn't changed)
        /// </summary>
        private class ApiResponseResult : IResult
        {
            private readonly TrakHoundApiResponse _response;


            public ApiResponseResult(TrakHoundApiResponse response)
            {
                _response = response;
            }


            public async Task ExecuteAsync(HttpContext httpContext)
            {
                httpContext.Response.StatusCode = _response.StatusCode;

                if (_response.Content != null)
                {
                    if (!string.IsNullOrEmpty(_response.ContentType)) httpContext.Response.ContentType = _response.ContentType;

                    if (_response.Content.CanSeek) _response.Content.Seek(0, SeekOrigin.Begin);
                    await _response.Content.CopyToAsync(httpContext.Response.Body, httpContext.RequestAborted);
                }
            }
        }
    }
}
