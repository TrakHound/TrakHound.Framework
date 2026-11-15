// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

using System.Reflection;
using TrakHound.Api;
using TrakHound.Clients;
using TrakHound.Logging;
using TrakHound.Variables;
using TrakHound.Volumes;

namespace TrakHound.Hosting
{
    /// <summary>
    /// What MapTrakHoundApi gives the Api's Controllers, and where it reports what happens (the Instance writes these to its Logs)
    /// </summary>
    public class TrakHoundApiHostOptions
    {
        /// <summary>
        /// The assembly with the Controllers (the entry assembly when null, which is the Api project when it runs its own host)
        /// </summary>
        public Assembly Assembly { get; set; }

        /// <summary>
        /// The Api's Configuration (its ID and Parameters)
        /// </summary>
        public TrakHoundApiConfiguration Configuration { get; set; }

        public ITrakHoundClient Client { get; set; }

        public ITrakHoundVolume Volume { get; set; }

        public ITrakHoundVariableCollection Variables { get; set; }

        /// <summary>
        /// The ID of the Instance the Client connects to (null when there is none)
        /// </summary>
        public string InstanceId { get; set; }

        /// <summary>
        /// Called with each Log entry a Controller writes (the source is the Controller's name)
        /// </summary>
        public Action<string, TrakHoundLogItem> LogReceived { get; set; }

        /// <summary>
        /// Called when the host can't map or run an Endpoint (ex. a Controller's Initialize throws, or an Endpoint throws)
        /// </summary>
        public Action<string, string> ErrorReceived { get; set; }

        /// <summary>
        /// Called when a Request completes or a Stream connects
        /// </summary>
        public Action<TrakHoundApiHostRequest> RequestCompleted { get; set; }
    }

    /// <summary>
    /// A Request served by MapTrakHoundApi. A Stream connection has the Method "STREAM" and the StatusCode 101
    /// </summary>
    public record TrakHoundApiHostRequest(string Method, string Path, int StatusCode, double Duration);
}
