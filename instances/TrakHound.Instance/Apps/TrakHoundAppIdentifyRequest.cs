// Copyright (c) 2026 TrakHound Inc., All Rights Reserved.
// TrakHound Inc. licenses this file to you under the Apache License, Version 2.0.

namespace TrakHound.Apps
{
    /// <summary>
    /// A request to show the connection Code on a page (for one connection, or every open page of an App)
    /// </summary>
    public class TrakHoundAppIdentifyRequest
    {
        public const int DefaultSeconds = 5;

        /// <summary>
        /// The connection to Identify (null to Identify every open page of the App)
        /// </summary>
        public string ConnectionId { get; set; }

        /// <summary>
        /// The App whose open pages to Identify (when ConnectionId is null)
        /// </summary>
        public string AppId { get; set; }

        /// <summary>
        /// How long the Code is shown
        /// </summary>
        public int Seconds { get; set; } = DefaultSeconds;
    }
}
