// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Routing.Matching;

namespace TrakHound.Instance.Http
{
    /// <summary>
    /// The Razor Components are always mapped since Apps need the Blazor endpoints (/_blazor) for interactivity. When the
    /// Admin Interface is disabled, or the Request is not on the Admin Port (when there is a separate Admin Port), its Pages
    /// (/_admin/*) are not matched, so those requests fall through to the App fallback the same as if the
    /// Admin Interface was not mapped.
    /// </summary>
    internal class AdminInterfaceMatcherPolicy : MatcherPolicy, IEndpointSelectorPolicy
    {

        private readonly bool _adminInterfaceEnabled;


        public override int Order => 0;


        public AdminInterfaceMatcherPolicy(bool adminInterfaceEnabled)
        {
            _adminInterfaceEnabled = adminInterfaceEnabled;
        }


        public bool AppliesToEndpoints(IReadOnlyList<Endpoint> endpoints)
        {
            if (_adminInterfaceEnabled && !TrakHoundAdminPort.IsEnabled) return false;

            for (var i = 0; i < endpoints.Count; i++)
            {
                if (IsAdminPage(endpoints[i])) return true;
            }

            return false;
        }

        public Task ApplyAsync(HttpContext httpContext, CandidateSet candidates)
        {
            if (_adminInterfaceEnabled && TrakHoundAdminPort.IsAdminConnection(httpContext)) return Task.CompletedTask;

            for (var i = 0; i < candidates.Count; i++)
            {
                if (candidates.IsValidCandidate(i) && IsAdminPage(candidates[i].Endpoint))
                {
                    candidates.SetValidity(i, false);
                }
            }

            return Task.CompletedTask;
        }

        private static bool IsAdminPage(Endpoint endpoint) => TrakHoundAdminPort.IsAdminPage(endpoint);
    }
}
