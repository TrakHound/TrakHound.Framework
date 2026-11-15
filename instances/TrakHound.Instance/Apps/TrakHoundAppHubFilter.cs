// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.SignalR;

namespace TrakHound.Instance.Apps
{
    /// <summary>
    /// Makes the Blazor Hub's Request available to the Circuit's Services while a Hub method runs (ex. when the Circuit starts)
    /// </summary>
    /// <remarks>
    /// IHttpContextAccessor only has the Hub's Request when the Circuit uses WebSockets. With Server-Sent Events or Long Polling
    /// (ex. IIS without the WebSocket Protocol feature, or a proxy that blocks WebSockets) it is null in the Circuit.
    /// </remarks>
    public class TrakHoundAppHubFilter : IHubFilter
    {
        private static readonly AsyncLocal<HttpContext> _httpContext = new();

        /// <summary>
        /// The Hub's Request for the Hub method that is running (null outside of a Hub method)
        /// </summary>
        public static HttpContext HttpContext => _httpContext.Value;


        public async ValueTask<object> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object>> next)
        {
            // Set in this async method, so it is restored when the method returns
            _httpContext.Value = invocationContext.Context.GetHttpContext();
            return await next(invocationContext);
        }
    }
}
