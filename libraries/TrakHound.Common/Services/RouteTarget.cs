// Copyright (c) 2025 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TrakHound.Logging;
using TrakHound.Security;
using TrakHound.Zones;

namespace TrakHound.Services
{
    class RouteTarget : IRouteTarget
    {
        // The Routers the current request has passed through (flows with the async call chain)
        private static readonly AsyncLocal<ImmutableList<string>> _routerChain = new AsyncLocal<ImmutableList<string>>();

        public string Id { get; set; }

        public string RouteId { get; set; }

        public TrakHoundServiceRouteConfiguration RouteConfiguration { get; set; }

        public TrakHoundServiceTargetConfiguration TargetConfiguration { get; set; }

        public TrakHoundServiceRouteTargetType Type { get; set; }


        public RouteTarget(
            TrakHoundServiceRouterConfiguration routerConfiguration,
            TrakHoundServiceRouteConfiguration routeConfiguration,
            TrakHoundServiceTargetConfiguration targetConfiguration
            )
        {
            if (routeConfiguration != null && targetConfiguration != null)
            {
                Id = targetConfiguration.Source;
                RouteId = routeConfiguration.Id;
                RouteConfiguration = routeConfiguration;
                TargetConfiguration = targetConfiguration;
                Type = targetConfiguration.Type.ConvertEnum<TrakHoundServiceRouteTargetType>();
            }
        }


        /// <summary>
        /// Records that the current request has entered the specified Router. Returns false if the request
        /// has already passed through that Router (a Route loop such as A => B => A), which would otherwise recurse indefinitely.
        /// </summary>
        private static bool TryEnterRouter(string routerId, out string loopMessage)
        {
            var chain = _routerChain.Value ?? ImmutableList<string>.Empty;
            if (routerId != null && chain.Contains(routerId))
            {
                loopMessage = $"Route Loop Detected : {string.Join(" => ", chain.Add(routerId))}";
                return false;
            }

            // Changes made inside an async method do not flow back to the caller, so this is scoped to this request path
            _routerChain.Value = chain.Add(routerId);
            loopMessage = null;
            return true;
        }

        /// <summary>
        /// Runs a Service call outside of the Router chain. A Service may start background work during the call (ex. a Message Queue Consumer)
        /// that captures the current ExecutionContext, and requests made from that work (ex. Acknowledge from a Received callback)
        /// are new requests that must not inherit this request's chain (which would be reported as a Route Loop)
        /// </summary>
        private static async Task<TResponse> RunOutsideRouterChain<TResponse>(Func<Task<TResponse>> function)
        {
            // Changes made inside an async method do not flow back to the caller, so the chain is restored after the call
            _routerChain.Value = null;
            return await function();
        }

        /// <summary>
        /// Runs the request against the Targets of the specified Router. Service calls are recorded to the Router's Metrics Recorder.
        /// </summary>
        public static Task<TrakHoundServiceResponse<TResult>> Run<TDriver, TResult>(
            TrakHoundServiceRouter router,
            RouteRequest request,
            IEnumerable<IRouteTarget> targets,
            Func<RouteTargetDriverRequest<TDriver, TResult>, Task<TrakHoundServiceResponse<TResult>>> serviceFunction,
            Func<RouteTargetRouterRequest<TResult>, Task<TrakHoundServiceResponse<TResult>>> routerFunction
            )
            where TDriver : ITrakHoundService
        {
            request.MetricsRecorder = router.MetricsRecorder;
            return Run(router.Id, request, router.Logger, targets, serviceFunction, routerFunction);
        }

        /// <summary>
        /// Runs the request against the Targets of the specified Router. Service calls are recorded to the Router's Metrics Recorder.
        /// </summary>
        public static Task<TrakHoundServiceResponse> Run<TDriver>(
            TrakHoundServiceRouter router,
            RouteRequest request,
            IEnumerable<IRouteTarget> targets,
            Func<RouteTargetServiceRequest<TDriver>, Task<TrakHoundServiceResponse>> serviceFunction,
            Func<RouteTargetRouterRequest, Task<TrakHoundServiceResponse>> routerFunction
            )
            where TDriver : ITrakHoundService
        {
            request.MetricsRecorder = router.MetricsRecorder;
            return Run(router.Id, request, router.Logger, targets, serviceFunction, routerFunction);
        }

        public static async Task<TrakHoundServiceResponse<TResult>> Run<TDriver, TResult>(
            string routerId,
            RouteRequest request,
            ITrakHoundLogger logger,
            IEnumerable<IRouteTarget> targets,
            Func<RouteTargetDriverRequest<TDriver, TResult>, Task<TrakHoundServiceResponse<TResult>>> serviceFunction,
            Func<RouteTargetRouterRequest<TResult>, Task<TrakHoundServiceResponse<TResult>>> routerFunction
            )
            where TDriver : ITrakHoundService
        {
            if (!TryEnterRouter(routerId, out var loopMessage))
            {
                logger.LogError($"{request.Id} : {request.Name} : {loopMessage}");
                return TrakHoundServiceResponse<TResult>.InternalError(loopMessage);
            }

            var stpw = System.Diagnostics.Stopwatch.StartNew();
            var response = TrakHoundServiceResponse<TResult>.RouteNotConfigured();

            if (logger.IsEnabled(TrakHoundLogLevel.Trace)) logger.LogTrace($"{request.Id} : {request.Name} Request to Router ({routerId}) Received");

            if (!targets.IsNullOrEmpty())
            {
                // An empty Result from a Target that falls back on Empty is returned if no later Target has a Result
                TrakHoundServiceResponse<TResult>? emptyResponse = null;

                foreach (var target in targets)
                {
                    request.CancellationToken.ThrowIfCancellationRequested();

                    // 405 = the Target doesn't handle the Request (the next Target is tried)
                    var targetResponse = await RunTarget(routerId, request, logger, target, serviceFunction, routerFunction);
                    if (targetResponse.StatusCode == 405)
                    {
                        // Kept in case no Target handles the Request, so the caller learns why
                        if (response.StatusCode == 405 && string.IsNullOrEmpty(response.Message)) response = targetResponse;
                        continue;
                    }

                    response = targetResponse;
                    if (response.Success)
                    {
                        if (!TrakHoundServiceFallback.IsEmptyFallback(target.TargetConfiguration, response.Result)) break;

                        emptyResponse ??= response;
                        if (logger.IsEnabled(TrakHoundLogLevel.Trace)) logger.LogTrace($"{request.Id} : Router ({routerId}) : {request.Name} => ({target.Id}) : Empty Result : Falling back to the next Target");
                    }
                    else if (TrakHoundServiceFallback.IsFallback(target.TargetConfiguration, response.StatusCode))
                    {
                        if (logger.IsEnabled(TrakHoundLogLevel.Trace)) logger.LogTrace($"{request.Id} : Router ({routerId}) : {request.Name} => ({target.Id}) : {response.StatusCode} : Falling back to the next Target");
                    }
                    else break;
                }

                if (!response.Success && emptyResponse.HasValue) response = emptyResponse.Value;
            }

            stpw.Stop();

            if (logger.IsEnabled(TrakHoundLogLevel.Trace)) logger.LogTrace($"{request.Id} : {request.Name} Request to Router ({routerId}) Completed in {stpw.ElapsedMilliseconds}ms");

            return response;
        }

        public static async Task<TrakHoundServiceResponse> Run<TDriver>(
            string routerId,
            RouteRequest request,
            ITrakHoundLogger logger,
            IEnumerable<IRouteTarget> targets,
            Func<RouteTargetServiceRequest<TDriver>, Task<TrakHoundServiceResponse>> serviceFunction,
            Func<RouteTargetRouterRequest, Task<TrakHoundServiceResponse>> routerFunction
            )
            where TDriver : ITrakHoundService
        {
            if (!TryEnterRouter(routerId, out var loopMessage))
            {
                logger.LogError($"{request.Id} : {request.Name} : {loopMessage}");
                return TrakHoundServiceResponse.InternalError(loopMessage);
            }

            var stpw = System.Diagnostics.Stopwatch.StartNew();
            var response = TrakHoundServiceResponse.RouteNotConfigured();

            if (logger.IsEnabled(TrakHoundLogLevel.Trace)) logger.LogTrace($"{request.Id} : {request.Name} Request to Router ({routerId}) Received");

            if (!targets.IsNullOrEmpty())
            {
                foreach (var target in targets)
                {
                    request.CancellationToken.ThrowIfCancellationRequested();

                    // 405 = the Target doesn't handle the Request (the next Target is tried)
                    var targetResponse = await RunTarget(routerId, request, logger, target, serviceFunction, routerFunction);
                    if (targetResponse.StatusCode == 405)
                    {
                        // Kept in case no Target handles the Request, so the caller learns why
                        if (response.StatusCode == 405 && string.IsNullOrEmpty(response.Message)) response = targetResponse;
                        continue;
                    }

                    response = targetResponse;
                    if (response.Success) break;

                    if (!TrakHoundServiceFallback.IsFallback(target.TargetConfiguration, response.StatusCode)) break;
                    if (logger.IsEnabled(TrakHoundLogLevel.Trace)) logger.LogTrace($"{request.Id} : Router ({routerId}) : {request.Name} => ({target.Id}) : {response.StatusCode} : Falling back to the next Target");
                }
            }

            stpw.Stop();

            if (logger.IsEnabled(TrakHoundLogLevel.Trace)) logger.LogTrace($"{request.Id} : {request.Name} Request to Router ({routerId}) Completed in {stpw.ElapsedMilliseconds}ms");

            return response;
        }


        #region "Internal"

        private static void RecordMetrics<TService>(string routerId, RouteRequest request, ITrakHoundService service, int statusCode, string message, TimeSpan duration)
        {
            // 405 = the Service does not support the request (the next Service is tried)
            var recorder = request.MetricsRecorder;
            if (recorder != null && statusCode != 405)
            {
                // Metrics must never affect the request
                try
                {
                    recorder.Record(routerId, service, typeof(TService), request.Name, statusCode, message, duration);
                }
                catch { }
            }
        }

        private static TrakHoundAuthorizationResult AuthorizeService<TService>(RouteRequest request, ITrakHoundService service, ref string resourceId)
        {
            // Service calls that are not made on behalf of a Request (ex. from a Worker or Function) are trusted
            var securityContext = TrakHoundSecurityContext.Current;
            if (securityContext == null) return TrakHoundAuthorizationResult.Unrestricted();

            // Availability checks are internal (they don't read or write data)
            if (request.Name == TrakHoundServiceSecurity.CheckAvailabilityOperation) return TrakHoundAuthorizationResult.Unrestricted();

            // Built only when needed, and once per Service call (shared with the Zone check)
            resourceId ??= TrakHoundServiceSecurity.GetResourceId(service, typeof(TService), request.Name);
            return securityContext.Authorize(resourceId, TrakHoundServiceSecurity.GetPermission(request.Name));
        }

        private static TrakHoundZoneDecision AuthorizeZone<TService>(RouteRequest request, ITrakHoundService service, ref string resourceId)
        {
            // Calls that are not made through a Module's Client are not governed by a Zone
            var zoneContext = TrakHoundZoneContext.Current;
            if (zoneContext == null) return TrakHoundZoneDecision.Allowed;

            // Availability checks are internal (they don't read or write data)
            if (request.Name == TrakHoundServiceSecurity.CheckAvailabilityOperation) return TrakHoundZoneDecision.Allowed;

            var serviceMemberId = TrakHoundZoneResources.GetMemberId(TrakHoundZoneResources.ServicePrefix, service?.Configuration?.Id);
            resourceId ??= TrakHoundServiceSecurity.GetResourceId(service, typeof(TService), request.Name);
            return zoneContext.Authorize(serviceMemberId, resourceId, TrakHoundServiceSecurity.GetPermission(request.Name));
        }

        private static async Task<TrakHoundServiceResponse> RunServiceTarget<TService>(
            string routerId,
            RouteRequest request,
            ITrakHoundLogger logger,
            IRouteServiceTarget target,
            TService service,
            Func<RouteTargetServiceRequest<TService>, Task<TrakHoundServiceResponse>> serviceFunction,
            Func<RouteTargetRouterRequest, Task<TrakHoundServiceResponse>> routerFunction
            )
            where TService : ITrakHoundService
        {
            if (target != null)
            {
                var stpw = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    // Authorize the Identity of the Request that is calling the Service
                    string resourceId = null;
                    var authorization = AuthorizeService<TService>(request, service, ref resourceId);
                    if (!authorization.IsAllowed)
                    {
                        // Only the ClientMessage goes back to the caller; the Message (which names the Identity and Resource) is only logged
                        var deniedResponse = authorization.Status == TrakHoundAuthorizationStatus.AuthenticationRequired ? TrakHoundServiceResponse.Unauthorized() : TrakHoundServiceResponse.Forbidden(authorization.ClientMessage);
                        RecordMetrics<TService>(routerId, request, service, deniedResponse.StatusCode, authorization.Message, stpw.Elapsed);
                        logger.LogDebug($"{request.Id} : Router ({routerId}) : {request.Name} => ({service.Id}) : Not Authorized : {authorization.Message}");
                        return deniedResponse;
                    }

                    // Check the call against the Zone of the Module making it
                    var zoneDecision = AuthorizeZone<TService>(request, service, ref resourceId);
                    if (!zoneDecision.IsAllowed)
                    {
                        var zoneDeniedResponse = TrakHoundServiceResponse.Forbidden(zoneDecision.Message);
                        RecordMetrics<TService>(routerId, request, service, zoneDeniedResponse.StatusCode, zoneDecision.Message, stpw.Elapsed);
                        logger.LogDebug($"{request.Id} : Router ({routerId}) : {request.Name} => ({service.Id}) : Zone Access Denied : {zoneDecision.Message}");
                        return zoneDeniedResponse;
                    }

                    // Read the Response from the Service
                    var response = await RunOutsideRouterChain(() => serviceFunction(new RouteTargetServiceRequest<TService>(target, service, request)));

                    RecordMetrics<TService>(routerId, request, service, response.StatusCode, response.Message, stpw.Elapsed);

                    if (logger.IsEnabled(TrakHoundLogLevel.Trace)) logger.LogTrace($"{request.Id} : Router ({routerId}) : {request.Name} => ({service.Id}) : Service Target Response in {response.Duration.TotalMilliseconds}ms");

                    return response;
                }
                catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    RecordMetrics<TService>(routerId, request, service, 500, ex.Message, stpw.Elapsed);
                    return TrakHoundServiceResponse.InternalError(ex.Message);
                }
            }

            return TrakHoundServiceResponse.RouteNotConfigured();
        }

        private static async Task<TrakHoundServiceResponse<TResult>> RunServiceTarget<TService, TResult>(
            string routerId,
            RouteRequest request,
            ITrakHoundLogger logger,
            IRouteServiceTarget target,
            TService service,
            Func<RouteTargetDriverRequest<TService, TResult>, Task<TrakHoundServiceResponse<TResult>>> serviceFunction,
            Func<RouteTargetRouterRequest<TResult>, Task<TrakHoundServiceResponse<TResult>>> routerFunction
            )
            where TService : ITrakHoundService
        {
            if (target != null)
            {
                var stpw = System.Diagnostics.Stopwatch.StartNew();

                try
                {
                    // Authorize the Identity of the Request that is calling the Service
                    string resourceId = null;
                    var authorization = AuthorizeService<TService>(request, service, ref resourceId);
                    if (!authorization.IsAllowed)
                    {
                        // Only the ClientMessage goes back to the caller; the Message (which names the Identity and Resource) is only logged
                        var deniedResponse = authorization.Status == TrakHoundAuthorizationStatus.AuthenticationRequired ? TrakHoundServiceResponse<TResult>.Unauthorized() : TrakHoundServiceResponse<TResult>.Forbidden(authorization.ClientMessage);
                        deniedResponse.RouterId = routerId;
                        deniedResponse.ServiceId = service.Configuration?.Id;
                        RecordMetrics<TService>(routerId, request, service, deniedResponse.StatusCode, authorization.Message, stpw.Elapsed);
                        logger.LogDebug($"{request.Id} : Router ({routerId}) : {request.Name} => ({service.Id}) : Not Authorized : {authorization.Message}");
                        return deniedResponse;
                    }

                    // Check the call against the Zone of the Module making it
                    var zoneDecision = AuthorizeZone<TService>(request, service, ref resourceId);
                    if (!zoneDecision.IsAllowed)
                    {
                        var zoneDeniedResponse = TrakHoundServiceResponse<TResult>.Forbidden(zoneDecision.Message);
                        zoneDeniedResponse.RouterId = routerId;
                        zoneDeniedResponse.ServiceId = service.Configuration?.Id;
                        RecordMetrics<TService>(routerId, request, service, zoneDeniedResponse.StatusCode, zoneDecision.Message, stpw.Elapsed);
                        logger.LogDebug($"{request.Id} : Router ({routerId}) : {request.Name} => ({service.Id}) : Zone Access Denied : {zoneDecision.Message}");
                        return zoneDeniedResponse;
                    }

                    // Read the Response from the Service
                    var response = await RunOutsideRouterChain(() => serviceFunction(new RouteTargetDriverRequest<TService, TResult>(target, service, request)));

                    RecordMetrics<TService>(routerId, request, service, response.StatusCode, response.Message, stpw.Elapsed);

                    // Set Router ID
                    response.RouterId = routerId;

                    // Set Service ID
                    response.ServiceId = service.Configuration?.Id;

                    if (logger.IsEnabled(TrakHoundLogLevel.Trace)) logger.LogTrace($"{request.Id} : Router ({routerId}) : {request.Name} => ({service.Id}) : Service Target Response in {response.Duration.TotalMilliseconds}ms");

                    return response;
                }
                catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    RecordMetrics<TService>(routerId, request, service, 500, ex.Message, stpw.Elapsed);
                    return TrakHoundServiceResponse<TResult>.InternalError(ex.Message);
                }
            }

            return TrakHoundServiceResponse<TResult>.RouteNotConfigured();
        }

        private static async Task<TrakHoundServiceResponse> RunServiceTargets<TService>(
            string routerId,
            RouteRequest request,
            ITrakHoundLogger logger,
            IRouteServiceTarget target,
            Func<RouteTargetServiceRequest<TService>, Task<TrakHoundServiceResponse>> serviceFunction,
            Func<RouteTargetRouterRequest, Task<TrakHoundServiceResponse>> routerFunction
            )
            where TService : ITrakHoundService
        {
            var response = TrakHoundServiceResponse.RouteNotConfigured();

            if (target != null)
            {
                // Get Service(s) (of type) from ServiceTarget
                var services = target.GetServices<TService>();
                if (!services.IsNullOrEmpty())
                {
                    foreach (var service in services)
                    {
                        request.CancellationToken.ThrowIfCancellationRequested();

                        // Run Service Target (the Target's Services are tried in order, like the Targets of a Route)
                        var serviceResponse = await RunServiceTarget<TService>(routerId, request, logger, target, service, serviceFunction, routerFunction);
                        if (serviceResponse.StatusCode == 405)
                        {
                            if (response.StatusCode == 405 && string.IsNullOrEmpty(response.Message)) response = serviceResponse;
                            continue;
                        }

                        response = serviceResponse;
                        if (response.Success || !TrakHoundServiceFallback.IsFallback(target.TargetConfiguration, response.StatusCode)) break;
                    }
                }
            }

            return response;
        }

        private static async Task<TrakHoundServiceResponse<TResult>> RunServiceTargets<TService, TResult>(
            string routerId,
            RouteRequest request,
            ITrakHoundLogger logger,
            IRouteServiceTarget target,
            Func<RouteTargetDriverRequest<TService, TResult>, Task<TrakHoundServiceResponse<TResult>>> serviceFunction,
            Func<RouteTargetRouterRequest<TResult>, Task<TrakHoundServiceResponse<TResult>>> routerFunction
            ) 
            where TService : ITrakHoundService
        {
            var response = TrakHoundServiceResponse<TResult>.RouteNotConfigured();

            if (target != null)
            {
                // Get Service(s) (of type) from ServiceTarget
                var services = target.GetServices<TService>();
                if (!services.IsNullOrEmpty())
                {
                    TrakHoundServiceResponse<TResult>? emptyResponse = null;

                    foreach (var service in services)
                    {
                        request.CancellationToken.ThrowIfCancellationRequested();

                        // Run Service Target (the Target's Services are tried in order, like the Targets of a Route)
                        var serviceResponse = await RunServiceTarget<TService, TResult>(routerId, request, logger, target, service, serviceFunction, routerFunction);
                        if (serviceResponse.StatusCode == 405)
                        {
                            if (response.StatusCode == 405 && string.IsNullOrEmpty(response.Message)) response = serviceResponse;
                            continue;
                        }

                        response = serviceResponse;
                        if (response.Success)
                        {
                            if (!TrakHoundServiceFallback.IsEmptyFallback(target.TargetConfiguration, response.Result)) break;
                            emptyResponse ??= response;
                        }
                        else if (!TrakHoundServiceFallback.IsFallback(target.TargetConfiguration, response.StatusCode)) break;
                    }

                    if (!response.Success && emptyResponse.HasValue) response = emptyResponse.Value;
                }
            }

            return response;
        }


        private static async Task<TrakHoundServiceResponse> RunRouterTarget<TService>(
            string routerId,
            RouteRequest request,
            ITrakHoundLogger logger,
            IRouteRouterTarget target,
            Func<RouteTargetServiceRequest<TService>, Task<TrakHoundServiceResponse>> serviceFunction,
            Func<RouteTargetRouterRequest, Task<TrakHoundServiceResponse>> routerFunction
            )
            where TService : ITrakHoundService
        {
            if (target != null)
            {
                var router = target.Router;
                if (router != null)
                {
                    // Read the Response from the Service
                    var response = await routerFunction(new RouteTargetRouterRequest(target, router, request));

                    if (logger.IsEnabled(TrakHoundLogLevel.Trace)) logger.LogTrace($"{request.Id} : Router ({routerId}) : {request.Name} => ({router.Id}) : Service Target Response in {response.Duration.TotalMilliseconds}ms");

                    return response;
                }
            }

            return TrakHoundServiceResponse.RouteNotConfigured();
        }

        private static async Task<TrakHoundServiceResponse<TResult>> RunRouterTarget<TService, TResult>(
            string routerId,
            RouteRequest request,
            ITrakHoundLogger logger,
            IRouteRouterTarget target,
            Func<RouteTargetDriverRequest<TService, TResult>, Task<TrakHoundServiceResponse<TResult>>> serviceFunction,
            Func<RouteTargetRouterRequest<TResult>, Task<TrakHoundServiceResponse<TResult>>> routerFunction
            )
            where TService : ITrakHoundService
        {
            if (target != null)
            {
                var router = target.Router;
                if (router != null)
                {
                    // Read the Response from the Service
                    var response = await routerFunction(new RouteTargetRouterRequest<TResult>(target, router, request));

                    if (logger.IsEnabled(TrakHoundLogLevel.Trace)) logger.LogTrace($"{request.Id} : Router ({routerId}) : {request.Name} => ({router.Id}) : Service Target Response in {response.Duration.TotalMilliseconds}ms");

                    return response;
                }
            }

            return TrakHoundServiceResponse<TResult>.RouteNotConfigured();
        }


        private static async Task<TrakHoundServiceResponse> RunTarget<TService>(
            string routerId,
            RouteRequest request,
            ITrakHoundLogger logger,
            IRouteTarget target,
            Func<RouteTargetServiceRequest<TService>, Task<TrakHoundServiceResponse>> serviceFunction,
            Func<RouteTargetRouterRequest, Task<TrakHoundServiceResponse>> routerFunction
            )
            where TService : ITrakHoundService
        {
            if (target != null)
            {
                // Check to see if Target is a Service Target for the correct Service Type
                if (target.GetType() == typeof(RouteServiceTarget))
                {
                    // Cast to Target Service to the Type of Service in Router
                    var serviceTarget = (RouteServiceTarget)target;

                    return await RunServiceTargets(routerId, request, logger, serviceTarget, serviceFunction, routerFunction);
                }
                else if (target.GetType() == typeof(RouteRouterTarget))
                {
                    // Cast to Target Router
                    var routerTarget = (RouteRouterTarget)target;

                    return await RunRouterTarget(routerId, request, logger, routerTarget, serviceFunction, routerFunction);
                }
            }

            return TrakHoundServiceResponse.RouteNotConfigured();
        }

        private static async Task<TrakHoundServiceResponse<TResult>> RunTarget<TService, TResult>(
            string routerId,
            RouteRequest request,
            ITrakHoundLogger logger,
            IRouteTarget target,
            Func<RouteTargetDriverRequest<TService, TResult>, Task<TrakHoundServiceResponse<TResult>>> serviceFunction,
            Func<RouteTargetRouterRequest<TResult>, Task<TrakHoundServiceResponse<TResult>>> routerFunction
            )
            where TService : ITrakHoundService
        {
            if (target != null)
            {
                // Check to see if Target is a Service Target for the correct Service Type
                if (target.GetType() == typeof(RouteServiceTarget))
                {
                    // Cast to Target Service to the Type of Service in Router
                    var driverTarget = (RouteServiceTarget)target;

                    return await RunServiceTargets(routerId, request, logger, driverTarget, serviceFunction, routerFunction);
                }
                else if (target.GetType() == typeof(RouteRouterTarget))
                {
                    // Cast to Target Router
                    var routerTarget = (RouteRouterTarget)target;

                    return await RunRouterTarget(routerId, request, logger, routerTarget, serviceFunction, routerFunction);
                }
            }

            return TrakHoundServiceResponse<TResult>.RouteNotConfigured();
        }

        #endregion

    }


    /// <summary>
    /// Request content (ex. an Upload or Publish body) that may be given to more than one Target when a Route falls back.
    /// A seekable Stream is rewound before each attempt. A forward-only Stream (ex. an HTTP Request Body) is only given to
    /// another Target if the previous attempts did not read any of it, so a fallback never receives partial content.
    /// </summary>
    internal class RouteContent
    {
        private readonly Stream _source;
        private readonly Stream _stream;
        private readonly long _start;
        private bool _attempted;


        /// <summary>
        /// The Stream to pass to the Target
        /// </summary>
        public Stream Stream => _stream;


        public RouteContent(Stream content)
        {
            _source = content;

            if (content != null)
            {
                if (content.CanSeek)
                {
                    _stream = content;
                    _start = content.Position;
                }
                else
                {
                    _stream = new ReadTrackingStream(content);
                }
            }
        }

        /// <summary>
        /// Prepares the content for the next attempt. Returns false if it can no longer be given to a Target from its start.
        /// </summary>
        public bool TryBegin()
        {
            if (_source == null) return true;

            if (!_attempted)
            {
                _attempted = true;
                return true;
            }

            if (_source.CanSeek)
            {
                // A Service may have disposed the content
                try
                {
                    _source.Position = _start;
                    return true;
                }
                catch
                {
                    return false;
                }
            }

            return !((ReadTrackingStream)_stream).HasRead;
        }

        /// <summary>
        /// Returned instead of calling a Target that can't be given the content. A 405 skips the Target,
        /// so the Route stops falling back and returns the previous Target's Response.
        /// </summary>
        public static TrakHoundServiceResponse NotResendable()
        {
            return TrakHoundServiceResponse.RouteNotConfigured("The content was already read by a previous Target and cannot be resent");
        }

        public static TrakHoundServiceResponse<TResult> NotResendable<TResult>()
        {
            return TrakHoundServiceResponse<TResult>.RouteNotConfigured("The content was already read by a previous Target and cannot be resent");
        }


        // Forwards reads and records whether any content was read
        private class ReadTrackingStream : Stream
        {
            private readonly Stream _inner;

            public bool HasRead { get; private set; }

            public ReadTrackingStream(Stream inner)
            {
                _inner = inner;
            }

            public override bool CanRead => _inner.CanRead;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => _inner.Length;
            public override long Position { get => _inner.Position; set => throw new NotSupportedException(); }

            public override int Read(byte[] buffer, int offset, int count) => Track(_inner.Read(buffer, offset, count));
            public override int Read(Span<byte> buffer) => Track(_inner.Read(buffer));
            public override int ReadByte()
            {
                var value = _inner.ReadByte();
                if (value >= 0) HasRead = true;
                return value;
            }
            public override async Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) => Track(await _inner.ReadAsync(buffer, offset, count, cancellationToken));
            public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) => Track(await _inner.ReadAsync(buffer, cancellationToken));

            public override void Flush() { }
            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

            protected override void Dispose(bool disposing)
            {
                // The Target sees this as the content, so disposing it disposes the content (as it would without the wrapper)
                if (disposing) _inner.Dispose();
                base.Dispose(disposing);
            }

            public override async ValueTask DisposeAsync()
            {
                await _inner.DisposeAsync();
                await base.DisposeAsync();
            }

            private int Track(int bytesRead)
            {
                if (bytesRead > 0) HasRead = true;
                return bytesRead;
            }
        }
    }
}
