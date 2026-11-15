// Copyright (c) 2024 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Blazor.Routing
{
    public class MatchResult
    {
        public bool IsMatch { get; set; }

        public Route MatchedRoute { get; set; }

        /// <summary>
        /// The matched Template (its Parameter names)
        /// </summary>
        public TrakHoundRouteTemplate Template { get; set; }

        /// <summary>
        /// The URL decoded value of each Template Parameter in the Path (keyed case-insensitively). Missing optional Parameters aren't included
        /// </summary>
        public IReadOnlyDictionary<string, string> RouteValues { get; set; }


        public MatchResult(bool isMatch, Route matchedRoute)
        {
            IsMatch = isMatch;
            MatchedRoute = matchedRoute;
        }

        public static MatchResult Match(Route matchedRoute)
        {
            return new MatchResult(true, matchedRoute);
        }

        public static MatchResult Match(Route matchedRoute, TrakHoundRouteTemplate template, IReadOnlyDictionary<string, string> routeValues)
        {
            return new MatchResult(true, matchedRoute) { Template = template, RouteValues = routeValues };
        }

        public static MatchResult NoMatch()
        {
            return new MatchResult(false, null);
        }
    }
}
