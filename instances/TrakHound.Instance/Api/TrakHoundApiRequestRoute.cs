// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using TrakHound.Api;

namespace TrakHound.Instance.Api
{
    /// <summary>
    /// The Api route checks for a Request, kept in the Request's Items so that the routing, authentication, rate limiting and Api middleware
    /// resolve the Request's route once (instead of each resolving it again). Kept per Path, in case a Route Rewrite changes the Path
    /// </summary>
    public static class TrakHoundApiRequestRoute
    {
        private static readonly object _itemKey = new object();


        private sealed class Entry
        {
            public string Path;
            public bool? IsRouteValid;
            public bool? IsHttpEnabled;
            public bool StreamEndpointResolved;
            public TrakHoundApiEndpointInformation StreamEndpoint;
            public bool RequestEndpointResolved;
            public TrakHoundApiMethod RequestEndpointMethod;
            public TrakHoundApiEndpointInformation RequestEndpoint;
        }


        /// <summary>
        /// Whether the Request is for an Api (see ITrakHoundApiProvider.IsRouteValid)
        /// </summary>
        public static bool IsRouteValid(this ITrakHoundApiProvider apiProvider, HttpContext context)
        {
            if (apiProvider == null || context == null) return false;

            var entry = GetEntry(context);
            entry.IsRouteValid ??= apiProvider.IsRouteValid(entry.Path);
            return entry.IsRouteValid.Value;
        }

        /// <summary>
        /// Whether the Api the Request is for is served over HTTP (see ITrakHoundApiProvider.IsHttpEnabled)
        /// </summary>
        public static bool IsHttpEnabled(this ITrakHoundApiProvider apiProvider, HttpContext context)
        {
            if (apiProvider == null || context == null) return false;

            var entry = GetEntry(context);
            entry.IsHttpEnabled ??= apiProvider.IsHttpEnabled(entry.Path);
            return entry.IsHttpEnabled.Value;
        }

        /// <summary>
        /// The Endpoint the Request is dispatched to (see ITrakHoundApiProvider.GetEndpointInformation)
        /// </summary>
        public static TrakHoundApiEndpointInformation GetEndpointInformation(this ITrakHoundApiProvider apiProvider, HttpContext context, TrakHoundApiEndpointType endpointType, TrakHoundApiMethod method)
        {
            if (apiProvider == null || context == null) return null;

            var entry = GetEntry(context);

            // Stream Endpoints are matched regardless of the Method
            if (endpointType == TrakHoundApiEndpointType.Stream)
            {
                if (!entry.StreamEndpointResolved)
                {
                    entry.StreamEndpoint = apiProvider.GetEndpointInformation(endpointType, method, entry.Path);
                    entry.StreamEndpointResolved = true;
                }

                return entry.StreamEndpoint;
            }

            if (!entry.RequestEndpointResolved || entry.RequestEndpointMethod != method)
            {
                entry.RequestEndpoint = apiProvider.GetEndpointInformation(endpointType, method, entry.Path);
                entry.RequestEndpointMethod = method;
                entry.RequestEndpointResolved = true;
            }

            return entry.RequestEndpoint;
        }


        private static Entry GetEntry(HttpContext context)
        {
            var path = context.Request.Path.ToString();
            if (context.Items.TryGetValue(_itemKey, out var value) && value is Entry entry && entry.Path == path) return entry;

            entry = new Entry { Path = path };
            context.Items[_itemKey] = entry;
            return entry;
        }
    }
}
