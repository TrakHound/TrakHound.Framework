// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using TrakHound.Instance.Http;
using TrakHound.Instance.Security;
using TrakHound.Instances;
using TrakHound.Logging;
using TrakHound.Security;

namespace TrakHound.Instance.Mcp
{
    /// <summary>
    /// The MCP Server (Model Context Protocol) served at /_mcp. It runs Stateless (each request is handled on its own), so every Tool call
    /// runs within its HTTP Request and the caller's Security Context (set by the TrakHoundAuthenticationMiddleware)
    /// </summary>
    public static class TrakHoundMcpServer
    {
        private const string Instructions =
            "Tools for a TrakHound Instance (an industrial data and application platform). " +
            "Start by reading the trakhound://llms.txt resource: what the Instance is, and the Apis and Endpoints you can call. " +
            "Each Api's OpenAPI Document is a resource too (trakhound://apis/<apiId>/openapi), and the prompts are starting points for common tasks. " +
            "Tools whose names start with an Api's route (ex. api_orders_) call that Api's Endpoints. " +
            "Use functions_list and functions_get (its ReadMe describes the input parameters) before functions_run. " +
            "Tools that change something (running Functions, SQL, setting values, publishing) may be denied by the Instance's security settings. " +
            "Services (Key Values, Tables, Messages) can have more than one Router; leave routerId empty for the default. " +
            "Log and Message tools that wait for live data return after a time limit, so ask for a short duration first.";

        /// <summary>
        /// The Tool classes (each public method with [McpServerTool] is a Tool)
        /// </summary>
        private static readonly Type[] _toolTypes = new[]
        {
            typeof(TrakHoundMcpFunctionTools),
            typeof(TrakHoundMcpLogTools),
            typeof(TrakHoundMcpServiceTools),
            typeof(TrakHoundMcpTimeSeriesTools),
            typeof(TrakHoundMcpVolumeTools),
            typeof(TrakHoundMcpApiTools),
            typeof(TrakHoundMcpInstanceTools)
        };

        private static readonly ITrakHoundLogger _logger = new TrakHoundLogger("mcp");

        // Tool Name => what it requires
        private static readonly Dictionary<string, TrakHoundMcpToolAttribute> _tools = GetTools();


        public static void AddTrakHoundMcp(this IServiceCollection services, ITrakHoundInstance instance)
        {
            services.AddHttpContextAccessor();

            // Tools created from the Endpoints of the Apis that serve MCP Tools (mcpTools)
            var apiTools = new TrakHoundMcpApiEndpointTools(instance);

            // The SDK logs every request at Information (Tool calls are recorded in the MCP Metrics instead)
            services.AddLogging(logging => logging.AddFilter("ModelContextProtocol", Microsoft.Extensions.Logging.LogLevel.Warning));

            services.AddMcpServer(options =>
            {
                options.ServerInfo = new Implementation { Name = "trakhound", Title = "TrakHound Instance", Version = instance.Version ?? "0.0.0" };
                options.ServerInstructions = Instructions;
            })
            .WithHttpTransport(options => options.Stateless = true)
            .WithTools((IEnumerable<Type>)_toolTypes) // Not the Type[] itself, which would match WithTools<T>(T target)
            .WithPrompts((IEnumerable<Type>)new[] { typeof(TrakHoundMcpPrompts) })
            .WithListResourcesHandler((context, cancellationToken) => TrakHoundMcpResources.List(instance, context))
            .WithReadResourceHandler((context, cancellationToken) => TrakHoundMcpResources.Read(instance, context))
            .WithRequestFilters(filters =>
            {
                // Only list the Tools the caller can use
                filters.AddListToolsFilter(next => async (context, cancellationToken) =>
                {
                    var result = await next(context, cancellationToken);
                    if (result != null)
                    {
                        var tools = result.Tools?.Where(o => Authorize(o.Name) == null).ToList() ?? new List<Tool>();
                        tools.AddRange(apiTools.GetTools().Where(o => !_tools.ContainsKey(o.Name) && AuthorizeApiTool(o) == null).Select(o => o.Tool));
                        result.Tools = tools;
                    }
                    return result;
                });

                filters.AddCallToolFilter(next => async (context, cancellationToken) =>
                {
                    var name = context.Params?.Name;
                    var stopwatch = Stopwatch.StartNew();

                    // Tools created from Api Endpoints aren't registered with the SDK, so they are called here
                    var apiTool = !string.IsNullOrEmpty(name) && !_tools.ContainsKey(name) ? apiTools.GetTool(name) : null;

                    // Only known Tools are recorded (a Client can send any name)
                    var metrics = apiTool != null || (name != null && _tools.ContainsKey(name)) ? instance.McpMetrics : null;

                    var denied = apiTool != null ? AuthorizeApiTool(apiTool) : Authorize(name);
                    if (denied != null)
                    {
                        _logger.LogDebug($"Tool Call Denied : {name} : {denied}");
                        metrics?.ToolCalled(name, stopwatch.Elapsed.TotalMilliseconds, denied, true);
                        return Error(denied);
                    }

                    // Rate Limits are applied to known Tools (and returned as a Tool error, which an AI Client can act on, rather than an HTTP 429)
                    using var rateLimitLease = metrics != null ? AcquireRateLimit(context.Services) : TrakHoundRateLimitLease.Allowed;
                    if (!rateLimitLease.IsAcquired)
                    {
                        var message = $"Rate Limit Exceeded : {rateLimitLease.Reason}. Retry after {rateLimitLease.GetRetryAfterSeconds()} seconds";
                        _logger.LogDebug($"Tool Call Rate Limited : {name} : {rateLimitLease.Reason}");
                        metrics.ToolCalled(name, stopwatch.Elapsed.TotalMilliseconds, message, true);
                        return Error(message);
                    }

                    try
                    {
                        CallToolResult result;
                        if (apiTool != null)
                        {
                            var httpContext = context.Services?.GetService<IHttpContextAccessor>()?.HttpContext;
                            result = await apiTools.Call(apiTool, context.Params.Arguments, httpContext, cancellationToken);
                        }
                        else
                        {
                            result = await next(context, cancellationToken);
                        }

                        metrics?.ToolCalled(name, stopwatch.Elapsed.TotalMilliseconds, result?.IsError == true ? GetText(result) ?? "Error" : null, false);
                        return result;
                    }
                    catch (Exception ex)
                    {
                        metrics?.ToolCalled(name, stopwatch.Elapsed.TotalMilliseconds, ex.Message, false);
                        throw;
                    }
                });
            })
            .WithMessageFilters(filters =>
            {
                // Record the Clients that connect (the initialize request has the Client's name and version)
                filters.AddIncomingFilter(next => async (context, cancellationToken) =>
                {
                    if (context.JsonRpcMessage is JsonRpcRequest request && request.Method == RequestMethods.Initialize)
                    {
                        try
                        {
                            var initialize = request.Params?.Deserialize<InitializeRequestParams>();
                            var httpContext = context.Services?.GetService<IHttpContextAccessor>()?.HttpContext;
                            instance.McpMetrics?.ClientInitialized(initialize?.ClientInfo?.Name, initialize?.ClientInfo?.Version, GetRemoteAddress(httpContext));
                        }
                        catch { } // Recording must never affect the Request
                    }

                    await next(context, cancellationToken);
                });
            });
        }

        public static void MapTrakHoundMcp(this IEndpointRouteBuilder endpoints)
        {
            // Connecting (and every MCP request) requires "read" on system:mcp. Each Tool is authorized when it's called (see Authorize)
            endpoints.MapMcp(TrakHoundInstanceRoutes.McpPrefix)
                .WithMetadata(new TrakHoundSystemResourceAttribute(TrakHoundSystemSecurity.Mcp));
        }


        /// <summary>
        /// Checks that the caller can use the Tool. Returns why not, or null when allowed. Tools that change something require "write"
        /// on system:mcp, and Tools that use a System Resource require its Permission. Service operations are also authorized by the Service
        /// </summary>
        private static string Authorize(string name)
        {
            if (string.IsNullOrEmpty(name) || !_tools.TryGetValue(name, out var tool)) return null; // Answered by the SDK (Unknown Tool)

            // Requests always run within a Security Context, so without one the caller can't be identified (never allow)
            var securityContext = TrakHoundSecurityContext.Current;
            if (securityContext == null) return "Access Denied : the caller could not be identified";

            if (tool.Write)
            {
                var mcpAuthorization = securityContext.Authorize(TrakHoundSystemSecurity.Mcp, TrakHoundSystemSecurity.WritePermission);
                if (mcpAuthorization.Status != TrakHoundAuthorizationStatus.Allowed)
                {
                    return $"Access Denied : '{name}' changes the Instance, which requires the '{TrakHoundSystemSecurity.WritePermission}' Permission on '{TrakHoundSystemSecurity.Mcp}'";
                }
            }

            if (!string.IsNullOrEmpty(tool.Resource))
            {
                var permission = TrakHoundSystemSecurity.GetPermission(tool.Write);
                var resourceAuthorization = securityContext.Authorize(tool.Resource, permission);
                if (resourceAuthorization.Status != TrakHoundAuthorizationStatus.Allowed)
                {
                    return $"Access Denied : '{name}' requires the '{permission}' Permission on '{tool.Resource}'";
                }
            }

            return null;
        }

        /// <summary>
        /// Checks that the caller can use a Tool created from an Api Endpoint (returns why not, or null when allowed). Tools that aren't ReadOnly
        /// require "write" on system:mcp, and every one requires access to its Endpoint (authorized the same as an HTTP Request to it)
        /// </summary>
        private static string AuthorizeApiTool(TrakHoundMcpApiEndpointTool tool)
        {
            var securityContext = TrakHoundSecurityContext.Current;
            if (securityContext == null) return "Access Denied : the caller could not be identified";

            if (!tool.ReadOnly)
            {
                var mcpAuthorization = securityContext.Authorize(TrakHoundSystemSecurity.Mcp, TrakHoundSystemSecurity.WritePermission);
                if (mcpAuthorization.Status != TrakHoundAuthorizationStatus.Allowed)
                {
                    return $"Access Denied : '{tool.Name}' changes the Instance, which requires the '{TrakHoundSystemSecurity.WritePermission}' Permission on '{TrakHoundSystemSecurity.Mcp}'";
                }
            }

            var endpointAuthorization = securityContext.Authorize(tool.ResourceId);
            if (endpointAuthorization.Status != TrakHoundAuthorizationStatus.Allowed)
            {
                return $"Access Denied : '{tool.Name}' requires access to its Api Endpoint ('{tool.ResourceId}')";
            }

            return null;
        }

        /// <summary>
        /// Acquires a Tool call for the caller (the Lease holds its place in the Concurrency Limit until the call completes)
        /// </summary>
        private static TrakHoundRateLimitLease AcquireRateLimit(IServiceProvider services)
        {
            var rateLimiter = services?.GetService<TrakHoundRateLimiter>();
            if (rateLimiter == null) return TrakHoundRateLimitLease.Allowed;

            var httpContext = services.GetService<IHttpContextAccessor>()?.HttpContext;
            var callerKey = TrakHoundRateLimiter.GetCallerKey(TrakHoundSecurityContext.Current?.Identity, httpContext);
            return rateLimiter.Acquire(TrakHoundRateLimitAreas.McpTools, callerKey);
        }

        private static Dictionary<string, TrakHoundMcpToolAttribute> GetTools()
        {
            var tools = new Dictionary<string, TrakHoundMcpToolAttribute>(StringComparer.Ordinal);
            foreach (var type in _toolTypes)
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
                {
                    var mcpTool = method.GetCustomAttribute<McpServerToolAttribute>();
                    if (mcpTool == null) continue;

                    // Every Tool declares what it requires (a Tool without the attribute is treated as one that changes something)
                    tools[mcpTool.Name ?? method.Name] = method.GetCustomAttribute<TrakHoundMcpToolAttribute>() ?? new TrakHoundMcpToolAttribute { Write = true };
                }
            }
            return tools;
        }

        private static CallToolResult Error(string message)
        {
            return new CallToolResult { IsError = true, Content = new List<ContentBlock> { new TextContentBlock { Text = message } } };
        }

        private static string GetText(CallToolResult result)
        {
            return result.Content?.OfType<TextContentBlock>().FirstOrDefault()?.Text;
        }

        private static string GetRemoteAddress(HttpContext httpContext)
        {
            var address = httpContext?.Connection.RemoteIpAddress;
            if (address == null) return null;
            if (address.IsIPv4MappedToIPv6) address = address.MapToIPv4();
            return address.ToString();
        }
    }


    /// <summary>
    /// What an MCP Tool requires. Write Tools change something (they require "write" on system:mcp, and are marked Destructive for Clients).
    /// Resource is the System Resource the Tool uses (ex. system:functions), which requires "read" (or "write" for a Write Tool)
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class TrakHoundMcpToolAttribute : Attribute
    {
        public bool Write { get; set; }

        public string Resource { get; set; }
    }
}
