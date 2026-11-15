// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;
using TrakHound.Security;

namespace TrakHound.Clients
{
    public static class TrakHoundInstanceClientExtensions
    {
        private const string _moduleTokenItemKey = "TrakHound.ModuleToken";


        /// <summary>
        /// Sets what API Requests made through this Client are attributed to (for API Metrics)
        /// </summary>
        public static ITrakHoundClient WithRequestSource(this ITrakHoundClient client, TrakHoundApiRequestSource source)
        {
            if (client is TrakHoundInstanceClient instanceClient)
            {
                instanceClient.RequestSource = source;
            }

            return client;
        }

        /// <summary>
        /// Attributes API Requests made through this Client to an external HTTP Request (identified by its remote address),
        /// or to the Module whose Module Token the Request was made with
        /// </summary>
        public static ITrakHoundClient WithHttpRequestSource(this ITrakHoundClient client, HttpContext httpContext)
        {
            if (httpContext.GetModuleToken() != null) return client.WithModuleRequestSource(httpContext);

            var remoteAddress = httpContext?.Connection?.RemoteIpAddress?.ToString();
            return client.WithRequestSource(new TrakHoundApiRequestSource(TrakHoundApiRequestSourceType.Http, remoteAddress));
        }

        /// <summary>
        /// Makes calls through this Client as the Module whose Module Token the Request was made with, so that the Module's Zone applies
        /// (and the Module's Router is used when a call doesn't specify one). Does nothing for a Request made without a Module Token
        /// </summary>
        public static ITrakHoundClient WithModuleRequestSource(this ITrakHoundClient client, HttpContext httpContext)
        {
            var moduleToken = httpContext.GetModuleToken();
            if (moduleToken != null && client is TrakHoundInstanceClient instanceClient)
            {
                instanceClient.RequestSource = moduleToken.Source;
                if (string.IsNullOrEmpty(instanceClient.RouterId)) instanceClient.RouterId = moduleToken.RouterId;
            }

            return client;
        }


        /// <summary>
        /// Sets the Module Token the Request was made with (set by the TrakHoundAuthenticationMiddleware once the Token is verified)
        /// </summary>
        internal static void SetModuleToken(this HttpContext httpContext, TrakHoundModuleTokenEntry moduleToken)
        {
            if (httpContext != null) httpContext.Items[_moduleTokenItemKey] = moduleToken;
        }

        /// <summary>
        /// Gets the verified Module Token the Request was made with (null if it was not made with one)
        /// </summary>
        public static TrakHoundModuleTokenEntry GetModuleToken(this HttpContext httpContext)
        {
            return httpContext?.Items[_moduleTokenItemKey] as TrakHoundModuleTokenEntry;
        }

        /// <summary>
        /// Gets the Module the Request was made as (with a Module Token). Null if it was not made with one
        /// </summary>
        public static TrakHoundApiRequestSource GetModuleSource(this HttpContext httpContext) => httpContext.GetModuleToken()?.Source;
    }
}
