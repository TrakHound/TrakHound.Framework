// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Net.Http.Headers;
using TrakHound.Api;
using TrakHound.Clients;
using TrakHound.Http;
using TrakHound.Instances;
using TrakHound.Logging;

namespace TrakHound.Instance.Api
{
    public class TrakHoundApiMiddleware
    {
        private readonly ITrakHoundInstance _instance;
        private readonly TrakHoundHttpWebSocketManager _webSocketManager;
        private readonly RequestDelegate _next;
        private readonly ITrakHoundLogger _logger = new TrakHoundLogger<TrakHoundApiMiddleware>();


        public TrakHoundApiMiddleware(ITrakHoundInstance instance, TrakHoundHttpWebSocketManager webSocketManager, RequestDelegate next)
        {
            _instance = instance;
            _webSocketManager = webSocketManager;
            _next = next;
        }


        public async Task Invoke(HttpContext context)
        {
            if (context.Response.HasStarted) return;

            if (_instance.ApiProvider.IsRouteValid(context))
            {
                // APIs not served over HTTP, for the Instance or this Api (they are still available within the Instance)
                if (!_instance.Configuration.ApiHttpEnabled || !_instance.ApiProvider.IsHttpEnabled(context))
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                    await context.Response.CompleteAsync();
                    return;
                }

                if (IsStreamRequest(_instance, context))
                {
                    await HandleStream(context);
                }
                else
                {
                    await HandleRequest(context);
                }

                await context.Response.CompleteAsync();
            }
            else
            {
                await _next(context);
            }
        }

        /// <summary>
        /// A WebSocket Request, or a Server-Sent Events Request for a Route with a Stream Endpoint (otherwise it's a Request, so a GET Endpoint
        /// still answers a client that sends Accept: text/event-stream)
        /// </summary>
        public static bool IsStreamRequest(ITrakHoundInstance instance, HttpContext context)
        {
            if (context.WebSockets.IsWebSocketRequest) return true;
            if (!TrakHoundHttpEventStreamManager.IsEventStreamRequest(context)) return false;

            var requestMethod = TrakHoundApi.GetApiMethod(context.Request.Method);
            return instance.ApiProvider.GetEndpointInformation(context, TrakHoundApiEndpointType.Stream, requestMethod) != null;
        }

        private async Task HandleRequest(HttpContext context)
        {
            var client = _instance.ClientProvider.GetClient().WithHttpRequestSource(context);
            if (client == null)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            var cancellationToken = context.RequestAborted;
            var queryParameters = GetQueryParameters(context);
            var requestHeaders = GetRequestHeaders(context);
            var requestContentType = context.Request.Headers.ContentType;
            var requestMethod = TrakHoundApi.GetApiMethod(context.Request.Method);
            var requestRoute = context.Request.Path.Value;

            TrakHoundApiResponse response;
            try
            {
                // Read Request Body (the maximum size is enforced by the server's MaxRequestBodySize limit). It is buffered so that
                // Endpoints can read it synchronously, in memory for small Bodies and in a temporary file for large ones (instead of one large MemoryStream)
                Stream requestBody = null;
                if (HasRequestBody(context.Request))
                {
                    context.Request.EnableBuffering();
                    await context.Request.Body.DrainAsync(cancellationToken);
                    context.Request.Body.Seek(0, SeekOrigin.Begin);
                    requestBody = context.Request.Body;
                }

                response = await client.Api.Send(requestMethod, requestRoute, requestBody, requestContentType, queryParameters, requestHeaders, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // Client disconnected
                return;
            }

            try
            {
                if (response.IsValid())
                {
                    // Set Parameter Headers
                    if (!response.Parameters.IsNullOrEmpty())
                    {
                        foreach (var parameter in response.Parameters)
                        {
                            if (!string.IsNullOrEmpty(parameter.Key) && !string.IsNullOrEmpty(parameter.Value) && parameter.Key != "Filename")
                            {
                                context.Response.Headers.Append(parameter.Key, parameter.Value);
                            }
                        }
                    }

                    // Set Filename Header
                    if (MimeTypes.IsAttachment(response.ContentType))
                    {
                        var contentDisposition = new ContentDispositionHeaderValue("attachment");
                        var filename = response.GetParameter("Filename");
                        if (!string.IsNullOrEmpty(filename)) contentDisposition.SetHttpFileName(filename);
                        context.Response.Headers.ContentDisposition = contentDisposition.ToString();
                    }

                    // Set Status Code
                    context.Response.StatusCode = response.StatusCode;

                    // Set Content
                    if (response.Content != null)
                    {
                        context.Response.ContentType = response.ContentType;
                        if (response.Content.CanSeek) context.Response.ContentLength = response.Content.Length;

                        // Write to Response Body
                        await response.Content.CopyToAsync(context.Response.Body, cancellationToken);
                    }
                }
                else
                {
                    context.Response.StatusCode = StatusCodes.Status404NotFound;
                }
            }
            catch (OperationCanceledException)
            {
                // Client disconnected
            }
            catch (Exception ex)
            {
                _logger.LogError($"API Response Error : {context.Request.Method} : {requestRoute} : {ex.Message}");

                if (!context.Response.HasStarted)
                {
                    context.Response.Clear();
                    context.Response.StatusCode = StatusCodes.Status500InternalServerError;
                }
            }
            finally
            {
                response.Content?.Dispose();
            }
        }

        private async Task HandleStream(HttpContext context)
        {
            if (_instance.ClientProvider == null)
            {
                context.Response.StatusCode = StatusCodes.Status503ServiceUnavailable;
                return;
            }

            var queryParameters = GetQueryParameters(context);
            var requestHeaders = GetRequestHeaders(context);
            var requestRoute = context.Request.Path.Value.TrimStart('/');

            var getStream = async () =>
            {
                var client = _instance.ClientProvider.GetClient().WithHttpRequestSource(context);
                if (client != null)
                {
                    return await client.Api.Stream(requestRoute, queryParameters, requestHeaders, context.RequestAborted);
                }

                return null;
            };

            var inputFormatResponse = (byte[] inputBytes) =>
            {
                var json = StringFunctions.GetUtf8String(inputBytes);
                var jsonResponse = Json.Convert<TrakHoundApiJsonResponse>(json);
                if (jsonResponse == null) jsonResponse = new TrakHoundApiJsonResponse();
                return jsonResponse.ToResponse();
            };

            var outputFormatResponse = (TrakHoundApiResponse response) =>
            {
                var jsonResponse = new TrakHoundApiJsonResponse(response);
                var json = Json.Convert(jsonResponse);
                return json.ToUtf8Bytes();
            };

            await _webSocketManager.Create<TrakHoundApiResponse>(context, getStream, inputFormatResponse, outputFormatResponse);
        }

        private static Dictionary<string, string> GetQueryParameters(HttpContext context)
        {
            var queryParameters = new Dictionary<string, string>();

            foreach (var queryParameter in context.Request.Query)
            {
                queryParameters[queryParameter.Key] = queryParameter.Value;
            }

            TrakHoundApiClientParameters.Set(context, queryParameters);

            return queryParameters;
        }

        /// <summary>
        /// The Request Headers passed to the Api ([FromHeader] Parameters). Headers with the caller's credentials for the Instance
        /// (ex. its Cookie, API Key or Bearer Token) are withheld, so an Api can't act as its caller
        /// </summary>
        public static Dictionary<string, string> GetRequestHeaders(HttpContext context)
        {
            return TrakHoundApiHeaders.Filter(context.Request.Headers.Select(o => new KeyValuePair<string, string>(o.Key, o.Value.ToString())));
        }

        private static bool HasRequestBody(HttpRequest request)
        {
            return request.ContentLength > 0 || (request.ContentLength == null && request.Headers.TransferEncoding.Count > 0);
        }
    }
}
