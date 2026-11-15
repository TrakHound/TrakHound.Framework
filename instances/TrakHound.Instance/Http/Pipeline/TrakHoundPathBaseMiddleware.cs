// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// Hosted under a Path Base (ex. an IIS Application), a request for the Path Base without a trailing slash has an empty Path :
    /// redirect to the trailing slash, since Blazor requires the page URL to be within the base href
    /// </summary>
    public class TrakHoundPathBaseMiddleware
    {
        private readonly RequestDelegate _next;


        public TrakHoundPathBaseMiddleware(RequestDelegate next)
        {
            _next = next;
        }


        public async Task Invoke(HttpContext context)
        {
            if (!context.Request.Path.HasValue)
            {
                if (context.Request.PathBase.HasValue)
                {
                    context.Response.Redirect($"{context.Request.PathBase}/{context.Request.QueryString}");
                    return;
                }

                context.Request.Path = "/";
            }

            await _next(context);
        }
    }
}
