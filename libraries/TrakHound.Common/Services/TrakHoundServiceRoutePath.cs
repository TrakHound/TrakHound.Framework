// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Collections.Generic;

namespace TrakHound.Services
{
    /// <summary>
    /// A configured Service, as <see cref="TrakHoundServiceRouteResolver.Resolve"/> sees it
    /// </summary>
    public class TrakHoundServiceRouteService
    {
        /// <summary>
        /// The Service's Configuration ID
        /// </summary>
        public string Id { get; set; }

        public string Name { get; set; }

        /// <summary>
        /// The Routes the Service handles (ex. "KeyValues"). Null if they aren't known (ex. the Service isn't running)
        /// </summary>
        public IReadOnlyCollection<string> Routes { get; set; }
    }

    /// <summary>
    /// Where a Request for a Route (ex. "KeyValues") sent to a Router would go
    /// </summary>
    public class TrakHoundServiceRoutePath
    {
        public string RouterId { get; set; }

        public string RouterName { get; set; }

        public string Route { get; set; }

        /// <summary>
        /// The Targets of the Router's Routes that match the Route, in the order they are tried
        /// </summary>
        public List<TrakHoundServiceRoutePathTarget> Targets { get; } = new List<TrakHoundServiceRoutePathTarget>();
    }

    /// <summary>
    /// A Target a Request would be sent to
    /// </summary>
    public class TrakHoundServiceRoutePathTarget
    {
        public string RouteId { get; set; }

        public string TargetId { get; set; }

        /// <summary>
        /// "Service" or "Router"
        /// </summary>
        public string Type { get; set; }

        /// <summary>
        /// The Service Configuration ID (or "*") or Router ID the Target names
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// The Name of the Service or Router the Source names (the Source if it doesn't have one, or doesn't exist)
        /// </summary>
        public string SourceName { get; set; }

        /// <summary>
        /// The results that send the Request on to the next Target
        /// </summary>
        public IReadOnlyList<TrakHoundServiceResultType> FallbackOn { get; set; } = new List<TrakHoundServiceResultType>();

        /// <summary>
        /// The Services a Service Target sends the Request to, in order (only those that handle the Route, or might)
        /// </summary>
        public List<TrakHoundServiceRouteService> Services { get; set; } = new List<TrakHoundServiceRouteService>();

        /// <summary>
        /// Where a Router Target's Router sends the Request (null if it doesn't exist or would loop)
        /// </summary>
        public TrakHoundServiceRoutePath Router { get; set; }

        /// <summary>
        /// Why the Target won't handle the Request (null if it will)
        /// </summary>
        public string Problem { get; set; }
    }
}
