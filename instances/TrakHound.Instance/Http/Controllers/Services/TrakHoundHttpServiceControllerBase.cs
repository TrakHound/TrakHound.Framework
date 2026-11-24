// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Mvc;
using TrakHound.Clients;
using TrakHound.Instances;
using TrakHound.Services;

namespace TrakHound.Http
{
    public abstract class TrakHoundHttpServiceControllerBase : TrakHoundHttpControllerBase
    {
        private readonly ITrakHoundInstance _instance;


        public TrakHoundHttpServiceControllerBase(ITrakHoundInstance instance)
        {
            _instance = instance;
        }

        protected TrakHoundInstanceClient GetClient(string routerId = null)
        {
            return (TrakHoundInstanceClient)_instance.ClientProvider.GetClient(routerId).WithModuleRequestSource(HttpContext);
        }

        protected IActionResult ProcessServiceResponse(TrakHoundServiceResponse serviceResponse)
        {
            SetServiceHeaders(Response, serviceResponse.RouterId, serviceResponse.ServiceId, serviceResponse.StatusCode);

            var result = new ContentResult();
            result.StatusCode = GetHttpStatusCode(serviceResponse.StatusCode);
            result.Content = serviceResponse.Message;
            result.ContentType = "text/plain";
            return result;
        }

        protected IActionResult ProcessServiceResponse<TResult>(TrakHoundServiceResponse<TResult> serviceResponse)
        {
            SetServiceHeaders(Response, serviceResponse.RouterId, serviceResponse.ServiceId, serviceResponse.StatusCode);

            if (serviceResponse.StatusCode >= 200 && serviceResponse.StatusCode < 400)
            {
                // Written as compact UTF-8 (no indented string copy) since Results can be large (ex. Table queries).
                // Empty when the Result is null or can't be serialized
                return new Utf8JsonResult(serviceResponse.StatusCode, Json.ConvertToUtf8(serviceResponse.Result));
            }
            else
            {
                var result = new ContentResult();
                result.StatusCode = GetHttpStatusCode(serviceResponse.StatusCode);
                result.Content = serviceResponse.Message;
                result.ContentType = "text/plain";
                return result;
            }
        }

        protected async Task ProcessServiceResponse(HttpResponse httpResponse, TrakHoundServiceResponse serviceResponse)
        {
            httpResponse.StatusCode = GetHttpStatusCode(serviceResponse.StatusCode);

            SetServiceHeaders(httpResponse, serviceResponse.RouterId, serviceResponse.ServiceId, serviceResponse.StatusCode);

            if (!string.IsNullOrEmpty(serviceResponse.Message))
            {
                var responseBytes = serviceResponse.Message.ToUtf8Bytes();
                await Response.Body.WriteAsync(responseBytes);
            }
        }

        protected async Task ProcessServiceResponse<TResult>(HttpResponse httpResponse, TrakHoundServiceResponse<TResult> serviceResponse)
        {
            httpResponse.StatusCode = GetHttpStatusCode(serviceResponse.StatusCode);

            SetServiceHeaders(httpResponse, serviceResponse.RouterId, serviceResponse.ServiceId, serviceResponse.StatusCode);

            if (!string.IsNullOrEmpty(serviceResponse.Message))
            {
                var responseBytes = serviceResponse.Message.ToUtf8Bytes();
                await Response.Body.WriteAsync(responseBytes);
            }
        }


        /// <summary>
        /// Name the Router and Service that answered (ex. for a client to tell which Target handled a Request). Not sent with a
        /// 401 or 403, so a caller that isn't allowed to use the Service doesn't learn how the Instance routes it
        /// </summary>
        internal static void SetServiceHeaders(HttpResponse httpResponse, string routerId, string serviceId, int statusCode)
        {
            if (statusCode == StatusCodes.Status401Unauthorized || statusCode == StatusCodes.Status403Forbidden) return;

            if (!string.IsNullOrEmpty(routerId)) httpResponse.Headers[TrakHoundHttpConstants.RouterIdHeader] = routerId;
            if (!string.IsNullOrEmpty(serviceId)) httpResponse.Headers[TrakHoundHttpConstants.ServiceIdHeader] = serviceId;
        }


        /// <summary>
        /// The HTTP Status Code to answer with for a Service Response's Status Code. A code that isn't a final HTTP status
        /// (ex. 0 from a Response that was never set) would be written as an invalid status line that clients can't read,
        /// so it's answered as 503 (no Service handled the Request)
        /// </summary>
        internal static int GetHttpStatusCode(int serviceStatusCode)
        {
            return serviceStatusCode >= 200 && serviceStatusCode <= 599 ? serviceStatusCode : StatusCodes.Status503ServiceUnavailable;
        }


        private sealed class Utf8JsonResult : IActionResult
        {
            private readonly int _statusCode;
            private readonly byte[] _content;

            public Utf8JsonResult(int statusCode, byte[] content)
            {
                _statusCode = statusCode;
                _content = content ?? Array.Empty<byte>();
            }

            public async Task ExecuteResultAsync(ActionContext context)
            {
                var response = context.HttpContext.Response;
                response.StatusCode = _statusCode;
                response.ContentType = "application/json";
                response.ContentLength = _content.Length;

                if (_content.Length > 0) await response.Body.WriteAsync(_content, context.HttpContext.RequestAborted);
            }
        }
    }
}
