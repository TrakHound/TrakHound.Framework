// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server.Circuits;
using TrakHound.Instance.Security;

namespace TrakHound.Instance.Audit
{
    /// <summary>
    /// Sets the Audit Context and the Admin Context for each event in a Blazor Circuit, so that a change made from any Admin Interface page is
    /// recorded with who made it, and is checked against that Admin's Roles by the code that makes it.
    /// Each activity reaches a Circuit on its own (not from the context the Circuit started in), so they can't be set once for the Circuit
    /// </summary>
    public class TrakHoundAuditCircuitHandler : CircuitHandler
    {
        private readonly AuthenticationStateProvider _authenticationStateProvider;
        private readonly AdminAccess _adminAccess;


        public TrakHoundAuditCircuitHandler(AuthenticationStateProvider authenticationStateProvider, AdminAccess adminAccess)
        {
            _authenticationStateProvider = authenticationStateProvider;
            _adminAccess = adminAccess;
        }


        public override Func<CircuitInboundActivityContext, Task> CreateInboundActivityHandler(Func<CircuitInboundActivityContext, Task> next)
        {
            return async context =>
            {
                System.Security.Claims.ClaimsPrincipal principal = null;
                try
                {
                    principal = (await _authenticationStateProvider.GetAuthenticationStateAsync())?.User;
                }
                catch (InvalidOperationException) { } // The Authentication State isn't available until the Circuit has it

                var username = TrakHoundAdminSessions.GetUsername(principal);
                var source = username != null ? TrakHoundAuditSources.Admin : TrakHoundAuditSources.App;

                using (TrakHoundAuditContext.Begin(source, username, TrakHoundAdminSessions.GetClientAddress(principal)))
                using (TrakHoundAdminContext.Begin(_adminAccess.CreateContext()))
                {
                    await next(context);
                }
            };
        }
    }
}
