// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Authentication;
using System.Security.Claims;
using TrakHound.Instance.Security;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// Adds the Admin Session (from its Cookie) to the Request's User for the Admin Interface and its Blazor connection, so the
    /// Circuit's Authentication State has it. As a separate Identity (its own Authentication Type), so App pages, which only read
    /// the Identity Provider Session (TrakHoundIdentityClaims.FromPrincipal), never treat an Admin as signed in
    /// </summary>
    public class TrakHoundAdminSessionMiddleware
    {
        private readonly RequestDelegate _next;


        public TrakHoundAdminSessionMiddleware(RequestDelegate next)
        {
            _next = next;
        }


        public async Task Invoke(HttpContext context)
        {
            var path = context.Request.Path;
            if (TrakHoundInstanceRoutes.IsAdminRoute(path) || TrakHoundInstanceRoutes.IsBlazorRoute(path))
            {
                var result = await context.AuthenticateAsync(TrakHoundAdminSessions.AuthenticationScheme);
                if (result.Succeeded && result.Principal?.Identity is ClaimsIdentity adminIdentity)
                {
                    context.User.AddIdentity(adminIdentity);
                }

                // The client address for the Admin Login's Sign In Throttle (the HttpContext isn't available in a Circuit). An Identity
                // without an Authentication Type, so it is never treated as signed in
                var clientAddress = context.Connection.RemoteIpAddress?.ToString();
                if (clientAddress != null) context.User.AddIdentity(TrakHoundAdminSessions.CreateConnectionIdentity(clientAddress));
            }

            await _next(context);
        }
    }
}
