// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;

namespace TrakHound.Instance.Api
{
    public static class TrakHoundApiClientParameters
    {
        /// <summary>
        /// Adds the client's IP Address and User-Agent to the Query Parameters passed to an API Controller
        /// </summary>
        public static void Set(HttpContext context, Dictionary<string, string> queryParameters)
        {
            if (context != null)
            {
                var remoteAddress = context.Connection.RemoteIpAddress;
                if (remoteAddress != null && remoteAddress.IsIPv4MappedToIPv6) remoteAddress = remoteAddress.MapToIPv4();

                var clientAddress = remoteAddress?.ToString();
                var clientAgent = context.Request.Headers.UserAgent.FirstOrDefault();

                TrakHoundApi.SetClientParameters(queryParameters, clientAddress, clientAgent);
            }
        }
    }
}
