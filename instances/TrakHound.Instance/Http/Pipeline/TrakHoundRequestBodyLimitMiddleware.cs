// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Http.Features;
using TrakHound.Instances;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// Limits the size of Request bodies (maxRequestBodySizeMb in the Instance Configuration, 0 = no limit). The server itself has no
    /// limit, so the limit is read on every Request and a saved change applies immediately. Requests over the limit get a 413
    /// </summary>
    public class TrakHoundRequestBodyLimitMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ITrakHoundInstance _instance;


        public TrakHoundRequestBodyLimitMiddleware(RequestDelegate next, ITrakHoundInstance instance)
        {
            _next = next;
            _instance = instance;
        }


        public async Task Invoke(HttpContext context)
        {
            var feature = context.Features.Get<IHttpMaxRequestBodySizeFeature>();
            if (feature != null && !feature.IsReadOnly)
            {
                feature.MaxRequestBodySize = GetLimit(_instance.Configuration);
            }

            await _next(context);
        }

        /// <summary>
        /// The limit in bytes (null for no limit)
        /// </summary>
        public static long? GetLimit(TrakHoundInstanceConfiguration configuration)
        {
            var megabytes = configuration?.MaxRequestBodySizeMb ?? TrakHoundInstanceConfiguration.DefaultMaxRequestBodySizeMb;
            return megabytes > 0 ? megabytes * 1024L * 1024L : null;
        }
    }
}
